using Mizan.Application.Abstractions;
using Mizan.Application.Models;
using Mizan.Domain.Models;
using SQLite;

namespace Mizan.Infrastructure.Persistence;

public sealed partial class SqliteMizanStore
{
    public async Task ApplyOnboardingSetupAsync(
        OnboardingPersistenceBatch batch,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        await _database.RunInTransactionAsync(connection =>
        {
            UpdateSettings(connection, batch.Settings);

            foreach (var salary in batch.Salaries)
            {
                connection.InsertOrReplace(ToRow(salary));
            }

            foreach (var income in batch.OtherIncomes)
            {
                connection.InsertOrReplace(ToRow(income));
            }

            foreach (var loan in batch.Loans)
            {
                connection.InsertOrReplace(ToRow(loan));
            }

            foreach (var plan in batch.PaymentPlans)
            {
                InsertPaymentPlan(connection, plan);
            }

            foreach (var card in batch.CreditCards)
            {
                InsertCreditCard(connection, card);
            }

            foreach (var expense in batch.PlannedLargeExpenses)
            {
                connection.InsertOrReplace(ToRow(expense));
            }

            foreach (var strategy in batch.PaymentAssignmentStrategies)
            {
                connection.InsertOrReplace(ToRow(strategy));
            }

            connection.Execute(
                "UPDATE financial_snapshots SET IsCurrent = 0 WHERE IsCurrent = 1");
            connection.Insert(ToRow(batch.CurrentSnapshot with
            {
                IsCurrent = true
            }));
            InsertPlan(connection, batch.CurrentPlan);
        });
    }

    public async Task<FinancialHistoryData> GetFinancialHistoryAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var snapshotsTask = _database.Table<FinancialSnapshotRow>().ToListAsync();
        var plansTask = _database.Table<PeriodPlanSnapshotRow>().ToListAsync();
        var linesTask = _database.Table<PeriodPlanPaymentLineRow>().ToListAsync();
        var revisionsTask = _database.Table<PeriodPlanRevisionRow>().ToListAsync();
        var revisionLinesTask = _database
            .Table<PeriodPlanRevisionPaymentLineRow>()
            .ToListAsync();
        var actualsTask = _database.Table<PeriodActualRow>().ToListAsync();
        var paymentsTask = _database.Table<ActualPaymentRow>().ToListAsync();
        var flowsTask = _database.Table<ActualFlowRow>().ToListAsync();
        var breakdownTask = _database.Table<ActualLivingBreakdownRow>().ToListAsync();
        await Task.WhenAll(
            snapshotsTask, plansTask, linesTask, revisionsTask,
            revisionLinesTask, actualsTask, paymentsTask, flowsTask,
            breakdownTask);

