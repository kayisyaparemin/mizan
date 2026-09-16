using CoinFlow.Application.Abstractions;
using CoinFlow.Application.Models;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Services;

/// <summary>
/// Finansal operasyonlar için merkezi ön cephe (Facade).
/// Sorumluluklar odaklı domain servislerine (<see cref="IFinancialPlanQueryService"/>,
/// <see cref="ISimulationWorkflowService"/>, <see cref="IPeriodWorkflowService"/>,
/// <see cref="IObligationManagementService"/>) devredilmiştir.
/// </summary>
public sealed partial class CoinFlowService(
    ICoinFlowStore store,
    IFinancialPlanQueryService queryService,
    ISimulationWorkflowService simulationService,
    IPeriodWorkflowService periodService,
    IObligationManagementService obligationService,
    HistoryQueryService historyService)
{
    public CoinFlowService(
        ICoinFlowStore store,
        IClock clock,
        FinancialProjectionService projectionService,
        SimulationCalculator simulationCalculator,
        TargetAmountCalculator targetAmountCalculator,
        PaymentAssignmentStrategyResolver strategyResolver,
        CreditCardPaymentPreferenceResolver paymentPreferenceResolver,
        SalaryPeriodCalculator salaryPeriodCalculator,
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
                salaryPeriodCalculator,
                projectionBoundaryResolver,
                strategyResolver,
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
                    salaryPeriodCalculator,
                    projectionBoundaryResolver,
                    strategyResolver,
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
                    salaryPeriodCalculator,
                    projectionBoundaryResolver,
                    strategyResolver,
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
                    salaryPeriodCalculator,
                    projectionBoundaryResolver,
                    strategyResolver,
                    snapshotService,
                    historicalPlanRevisionService,
                    loanPayoffAdvisor),
                snapshotService,
                salaryPeriodCalculator,
                strategyResolver,
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
                        salaryPeriodCalculator,
                        projectionBoundaryResolver,
                        strategyResolver,
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

    public Task<IReadOnlyList<SalaryPeriodProjection>> GetFuturePeriodsAsync(
        DateOnly? asOf = null,
        int periodCount = 12,
        decimal? monthlyLivingBudgetOverride = null,
        CancellationToken cancellationToken = default) =>
        queryService.GetFuturePeriodsAsync(
            asOf,
            periodCount,
            monthlyLivingBudgetOverride,
            cancellationToken);

    public Task<SimulationResult> SimulateAsync(
        SimulationRequest request,
        DateOnly? asOf = null,
        CancellationToken cancellationToken = default) =>
        simulationService.SimulateAsync(request, asOf, cancellationToken);

    public Task<SimulationResult> SimulateAsync(
        IReadOnlyList<SimulationRequest> requests,
        DateOnly? asOf = null,
        decimal? monthlyLivingBudgetOverride = null,
        CancellationToken cancellationToken = default) =>
        simulationService.SimulateAsync(
            requests,
            asOf,
            monthlyLivingBudgetOverride,
            cancellationToken);

    public Task<IReadOnlyList<LoanPayoffAdvice>> GetLoanPayoffAdviceAsync(
        CancellationToken cancellationToken = default) =>
        queryService.GetLoanPayoffAdviceAsync(cancellationToken);

    public Task<SalaryPeriodProjection?> FindTargetPeriodAsync(
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
        IReadOnlyList<SalaryPeriodProjection> periods,
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

    public Task<PaymentAssignmentStrategyOverview>
        GetPaymentAssignmentStrategyOverviewAsync(
            CancellationToken cancellationToken = default) =>
        queryService.GetPaymentAssignmentStrategyOverviewAsync(cancellationToken);

    public Task<PaymentStrategyChangePreview>
        PreviewPaymentAssignmentStrategyAsync(
            PaymentAssignmentMode newMode,
            DateOnly effectiveSalaryDate,
            CancellationToken cancellationToken = default) =>
        queryService.PreviewPaymentAssignmentStrategyAsync(
            newMode,
            effectiveSalaryDate,
            cancellationToken);
}
