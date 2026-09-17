using Mizan.Domain.Models;

namespace Mizan.Application.Abstractions;

public interface IPaymentPlanRepository
{
    Task<IReadOnlyList<TemporaryPaymentPlan>> GetPaymentPlansAsync(
        CancellationToken cancellationToken = default);
    Task UpsertPaymentPlanAsync(
        TemporaryPaymentPlan plan,
        CancellationToken cancellationToken = default);
    Task DeletePaymentPlanAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
