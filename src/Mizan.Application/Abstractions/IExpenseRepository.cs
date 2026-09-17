using Mizan.Domain.Models;

namespace Mizan.Application.Abstractions;

public interface IExpenseRepository
{
    Task<IReadOnlyList<PlannedLargeExpense>> GetPlannedLargeExpensesAsync(
        CancellationToken cancellationToken = default);
    Task UpsertPlannedLargeExpenseAsync(
        PlannedLargeExpense expense,
        CancellationToken cancellationToken = default);
    Task DeletePlannedLargeExpenseAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
