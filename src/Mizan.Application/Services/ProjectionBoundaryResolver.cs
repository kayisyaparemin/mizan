using Mizan.Domain.Calculations;
using Mizan.Domain.Models;

namespace Mizan.Application.Services;

public sealed record ProjectionBoundary(
    FinancialSnapshot Snapshot,
    DateOnly ProjectionAnchorDate,
    DateOnly FirstUnrealizedSalaryDate,
    decimal StartingSavings,
    DateOnly? ClosedCheckpointDate,
    Guid? SourcePeriodActualId);

public sealed class ProjectionBoundaryResolver(
    CashFlowPeriodCalculator CashFlowPeriodCalculator)
{
    public ProjectionBoundary Resolve(
        FinancialHistoryData history,
        FinancialSnapshot currentSnapshot,
        UserSettings settings,
        DateOnly asOf)
    {
        var sourceActual = history.Actuals
            .Where(x => x.ResultFinancialSnapshotId == currentSnapshot.Id)
            .OrderByDescending(x => x.FinalizedAtUtc)
            .FirstOrDefault();
        var IncomeDay = settings.IncomeDay;

        if (sourceActual is not null)
        {
            var firstUnrealizedSalary = CashFlowPeriodCalculator
                .GetFirstPeriodStartStrictlyAfter(
                    sourceActual.PeriodEnd,
                    IncomeDay);
            return new ProjectionBoundary(
                currentSnapshot,
                sourceActual.PeriodEnd,
                firstUnrealizedSalary,
                currentSnapshot.ProjectionOpeningBalance,
                sourceActual.PeriodEnd,
                sourceActual.Id);
        }

        var anchor = currentSnapshot.ProjectionAnchorDate == default
            ? asOf
            : currentSnapshot.ProjectionAnchorDate;
        var firstPeriodStart = CashFlowPeriodCalculator.GetFirstPeriodStartOnOrAfter(
            anchor,
            IncomeDay);
        return new ProjectionBoundary(
            currentSnapshot,
            anchor,
            firstPeriodStart,
            currentSnapshot.ProjectionOpeningBalance,
            null,
            null);
    }
}
