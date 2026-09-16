using CoinFlow.Application.Models;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Abstractions;

public interface IPeriodWorkflowService
{
    Task<PeriodReviewAvailability> GetPeriodReviewAvailabilityAsync(
        CancellationToken cancellationToken = default);

    Task<PeriodReviewContext> GetPeriodReviewContextAsync(
        Guid? planId = null,
        CancellationToken cancellationToken = default);

    Task<PeriodReviewDraft?> GetObservedReviewDraftAsync(
        Guid periodPlanSnapshotId,
        CancellationToken cancellationToken = default);

    Task<PeriodReviewPreview> PreviewPeriodReviewAsync(
        PeriodReviewDraft draft,
        CancellationToken cancellationToken = default);

    Task<FinancialReviewResult> FinalizePeriodReviewAsync(
        PeriodReviewDraft draft,
        CancellationToken cancellationToken = default);

    Task<PeriodProgress?> GetPeriodProgressAsync(
        CancellationToken cancellationToken = default);

    Task<PaymentReminderMode> GetPaymentReminderModeAsync(
        CancellationToken cancellationToken = default);

    Task SavePaymentReminderModeAsync(
        PaymentReminderMode mode,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaymentReminder>> GetPaymentRemindersAsync(
        DateTime now,
        CancellationToken cancellationToken = default);

    Task<PaymentReminderBoard> GetPaymentReminderBoardAsync(
        DateTime now,
        CancellationToken cancellationToken = default);

    Task RecordPaymentReminderAnswerAsync(
        PaymentReminderAnswer answer,
        CancellationToken cancellationToken = default);

    Task UndoPaymentReminderAnswerAsync(
        string dueKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaymentReminderResponse>> GetPaymentReminderResponsesAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaymentDue>> GetUpcomingPaymentDuesAsync(
        DateTime now,
        CancellationToken cancellationToken = default);

    Task<PeriodObservation> ObserveCurrentBalanceAsync(
        decimal balance,
        CancellationToken cancellationToken = default);

    Task<PeriodObservation> ObservePaymentAsync(
        Guid periodPlanPaymentLineId,
        ActualPaymentStatus status,
        decimal actualAmount,
        DateOnly? actualPaymentDate = null,
        string note = "",
        CancellationToken cancellationToken = default);
}
