using CoinFlow.Domain.Models;

namespace CoinFlow.Domain.Calculations;

public enum IncomeSourceType
{
    Salary,
    OneTimeIncome
}

public sealed record IncomeProjectionItem(
    string Name,
    IncomeSourceType Type,
    DateOnly SourceDate,
    decimal Amount);

public sealed record IncomeProjectionSummary(
    IReadOnlyList<IncomeProjectionItem> Items,
    decimal SalaryIncome,
    decimal OtherIncome,
    decimal TotalIncome);

public sealed class IncomeProjectionCalculator(SalaryResolver salaryResolver)
{
    /// <param name="prePeriodIncomeStart">
    /// Verildiğinde, bu tarih ile dönem başlangıcı arasına düşen tek seferlik
    /// gelirler de bu döneme yazılır. Yalnızca ilk dönem için anlamlıdır:
    /// projeksiyon ufku ilk maaş gününde başladığı için, `[çapa, ilk maaş)`
    /// aralığındaki gelir hiçbir dönemle eşleşmiyor ve sessizce kayboluyordu.
    /// Çapadan önceki gelir kapsam dışıdır; o para zaten açılış bakiyesinde (I3).
    /// </param>
    public IncomeProjectionSummary Calculate(
        SalaryPeriod period,
        IEnumerable<SalaryScheduleEntry> salaries,
        IEnumerable<OneTimeIncome> otherIncomes,
        DateOnly? prePeriodIncomeStart = null)
    {
        var items = new List<IncomeProjectionItem>();
        var salary = salaryResolver.Resolve(period.Start, salaries);
        if (salary is not null)
        {
            items.Add(new IncomeProjectionItem(
                string.IsNullOrWhiteSpace(salary.Description) ? "Maaş" : salary.Description,
                IncomeSourceType.Salary,
                period.Start,
                salary.Amount));
        }

        items.AddRange(otherIncomes
            .Where(x => period.Contains(x.ExactDate) ||
                        IsWithinPrePeriodWindow(
                            x.ExactDate,
                            prePeriodIncomeStart,
                            period.Start))
            .Select(x => new IncomeProjectionItem(
                string.IsNullOrWhiteSpace(x.Description) ? "Diğer gelir" : x.Description,
                IncomeSourceType.OneTimeIncome,
                x.ExactDate,
                x.Amount)));

        var ordered = items.OrderBy(x => x.SourceDate).ThenBy(x => x.Name).ToArray();
        var salaryTotal = ordered.Where(x => x.Type == IncomeSourceType.Salary).Sum(x => x.Amount);
        var otherTotal = ordered.Where(x => x.Type == IncomeSourceType.OneTimeIncome).Sum(x => x.Amount);
        return new IncomeProjectionSummary(ordered, salaryTotal, otherTotal, salaryTotal + otherTotal);
    }

    private static bool IsWithinPrePeriodWindow(
        DateOnly incomeDate,
        DateOnly? windowStart,
        DateOnly periodStart) =>
        windowStart is { } start &&
        incomeDate >= start &&
        incomeDate < periodStart;
}

