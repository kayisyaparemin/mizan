using Mizan.Application.Models;
using Mizan.Domain.Calculations;
using Mizan.Domain.Models;

namespace Mizan.Application.Services;

public sealed class FinancialProjectionService(
    FinancialProjectionCalculator projectionCalculator)
{
    public DashboardSnapshot BuildDashboard(
        FinancialPlan plan,
        DateOnly asOf,
        DateOnly? firstSalaryDate = null)
    {
        var projection = projectionCalculator.CalculatePlan(
            plan,
            asOf,
            12,
            firstSalaryDate);
        var periods = projection.Periods;
        var current = periods[0];
        var preFirst = projection.allocationPlan.PreFirstPeriodObligations
            .Where(x => x.DueDate >= asOf)
            .OrderBy(x => x.DueDate)
            .ThenByDescending(x => x.Amount)
            .ToArray();
        var upcoming = preFirst
            .Concat(periods
            .SelectMany(x => x.MandatoryItems)
            .Where(x => x.DueDate >= asOf)
            .OrderBy(x => x.DueDate)
            .ThenByDescending(x => x.Amount))
            .Take(5)
            .ToArray();
        var tightest = periods
            .OrderBy(x => x.EndingProjectedBalance)
            .ThenBy(x => x.PeriodStart)
            .First();

        var orderedStrategies = plan.PaymentAssignmentStrategies
            .OrderBy(x => x.EffectiveFromPeriodDate)
            .ToArray();
        var currentStrategy = orderedStrategies
            .Where(x => x.EffectiveFromPeriodDate <= current.PeriodStart)
            .Last();
        var pending = orderedStrategies.FirstOrDefault(x =>
            x.EffectiveFromPeriodDate > current.PeriodStart);

        return new DashboardSnapshot(
            current,
            preFirst,
            upcoming,
            periods[^1].EndingProjectedBalance,
            tightest,
            periods.Any(x => x.HasUndeterminedCardPayment),
            currentStrategy,
            pending,
            plan.Settings.ProjectionAnchorDate,
            plan.Settings.ProjectionOpeningBalance,
            projection.TotalCreditCardInterest,
            projection.TotalDeficitFinancingInterest,
            projection.TotalInterestCost);
    }

    public IReadOnlyList<CashFlowPeriodProjection> BuildFuturePeriods(
        FinancialPlan plan,
        DateOnly asOf,
        int periodCount,
        DateOnly? firstSalaryDate = null) =>
        projectionCalculator.Calculate(plan, asOf, periodCount, firstSalaryDate);
}
