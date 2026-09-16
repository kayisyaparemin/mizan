using CoinFlow.Application.Models;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Abstractions;

public interface ISettingsRepository
{
    Task<UserSettings> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveSettingsAsync(
        UserSettings settings,
        CancellationToken cancellationToken = default);

    Task<PaymentReminderMode> GetPaymentReminderModeAsync(
        CancellationToken cancellationToken = default);
    Task SavePaymentReminderModeAsync(
        PaymentReminderMode mode,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaymentAssignmentStrategy>>
        GetPaymentAssignmentStrategiesAsync(
            CancellationToken cancellationToken = default);
    Task UpsertPaymentAssignmentStrategyAsync(
        PaymentAssignmentStrategy strategy,
        CancellationToken cancellationToken = default);
    Task DeletePaymentAssignmentStrategyAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
