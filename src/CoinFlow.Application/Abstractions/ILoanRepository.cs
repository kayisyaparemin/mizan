using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Abstractions;

public interface ILoanRepository
{
    Task<IReadOnlyList<Loan>> GetLoansAsync(
        CancellationToken cancellationToken = default);
    Task UpsertLoanAsync(Loan loan, CancellationToken cancellationToken = default);
    Task DeleteLoanAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LoanPrepayment>> GetLoanPrepaymentsAsync(
        CancellationToken cancellationToken = default);
    Task DeleteLoanPrepaymentAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
