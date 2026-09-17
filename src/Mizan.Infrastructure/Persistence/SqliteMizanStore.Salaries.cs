using Mizan.Application.Abstractions;
using Mizan.Domain.Models;

namespace Mizan.Infrastructure.Persistence;

public sealed partial class SqliteMizanStore
{
    public async Task<IReadOnlyList<SalaryScheduleEntry>>
        GetSalaryScheduleAsync(
            CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        return (await _database.Table<SalaryRow>().ToListAsync())
            .Select(FromRow)
            .OrderBy(x => x.EffectiveDate)
            .ToArray();
    }

    public async Task UpsertSalaryAsync(
        SalaryScheduleEntry entry,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.InsertOrReplaceAsync(ToRow(entry));
    }

    public async Task DeleteSalaryAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.ExecuteAsync(
            "DELETE FROM salary_schedule WHERE Id = ?",
            Key(id));
    }

    public async Task<IReadOnlyList<OneTimeIncome>>
        GetOtherIncomesAsync(
            CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        return (await _database.Table<OtherIncomeRow>().ToListAsync())
            .Select(FromRow)
            .OrderBy(x => x.ExactDate)
            .ToArray();
    }

    public async Task UpsertOtherIncomeAsync(
        OneTimeIncome income,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.InsertOrReplaceAsync(ToRow(income));
    }

    public async Task DeleteOtherIncomeAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.ExecuteAsync(
            "DELETE FROM other_incomes WHERE Id = ?",
            Key(id));
    }

    internal static SalaryRow ToRow(SalaryScheduleEntry value) => new()
    {
        Id = Key(value.Id),
        NetAmount = value.Amount,
        EffectiveFrom = FormatDate(value.EffectiveDate),
        Note = value.Description
    };

    private static SalaryScheduleEntry FromRow(SalaryRow row) => new()
    {
        Id = ParseKey(row.Id),
        Amount = row.NetAmount,
        EffectiveDate = ParseDate(row.EffectiveFrom),
        Description = row.Note
    };

    private static OtherIncomeRow ToRow(OneTimeIncome value) => new()
    {
        Id = Key(value.Id),
        Amount = value.Amount,
        ExactDate = FormatDate(value.ExactDate),
        Description = value.Description
    };

    private static OneTimeIncome FromRow(OtherIncomeRow row) => new()
    {
        Id = ParseKey(row.Id),
        Amount = row.Amount,
        ExactDate = ParseDate(row.ExactDate),
        Description = row.Description
    };
}
