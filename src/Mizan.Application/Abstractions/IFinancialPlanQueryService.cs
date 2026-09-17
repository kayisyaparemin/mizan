using Mizan.Application.Models;
using Mizan.Application.Services;
using Mizan.Domain.Calculations;
using Mizan.Domain.Models;

namespace Mizan.Application.Abstractions;

public interface IFinancialPlanQueryService
{
    Task<FinancialPlan> GetFinancialPlanAsync(
        CancellationToken cancellationToken = default);

    Task CapturePlanningChangeAsync(
        string trigger,
        CancellationToken cancellationToken = default);

    Task<ProjectionQueryPlan> GetProjectionPlanAsync(
        DateOnly asOf,
        CancellationToken cancellationToken = default);

    Task<DashboardSnapshot?> GetDashboardAsync(
        DateOnly? asOf = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CashFlowPeriodProjection>> GetFuturePeriodsAsync(
        DateOnly? asOf = null,
        int periodCount = 12,
        decimal? MonthlyVariableExpenseAllowanceOverride = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LoanPayoffAdvice>> GetLoanPayoffAdviceAsync(
        CancellationToken cancellationToken = default);

    Task<CashFlowPeriodProjection?> FindTargetPeriodAsync(
        decimal targetAmount,
        DateOnly? asOf = null,
        CancellationToken cancellationToken = default);

    Task<TargetReachabilityResult> FindTargetReachabilityAsync(
        decimal targetAmount,
        DateOnly? asOf = null,
        CancellationToken cancellationToken = default);

    TargetReachabilityResult FindTargetReachability(
        IReadOnlyList<CashFlowPeriodProjection> periods,
        decimal targetAmount);

    Task<CashFlowAllocationStrategyOverview> GetCashFlowAllocationStrategyOverviewAsync(
        CancellationToken cancellationToken = default);

    Task<PaymentAllocationChangePreview> PreviewCashFlowAllocationStrategyAsync(
        CashFlowAllocationMode newMode,
        DateOnly EffectivePeriodDate,
        CancellationToken cancellationToken = default);
}
