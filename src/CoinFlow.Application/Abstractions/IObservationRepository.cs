using CoinFlow.Application.Models;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Abstractions;

public interface IObservationRepository
{
    Task<PeriodObservation?> GetPeriodObservationAsync(
        Guid periodPlanSnapshotId,
        CancellationToken cancellationToken = default);
    Task UpsertPeriodObservationAsync(
        PeriodObservation observation,
        CancellationToken cancellationToken = default);
    Task DeletePeriodObservationAsync(
        Guid periodPlanSnapshotId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaymentReminderResponse>> GetPaymentReminderResponsesAsync(
        CancellationToken cancellationToken = default);
    Task UpsertPaymentReminderResponsesAsync(
        IReadOnlyList<PaymentReminderResponse> responses,
        CancellationToken cancellationToken = default);
    Task DeletePaymentReminderResponseAsync(
        string dueKey,
        CancellationToken cancellationToken = default);
}
