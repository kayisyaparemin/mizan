using CoinFlow.Application.Models;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Abstractions;

public interface ISimulationWorkflowService
{
    Task<SimulationResult> SimulateAsync(
        SimulationRequest request,
        DateOnly? asOf = null,
        CancellationToken cancellationToken = default);

    Task<SimulationResult> SimulateAsync(
        IReadOnlyList<SimulationRequest> requests,
        DateOnly? asOf = null,
        decimal? monthlyLivingBudgetOverride = null,
        CancellationToken cancellationToken = default);

    Task<SimulationDraft> SaveSimulationDraftAsync(
        string name,
        IReadOnlyList<SimulationDraftCondition> conditions,
        Guid? draftId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SimulationDraft>> GetSimulationDraftsAsync(
        CancellationToken cancellationToken = default);

    Task DeleteSimulationDraftAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<SimulationApplyResult> ApplySimulationAsync(
        SimulationRequest request,
        bool confirmed,
        CancellationToken cancellationToken = default);

    Task<SimulationApplyResult> ApplySimulationAsync(
        IReadOnlyList<SimulationRequest> requests,
        bool confirmed,
        CancellationToken cancellationToken = default);

    Task<SimulationApplyResult> AddRecordFromScenarioAsync(
        SimulationRequest request,
        CancellationToken cancellationToken = default);
}
