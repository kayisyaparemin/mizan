using System.Globalization;
using CoinFlow.Application.Models;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Services;

public sealed partial class SimulatorInsightService
{
    private static string SalaryText(DateOnly salaryDate) =>
        $"{salaryDate.ToString("dd MMMM yyyy", TurkishCulture)} Dönemi";

    private static string AssignmentText(SalaryPeriodProjection row)
    {
        var action = row.PaymentAssignmentMode ==
                     PaymentAssignmentMode.PreviousPeriod
            ? "ödemelerini kapatır"
            : "ödemelerini karşılar";
        return $"{row.PaymentWindowStart.ToString("dd MMM", TurkishCulture)}–" +
               $"{row.PaymentWindowEnd.ToString("dd MMM", TurkishCulture)} {action}";
    }

    private static string Month(SimulatorPeriodView? period) =>
        period?.Projection.PeriodStart.ToString("MMMM yyyy", TurkishCulture) ??
        string.Empty;

    private static bool SamePeriod(
        SimulatorPeriodView? left,
        SimulatorPeriodView? right) =>
        left is not null &&
        right is not null &&
        left.Projection.PeriodStart == right.Projection.PeriodStart;

    private static string Money(decimal value) =>
        $"{value.ToString("N2", TurkishCulture)} TL";

    // Bir senaryo faizi tek yönde hareket ettirmez: kartı erken kapatmak kart
    // faizini düşürürken parayı erkenden çıkardığı için açık faizini
    // yükseltebilir. Tek toplam bunu gizlediğinden kalemler ayrı gösterilir.
    /// <param name="scenarioFinancingCost">
    /// Denenen plandaki kredinin geri ödeme ile anapara farkı. Bu da faizdir:
    /// 120.000 çekip 145.000 ödüyorsan 25.000'i faiz yüküdür. Projeksiyonun
    /// dönem sonu rakamı bunu zaten içerir, ama kart/KMH faizinden ayrı
    /// tutulduğu için tabloya elle katılması gerekir; katılmazsa krediyi
    /// faiz düşürüyormuş gibi gösterir.
    /// </param>
    public static IReadOnlyList<SimulatorInterestRow> BuildInterestComparison(
        ProjectionInterestSummary baseline,
        ProjectionInterestSummary scenario,
        decimal? scenarioFinancingCost = null)
    {
        var financing = scenarioFinancingCost ?? 0m;
        var rows = new List<SimulatorInterestRow>
        {
            InterestRow(
                "Kredi kartı faizi",
                baseline.CreditCardInterest,
                scenario.CreditCardInterest),
            InterestRow(
                "Finansman açığı (KMH) faizi",
                baseline.DeficitFinancingInterest,
                scenario.DeficitFinancingInterest)
        };

        if (financing > 0m)
        {
            rows.Add(InterestRow("Kredi finansman maliyeti", 0m, financing));
        }

        rows.Add(InterestRow(
            "Toplam faiz yükü",
            baseline.TotalInterestCost,
            scenario.TotalInterestCost + financing,
            isTotal: true));
        return rows;
    }

    private static SimulatorInterestRow InterestRow(
        string label,
        decimal baseline,
        decimal scenario,
        bool isTotal = false)
    {
        var difference = scenario - baseline;
        // Sağdaki rakam farktır, faizin kendisi değil. Sıfırken "0,00 TL"
        // yazmak "bu faiz yok" diye okunuyordu; değişmediğini söylüyoruz.
        var differenceText = difference switch
        {
            0m => "Değişmiyor",
            > 0m => $"+{Money(difference)}",
            _ => Money(difference)
        };
        return new SimulatorInterestRow(
            label,
            Money(baseline),
            Money(scenario),
            differenceText,
            difference,
            isTotal);
    }
}
