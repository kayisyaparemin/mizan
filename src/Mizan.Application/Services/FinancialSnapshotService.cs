using Mizan.Application.Abstractions;
using Mizan.Domain.Calculations;
using Mizan.Domain.Models;

namespace Mizan.Application.Services;

public sealed record FinancialSnapshotBundle(
    FinancialSnapshot Snapshot,
    PeriodPlanSnapshot Plan,
    UserSettings UpdatedSettings);

public sealed class FinancialSnapshotService(
    IMizanStore store,
    IClock clock,
    PeriodPlanSnapshotService planSnapshotService,
    CashFlowPeriodCalculator CashFlowPeriodCalculator)
{
    public async Task<FinancialSnapshot?> EnsureInitialSnapshotAsync(
        FinancialPlan plan,
        CancellationToken cancellationToken = default)
    {
        var history = await store.GetFinancialHistoryAsync(cancellationToken);
        var current = LatestCurrent(history);
        if (current is not null)
        {
            var expectedReviewDate = CashFlowPeriodCalculator
                .GetNextSettlementDate(current.SnapshotDate, current.IncomeDay);
            var pendingPlan = history.Plans
                .Where(x => x.FinancialSnapshotId == current.Id)
                .Where(x => history.Actuals.All(actual =>
                    actual.PeriodPlanSnapshotId != x.Id))
                .OrderByDescending(x => x.CreatedAtUtc)
                .FirstOrDefault();
            var requiresCadenceRepair = pendingPlan is not null &&
                (current.NextSettlementDate != expectedReviewDate ||
                 pendingPlan.PeriodStart != current.SnapshotDate ||
                 pendingPlan.PeriodEnd != expectedReviewDate ||
                 pendingPlan.SettlementAvailableFrom != expectedReviewDate);
            if (requiresCadenceRepair)
            {
                var correctedSnapshot = current with
                {
                    NextSettlementDate = expectedReviewDate
                };
                var snapshotPlan = plan with
                {
                    Settings = plan.Settings with
                    {
                        ProjectionOpeningBalance =
                            current.ProjectionOpeningBalance,
                        ProjectionAnchorDate = current.SnapshotDate,
                        IncomeDay = current.IncomeDay
                    }
                };
                var correctedPlan = planSnapshotService.Freeze(
                    snapshotPlan,
                    correctedSnapshot,
                    clock.UtcNow);
                await store.ReplacePendingFinancialSnapshotPlanAsync(
                    correctedSnapshot,
                    correctedPlan,
                    cancellationToken);
                return correctedSnapshot;
            }

            return current;
        }

        if (!CanBuildProjection(plan))
        {
            return null;
        }

        var bundle = Build(
            plan,
            plan.Settings.ProjectionOpeningBalance,
            plan.Settings.ProjectionAnchorDate,
            FinancialSnapshotSource.Initial,
            "İlk güncel finansal durum",
            null);
        await store.SaveCurrentFinancialSnapshotAsync(
            bundle.Snapshot,
            bundle.Plan,
            cancellationToken: cancellationToken);
        return bundle.Snapshot;
    }

    public async Task<FinancialSnapshotBundle> CreateCurrentSnapshotAsync(
        FinancialPlan plan,
        decimal startingSavings,
        DateOnly snapshotDate,
        FinancialSnapshotSource source,
        string note,
        CancellationToken cancellationToken = default)
    {
        var history = await store.GetFinancialHistoryAsync(cancellationToken);
        var previous = LatestCurrent(history);
        var bundle = Build(
            plan,
            startingSavings,
            snapshotDate,
            source,
            note,
            previous?.Id);
        await store.SaveCurrentFinancialSnapshotAsync(
            bundle.Snapshot,
            bundle.Plan,
            bundle.UpdatedSettings,
            cancellationToken);
        return bundle;
    }

    public FinancialSnapshotBundle Build(
        FinancialPlan plan,
        decimal startingSavings,
        DateOnly snapshotDate,
        FinancialSnapshotSource source,
        string note,
        Guid? previousSnapshotId)
    {
        if (!CanBuildProjection(plan))
        {
            throw new InvalidOperationException(
                "Güncel durum için gelir, gelir kullanım düzeni ve son güncelleme tarihi gereklidir.");
        }

        var updatedSettings = plan.Settings with
        {
            ProjectionOpeningBalance = startingSavings,
            ProjectionAnchorDate = snapshotDate
        };
        var snapshotPlan = plan with { Settings = updatedSettings };
        var now = clock.UtcNow;
        var snapshot = new FinancialSnapshot
        {
            SnapshotDate = snapshotDate,
            ProjectionAnchorDate = snapshotDate,
            ProjectionOpeningBalance = startingSavings,
            IncomeDay = updatedSettings.IncomeDay,
            PreviousSnapshotId = previousSnapshotId,
            Source = source,
            IsCurrent = true,
            CreatedAtUtc = now,
            Note = note.Trim()
        };
        var frozenPlan = planSnapshotService.Freeze(
            snapshotPlan,
            snapshot,
            now);
        snapshot = snapshot with
        {
            NextSettlementDate = frozenPlan.SettlementAvailableFrom
        };
        return new FinancialSnapshotBundle(
            snapshot,
            frozenPlan,
            updatedSettings);
    }

    public static FinancialSnapshot? LatestCurrent(
        FinancialHistoryData history) => history.Snapshots
        .Where(x => x.IsCurrent)
        .OrderByDescending(x => x.SnapshotDate)
        .ThenByDescending(x => x.CreatedAtUtc)
        .FirstOrDefault();

    private static bool CanBuildProjection(FinancialPlan plan) =>
        plan.Salaries.Count > 0 &&
        plan.PaymentAssignmentStrategies.Count > 0 &&
        plan.Settings.ProjectionAnchorDate != default;
}
