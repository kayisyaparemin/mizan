using Mizan.Application.Abstractions;
using Mizan.Application.Models;
using Mizan.Domain.Calculations;
using Mizan.Domain.Models;

namespace Mizan.Application.Services;

/// <summary>
/// Finansal operasyonlar için merkezi ön cephe (Facade).
/// Sorumluluklar odaklı domain servislerine (<see cref="IFinancialPlanQueryService"/>,
/// <see cref="ISimulationWorkflowService"/>, <see cref="IPeriodWorkflowService"/>,
/// <see cref="IObligationManagementService"/>) devredilmiştir.
/// </summary>
public sealed partial class MizanService(
    IMizanStore store,
    IFinancialPlanQueryService queryService,
    ISimulationWorkflowService simulationService,
    IPeriodWorkflowService periodService,
    IObligationManagementService obligationService,
    HistoryQueryService historyService)
{
    public MizanService(
        IMizanStore store,
        IClock clock,
        FinancialProjectionService projectionService,
        SimulationCalculator simulationCalculator,
        TargetAmountCalculator targetAmountCalculator,
        PaymentAllocationStrategyResolver allocationResolver,
        CreditCardPaymentPreferenceResolver paymentPreferenceResolver,
        CashFlowPeriodCalculator CashFlowPeriodCalculator,
        ProjectionBoundaryResolver projectionBoundaryResolver,
        FinancialSnapshotService snapshotService,
        HistoricalPlanRevisionService historicalPlanRevisionService,
        PeriodReviewService reviewService,
        PeriodProgressService periodProgressService,
        HistoryQueryService historyService,
        LoanPayoffService loanPayoffService,
        LoanPayoffAdvisor loanPayoffAdvisor) : this(
            store,
            new FinancialPlanQueryService(
                store,
                clock,
                projectionService,
                simulationCalculator,
                targetAmountCalculator,
                CashFlowPeriodCalculator,
                projectionBoundaryResolver,
                allocationResolver,
                snapshotService,
                historicalPlanRevisionService,
                loanPayoffAdvisor),
            new SimulationWorkflowService(
                store,
                clock,
                simulationCalculator,
                new FinancialPlanQueryService(
                    store,
                    clock,
                    projectionService,
                    simulationCalculator,
                    targetAmountCalculator,
                    CashFlowPeriodCalculator,
                    projectionBoundaryResolver,
                    allocationResolver,
                    snapshotService,
                    historicalPlanRevisionService,
                    loanPayoffAdvisor)),
            new PeriodWorkflowService(
                store,
                clock,
                reviewService,
                periodProgressService,
                new FinancialPlanQueryService(
                    store,
                    clock,
                    projectionService,
                    simulationCalculator,
                    targetAmountCalculator,
                    CashFlowPeriodCalculator,
                    projectionBoundaryResolver,
                    allocationResolver,
                    snapshotService,
                    historicalPlanRevisionService,
                    loanPayoffAdvisor)),
            new ObligationManagementService(
                store,
                clock,
                new FinancialPlanQueryService(
                    store,
                    clock,
                    projectionService,
                    simulationCalculator,
                    targetAmountCalculator,
                    CashFlowPeriodCalculator,
                    projectionBoundaryResolver,
                    allocationResolver,
                    snapshotService,
                    historicalPlanRevisionService,
                    loanPayoffAdvisor),
                snapshotService,
                CashFlowPeriodCalculator,
                allocationResolver,
                loanPayoffService,
                new CreditCardObligationService(
                    store,
                    clock,
                    paymentPreferenceResolver,
                    new FinancialPlanQueryService(
                        store,
                        clock,
                        projectionService,
                        simulationCalculator,
                        targetAmountCalculator,
                        CashFlowPeriodCalculator,
                        projectionBoundaryResolver,
                        allocationResolver,
                        snapshotService,
                        historicalPlanRevisionService,
                        loanPayoffAdvisor))),
            historyService)
    {
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        store.InitializeAsync(cancellationToken);

    public Task ClearDevelopmentDataAsync(
        CancellationToken cancellationToken = default) =>
        store.ClearAllFinancialDataAsync(cancellationToken);

    public Task LoadCanonicalDevelopmentDataAsync(
        CancellationToken cancellationToken = default) =>
        store.LoadCanonicalDevelopmentDataAsync(cancellationToken);

    public Task<bool> IsOnboardingRequiredAsync(
        CancellationToken cancellationToken = default) =>
        obligationService.IsOnboardingRequiredAsync(cancellationToken);

    public Task InitializeFromOnboardingAsync(
        OnboardingDraft draft,
        CancellationToken cancellationToken = default) =>
        obligationService.InitializeFromOnboardingAsync(draft, cancellationToken);

    public Task<FinancialPlan> GetFinancialPlanAsync(
        CancellationToken cancellationToken = default) =>
        queryService.GetFinancialPlanAsync(cancellationToken);

    public Task<DashboardSnapshot?> GetDashboardAsync(
        DateOnly? asOf = null,
        CancellationToken cancellationToken = default) =>
        queryService.GetDashboardAsync(asOf, cancellationToken);

    public Task<IReadOnlyList<CashFlowPeriodProjection>> GetFuturePeriodsAsync(
        DateOnly? asOf = null,
        int periodCount = 12,
        decimal? MonthlyVariableExpenseAllowanceOverride = null,
        CancellationToken cancellationToken = default) =>
        queryService.GetFuturePeriodsAsync(
            asOf,
            periodCount,
            MonthlyVariableExpenseAllowanceOverride,
            cancellationToken);

    public Task<SimulationResult> SimulateAsync(
        SimulationRequest request,
        DateOnly? asOf = null,
        CancellationToken cancellationToken = default) =>
        simulationService.SimulateAsync(request, asOf, cancellationToken);

    public Task<SimulationResult> SimulateAsync(
        IReadOnlyList<SimulationRequest> requests,
        DateOnly? asOf = null,
        decimal? MonthlyVariableExpenseAllowanceOverride = null,
        CancellationToken cancellationToken = default) =>
        simulationService.SimulateAsync(
            requests,
            asOf,
            MonthlyVariableExpenseAllowanceOverride,
            cancellationToken);

    public Task<IReadOnlyList<LoanPayoffAdvice>> GetLoanPayoffAdviceAsync(
        CancellationToken cancellationToken = default) =>
        queryService.GetLoanPayoffAdviceAsync(cancellationToken);

    public Task<CashFlowPeriodProjection?> FindTargetPeriodAsync(
        decimal targetAmount,
        DateOnly? asOf = null,
        CancellationToken cancellationToken = default) =>
        queryService.FindTargetPeriodAsync(targetAmount, asOf, cancellationToken);

    public Task<TargetReachabilityResult> FindTargetReachabilityAsync(
        decimal targetAmount,
        DateOnly? asOf = null,
        CancellationToken cancellationToken = default) =>
        queryService.FindTargetReachabilityAsync(targetAmount, asOf, cancellationToken);

    public TargetReachabilityResult FindTargetReachability(
        IReadOnlyList<CashFlowPeriodProjection> periods,
        decimal targetAmount) =>
        queryService.FindTargetReachability(periods, targetAmount);

    public Task<SimulationDraft> SaveSimulationDraftAsync(
        string name,
        IReadOnlyList<SimulationDraftCondition> conditions,
        Guid? draftId = null,
        CancellationToken cancellationToken = default) =>
        simulationService.SaveSimulationDraftAsync(
            name,
            conditions,
            draftId,
            cancellationToken);

    public Task<IReadOnlyList<SimulationDraft>> GetSimulationDraftsAsync(
        CancellationToken cancellationToken = default) =>
        simulationService.GetSimulationDraftsAsync(cancellationToken);

    public Task DeleteSimulationDraftAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        simulationService.DeleteSimulationDraftAsync(id, cancellationToken);

    public Task<SimulationApplyResult> ApplySimulationAsync(
        SimulationRequest request,
        bool confirmed,
        CancellationToken cancellationToken = default) =>
        simulationService.ApplySimulationAsync(request, confirmed, cancellationToken);

    public Task<SimulationApplyResult> ApplySimulationAsync(
        IReadOnlyList<SimulationRequest> requests,
        bool confirmed,
        CancellationToken cancellationToken = default) =>
        simulationService.ApplySimulationAsync(requests, confirmed, cancellationToken);

    public Task<SimulationApplyResult> AddRecordFromScenarioAsync(
        SimulationRequest request,
        CancellationToken cancellationToken = default) =>
        simulationService.AddRecordFromScenarioAsync(request, cancellationToken);

    public Task<CashFlowAllocationStrategyOverview>
        GetCashFlowAllocationStrategyOverviewAsync(
            CancellationToken cancellationToken = default) =>
        queryService.GetCashFlowAllocationStrategyOverviewAsync(cancellationToken);

    public Task<PaymentAllocationChangePreview>
        PreviewCashFlowAllocationStrategyAsync(
            CashFlowAllocationMode newMode,
            DateOnly EffectivePeriodDate,
            CancellationToken cancellationToken = default) =>
        queryService.PreviewCashFlowAllocationStrategyAsync(
            newMode,
            EffectivePeriodDate,
            cancellationToken);
}
