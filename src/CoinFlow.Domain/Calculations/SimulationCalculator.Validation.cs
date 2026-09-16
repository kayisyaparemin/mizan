using System.Globalization;
using CoinFlow.Domain.Models;

namespace CoinFlow.Domain.Calculations;

public sealed partial class SimulationCalculator
{
    /// <param name="projectionAnchorDate">
    /// Verildiğinde, projeksiyon ufkunun dışına düşen tek seferlik gelir
    /// reddedilir. Çapadan önceki para zaten mevcut tutarın içindedir (I3);
    /// sessizce yutulmasındansa kullanıcıya söylenir.
    /// </param>
    public static void Validate(
        SimulationRequest request,
        DateOnly? projectionAnchorDate = null)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new InvalidOperationException("Plan adı gereklidir.");
        }

        if (request.Type == SimulationScenarioType.FutureIncome &&
            projectionAnchorDate is { } anchor &&
            request.StartDate < anchor)
        {
            throw new InvalidOperationException(
                $"Gelir tarihi son güncelleme tarihinden ({anchor:dd.MM.yyyy}) " +
                "önce olamaz. Bu tarihte gelen para zaten mevcut tutarına " +
                "dahil olmalı; onu güncelle.");
        }

        if (request.Type is SimulationScenarioType.LoanEarlyClosure or
                SimulationScenarioType.LoanPartialPrepayment &&
            request.LoanId is null)
        {
            throw new InvalidOperationException(
                "Erken ödeme için bir kredi seçmelisin.");
        }

        if (request.Type == SimulationScenarioType.LoanPartialPrepayment &&
            request.PrepaymentMode is not (LoanPrepaymentMode.ReduceTerm or
                LoanPrepaymentMode.ReduceInstallment))
        {
            throw new InvalidOperationException(
                "Ara ödemede vadenin mi taksitin mi azalacağını seçmelisin.");
        }

        if (request.Type is not SimulationScenarioType.PaymentStrategyChange and
            not SimulationScenarioType.CreditCardPaymentMode and
            not SimulationScenarioType.LoanEarlyClosure &&
            request.Amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Plan tutarı 0'dan büyük olmalı.");
        }

        if (request.Type == SimulationScenarioType.PaymentStrategyChange &&
            (request.NewPaymentAssignmentMode is null ||
             request.EffectiveSalaryDate is null))
        {
            throw new InvalidOperationException(
                "Yeni düzen ve geçerli dönem tarihi seçilmelidir.");
        }

        var needsCount = request.Type is
            SimulationScenarioType.CreditCardInstallmentPurchase or
            SimulationScenarioType.FinancingLoan or
            SimulationScenarioType.CashDebt or
            SimulationScenarioType.RecurringPayment;
        if (needsCount && request.PaymentCount is < 1 or > 120)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Ödeme sayısı 1 ile 120 arasında olmalıdır.");
        }

        if (request.Type == SimulationScenarioType.CreditCardPaymentMode)
        {
            if (request.CreditCardId is null)
            {
                throw new InvalidOperationException(
                    "Ödeme şekli planı için bir kredi kartı seçmelisin.");
            }

            if (request.CardPaymentType is CreditCardPaymentType.FixedAmount)
            {
                throw new InvalidOperationException(
                    "Kart ödeme şekli yalnızca asgari veya tamamı olabilir.");
            }
        }

        if (request.Type == SimulationScenarioType.FinancingLoan &&
            request.TotalRepaymentAmount is null or <= 0m)
        {
            throw new InvalidOperationException(
                "Finansman için toplam geri ödeme gereklidir.");
        }

        if (request.Type == SimulationScenarioType.FinancingLoan &&
            request.FirstPaymentDate is null)
        {
            throw new InvalidOperationException(
                "Finansman için ilk ödeme tarihi gereklidir.");
        }

        if (request.FirstPaymentDate is DateOnly firstPayment &&
            firstPayment < request.StartDate &&
            request.Type is SimulationScenarioType.FinancingLoan or
                SimulationScenarioType.CashDebt or
                SimulationScenarioType.RecurringPayment)
        {
            throw new InvalidOperationException(
                "İlk ödeme tarihi başlangıç tarihinden önce olamaz.");
        }
    }

    public static void Validate(IReadOnlyList<SimulationRequest> requests)
    {
        if (requests.Count == 0)
        {
            throw new InvalidOperationException(
                "Simülasyon için en az bir koşul eklemelisin.");
        }

        foreach (var request in requests)
        {
            Validate(request);
        }

        var conflictingSalary = requests
            .Where(x => x.Type == SimulationScenarioType.SalaryChange)
            .GroupBy(x => x.StartDate)
            .FirstOrDefault(x => x.Count() > 1);
        if (conflictingSalary is not null)
        {
            throw new InvalidOperationException(
                $"{conflictingSalary.Key.ToString("dd MMMM yyyy", TurkishCulture)} için iki farklı gelir değişikliği var. Simülasyonu çalıştırmadan önce birini düzenle veya kaldır.");
        }

        var conflictingStrategy = requests
            .Where(x => x.Type == SimulationScenarioType.PaymentStrategyChange)
            .GroupBy(x => x.EffectiveSalaryDate ?? x.StartDate)
            .FirstOrDefault(x => x.Count() > 1);
        if (conflictingStrategy is not null)
        {
            throw new InvalidOperationException(
                $"{conflictingStrategy.Key.ToString("dd MMMM yyyy", TurkishCulture)} dönemi için iki farklı kullanım düzeni değişikliği var. Simülasyonu çalıştırmadan önce birini düzenle veya kaldır.");
        }
    }
}