        var lines = await linesTask;
        var revisionLines = await revisionLinesTask;
        var payments = await paymentsTask;
        var flows = await flowsTask;
        var breakdown = await breakdownTask;
        return new FinancialHistoryData(
            (await snapshotsTask).Select(FromRow).OrderBy(x => x.SnapshotDate).ThenBy(x => x.CreatedAtUtc).ToArray(),
            (await plansTask)
                .Select(row => FromRow(row, lines.Where(x => x.PeriodPlanSnapshotId == row.Id)))
                .OrderBy(x => x.PeriodStart)
                .ToArray(),
            (await revisionsTask)
                .Select(row => FromRow(
                    row,
                    revisionLines.Where(x =>
                        x.PeriodPlanRevisionId == row.Id)))
                .OrderBy(x => x.CreatedAtUtc)
                .ToArray(),
            (await actualsTask)
                .Select(row => FromRow(
                    row,
                    payments.Where(x => x.PeriodActualId == row.Id),
                    flows.Where(x => x.PeriodActualId == row.Id),
                    breakdown.Where(x => x.PeriodActualId == row.Id)))
                .OrderBy(x => x.PeriodStart)
                .ToArray());
    }

    public async Task SaveCurrentFinancialSnapshotAsync(
        FinancialSnapshot snapshot,
        PeriodPlanSnapshot plan,
        UserSettings? updatedSettings = null,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await _database.RunInTransactionAsync(connection =>
        {
            connection.Execute("UPDATE financial_snapshots SET IsCurrent = 0 WHERE IsCurrent = 1");
            if (updatedSettings is not null)
            {
                UpdateSettings(connection, updatedSettings);
            }
            connection.Insert(ToRow(snapshot with { IsCurrent = true }));
            InsertPlan(connection, plan);
        });
    }

    public async Task ReplacePendingFinancialSnapshotPlanAsync(
        FinancialSnapshot snapshot,
        PeriodPlanSnapshot plan,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await _database.RunInTransactionAsync(connection =>
        {
            var current = connection.Find<FinancialSnapshotRow>(
                Key(snapshot.Id));
            if (current is null || !current.IsCurrent)
            {
                throw new InvalidOperationException(
                    "Yalnızca güncel snapshot planı düzeltilebilir.");
            }

            var oldPlans = connection.Query<PeriodPlanSnapshotRow>(
                "SELECT * FROM period_plan_snapshots WHERE FinancialSnapshotId = ?",
                Key(snapshot.Id));
            if (oldPlans.Any(oldPlan =>
                    connection.FindWithQuery<PeriodActualRow>(
                        "SELECT * FROM period_actuals WHERE PeriodPlanSnapshotId = ? LIMIT 1",
                        oldPlan.Id) is not null))
            {
                throw new InvalidOperationException(
                    "Tamamlanmış dönem planı değiştirilemez.");
            }

            foreach (var oldPlan in oldPlans)
            {
                var oldRevisions = connection.Query<PeriodPlanRevisionRow>(
                    "SELECT * FROM period_plan_revisions WHERE PeriodPlanSnapshotId = ?",
                    oldPlan.Id);
                foreach (var oldRevision in oldRevisions)
                {
                    connection.Execute(
                        "DELETE FROM period_plan_revision_payment_lines WHERE PeriodPlanRevisionId = ?",
                        oldRevision.Id);
                    connection.Delete(oldRevision);
                }

                connection.Execute(
                    "DELETE FROM period_plan_payment_lines WHERE PeriodPlanSnapshotId = ?",
                    oldPlan.Id);
                connection.Delete(oldPlan);
            }

            connection.Update(ToRow(snapshot with { IsCurrent = true }));
            InsertPlan(connection, plan);
        });
    }

    public async Task SavePeriodPlanRevisionAsync(
        PeriodPlanRevision revision,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await _database.RunInTransactionAsync(connection =>
        {
            var plan = connection.Find<PeriodPlanSnapshotRow>(
                Key(revision.PeriodPlanSnapshotId));
            if (plan is null)
            {
                throw new InvalidOperationException(
                    "Revize edilecek dönem planı bulunamadı.");
            }

            var finalized = connection.FindWithQuery<PeriodActualRow>(
                "SELECT * FROM period_actuals WHERE PeriodPlanSnapshotId = ? LIMIT 1",
                Key(revision.PeriodPlanSnapshotId));
            if (finalized is not null)
            {
                throw new InvalidOperationException(
                    "Tamamlanmış dönem planı revize edilemez.");
            }

            InsertRevision(connection, revision);
        });
    }

    public async Task FinalizeFinancialReviewAsync(
        FinancialReviewCommit commit,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await _database.RunInTransactionAsync(connection =>
        {
            var current = connection.FindWithQuery<FinancialSnapshotRow>(
                "SELECT * FROM financial_snapshots WHERE IsCurrent = 1 LIMIT 1");
            if (current is null ||
                current.Id != Key(commit.Actual.SourceFinancialSnapshotId))
            {
                throw new InvalidOperationException(
                    "Güncel finansal durum değişti. Dönemi yeniden açın.");
            }

            var existing = connection.FindWithQuery<PeriodActualRow>(
                "SELECT * FROM period_actuals WHERE PeriodPlanSnapshotId = ? LIMIT 1",
                Key(commit.Actual.PeriodPlanSnapshotId));
            if (existing is not null)
            {
                throw new InvalidOperationException("Bu dönem daha önce kaydedildi.");
            }

            if (commit.Revision is not null)
            {
                InsertRevision(connection, commit.Revision);
            }
            InsertActual(connection, commit.Actual);
            foreach (var loan in commit.UpdatedLoans)
            {
                connection.InsertOrReplace(ToRow(loan));
            }
            foreach (var plan in commit.UpdatedPaymentPlans)
            {
                InsertPaymentPlan(connection, plan);
            }
            foreach (var card in commit.UpdatedCreditCards)
            {
                InsertCreditCard(connection, card);
            }
            foreach (var expense in commit.UpdatedLargeExpenses)
            {
                connection.InsertOrReplace(ToRow(expense));
            }
            foreach (var prepaymentId in commit.RemovedLoanPrepaymentIds)
            {
                connection.Execute(
                    "DELETE FROM loan_prepayments WHERE Id = ?",
                    Key(prepaymentId));
            }
            UpdateSettings(connection, commit.UpdatedSettings);
            connection.Execute("UPDATE financial_snapshots SET IsCurrent = 0 WHERE IsCurrent = 1");
            connection.Insert(ToRow(commit.NewSnapshot with { IsCurrent = true }));
            InsertPlan(connection, commit.NewPlan);
        });
    }

    private static void InsertPlan(SQLiteConnection connection, PeriodPlanSnapshot plan)
    {
        connection.Insert(ToRow(plan));
        foreach (var line in plan.PaymentLines)
        {
            connection.Insert(ToRow(line with { PeriodPlanSnapshotId = plan.Id }));
        }
    }

    private static void InsertActual(SQLiteConnection connection, PeriodActual actual)
    {
        connection.Insert(ToRow(actual));
        foreach (var payment in actual.Payments)
        {
            connection.Insert(ToRow(payment with
            {
                PeriodActualId = actual.Id
            }));
        }

        foreach (var flow in actual.Flows)
        {
            connection.Insert(ToRow(flow with
            {
                PeriodActualId = actual.Id
            }));
        }

        foreach (var item in actual.LivingBreakdown)
        {
            connection.Insert(ToRow(item with
            {
                PeriodActualId = actual.Id
            }));
        }
    }

    private static void InsertRevision(
        SQLiteConnection connection,
        PeriodPlanRevision revision)
    {
        connection.Insert(ToRow(revision));
        foreach (var line in revision.PaymentLines)
        {
            connection.Insert(ToRevisionRow(
                revision.Id,
                line with
                {
                    PeriodPlanSnapshotId = revision.PeriodPlanSnapshotId
                }));
        }
    }
}
