using CoinFlow.Application.Abstractions;
using CoinFlow.Domain.Models;

namespace CoinFlow.Infrastructure.Persistence;

public sealed partial class SqliteCoinFlowStore
{
    public async Task<IReadOnlyList<Loan>> GetLoansAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        return (await _database.Table<LoanRow>().ToListAsync())
            .Select(FromRow)
            .OrderBy(x => x.NextPaymentDate)
            .ToArray();
    }

    public async Task UpsertLoanAsync(
        Loan loan,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.InsertOrReplaceAsync(ToRow(loan));
    }

    public async Task DeleteLoanAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.RunInTransactionAsync(connection =>
        {
            // Kredisi olmayan erken ödeme anlamsızdır.
            connection.Execute(
                "DELETE FROM loan_prepayments WHERE LoanId = ?",
                Key(id));
            connection.Execute("DELETE FROM loans WHERE Id = ?", Key(id));
        });
    }

    public async Task<IReadOnlyList<LoanPrepayment>> GetLoanPrepaymentsAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        return (await _database.Table<LoanPrepaymentRow>().ToListAsync())
            .Select(FromRow)
            .OrderBy(x => x.Date)
            .ToArray();
    }

    public async Task DeleteLoanPrepaymentAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.ExecuteAsync(
            "DELETE FROM loan_prepayments WHERE Id = ?",
            Key(id));
    }

    internal static LoanPrepaymentRow ToRow(LoanPrepayment value) => new()
    {
        Id = Key(value.Id),
        LoanId = Key(value.LoanId),
        Date = FormatDate(value.Date),
        Mode = (int)value.Mode,
        PrincipalAmount = value.PrincipalAmount
    };

    private static LoanPrepayment FromRow(LoanPrepaymentRow row) => new()
    {
        Id = ParseKey(row.Id),
        LoanId = ParseKey(row.LoanId),
        Date = ParseDate(row.Date),
        Mode = Enum.IsDefined(typeof(LoanPrepaymentMode), row.Mode)
            ? (LoanPrepaymentMode)row.Mode
            : LoanPrepaymentMode.FullClosure,
        PrincipalAmount = row.PrincipalAmount
    };

    internal static LoanRow ToRow(Loan value) => new()
    {
        Id = Key(value.Id),
        Name = value.Name,
        Bank = value.Bank,
        MonthlyInstallment = value.MonthlyPayment,
        PaymentDay = value.PaymentDay,
        StartDate = FormatDate(value.NextPaymentDate),
        EndDate = null,
        InstallmentCount = value.RemainingInstallmentCount,
        FinalPaymentAmount = value.FinalPaymentAmount,
        RemainingDebt = value.RemainingDebt,
        EarlyClosureAmount = value.EarlyClosureAmount,
        EarlyClosureAmountAsOf = value.EarlyClosureAmountAsOf is DateOnly asOf
            ? FormatDate(asOf)
            : null,
        Kind = (int)value.Kind,
        IsActive = value.IsActive
    };

    private static Loan FromRow(LoanRow row) => new()
    {
        Id = ParseKey(row.Id),
        Name = row.Name,
        Bank = row.Bank,
        MonthlyPayment = row.MonthlyInstallment,
        PaymentDay = row.PaymentDay,
        NextPaymentDate = ParseDate(row.StartDate),
        RemainingInstallmentCount = row.InstallmentCount.GetValueOrDefault(),
        FinalPaymentAmount = row.FinalPaymentAmount,
        RemainingDebt = row.RemainingDebt,
        EarlyClosureAmount = row.EarlyClosureAmount,
        EarlyClosureAmountAsOf = ParseNullableDate(row.EarlyClosureAmountAsOf),
        Kind = Enum.IsDefined(typeof(LoanKind), row.Kind)
            ? (LoanKind)row.Kind
            : LoanKind.Consumer,
        IsActive = row.IsActive
    };
}
