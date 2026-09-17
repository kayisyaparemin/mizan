using Mizan.Domain.Models;

namespace Mizan.Domain.Calculations;

public sealed class IncomeResolver
{
    public SalaryScheduleEntry? Resolve(
        DateOnly periodStart,
        IEnumerable<SalaryScheduleEntry> schedule) => schedule
        .Where(x => x.EffectiveDate <= periodStart)
        .OrderByDescending(x => x.EffectiveDate)
        .ThenByDescending(x => x.Id)
        .FirstOrDefault();
}

