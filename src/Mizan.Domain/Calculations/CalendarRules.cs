namespace Mizan.Domain.Calculations;

public static class CalendarRules
{
    public static DateOnly ResolveDay(int year, int month, int preferredDay)
    {
        ValidateDay(preferredDay);
        return new DateOnly(year, month, Math.Min(preferredDay, DateTime.DaysInMonth(year, month)));
    }

    public static DateOnly AddMonthsKeepingDay(DateOnly date, int months, int preferredDay) =>
        ResolveDay(date.AddMonths(months).Year, date.AddMonths(months).Month, preferredDay);

    public static void ValidateDay(int day)
    {
        if (day is < 1 or > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(day), "Gün 1 ile 31 arasında olmalıdır.");
        }
    }
}
