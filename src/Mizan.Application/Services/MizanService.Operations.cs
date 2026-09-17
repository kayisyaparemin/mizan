using Mizan.Application.Models;
using Mizan.Domain.Models;

namespace Mizan.Application.Services;

public sealed partial class MizanService
{
    public Task<InitialPaymentStrategySetup?> SaveSalaryAsync(
        SalaryScheduleEntry entry,
        CancellationToken cancellationToken = default) =>
        obligationService.SaveSalaryAsync(entry, cancellationToken);

    public Task<InitialPaymentStrategySetup?> GetInitialPaymentStrategySetupAsync(
        CancellationToken cancellationToken = default) =>
        obligationService.GetInitialPaymentStrategySetupAsync(cancellationToken);

    public Task CompleteInitialPaymentStrategySetupAsync(
        CashFlowAllocationMode mode,
        CancellationToken cancellationToken = default) =>
        obligationService.CompleteInitialPaymentStrategySetupAsync(mode, cancellationToken);

    public Task DeleteSalaryAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        obligationService.DeleteSalaryAsync(id, cancellationToken);

    public Task SaveOtherIncomeAsync(
        OneTimeIncome income,
        CancellationToken cancellationToken = default) =>
        obligationService.SaveOtherIncomeAsync(income, cancellationToken);

    public Task DeleteOtherIncomeAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        obligationService.DeleteOtherIncomeAsync(id, cancellationToken);

    public Task SaveLoanAsync(
        Loan loan,
        CancellationToken cancellationToken = default) =>
        obligationService.SaveLoanAsync(loan, cancellationToken);

    public Task DeleteLoanAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        obligationService.DeleteLoanAsync(id, cancellationToken);

    public Task DeleteLoanPrepaymentAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        obligationService.DeleteLoanPrepaymentAsync(id, cancellationToken);

    public Task SavePaymentPlanAsync(
        TemporaryPaymentPlan plan,
        CancellationToken cancellationToken = default) =>
        obligationService.SavePaymentPlanAsync(plan, cancellationToken);

    public Task DeletePaymentPlanAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        obligationService.DeletePaymentPlanAsync(id, cancellationToken);

    public Task SavePlannedLargeExpenseAsync(
        PlannedLargeExpense expense,
        CancellationToken cancellationToken = default) =>
        obligationService.SavePlannedLargeExpenseAsync(expense, cancellationToken);

    public Task DeletePlannedLargeExpenseAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        obligationService.DeletePlannedLargeExpenseAsync(id, cancellationToken);

    public Task SaveCreditCardAsync(
        CreditCard card,
        CancellationToken cancellationToken = default) =>
        obligationService.SaveCreditCardAsync(card, cancellationToken);

    public Task SaveCreditCardStatementAsync(
        Guid creditCardId,
        CreditCardStatement statement,
        CurrentStatementPaymentPlan paymentPlan,
        CancellationToken cancellationToken = default) =>
        obligationService.SaveCreditCardStatementAsync(
            creditCardId,
            statement,
            paymentPlan,
            cancellationToken);

    public Task DeleteCreditCardAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        obligationService.DeleteCreditCardAsync(id, cancellationToken);

    public Task SaveCreditCardPaymentPlanAsync(
        Guid creditCardId,
        DateOnly dueDate,
        CreditCardPaymentType paymentType,
        decimal? amount = null,
        CancellationToken cancellationToken = default) =>
        obligationService.SaveCreditCardPaymentPlanAsync(
            creditCardId,
            dueDate,
            paymentType,
            amount,
            cancellationToken);

    public Task SetStatementPaymentModeAsync(
        Guid creditCardId,
        DateOnly dueDate,
        CreditCardPaymentType paymentType,
        CancellationToken cancellationToken = default) =>
        obligationService.SetStatementPaymentModeAsync(
            creditCardId,
            dueDate,
            paymentType,
            cancellationToken);

    public Task RemoveCreditCardPaymentPlanAsync(
        Guid creditCardId,
        DateOnly dueDate,
        CreditCardPaymentType? paymentType = null,
        CancellationToken cancellationToken = default) =>
        obligationService.RemoveCreditCardPaymentPlanAsync(
            creditCardId,
            dueDate,
            cancellationToken);

    public Task SaveSettingsAsync(
        UserSettings settings,
        CancellationToken cancellationToken = default) =>
        obligationService.SaveSettingsAsync(settings, cancellationToken);

    public Task SaveCashFlowAllocationStrategyAsync(
        CashFlowAllocationStrategy strategy,
        bool confirmedHistoricalCorrection = false,
        CancellationToken cancellationToken = default) =>
        obligationService.SaveCashFlowAllocationStrategyAsync(
            strategy,
            confirmedHistoricalCorrection,
            cancellationToken);

    public Task DeleteCashFlowAllocationStrategyAsync(
        Guid id,
        bool confirmedHistoricalCorrection = false,
        CancellationToken cancellationToken = default) =>
        obligationService.DeleteCashFlowAllocationStrategyAsync(
            id,
            confirmedHistoricalCorrection,
            cancellationToken);

