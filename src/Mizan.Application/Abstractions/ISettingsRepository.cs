using Mizan.Application.Models;
using Mizan.Domain.Models;

namespace Mizan.Application.Abstractions;

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

    Task<IReadOnlyList<CashFlowAllocationStrategy>>
        GetPaymentAssignmentStrategiesAsync(
            CancellationToken cancellationToken = default);
    Task UpsertCashFlowAllocationStrategyAsync(
        CashFlowAllocationStrategy strategy,
        CancellationToken cancellationToken = default);
    Task DeleteCashFlowAllocationStrategyAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
