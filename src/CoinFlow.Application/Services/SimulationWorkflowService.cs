using CoinFlow.Application.Abstractions;
using CoinFlow.Application.Models;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Services;

public sealed class SimulationWorkflowService(
    ICoinFlowStore store,
    IClock clock,
    SimulationCalculator simulationCalculator,
    IFinancialPlanQueryService queryService) : ISimulationWorkflowService
{
    public async Task<SimulationResult> SimulateAsync(
        SimulationRequest request,
        DateOnly? asOf = null,
        CancellationToken cancellationToken = default) =>
        await SimulateAsync(
            [request],
            asOf,
            cancellationToken: cancellationToken);

    public async Task<SimulationResult> SimulateAsync(
        IReadOnlyList<SimulationRequest> requests,
        DateOnly? asOf = null,
        decimal? monthlyLivingBudgetOverride = null,
        CancellationToken cancellationToken = default)
    {
        var date = asOf ?? clock.Today;
        var query = await queryService.GetProjectionPlanAsync(date, cancellationToken);
        if (!FinancialPlanQueryService.CanBuildProjection(query.Plan))
        {
            throw new InvalidOperationException(
                "Simülasyon yapabilmek için önce gelirini ve gelir kullanım düzenini oluştur.");
        }

        return simulationCalculator.Calculate(
            FinancialPlanQueryService.ApplyLivingBudgetOverride(
                query.Plan,
                monthlyLivingBudgetOverride),
            date,
            requests,
            firstSalaryDate: query.Boundary?.FirstUnrealizedSalaryDate);
    }

    public async Task<SimulationDraft> SaveSimulationDraftAsync(
        string name,
        IReadOnlyList<SimulationDraftCondition> conditions,
        Guid? draftId = null,
        CancellationToken cancellationToken = default)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new InvalidOperationException(
                "Geçici plana bir ad vermelisin.");
        }

        if (conditions.Count == 0)
        {
            throw new InvalidOperationException(
                "Kaydedilecek en az bir koşul gerekiyor.");
        }

        var existing = draftId is { } id
            ? (await store.GetSimulationDraftsAsync(cancellationToken))
                .FirstOrDefault(x => x.Id == id)
            : null;
        var draft = new SimulationDraft(
            existing?.Id ?? Guid.NewGuid(),
            trimmed,
            existing?.CreatedAt ?? clock.UtcNow,
            clock.UtcNow,
            conditions);
        await store.UpsertSimulationDraftAsync(draft, cancellationToken);
        return draft;
    }

    public Task<IReadOnlyList<SimulationDraft>> GetSimulationDraftsAsync(
        CancellationToken cancellationToken = default) =>
        store.GetSimulationDraftsAsync(cancellationToken);

    public Task DeleteSimulationDraftAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        store.DeleteSimulationDraftAsync(id, cancellationToken);

    public async Task<SimulationApplyResult> ApplySimulationAsync(
        SimulationRequest request,
        bool confirmed,
        CancellationToken cancellationToken = default) =>
        await ApplySimulationAsync(
            [request],
            confirmed,
            cancellationToken);

    public async Task<SimulationApplyResult> ApplySimulationAsync(
        IReadOnlyList<SimulationRequest> requests,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        if (!confirmed)
        {
            throw new InvalidOperationException(
                "Plan, açık kullanıcı onayı olmadan uygulanamaz.");
        }

        return await ApplyScenarioRequestsAsync(
            requests,
            "Simülasyon planı uygulandı",
            cancellationToken);
    }

    public async Task<SimulationApplyResult> AddRecordFromScenarioAsync(
        SimulationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!SimulationScenarioCatalog.IsDirectEntry(request.Type))
        {
            throw new InvalidOperationException(
                $"{SimulationScenarioCatalog.TypeText(request.Type)} Finansal Yapı'dan bu formla girilemez.");
        }

        var anchor = (await store.GetSettingsAsync(cancellationToken))
            .ProjectionAnchorDate;
        SimulationCalculator.Validate(
            request,
            anchor == default ? null : anchor);
        return await ApplyScenarioRequestsAsync(
            [request],
            "Finansal Yapı'dan eklendi",
            cancellationToken);
    }

    private async Task<SimulationApplyResult> ApplyScenarioRequestsAsync(
        IReadOnlyList<SimulationRequest> requests,
        string trigger,
        CancellationToken cancellationToken)
    {
        SimulationCalculator.Validate(requests);
        if (requests.Any(x => x.ScenarioId == Guid.Empty))
        {
            throw new InvalidOperationException(
                "Uygulanacak simülasyon kimliği bulunamadı. Planı yeniden simüle edin.");
        }

        var current = await queryService.GetFinancialPlanAsync(cancellationToken);
        var existingResults = requests
            .Select(request => SimulationPersistenceBatchBuilder.FindAppliedSimulation(current, request))
            .ToArray();
        if (existingResults.All(x => x is not null))
        {
            var first = existingResults[0]!;
            return first with
            {
                AlreadyApplied = true,
                Message = requests.Count == 1
                    ? first.Message
                    : "Bu simülasyon planı daha önce finans planına eklendi."
            };
        }

        if (existingResults.Any(x => x is not null))
        {
            throw new InvalidOperationException(
                "Bu simülasyon planının bir kısmı daha önce uygulanmış. Tekrar kaydı önlemek için planı temizleyip yeniden oluştur.");
        }

        SimulationPersistenceBatchBuilder.ValidateSimulationApplyConflicts(current, requests);

        var scenario = simulationCalculator.BuildScenarioPlan(current, requests);
        var batch = SimulationPersistenceBatchBuilder.Build(scenario, requests);
        await store.ApplySimulationBatchAsync(batch, cancellationToken);

        await queryService.CapturePlanningChangeAsync(trigger, cancellationToken);
        return SimulationPersistenceBatchBuilder.AppliedResult(requests, batch);
    }
}
