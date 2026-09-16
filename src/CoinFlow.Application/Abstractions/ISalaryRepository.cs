using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Abstractions;

public interface ISalaryRepository
{
    Task<IReadOnlyList<SalaryScheduleEntry>> GetSalaryScheduleAsync(
        CancellationToken cancellationToken = default);
    Task UpsertSalaryAsync(
        SalaryScheduleEntry entry,
        CancellationToken cancellationToken = default);
    Task DeleteSalaryAsync(Guid id, CancellationToken cancellationToken = default);
}
