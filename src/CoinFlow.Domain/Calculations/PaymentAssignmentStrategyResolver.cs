using CoinFlow.Domain.Models;

namespace CoinFlow.Domain.Calculations;

public sealed class PaymentAssignmentStrategyResolver(
    SalaryPeriodCalculator salaryPeriodCalculator)
{
    public PaymentAssignmentStrategy Resolve(
        DateOnly salaryDate,
        IEnumerable<PaymentAssignmentStrategy> history)
    {
        var strategy = history
            .Where(x => x.EffectiveFromSalaryDate <= salaryDate)
            .OrderByDescending(x => x.EffectiveFromSalaryDate)
            .ThenByDescending(x => x.CreatedAt)
            .FirstOrDefault();
        return strategy ?? throw new InvalidOperationException(
            $"{salaryDate:dd.MM.yyyy} dönemi için dönem kullanım düzeni bulunamadı.");
    }

    public bool IsSalaryDate(DateOnly date, int salaryDay) =>
        salaryPeriodCalculator.GetPeriod(date, salaryDay).Start == date;

    public void ValidateHistory(
        IEnumerable<PaymentAssignmentStrategy> history,
        int salaryDay,
        DateOnly firstProjectionSalary)
    {
        var strategies = history.ToArray();
        if (strategies.Length == 0)
        {
            throw new InvalidOperationException(
                "En az bir dönem kullanım düzeni gereklidir.");
        }

        if (strategies.Any(x =>
                !Enum.IsDefined(x.Mode) ||
                !IsSalaryDate(x.EffectiveFromSalaryDate, salaryDay)))
        {
            throw new InvalidOperationException(
                "Dönem kullanım düzeninin geçerlilik tarihi geçerli bir dönem tarihi olmalıdır.");
        }

        if (strategies
            .GroupBy(x => x.EffectiveFromSalaryDate)
            .Any(x => x.Count() > 1))
        {
            throw new InvalidOperationException(
                "Aynı dönem tarihi için birden fazla kullanım düzeni olamaz.");
        }

        if (strategies.All(x =>
                x.EffectiveFromSalaryDate > firstProjectionSalary))
        {
            throw new InvalidOperationException(
                "İlk dönemi kapsayan bir dönem kullanım düzeni gereklidir.");
        }
    }
}
