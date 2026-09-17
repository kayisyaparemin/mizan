using Mizan.Domain.Models;

namespace Mizan.Application.Abstractions;

public interface ISalaryRepository
{
    Task<IReadOnlyList<SalaryScheduleEntry>> GetSalaryScheduleAsync(
        CancellationToken cancellationToken = default);
    Task UpsertSalaryAsync(
        SalaryScheduleEntry entry,
        CancellationToken cancellationToken = default);
    Task DeleteSalaryAsync(Guid id, CancellationToken cancellationToken = default);
}
