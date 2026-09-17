using System.Globalization;
using Mizan.Application.Abstractions;
using Mizan.Application.Models;
using Mizan.Domain.Models;

namespace Mizan.Application.Services;

public sealed partial class PeriodReviewService(
    IMizanStore store,
    IClock clock,
    FinancialSnapshotService snapshotService,
    FinancialStateReconciliationService reconciliationService,
    FinancialInstrumentReconciliationService instrumentService,
    PlanActualComparisonCalculator comparisonCalculator)
{
    private static readonly CultureInfo TurkishCulture =
        CultureInfo.GetCultureInfo("tr-TR");

    public async Task<PeriodReviewAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken = default)
    {
        var history = await store.GetFinancialHistoryAsync(cancellationToken);
        var current = FinancialSnapshotService.LatestCurrent(history);
        if (current is null)
        {
            return new PeriodReviewAvailability(
                false,
                false,
                null,
                null,
                null,
                "Güncel finansal durumunu oluşturarak başlayabilirsin.");
        }

        var plan = history.Plans
            .Where(x => x.FinancialSnapshotId == current.Id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();
        var finalized = plan is not null && history.Actuals.Any(x =>
            x.PeriodPlanSnapshotId == plan.Id);
        var due = plan is not null &&
                  !finalized &&
                  clock.Today >= plan.SettlementAvailableFrom;
        var message = due
            ? $"{plan!.PeriodStart.ToString("dd MMMM", TurkishCulture)} dönemi güncellenmeye hazır."
            : $"Son güncelleme: {current.SnapshotDate.ToString("dd MMMM yyyy", TurkishCulture)}";
        return new PeriodReviewAvailability(
            true,
            due,
            current,
            due ? plan : null,
            current.SnapshotDate,
            message);
    }

    public async Task<PeriodReviewContext> GetContextAsync(
        Guid? planId = null,
        CancellationToken cancellationToken = default)
    {
        var history = await store.GetFinancialHistoryAsync(cancellationToken);
        var current = FinancialSnapshotService.LatestCurrent(history)
            ?? throw new InvalidOperationException(
                "Önce güncel finansal durumunu kaydetmelisin.");
        var plan = planId is null
            ? history.Plans
                .Where(x => x.FinancialSnapshotId == current.Id)
                .OrderByDescending(x => x.CreatedAtUtc)
                .FirstOrDefault()
            : history.Plans.SingleOrDefault(x => x.Id == planId.Value);
        if (plan is null)
        {
            throw new InvalidOperationException(
                "Güncellenecek dönem planı bulunamadı.");
        }

        var source = history.Snapshots.Single(x =>
            x.Id == plan.FinancialSnapshotId);
        var revisions = history.Revisions
            .Where(x => x.PeriodPlanSnapshotId == plan.Id)
            .Where(x => IsValidForFinalPlan(plan, x))
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.RevisionNumber)
            .ToArray();
        var revision = revisions.LastOrDefault();
        var actual = history.Actuals.SingleOrDefault(x =>
            x.PeriodPlanSnapshotId == plan.Id);
        var comparison = actual is null
            ? null
            : comparisonCalculator.Calculate(plan, revision, actual);
        var paymentLines = FinalPaymentLines(plan, revision);
        var planned = FinalPlanValues.From(plan, revision);
        return new PeriodReviewContext(
            source,
            plan,
            revision,
            revisions.Length,
            actual,
            source.ProjectionOpeningBalance +
            planned.PlannedIncome -
            paymentLines.Sum(x => x.PlannedAmount.GetValueOrDefault()) -
            planned.PlannedVariableExpenseAllowance -
            planned.PlannedDeficitInterest,
            comparison);
    }

    public async Task<PeriodReviewPreview> PreviewAsync(
        PeriodReviewDraft draft,
        CancellationToken cancellationToken = default)
    {
        var history = await store.GetFinancialHistoryAsync(cancellationToken);
        var plan = history.Plans.SingleOrDefault(x =>
            x.Id == draft.PeriodPlanSnapshotId)
            ?? throw new InvalidOperationException("Dönem planı bulunamadı.");
        var snapshot = history.Snapshots.Single(x =>
            x.Id == plan.FinancialSnapshotId);
        var revision = SelectFinalRevision(history, plan);
        var paymentLines = FinalPaymentLines(plan, revision);
        var actual = BuildActual(
            plan,
            snapshot,
            revision,
            paymentLines,
            draft,
            Guid.Empty,
            false);
        var comparison = comparisonCalculator.Calculate(
            plan,
            revision,
            actual);
        return new PeriodReviewPreview(
            actual.DerivedEndingBalance,
            actual.ConfirmedEndingBalance,
            actual.ReconciliationAdjustment,
            comparison);
    }

    public async Task<FinancialReviewResult> FinalizeAsync(
        FinancialPlan financialPlan,
        PeriodReviewDraft draft,
        CancellationToken cancellationToken = default)
    {
        var history = await store.GetFinancialHistoryAsync(cancellationToken);
        var current = FinancialSnapshotService.LatestCurrent(history)
            ?? throw new InvalidOperationException(
                "Güncel finansal durum bulunamadı.");
        var plan = history.Plans.SingleOrDefault(x =>
            x.Id == draft.PeriodPlanSnapshotId)
            ?? throw new InvalidOperationException("Dönem planı bulunamadı.");
        if (plan.FinancialSnapshotId != current.Id)
        {
            throw new InvalidOperationException(
                "Yalnızca güncel plan dönemi kapatılabilir.");
        }

        if (history.Actuals.Any(x =>
                x.PeriodPlanSnapshotId == plan.Id))
        {
            throw new InvalidOperationException(
                "Bu dönem daha önce kaydedildi.");
        }

        if (clock.Today < plan.SettlementAvailableFrom)
        {
            throw new InvalidOperationException(
                "Bu dönem henüz güncellenmeye hazır değil.");
        }

        var revision = SelectFinalRevision(history, plan);
        var paymentLines = FinalPaymentLines(plan, revision);
        var provisional = BuildActual(
            plan,
            current,
            revision,
            paymentLines,
            draft,
            Guid.Empty,
            true);
        var instruments = instrumentService.Apply(
            financialPlan,
            paymentLines,
            provisional.Payments,
            plan.SettlementAvailableFrom);
        var updatedPlan = financialPlan with
        {
            Loans = instruments.Loans,
            PaymentPlans = instruments.PaymentPlans,
            CreditCards = instruments.CreditCards,
            PlannedLargeExpenses = instruments.LargeExpenses,
            LoanPrepayments = financialPlan.LoanPrepayments
                .Where(x => !instruments.RemovedLoanPrepaymentIds.Contains(x.Id))
                .ToArray()
        };
        var newBundle = snapshotService.Build(
            updatedPlan,
            provisional.ConfirmedEndingBalance,
            plan.SettlementAvailableFrom,
            FinancialSnapshotSource.MonthlyUpdate,
            "Dönem güncellemesi",
            current.Id);
        var actual = provisional with
        {
            ResultFinancialSnapshotId = newBundle.Snapshot.Id
        };
        var comparison = comparisonCalculator.Calculate(
            plan,
            revision,
            actual);
        actual = actual with { ComparisonSummary = comparison.Summary };

        await store.FinalizeFinancialReviewAsync(
            new FinancialReviewCommit(
                null,
                actual,
                newBundle.Snapshot,
                newBundle.Plan,
                newBundle.UpdatedSettings,
                instruments.Loans,
                instruments.PaymentPlans,
                instruments.CreditCards,
                instruments.LargeExpenses,
                instruments.RemovedLoanPrepaymentIds),
            cancellationToken);

        return new FinancialReviewResult(
            newBundle.Snapshot,
            actual,
            comparison,
            newBundle.Plan);
    }

    private static PeriodPlanRevision? SelectFinalRevision(
        FinancialHistoryData history,
        PeriodPlanSnapshot plan) => history.Revisions
        .Where(x => x.PeriodPlanSnapshotId == plan.Id)
        .Where(x => IsValidForFinalPlan(plan, x))
        .OrderBy(x => x.CreatedAtUtc)
        .ThenBy(x => x.RevisionNumber)
        .LastOrDefault();

    private static bool IsValidForFinalPlan(
        PeriodPlanSnapshot plan,
        PeriodPlanRevision revision) =>
        DateOnly.FromDateTime(revision.CreatedAtUtc.UtcDateTime.Date) <=
        plan.SettlementAvailableFrom;

    private static IReadOnlyList<PeriodPlanPaymentLine> FinalPaymentLines(
        PeriodPlanSnapshot plan,
        PeriodPlanRevision? revision) =>
        revision?.PaymentLines.Count > 0
            ? revision.PaymentLines
            : plan.PaymentLines;

    private sealed record FinalPlanValues(
        decimal PlannedIncome,
        decimal PlannedVariableExpenseAllowance,
        decimal PlannedDeficitInterest)
    {
        public static FinalPlanValues From(
            PeriodPlanSnapshot plan,
            PeriodPlanRevision? revision) => revision is null
            ? new FinalPlanValues(
                plan.PlannedIncome,
                plan.PlannedVariableExpenseAllowance,
                plan.PlannedDeficitInterest)
            : new FinalPlanValues(
                revision.PlannedIncome,
                revision.PlannedVariableExpenseAllowance,
                revision.PlannedDeficitInterest);
    }
}
