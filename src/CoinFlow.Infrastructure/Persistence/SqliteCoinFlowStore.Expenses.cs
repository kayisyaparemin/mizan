using CoinFlow.Application.Abstractions;
using CoinFlow.Domain.Models;
using SQLite;

namespace CoinFlow.Infrastructure.Persistence;

public sealed partial class SqliteCoinFlowStore
{
    public async Task<IReadOnlyList<TemporaryPaymentPlan>>
        GetPaymentPlansAsync(
            CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var plans = await _database.Table<PaymentPlanRow>().ToListAsync();
        var installments = await _database
            .Table<PaymentInstallmentRow>()
            .ToListAsync();
        return plans.Select(row => new TemporaryPaymentPlan
        {
            Id = ParseKey(row.Id),
            Name = row.Name,
            Kind = Enum.IsDefined(typeof(PaymentPlanKind), row.Kind)
                ? (PaymentPlanKind)row.Kind
                : PaymentPlanKind.Temporary,
            OriginalAmount = row.OriginalAmount,
            TotalRepaymentAmount = row.TotalRepaymentAmount,
            Installments = installments
                .Where(x => x.PlanId == row.Id)
                .Select(FromRow)
                .OrderBy(x => x.DueDate)
                .ToArray()
        }).ToArray();
    }

    public async Task UpsertPaymentPlanAsync(
        TemporaryPaymentPlan plan,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await _database.RunInTransactionAsync(connection =>
        {
            connection.InsertOrReplace(new PaymentPlanRow
            {
                Id = Key(plan.Id),
                Name = plan.Name,
                Kind = (int)plan.Kind,
                OriginalAmount = plan.OriginalAmount,
                TotalRepaymentAmount = plan.TotalRepaymentAmount
            });
            connection.Execute(
                "DELETE FROM payment_installments WHERE PlanId = ?",
                Key(plan.Id));
            foreach (var installment in plan.Installments)
            {
                connection.Insert(
                    ToRow(installment with { PlanId = plan.Id }));
            }
        });
    }

    public async Task DeletePaymentPlanAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.ExecuteAsync(
            "DELETE FROM payment_installments WHERE PlanId = ?",
            Key(id));
        await _database.ExecuteAsync(
            "DELETE FROM payment_plans WHERE Id = ?",
            Key(id));
    }

    public async Task<IReadOnlyList<PlannedLargeExpense>>
        GetPlannedLargeExpensesAsync(
            CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        return (await _database
                .Table<PlannedLargeExpenseRow>()
                .ToListAsync())
            .Select(FromRow)
            .OrderBy(x => x.ExactDate)
            .ToArray();
    }

    public async Task UpsertPlannedLargeExpenseAsync(
        PlannedLargeExpense expense,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.InsertOrReplaceAsync(ToRow(expense));
    }

    public async Task DeletePlannedLargeExpenseAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.ExecuteAsync(
            "DELETE FROM planned_large_expenses WHERE Id = ?",
            Key(id));
    }

    private static void InsertPaymentPlan(SQLiteConnection connection, TemporaryPaymentPlan plan)
    {
        connection.InsertOrReplace(new PaymentPlanRow
        {
            Id = Key(plan.Id),
            Name = plan.Name,
            Kind = (int)plan.Kind,
            OriginalAmount = plan.OriginalAmount,
            TotalRepaymentAmount = plan.TotalRepaymentAmount
        });
        connection.Execute(
            "DELETE FROM payment_installments WHERE PlanId = ?",
            Key(plan.Id));
        foreach (var installment in plan.Installments)
        {
            connection.Insert(ToRow(installment with
            {
                PlanId = plan.Id
            }));
        }
    }

    internal static PaymentInstallmentRow ToRow(
        TemporaryPaymentInstallment value) => new()
        {
            Id = Key(value.Id),
            PlanId = Key(value.PlanId),
            DueDate = FormatDate(value.DueDate),
            Amount = value.Amount,
            IsPaid = value.IsPaid
        };

    private static TemporaryPaymentInstallment FromRow(
        PaymentInstallmentRow row) => new()
        {
            Id = ParseKey(row.Id),
            PlanId = ParseKey(row.PlanId),
            DueDate = ParseDate(row.DueDate),
            Amount = row.Amount,
            IsPaid = row.IsPaid
        };

    private static PlannedLargeExpenseRow ToRow(
        PlannedLargeExpense value) => new()
        {
            Id = Key(value.Id),
            Name = value.Name,
            Amount = value.Amount,
            ExactDate = FormatDate(value.ExactDate),
            Note = value.Note,
            Status = (int)value.Status
        };

    private static PlannedLargeExpense FromRow(
        PlannedLargeExpenseRow row) => new()
        {
            Id = ParseKey(row.Id),
            Name = row.Name,
            Amount = row.Amount,
            ExactDate = ParseDate(row.ExactDate),
            Note = row.Note,
            Status = (PlannedExpenseStatus)row.Status
        };
}
