
namespace Mizan.Domain.Calculations;

public sealed record CashFlowPeriod(DateOnly Start, DateOnly End)
{
    public int DayCount => End.DayNumber - Start.DayNumber;
    public bool Contains(DateOnly date) => date >= Start && date < End;
}

public sealed class CashFlowPeriodCalculator
{
    public CashFlowPeriod GetPeriod(DateOnly date, int IncomeDay)
    {
        CalendarRules.ValidateDay(IncomeDay);
        var currentMonthSalaryDate = CalendarRules.ResolveDay(date.Year, date.Month, IncomeDay);
        if (date >= currentMonthSalaryDate)
        {
            return new CashFlowPeriod(
                currentMonthSalaryDate,
                CalendarRules.AddMonthsKeepingDay(currentMonthSalaryDate, 1, IncomeDay));
        }

        var previousSalaryDate = CalendarRules.AddMonthsKeepingDay(currentMonthSalaryDate, -1, IncomeDay);
        return new CashFlowPeriod(previousSalaryDate, currentMonthSalaryDate);
    }

    public IReadOnlyList<CashFlowPeriod> GetPeriods(DateOnly asOf, int IncomeDay, int count)
    {
        if (count is < 1 or > 60)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Dönem sayısı 1 ile 60 arasında olmalıdır.");
        }

        var first = GetPeriod(asOf, IncomeDay);
        return Enumerable.Range(0, count)
            .Select(index =>
            {
                var start = CalendarRules.AddMonthsKeepingDay(first.Start, index, IncomeDay);
                var end = CalendarRules.AddMonthsKeepingDay(first.Start, index + 1, IncomeDay);
                return new CashFlowPeriod(start, end);
            })
            .ToArray();
    }

    public DateOnly GetFirstPeriodStartOnOrAfter(
        DateOnly anchorDate,
        int IncomeDay)
    {
        var containingPeriod = GetPeriod(anchorDate, IncomeDay);
        return anchorDate == containingPeriod.Start
            ? containingPeriod.Start
            : containingPeriod.End;
    }

    public DateOnly GetFirstPeriodStartStrictlyAfter(
        DateOnly anchorDate,
        int IncomeDay)
    {
        var first = GetFirstPeriodStartOnOrAfter(anchorDate, IncomeDay);
        return first > anchorDate
            ? first
            : CalendarRules.AddMonthsKeepingDay(first, 1, IncomeDay);
    }

    public DateOnly GetNextSettlementDate(
        DateOnly snapshotDate,
        int IncomeDay) => GetPeriod(snapshotDate, IncomeDay).End;
}
