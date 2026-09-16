using CoinFlow.Application.Models;
using CoinFlow.Application.Services;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Abstractions;

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

    Task<IReadOnlyList<SalaryPeriodProjection>> GetFuturePeriodsAsync(
        DateOnly? asOf = null,
        int periodCount = 12,
        decimal? monthlyLivingBudgetOverride = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LoanPayoffAdvice>> GetLoanPayoffAdviceAsync(
        CancellationToken cancellationToken = default);

    Task<SalaryPeriodProjection?> FindTargetPeriodAsync(
        decimal targetAmount,
        DateOnly? asOf = null,
        CancellationToken cancellationToken = default);

    Task<TargetReachabilityResult> FindTargetReachabilityAsync(
        decimal targetAmount,
        DateOnly? asOf = null,
        CancellationToken cancellationToken = default);

    TargetReachabilityResult FindTargetReachability(
        IReadOnlyList<SalaryPeriodProjection> periods,
        decimal targetAmount);

    Task<PaymentAssignmentStrategyOverview> GetPaymentAssignmentStrategyOverviewAsync(
        CancellationToken cancellationToken = default);

    Task<PaymentStrategyChangePreview> PreviewPaymentAssignmentStrategyAsync(
        PaymentAssignmentMode newMode,
        DateOnly effectiveSalaryDate,
        CancellationToken cancellationToken = default);
}
