namespace CoinFlow.Infrastructure.Persistence;

public sealed partial class SqliteCoinFlowStore
{
    private async Task RemoveObsoleteDailyTrackingTablesAsync()
    {
        await _database.ExecuteAsync("DROP TABLE IF EXISTS expenses");
        await _database.ExecuteAsync(
            "DROP TABLE IF EXISTS spendable_balance_snapshots");
        await _database.ExecuteAsync("DROP TABLE IF EXISTS emergency_fund");
        await _database.ExecuteAsync(
            "DROP TABLE IF EXISTS emergency_fund_transfers");
    }

    private async Task MigratePeriodPlanRevisionSchemaAsync()
    {
        await EnsureColumnAsync(
            "period_plan_revisions",
            "RevisionNumber",
            "RevisionNumber INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(
            "period_plan_revisions",
            "Trigger",
            "Trigger TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(
            "period_plan_revisions",
            "StrategyUsed",
            "StrategyUsed INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(
            "period_plan_revisions",
            "PlannedLoanPayments",
            "PlannedLoanPayments decimal NOT NULL DEFAULT 0");
        await EnsureColumnAsync(
            "period_plan_revisions",
            "PlannedCardPayments",
            "PlannedCardPayments decimal NOT NULL DEFAULT 0");
        await EnsureColumnAsync(
            "period_plan_revisions",
            "PlannedTemporaryPayments",
            "PlannedTemporaryPayments decimal NOT NULL DEFAULT 0");
        await EnsureColumnAsync(
            "period_plan_revisions",
            "PlannedInstallmentPayments",
            "PlannedInstallmentPayments decimal NOT NULL DEFAULT 0");
        await EnsureColumnAsync(
            "period_plan_revisions",
            "PlannedOtherScheduledPayments",
            "PlannedOtherScheduledPayments decimal NOT NULL DEFAULT 0");
        await EnsureColumnAsync(
            "period_plan_revisions",
            "PlannedCardInterest",
            "PlannedCardInterest decimal NOT NULL DEFAULT 0");
        await EnsureColumnAsync(
            "period_plan_revisions",
            "PlannedDeficitInterest",
            "PlannedDeficitInterest decimal NOT NULL DEFAULT 0");
        await BackfillLegacyPeriodPlanRevisionSummariesAsync();
    }

    private async Task BackfillLegacyPeriodPlanRevisionSummariesAsync()
    {
        await _database.ExecuteAsync(
            """
            UPDATE period_plan_revisions
            SET
                RevisionNumber = CASE
                    WHEN RevisionNumber = 0 THEN (
                        SELECT COUNT(*)
                        FROM period_plan_revisions prior
                        WHERE prior.PeriodPlanSnapshotId = period_plan_revisions.PeriodPlanSnapshotId
                          AND prior.CreatedAtUtc <= period_plan_revisions.CreatedAtUtc)
                    ELSE RevisionNumber
                END,
                Trigger = CASE
                    WHEN Trigger = '' THEN Note
                    ELSE Trigger
                END,
                StrategyUsed = COALESCE((
                    SELECT StrategyUsed
                    FROM period_plan_snapshots
                    WHERE Id = period_plan_revisions.PeriodPlanSnapshotId), StrategyUsed),
                PlannedLoanPayments = COALESCE((
                    SELECT PlannedLoanPayments
                    FROM period_plan_snapshots
                    WHERE Id = period_plan_revisions.PeriodPlanSnapshotId), PlannedLoanPayments),
                PlannedCardPayments = COALESCE((
                    SELECT PlannedCardPayments
                    FROM period_plan_snapshots
                    WHERE Id = period_plan_revisions.PeriodPlanSnapshotId), PlannedCardPayments),
                PlannedTemporaryPayments = COALESCE((
                    SELECT PlannedTemporaryPayments
                    FROM period_plan_snapshots
                    WHERE Id = period_plan_revisions.PeriodPlanSnapshotId), PlannedTemporaryPayments),
                PlannedInstallmentPayments = COALESCE((
                    SELECT PlannedInstallmentPayments
                    FROM period_plan_snapshots
                    WHERE Id = period_plan_revisions.PeriodPlanSnapshotId), PlannedInstallmentPayments),
                PlannedOtherScheduledPayments = COALESCE((
                    SELECT PlannedOtherScheduledPayments
                    FROM period_plan_snapshots
                    WHERE Id = period_plan_revisions.PeriodPlanSnapshotId), PlannedOtherScheduledPayments),
                PlannedCardInterest = COALESCE((
                    SELECT PlannedCardInterest
                    FROM period_plan_snapshots
                    WHERE Id = period_plan_revisions.PeriodPlanSnapshotId), PlannedCardInterest),
                PlannedDeficitInterest = PlannedInterest - COALESCE((
                    SELECT PlannedCardInterest
                    FROM period_plan_snapshots
                    WHERE Id = period_plan_revisions.PeriodPlanSnapshotId), 0)
            WHERE NOT EXISTS (
                SELECT 1
                FROM period_plan_revision_payment_lines
                WHERE PeriodPlanRevisionId = period_plan_revisions.Id)
            """);
    }
}
