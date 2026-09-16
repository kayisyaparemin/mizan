using CoinFlow.Application.Models;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Services;

public sealed partial class ObligationManagementService
{
    public async Task<InitialPaymentStrategySetup?> SaveSalaryAsync(
        SalaryScheduleEntry entry,
        CancellationToken cancellationToken = default)
    {
        if (entry.Amount <= 0m)
        {
            throw new InvalidOperationException(
                "Gelir tutarı sıfırdan büyük olmalıdır.");
        }

        await store.UpsertSalaryAsync(entry, cancellationToken);
        await queryService.CapturePlanningChangeAsync(
            "Gelir planı değişti",
            cancellationToken);
        return await GetInitialPaymentStrategySetupAsync(cancellationToken);
    }

    public async Task<InitialPaymentStrategySetup?>
        GetInitialPaymentStrategySetupAsync(
            CancellationToken cancellationToken = default)
    {
        var plan = await queryService.GetFinancialPlanAsync(cancellationToken);
        if (plan.Salaries.Count == 0 ||
            plan.PaymentAssignmentStrategies.Count > 0)
        {
            return null;
        }

        var settings = plan.Settings;
        var anchor = settings.ProjectionAnchorDate;
        if (anchor == default)
        {
            anchor = clock.Today;
            settings = settings with { ProjectionAnchorDate = anchor };
            await store.SaveSettingsAsync(settings, cancellationToken);
        }

        var effectiveSalary = salaryPeriodCalculator
            .GetFirstSalaryOnOrAfter(anchor, settings.SalaryDay);
        var exampleSalary = CalendarRules.AddMonthsKeepingDay(
            effectiveSalary,
            1,
            settings.SalaryDay);
        return new InitialPaymentStrategySetup(
            anchor,
            effectiveSalary,
            exampleSalary,
            effectiveSalary,
            CalendarRules.AddMonthsKeepingDay(
                exampleSalary,
                1,
                settings.SalaryDay));
    }

    public async Task CompleteInitialPaymentStrategySetupAsync(
        PaymentAssignmentMode mode,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new InvalidOperationException(
                "Gelir kullanım düzeni geçersiz.");
        }

        var setup = await GetInitialPaymentStrategySetupAsync(
            cancellationToken) ?? throw new InvalidOperationException(
                "İlk gelir kullanım düzeni kurulumu gerekli değil veya zaten tamamlandı.");
        await store.UpsertPaymentAssignmentStrategyAsync(
            new PaymentAssignmentStrategy
            {
                Mode = mode,
                EffectiveFromSalaryDate = setup.EffectiveSalaryDate,
                CreatedAt = clock.UtcNow,
                Note = "İlk gelir kullanım düzeni"
            },
            cancellationToken);
        await queryService.GetFinancialPlanAsync(cancellationToken);
    }

    public async Task DeleteSalaryAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await store.DeleteSalaryAsync(id, cancellationToken);
        await queryService.CapturePlanningChangeAsync(
            "Gelir planı değişti",
            cancellationToken);
    }

    public async Task SaveOtherIncomeAsync(
        OneTimeIncome income,
        CancellationToken cancellationToken = default)
    {
        if (income.Amount <= 0m)
        {
            throw new InvalidOperationException(
                "Gelir tutarı sıfırdan büyük olmalıdır.");
        }

        await store.UpsertOtherIncomeAsync(income, cancellationToken);
        await queryService.CapturePlanningChangeAsync(
            "Gelir planı değişti",
            cancellationToken);
    }

    public async Task DeleteOtherIncomeAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await store.DeleteOtherIncomeAsync(id, cancellationToken);
        await queryService.CapturePlanningChangeAsync(
            "Gelir planı değişti",
            cancellationToken);
    }

    public async Task SaveLoanAsync(
        Loan loan,
        CancellationToken cancellationToken = default)
    {
        if (loan.MonthlyPayment <= 0m ||
            loan.RemainingInstallmentCount < 1)
        {
            throw new InvalidOperationException(
                "Kredi taksiti ve kalan taksit sayısı pozitif olmalıdır.");
        }

        CalendarRules.ValidateDay(loan.PaymentDay);
        loan = loanPayoffService.PrepareForSave(loan);
        await store.UpsertLoanAsync(loan, cancellationToken);
        await queryService.CapturePlanningChangeAsync(
            "Kredi planı değişti",
            cancellationToken);
    }

    public async Task DeleteLoanAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await store.DeleteLoanAsync(id, cancellationToken);
        await queryService.CapturePlanningChangeAsync(
            "Kredi planı değişti",
            cancellationToken);
    }

    public async Task DeleteLoanPrepaymentAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await store.DeleteLoanPrepaymentAsync(id, cancellationToken);
        await queryService.CapturePlanningChangeAsync(
            "Kredi planı değişti",
            cancellationToken);
    }

    public async Task SavePaymentPlanAsync(
        TemporaryPaymentPlan plan,
        CancellationToken cancellationToken = default)
    {
        if (plan.Installments.Count == 0 ||
            plan.Installments.Any(x => x.Amount <= 0m))
        {
            throw new InvalidOperationException(
                "Ödeme planında en az bir pozitif ödeme olmalıdır.");
        }

        await store.UpsertPaymentPlanAsync(plan, cancellationToken);
        await queryService.CapturePlanningChangeAsync(
            "Planlı ödeme değişti",
            cancellationToken);
    }

    public async Task DeletePaymentPlanAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await store.DeletePaymentPlanAsync(id, cancellationToken);
        await queryService.CapturePlanningChangeAsync(
            "Planlı ödeme değişti",
            cancellationToken);
    }

    public async Task SavePlannedLargeExpenseAsync(
        PlannedLargeExpense expense,
        CancellationToken cancellationToken = default)
    {
        if (expense.Amount <= 0m)
        {
            throw new InvalidOperationException(
                "Planlı büyük ödeme tutarı 0'dan büyük olmalı.");
        }

        await store.UpsertPlannedLargeExpenseAsync(
            expense,
            cancellationToken);
        await queryService.CapturePlanningChangeAsync(
            "Büyük ödeme planı değişti",
            cancellationToken);
    }

    public async Task DeletePlannedLargeExpenseAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await store.DeletePlannedLargeExpenseAsync(id, cancellationToken);
        await queryService.CapturePlanningChangeAsync(
            "Büyük ödeme planı değişti",
            cancellationToken);
    }
}
