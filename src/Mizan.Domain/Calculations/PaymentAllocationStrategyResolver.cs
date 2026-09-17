using Mizan.Domain.Models;

namespace Mizan.Domain.Calculations;

public sealed class PaymentAllocationStrategyResolver(
    CashFlowPeriodCalculator CashFlowPeriodCalculator)
{
    public CashFlowAllocationStrategy Resolve(
        DateOnly PeriodDate,
        IEnumerable<CashFlowAllocationStrategy> history)
    {
        var strategy = history
            .Where(x => x.EffectiveFromPeriodDate <= PeriodDate)
            .OrderByDescending(x => x.EffectiveFromPeriodDate)
            .ThenByDescending(x => x.CreatedAt)
            .FirstOrDefault();
        return strategy ?? throw new InvalidOperationException(
            $"{PeriodDate:dd.MM.yyyy} dönemi için dönem kullanım düzeni bulunamadı.");
    }

    public bool IsPeriodStartDate(DateOnly date, int IncomeDay) =>
        CashFlowPeriodCalculator.GetPeriod(date, IncomeDay).Start == date;

    public void ValidateHistory(
        IEnumerable<CashFlowAllocationStrategy> history,
        int IncomeDay,
        DateOnly firstProjectionPeriod)
    {
        var strategies = history.ToArray();
        if (strategies.Length == 0)
        {
            throw new InvalidOperationException(
                "En az bir dönem kullanım düzeni gereklidir.");
        }

        if (strategies.Any(x =>
                !Enum.IsDefined(x.Mode) ||
                !IsPeriodStartDate(x.EffectiveFromPeriodDate, IncomeDay)))
        {
            throw new InvalidOperationException(
                "Dönem kullanım düzeninin geçerlilik tarihi geçerli bir dönem tarihi olmalıdır.");
        }

        if (strategies
            .GroupBy(x => x.EffectiveFromPeriodDate)
            .Any(x => x.Count() > 1))
        {
            throw new InvalidOperationException(
                "Aynı dönem tarihi için birden fazla kullanım düzeni olamaz.");
        }

        if (strategies.All(x =>
                x.EffectiveFromPeriodDate > firstProjectionPeriod))
        {
            throw new InvalidOperationException(
                "İlk dönemi kapsayan bir dönem kullanım düzeni gereklidir.");
        }
    }
}
