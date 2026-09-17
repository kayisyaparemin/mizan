using Mizan.Application.Models;
using Mizan.Domain.Models;

namespace Mizan.Application.Abstractions;

public interface ISimulationRepository
{
    Task<IReadOnlyList<SimulationDraft>> GetSimulationDraftsAsync(
        CancellationToken cancellationToken = default);
    Task UpsertSimulationDraftAsync(
        SimulationDraft draft,
        CancellationToken cancellationToken = default);
    Task DeleteSimulationDraftAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task ApplySimulationBatchAsync(
        SimulationPersistenceBatch batch,
        CancellationToken cancellationToken = default);
}
