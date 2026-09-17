using Mizan.Domain.Models;

namespace Mizan.Application.Abstractions;

public interface IIncomeRepository
{
    Task<IReadOnlyList<OneTimeIncome>> GetOtherIncomesAsync(
        CancellationToken cancellationToken = default);
    Task UpsertOtherIncomeAsync(
        OneTimeIncome income,
        CancellationToken cancellationToken = default);
    Task DeleteOtherIncomeAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
