namespace Mizan.Domain.Calculations;

public sealed record TargetReachabilityResult(
    bool IsAlreadyReached,
    CashFlowPeriodProjection? FirstReachedPeriod)
{
    public bool IsReached => IsAlreadyReached || FirstReachedPeriod is not null;
}

public sealed class TargetAmountCalculator
{
    public CashFlowPeriodProjection? FindFirstReached(
        IEnumerable<CashFlowPeriodProjection> projections,
        decimal targetAmount)
    {
        ValidateTarget(targetAmount);

        return projections
            .OrderBy(x => x.PeriodStart)
            .FirstOrDefault(x => x.EndingProjectedBalance >= targetAmount);
    }

    public TargetReachabilityResult FindFirstReachable(
        IEnumerable<CashFlowPeriodProjection> projections,
        decimal targetAmount)
    {
        ValidateTarget(targetAmount);
        var ordered = projections
            .OrderBy(x => x.PeriodStart)
            .ToArray();
        if (ordered.FirstOrDefault()?.OpeningProjectedBalance >= targetAmount)
        {
            return new TargetReachabilityResult(true, null);
        }

        return new TargetReachabilityResult(
            false,
            ordered.FirstOrDefault(x =>
                x.EndingProjectedBalance >= targetAmount));
    }

    private static void ValidateTarget(decimal targetAmount)
    {
        if (targetAmount <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetAmount),
                "Hedef tutar 0'dan büyük olmalıdır.");
        }
    }
}
