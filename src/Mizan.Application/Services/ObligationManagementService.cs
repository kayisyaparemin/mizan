using Mizan.Application.Abstractions;
using Mizan.Application.Models;
using Mizan.Domain.Calculations;
using Mizan.Domain.Models;

namespace Mizan.Application.Services;

public sealed partial class ObligationManagementService(
    IMizanStore store,
    IClock clock,
    IFinancialPlanQueryService queryService,
    FinancialSnapshotService snapshotService,
    CashFlowPeriodCalculator CashFlowPeriodCalculator,
    PaymentAllocationStrategyResolver allocationResolver,
    LoanPayoffService loanPayoffService,
    CreditCardObligationService creditCardObligationService) : IObligationManagementService
{
    public async Task<bool> IsOnboardingRequiredAsync(
        CancellationToken cancellationToken = default)
    {
        var plan = await queryService.GetFinancialPlanAsync(cancellationToken);
        var history = await store.GetFinancialHistoryAsync(cancellationToken);
        if (FinancialSnapshotService.LatestCurrent(history) is not null)
        {
            return false;
        }

        if (!FinancialPlanQueryService.CanBuildProjection(plan))
        {
            return true;
        }

        await snapshotService.EnsureInitialSnapshotAsync(
            plan,
            cancellationToken);
        return false;
    }

    public async Task InitializeFromOnboardingAsync(
        OnboardingDraft draft,
        CancellationToken cancellationToken = default)
    {
        ObligationValidation.ValidateOnboardingDraft(draft);

        var settings = draft.Settings with
        {
            ProjectionAnchorDate = draft.Settings.ProjectionAnchorDate == default
                ? clock.Today
                : draft.Settings.ProjectionAnchorDate
        };
        var paymentPlans = draft.PaymentPlans
            .Select(ObligationValidation.NormalizePaymentPlan)
            .ToArray();
        var cards = draft.CreditCards
            .Select(c => ObligationValidation.NormalizeCreditCard(c, clock))
            .ToArray();
        var strategy = new CashFlowAllocationStrategy
        {
            Mode = draft.InitialCashFlowAllocationMode,
            EffectiveFromPeriodDate = CashFlowPeriodCalculator
                .GetFirstPeriodStartOnOrAfter(
                    settings.ProjectionAnchorDate,
                    settings.IncomeDay),
            CreatedAt = clock.UtcNow,
            Note = "İlk gelir kullanım düzeni"
        };
        var plan = new FinancialPlan
        {
            Settings = settings,
            Salaries = draft.Salaries
                .OrderBy(x => x.EffectiveDate)
                .ToArray(),
            OtherIncomes = draft.OtherIncomes
                .OrderBy(x => x.ExactDate)
                .ToArray(),
            Loans = draft.Loans
                .OrderBy(x => x.NextPaymentDate)
                .ToArray(),
            PaymentPlans = paymentPlans
                .OrderBy(x => x.Installments.Min(i => i.DueDate))
                .ToArray(),
            CreditCards = cards
                .OrderBy(x => x.Bank)
                .ThenBy(x => x.Name)
                .ToArray(),
            PlannedLargeExpenses = draft.PlannedLargeExpenses
                .OrderBy(x => x.ExactDate)
                .ToArray(),
            PaymentAssignmentStrategies = [strategy]
        };
        var bundle = snapshotService.Build(
            plan,
            settings.ProjectionOpeningBalance,
            settings.ProjectionAnchorDate,
            FinancialSnapshotSource.Initial,
            string.IsNullOrWhiteSpace(draft.SnapshotNote)
                ? "İlk güncel finansal durum"
                : draft.SnapshotNote,
            null);

        await store.ApplyOnboardingSetupAsync(
            new OnboardingPersistenceBatch(
                bundle.UpdatedSettings,
                plan.Salaries,
                plan.OtherIncomes,
                plan.Loans,
                plan.PaymentPlans,
                plan.CreditCards,
                plan.PlannedLargeExpenses,
                plan.PaymentAssignmentStrategies,
                bundle.Snapshot,
                bundle.Plan),
            cancellationToken);
    }

    public Task SaveCreditCardAsync(
        CreditCard card,
        CancellationToken cancellationToken = default) =>
        creditCardObligationService.SaveCreditCardAsync(card, cancellationToken);

    public Task SaveCreditCardStatementAsync(
        Guid creditCardId,
        CreditCardStatement statement,
        CurrentStatementPaymentPlan paymentPlan,
        CancellationToken cancellationToken = default) =>
        creditCardObligationService.SaveCreditCardStatementAsync(
            creditCardId, statement, paymentPlan, cancellationToken);

    public Task DeleteCreditCardAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        creditCardObligationService.DeleteCreditCardAsync(id, cancellationToken);

    public Task SaveCreditCardPaymentPlanAsync(
        Guid creditCardId,
        DateOnly dueDate,
        CreditCardPaymentType paymentType,
        decimal? amount = null,
        CancellationToken cancellationToken = default) =>
        creditCardObligationService.SaveCreditCardPaymentPlanAsync(
            creditCardId, dueDate, paymentType, amount, cancellationToken);

    public Task SetStatementPaymentModeAsync(
        Guid creditCardId,
        DateOnly dueDate,
        CreditCardPaymentType paymentType,
        CancellationToken cancellationToken = default) =>
        creditCardObligationService.SetStatementPaymentModeAsync(
            creditCardId, dueDate, paymentType, cancellationToken);

    public Task RemoveCreditCardPaymentPlanAsync(
        Guid creditCardId,
        DateOnly dueDate,
        CancellationToken cancellationToken = default) =>
        creditCardObligationService.RemoveCreditCardPaymentPlanAsync(
            creditCardId, dueDate, cancellationToken);

    public async Task SaveSettingsAsync(
        UserSettings settings,
        CancellationToken cancellationToken = default)
    {
        CalendarRules.ValidateDay(settings.IncomeDay);
        if (settings.MonthlyVariableExpenseAllowance < 0m)
        {
            throw new InvalidOperationException(
                "Tahmini yaşam bütçesi negatif olamaz.");
        }

        if (settings.CreditCardCarryInterestRate is < 0m or > 1m ||
            settings.DeficitFinancingInterestRate is < 0m or > 1m)
        {
            throw new InvalidOperationException(
                "Faiz varsayımları %0 ile %100 arasında olmalıdır.");
        }

        var plan = await queryService.GetFinancialPlanAsync(cancellationToken);
        var history = await store.GetFinancialHistoryAsync(cancellationToken);
        var currentSnapshot = FinancialSnapshotService.LatestCurrent(history);
        var adjustedStrategies = settings.IncomeDay == plan.Settings.IncomeDay
            ? plan.PaymentAssignmentStrategies
            : plan.PaymentAssignmentStrategies.Select(strategy =>
                strategy with
                {
                    EffectiveFromPeriodDate = CalendarRules.ResolveDay(
                        strategy.EffectiveFromPeriodDate.Year,
                        strategy.EffectiveFromPeriodDate.Month,
                        settings.IncomeDay)
                }).ToArray();
        var createsRecoverySnapshot = currentSnapshot is not null &&
                                      FinancialPlanQueryService.CanBuildProjection(plan) &&
                                      (settings.ProjectionOpeningBalance !=
                                           plan.Settings.ProjectionOpeningBalance ||
                                       settings.ProjectionAnchorDate !=
                                           plan.Settings.ProjectionAnchorDate);
        if (createsRecoverySnapshot)
        {
            var snapshotDate = settings.ProjectionOpeningBalance !=
                               plan.Settings.ProjectionOpeningBalance
                ? clock.Today
                : settings.ProjectionAnchorDate;
            var normalized = settings with
            {
                ProjectionAnchorDate = snapshotDate
            };
            await snapshotService.CreateCurrentSnapshotAsync(
                plan with
                {
                    Settings = normalized,
                    PaymentAssignmentStrategies = adjustedStrategies
                },
                normalized.ProjectionOpeningBalance,
                snapshotDate,
                FinancialSnapshotSource.Recovery,
                "Güncel finansal durum yenilendi",
                cancellationToken);
            settings = normalized;
        }
        else
        {
            await store.SaveSettingsAsync(settings, cancellationToken);
        }
        if (settings.IncomeDay != plan.Settings.IncomeDay)
        {
            foreach (var strategy in adjustedStrategies)
            {
                await store.UpsertCashFlowAllocationStrategyAsync(
                    strategy,
                    cancellationToken);
            }
        }

        await queryService.CapturePlanningChangeAsync(
            "Planlama varsayımları değişti",
            cancellationToken);
    }

    public async Task SaveCashFlowAllocationStrategyAsync(
        CashFlowAllocationStrategy strategy,
        bool confirmedHistoricalCorrection = false,
        CancellationToken cancellationToken = default)
    {
        var plan = await queryService.GetFinancialPlanAsync(cancellationToken);
        ValidateStrategyDate(plan, strategy.EffectiveFromPeriodDate);
        if (!Enum.IsDefined(strategy.Mode))
        {
            throw new InvalidOperationException(
                "Gelir kullanım düzeni geçersiz.");
        }

        var existing = plan.PaymentAssignmentStrategies
            .FirstOrDefault(x => x.Id == strategy.Id);
        var isHistoricalCorrection = existing is not null &&
                                     existing.EffectiveFromPeriodDate <=
                                     clock.Today;
        if (isHistoricalCorrection && !confirmedHistoricalCorrection)
        {
            throw new InvalidOperationException(
                "Geçmiş bir kararı düzeltmek önceki plan sonuçlarını değiştirir ve ayrı onay gerektirir.");
        }

        var conflicting = plan.PaymentAssignmentStrategies.FirstOrDefault(x =>
            x.EffectiveFromPeriodDate == strategy.EffectiveFromPeriodDate &&
            x.Id != strategy.Id);
        if (conflicting is not null)
        {
            if (conflicting.EffectiveFromPeriodDate <= clock.Today &&
                !confirmedHistoricalCorrection)
            {
                throw new InvalidOperationException(
                    "Bu dönem tarihindeki geçmiş kayıt yalnızca onaylı düzeltme ile değiştirilebilir.");
            }

            await store.DeleteCashFlowAllocationStrategyAsync(
                conflicting.Id,
                cancellationToken);
        }

        await store.UpsertCashFlowAllocationStrategyAsync(
            strategy with
            {
                CreatedAt = existing?.CreatedAt ?? clock.UtcNow
            },
            cancellationToken);
        await queryService.CapturePlanningChangeAsync(
            "Gelir kullanım düzeni değişti",
            cancellationToken);
    }

    public async Task DeleteCashFlowAllocationStrategyAsync(
        Guid id,
        bool confirmedHistoricalCorrection = false,
        CancellationToken cancellationToken = default)
    {
        var plan = await queryService.GetFinancialPlanAsync(cancellationToken);
        var strategy = plan.PaymentAssignmentStrategies
            .SingleOrDefault(x => x.Id == id)
            ?? throw new InvalidOperationException("Düzen kaydı bulunamadı.");
        if (plan.PaymentAssignmentStrategies.Count == 1)
        {
            throw new InvalidOperationException("İlk düzen kaydı silinemez.");
        }

        if (strategy.EffectiveFromPeriodDate <= clock.Today &&
            !confirmedHistoricalCorrection)
        {
            throw new InvalidOperationException(
                "Geçmiş düzen kaydını silmek ayrı onay gerektirir.");
        }

        var remaining = plan.PaymentAssignmentStrategies
            .Where(x => x.Id != id)
            .ToArray();
        var firstPeriodStart = CashFlowPeriodCalculator.GetFirstPeriodStartOnOrAfter(
            plan.Settings.ProjectionAnchorDate,
            plan.Settings.IncomeDay);
        allocationResolver.ValidateHistory(
            remaining,
            plan.Settings.IncomeDay,
            firstPeriodStart);
        await store.DeleteCashFlowAllocationStrategyAsync(id, cancellationToken);
        await queryService.CapturePlanningChangeAsync(
            "Gelir kullanım düzeni değişti",
            cancellationToken);
    }

    public async Task<FinancialSnapshot> RefreshCurrentFinancialStateAsync(
        decimal startingSavings,
        string note = "",
        CancellationToken cancellationToken = default)
    {
        var plan = await queryService.GetFinancialPlanAsync(cancellationToken);
        var bundle = await snapshotService.CreateCurrentSnapshotAsync(
            plan,
            startingSavings,
            clock.Today,
            FinancialSnapshotSource.Recovery,
            string.IsNullOrWhiteSpace(note)
                ? "Güncel finansal durum yenilendi"
                : note,
            cancellationToken);
        return bundle.Snapshot;
    }

    private void ValidateStrategyDate(
        FinancialPlan plan,
        DateOnly EffectivePeriodDate)
    {
        if (!allocationResolver.IsPeriodStartDate(
                EffectivePeriodDate,
                plan.Settings.IncomeDay))
        {
            throw new InvalidOperationException(
                "Düzen değişikliği yalnızca bir dönem tarihinde başlayabilir.");
        }
    }
}
