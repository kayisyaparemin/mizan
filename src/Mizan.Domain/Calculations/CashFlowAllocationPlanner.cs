using Mizan.Domain.Models;

namespace Mizan.Domain.Calculations;

public sealed record CashFlowAllocationBudget(
    DateOnly PeriodDate,
    DateOnly CoverageStart,
    DateOnly CoverageEnd,
    CashFlowAllocationMode Mode,
    bool IsStrategyTransition,
    bool IsInitialSnapshotPeriod,
    IReadOnlyList<ObligationItem> Items)
{
    private IEnumerable<ObligationItem> MandatoryItems =>
        Items.Where(x => x.Type != ObligationType.PlannedLargeExpense);

    public decimal TransitionCatchUpAmount => MandatoryItems
        .Where(x => x.IsTransitionCatchUp)
        .Sum(x => x.Amount);

    public decimal ForwardFundedAmount => MandatoryItems
        .Where(x => x.IsForwardFunded)
        .Sum(x => x.Amount);

    public decimal NormalMandatoryAmount => MandatoryItems
        .Where(x => x.AssignmentReason is not
            PaymentAllocationReason.TransitionCatchUp and not
            PaymentAllocationReason.TransitionForward)
        .Sum(x => x.Amount);

    public PaymentAllocationReason ResolveReason(DateOnly paymentDate) =>
        Items.FirstOrDefault(x => x.DueDate == paymentDate)
            ?.AssignmentReason ??
        (Mode == CashFlowAllocationMode.PreviousPeriod
            ? PaymentAllocationReason.NormalPrevious
            : PaymentAllocationReason.NormalUpcoming);
}

public sealed record CashFlowAllocationPlan(
    IReadOnlyList<CashFlowAllocationBudget> Budgets,
    IReadOnlyList<ObligationItem> PreFirstPeriodObligations,
    int EligiblePaymentCount,
    int AssignedExactlyOnceCount,
    int UnassignedPaymentCount,
    int DuplicateAssignedCount)
{
    public CashFlowAllocationBudget? FindBudget(DateOnly paymentDate) =>
        Budgets.FirstOrDefault(x =>
            paymentDate >= x.CoverageStart &&
            paymentDate <= x.CoverageEnd);

    public bool IsPreFirstPeriodDate(DateOnly paymentDate) =>
        PreFirstPeriodObligations.Any(x => x.DueDate == paymentDate);
}

