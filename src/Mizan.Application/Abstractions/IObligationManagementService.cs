using Mizan.Application.Models;
using Mizan.Domain.Models;

namespace Mizan.Application.Abstractions;

public interface IObligationManagementService
{
    Task<bool> IsOnboardingRequiredAsync(
        CancellationToken cancellationToken = default);

    Task InitializeFromOnboardingAsync(
        OnboardingDraft draft,
        CancellationToken cancellationToken = default);

    Task<InitialPaymentStrategySetup?> SaveSalaryAsync(
        SalaryScheduleEntry entry,
        CancellationToken cancellationToken = default);

    Task<InitialPaymentStrategySetup?> GetInitialPaymentStrategySetupAsync(
        CancellationToken cancellationToken = default);

    Task CompleteInitialPaymentStrategySetupAsync(
        CashFlowAllocationMode mode,
        CancellationToken cancellationToken = default);

    Task DeleteSalaryAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task SaveOtherIncomeAsync(
        OneTimeIncome income,
        CancellationToken cancellationToken = default);

    Task DeleteOtherIncomeAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task SaveLoanAsync(
        Loan loan,
        CancellationToken cancellationToken = default);

    Task DeleteLoanAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task DeleteLoanPrepaymentAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task SavePaymentPlanAsync(
        TemporaryPaymentPlan plan,
        CancellationToken cancellationToken = default);

    Task DeletePaymentPlanAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task SavePlannedLargeExpenseAsync(
        PlannedLargeExpense expense,
        CancellationToken cancellationToken = default);

    Task DeletePlannedLargeExpenseAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task SaveCreditCardAsync(
        CreditCard card,
        CancellationToken cancellationToken = default);

    Task SaveCreditCardStatementAsync(
        Guid creditCardId,
        CreditCardStatement statement,
        CurrentStatementPaymentPlan paymentPlan,
        CancellationToken cancellationToken = default);

    Task DeleteCreditCardAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task SaveCreditCardPaymentPlanAsync(
        Guid creditCardId,
        DateOnly dueDate,
        CreditCardPaymentType paymentType,
        decimal? amount = null,
        CancellationToken cancellationToken = default);

    Task SetStatementPaymentModeAsync(
        Guid creditCardId,
        DateOnly dueDate,
        CreditCardPaymentType paymentType,
        CancellationToken cancellationToken = default);

    Task RemoveCreditCardPaymentPlanAsync(
        Guid creditCardId,
        DateOnly dueDate,
        CancellationToken cancellationToken = default);

    Task SaveSettingsAsync(
        UserSettings settings,
        CancellationToken cancellationToken = default);

    Task SaveCashFlowAllocationStrategyAsync(
        CashFlowAllocationStrategy strategy,
        bool confirmedHistoricalCorrection = false,
        CancellationToken cancellationToken = default);

    Task DeleteCashFlowAllocationStrategyAsync(
        Guid id,
        bool confirmedHistoricalCorrection = false,
        CancellationToken cancellationToken = default);

    Task<FinancialSnapshot> RefreshCurrentFinancialStateAsync(
        decimal startingSavings,
        string note = "",
        CancellationToken cancellationToken = default);
}