    public Task<FinancialSnapshot> RefreshCurrentFinancialStateAsync(
        decimal startingSavings,
        string note = "",
        CancellationToken cancellationToken = default) =>
        obligationService.RefreshCurrentFinancialStateAsync(
            startingSavings,
            note,
            cancellationToken);

    public Task<PeriodReviewAvailability> GetPeriodReviewAvailabilityAsync(
        CancellationToken cancellationToken = default) =>
        periodService.GetPeriodReviewAvailabilityAsync(cancellationToken);

    public Task<PeriodReviewContext> GetPeriodReviewContextAsync(
        Guid? planId = null,
        CancellationToken cancellationToken = default) =>
        periodService.GetPeriodReviewContextAsync(planId, cancellationToken);

    public Task<PeriodReviewDraft?> GetObservedReviewDraftAsync(
        Guid periodPlanSnapshotId,
        CancellationToken cancellationToken = default) =>
        periodService.GetObservedReviewDraftAsync(
            periodPlanSnapshotId,
            cancellationToken);

    public Task<PeriodReviewPreview> PreviewPeriodReviewAsync(
        PeriodReviewDraft draft,
        CancellationToken cancellationToken = default) =>
        periodService.PreviewPeriodReviewAsync(draft, cancellationToken);

    public Task<FinancialReviewResult> FinalizePeriodReviewAsync(
        PeriodReviewDraft draft,
        CancellationToken cancellationToken = default) =>
        periodService.FinalizePeriodReviewAsync(draft, cancellationToken);

    public Task<PeriodProgress?> GetPeriodProgressAsync(
        CancellationToken cancellationToken = default) =>
        periodService.GetPeriodProgressAsync(cancellationToken);

    public Task<PaymentReminderMode> GetPaymentReminderModeAsync(
        CancellationToken cancellationToken = default) =>
        periodService.GetPaymentReminderModeAsync(cancellationToken);

    public Task SavePaymentReminderModeAsync(
        PaymentReminderMode mode,
        CancellationToken cancellationToken = default) =>
        periodService.SavePaymentReminderModeAsync(mode, cancellationToken);

    public Task<IReadOnlyList<PaymentReminder>> GetPaymentRemindersAsync(
        DateTime now,
        CancellationToken cancellationToken = default) =>
        periodService.GetPaymentRemindersAsync(now, cancellationToken);

    public Task<PaymentReminderBoard> GetPaymentReminderBoardAsync(
        DateTime now,
        CancellationToken cancellationToken = default) =>
        periodService.GetPaymentReminderBoardAsync(now, cancellationToken);

    public Task RecordPaymentReminderAnswerAsync(
        PaymentReminderAnswer answer,
        CancellationToken cancellationToken = default) =>
        periodService.RecordPaymentReminderAnswerAsync(answer, cancellationToken);

    public Task UndoPaymentReminderAnswerAsync(
        string dueKey,
        CancellationToken cancellationToken = default) =>
        periodService.UndoPaymentReminderAnswerAsync(dueKey, cancellationToken);

    public Task<IReadOnlyList<PaymentReminderResponse>> GetPaymentReminderResponsesAsync(
        CancellationToken cancellationToken = default) =>
        periodService.GetPaymentReminderResponsesAsync(cancellationToken);

    public Task<IReadOnlyList<PaymentDue>> GetUpcomingPaymentDuesAsync(
        DateTime now,
        CancellationToken cancellationToken = default) =>
        periodService.GetUpcomingPaymentDuesAsync(now, cancellationToken);

    public Task<PeriodObservation> ObserveCurrentBalanceAsync(
        decimal balance,
        CancellationToken cancellationToken = default) =>
        periodService.ObserveCurrentBalanceAsync(balance, cancellationToken);

    public Task<PeriodObservation> ObservePaymentAsync(
        Guid periodPlanPaymentLineId,
        ActualPaymentStatus status,
        decimal actualAmount,
        DateOnly? actualPaymentDate = null,
        string note = "",
        CancellationToken cancellationToken = default) =>
        periodService.ObservePaymentAsync(
            periodPlanPaymentLineId,
            status,
            actualAmount,
            actualPaymentDate,
            note,
            cancellationToken);

    public Task<IReadOnlyList<HistoryPeriod>> GetHistoryPeriodsAsync(
        CancellationToken cancellationToken = default) =>
        historyService.GetPeriodsAsync(cancellationToken);

    public Task<HistoryPeriod> GetHistoryPeriodAsync(
        Guid actualId,
        CancellationToken cancellationToken = default) =>
        historyService.GetPeriodAsync(actualId, cancellationToken);

    public Task<HistorySummary?> GetHistorySummaryAsync(
        int periodCount = 3,
        CancellationToken cancellationToken = default) =>
        historyService.GetRecentSummaryAsync(
            periodCount,
            cancellationToken);
}