public sealed class CashFlowAllocationPlanner(
    PaymentAllocationStrategyResolver allocationResolver)
{
    public CashFlowAllocationPlan Plan(
        IReadOnlyList<CashFlowPeriod> cashFlowPeriods,
        IEnumerable<ObligationItem> obligations,
        DateOnly projectionAnchorDate,
        int IncomeDay,
        IReadOnlyList<CashFlowAllocationStrategy> allocationHistory)
    {
        if (cashFlowPeriods.Count == 0)
        {
            throw new ArgumentException(
                "En az bir dönem gereklidir.",
                nameof(cashFlowPeriods));
        }

        var firstPeriodStart = cashFlowPeriods[0].Start;
        allocationResolver.ValidateHistory(
            allocationHistory,
            IncomeDay,
            firstPeriodStart);
        var indexed = obligations
            .Where(x => x.DueDate >= projectionAnchorDate)
            .OrderBy(x => x.DueDate)
            .ThenBy(x => x.Name)
            .Select((item, index) => new IndexedObligation(index, item))
            .ToArray();
        var assignmentCounts = indexed.ToDictionary(x => x.Index, _ => 0);
        var firstMode = allocationResolver
            .Resolve(firstPeriodStart, allocationHistory)
            .Mode;
        var preFirst = new List<ObligationItem>();

        if (firstMode == CashFlowAllocationMode.UpcomingPeriod)
        {
            foreach (var entry in indexed.Where(x =>
                         x.Item.DueDate < firstPeriodStart))
            {
                assignmentCounts[entry.Index]++;
                preFirst.Add(entry.Item with
                {
                    ActiveMode = firstMode,
                    AssignmentReason =
                        PaymentAllocationReason.PreFirstPeriodUpcoming,
                    IsPreFirstPeriodObligation = true
                });
            }
        }

        var lastCoveredDate = firstMode ==
                              CashFlowAllocationMode.UpcomingPeriod
            ? firstPeriodStart.AddDays(-1)
            : projectionAnchorDate.AddDays(-1);
        var budgets = new List<CashFlowAllocationBudget>(cashFlowPeriods.Count);
        CashFlowAllocationMode? previousMode = null;

        foreach (var period in cashFlowPeriods)
        {
            var strategy = allocationResolver.Resolve(
                period.Start,
                allocationHistory);
            var mode = strategy.Mode;
            var isTransition = previousMode is not null &&
                               previousMode != mode;
            var coverageStart = lastCoveredDate.AddDays(1);
            var targetCoveredDate = mode ==
                                    CashFlowAllocationMode.PreviousPeriod
                ? period.Start
                : period.End.AddDays(-1);
            var assigned = new List<ObligationItem>();

            if (coverageStart <= targetCoveredDate)
            {
                foreach (var entry in indexed.Where(x =>
                             x.Item.DueDate >= coverageStart &&
                             x.Item.DueDate <= targetCoveredDate))
                {
                    assignmentCounts[entry.Index]++;
                    var reason = ResolveReason(
                        cashFlowPeriods[0].Start == period.Start,
                        isTransition,
                        previousMode,
                        mode,
                        entry.Item.DueDate,
                        period.Start);
                    assigned.Add(entry.Item with
                    {
                        AssignedPeriodDate = period.Start,
                        PaymentBeforePeriodStart =
                            entry.Item.DueDate < period.Start,
                        ActiveMode = mode,
                        AssignmentReason = reason,
                        IsTransitionCatchUp = reason ==
                            PaymentAllocationReason.TransitionCatchUp,
                        IsForwardFunded =
                            mode == CashFlowAllocationMode.UpcomingPeriod &&
                            entry.Item.DueDate >= period.Start
                    });
                }
            }

            budgets.Add(new CashFlowAllocationBudget(
                period.Start,
                coverageStart,
                targetCoveredDate,
                mode,
                isTransition,
                cashFlowPeriods[0].Start == period.Start &&
                mode == CashFlowAllocationMode.PreviousPeriod,
                assigned));
            if (targetCoveredDate > lastCoveredDate)
            {
                lastCoveredDate = targetCoveredDate;
            }

            previousMode = mode;
        }

        var eligibleIndexes = indexed
            .Where(x => x.Item.DueDate <= lastCoveredDate)
            .Select(x => x.Index)
            .ToArray();
        var exactlyOnce = eligibleIndexes.Count(x =>
            assignmentCounts[x] == 1);
        var unassigned = eligibleIndexes.Count(x =>
            assignmentCounts[x] == 0);
        var duplicate = eligibleIndexes.Count(x =>
            assignmentCounts[x] > 1);

        return new CashFlowAllocationPlan(
            budgets,
            preFirst,
            eligibleIndexes.Length,
            exactlyOnce,
            unassigned,
            duplicate);
    }

    private static PaymentAllocationReason ResolveReason(
        bool isFirstSalary,
        bool isTransition,
        CashFlowAllocationMode? previousMode,
        CashFlowAllocationMode mode,
        DateOnly paymentDate,
        DateOnly PeriodDate)
    {
        if (isFirstSalary && mode == CashFlowAllocationMode.PreviousPeriod)
        {
            return PaymentAllocationReason.InitialSnapshotCatchUp;
        }

        if (isTransition &&
            previousMode == CashFlowAllocationMode.PreviousPeriod &&
            mode == CashFlowAllocationMode.UpcomingPeriod)
        {
            return paymentDate < PeriodDate
                ? PaymentAllocationReason.TransitionCatchUp
                : PaymentAllocationReason.TransitionForward;
        }

        return mode == CashFlowAllocationMode.PreviousPeriod
            ? PaymentAllocationReason.NormalPrevious
            : PaymentAllocationReason.NormalUpcoming;
    }

    private sealed record IndexedObligation(
        int Index,
        ObligationItem Item);
}
