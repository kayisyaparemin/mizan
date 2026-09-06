using System.Globalization;
using CoinFlow.Domain.Calculations;

namespace CoinFlow.Application.Models;

public sealed record SimulatorProjectionSummary(
    IReadOnlyList<string> NarrativeInsights,
    IReadOnlyList<SimulatorSummaryMetric> KeyMetrics,
    IReadOnlyList<SimulatorPeriodView> Periods,
    SimulatorPeriodView HighestNeedPeriod,
    SimulatorPeriodView LowestEndingPeriod,
    SimulatorPeriodView? FirstIncomeInsufficientPeriod,
    SimulatorPeriodView? FirstDeficitPeriod,
    SimulatorPeriodView? DeficitRecoveryPeriod,
    SimulatorPeriodView? BurdenReliefPeriod,
    decimal EndingSituation)
{
    public bool HasKeyMetrics => KeyMetrics.Count > 0;
}

/// <summary>
/// Grafikteki tek bir dönem. Nakit işaretli bir sayıdır ve
/// <c>UpcomingPeriod</c> modunda gerçek banka bakiyesine eşittir; bu yüzden
/// sıfırın altı ve üstü birlikte çizilir.
/// </summary>
public sealed record SimulatorChartPoint(
    DateOnly PeriodStart,
    string Label,
    decimal Baseline,
    decimal Scenario);

public sealed record SimulatorChartSeries(
    IReadOnlyList<SimulatorChartPoint> Points,
    decimal Minimum,
    decimal Maximum,
    bool HasScenario)
{
    public static SimulatorChartSeries Empty { get; } =
        new([], 0m, 0m, false);

    public bool HasData => Points.Count > 0;

    /// <summary>
    /// Çizgi sıfırı kesiyorsa açık kapanıyor demektir; grafiğin taşıdığı asıl
    /// bilgi bu.
    /// </summary>
    public bool CrossesZero => Minimum < 0m && Maximum > 0m;

    /// <summary>Açığın kapandığı ilk dönem; hiç kapanmıyorsa null.</summary>
    public SimulatorChartPoint? FirstNonNegativePoint => Points
        .FirstOrDefault(x =>
            (HasScenario ? x.Scenario : x.Baseline) >= 0m);
}

// Faiz karşılaştırma tablosunun tek satırı. Motor kart faizi ile finansman
// açığı faizini ayrı tutar (I8); sunumda da ayrı kalmaları gerekiyor, çünkü
// bir senaryo birini düşürürken diğerini yükseltebilir.
public sealed record SimulatorInterestRow(
    string Label,
    string Baseline,
    string Scenario,
    string Difference,
    decimal DifferenceAmount,
    bool IsTotal = false)
{
    public bool IsSaving => DifferenceAmount < 0m;
    public bool IsExtra => DifferenceAmount > 0m;
    public string Transition => $"{Baseline} → {Scenario}";
}

public sealed record SimulatorSummaryMetric(
    string Label,
    string Value,
    string Detail = "",
    DetailSemanticType Semantic = DetailSemanticType.Neutral)
{
    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);
    public bool IsIncome => Semantic == DetailSemanticType.Income;
    public bool IsMandatory => Semantic == DetailSemanticType.Mandatory;
    public bool IsSavings => Semantic == DetailSemanticType.Savings;
    public bool IsDeficit => Semantic == DetailSemanticType.Deficit;
    public bool IsInterest => Semantic == DetailSemanticType.Interest;
    public bool IsExpense => Semantic == DetailSemanticType.Expense;
    public bool IsProjection => Semantic == DetailSemanticType.Projection;
}

public sealed record SimulatorPeriodView(
    SalaryPeriodProjection Projection,
    string Period,
    string Assignment,
    decimal OpeningSituation,
    decimal Income,
    decimal NeedTotal,
    decimal IncomeCoverage,
    decimal EndingSituation,
    IReadOnlyList<DetailMetric> NeedBreakdown,
    IReadOnlyList<string> InsightChips)
{
    private static readonly CultureInfo TurkishCulture =
        CultureInfo.GetCultureInfo("tr-TR");

    public string OpeningText => Money(OpeningSituation);
    public string IncomeText => Money(Income);
    public string NeedText => Money(NeedTotal);
    public string CoverageAmountText => Money(Math.Abs(IncomeCoverage));
    public string EndingText => Money(EndingSituation);
    public string CoverageLabel => IncomeCoverage >= 0m
        ? "Gelirlerden kalan"
        : "Gelirlerin karşılamadığı";
    public bool CoverageIsNegative => IncomeCoverage < 0m;
    public bool EndingIsNegative => EndingSituation < 0m;
    public string PrimaryInsight => InsightChips.FirstOrDefault() ?? string.Empty;
    public bool HasPrimaryInsight => InsightChips.Count > 0;
    public string SecondaryInsight => InsightChips.Skip(1).FirstOrDefault() ?? string.Empty;
    public bool HasSecondaryInsight => InsightChips.Count > 1;

    private static string Money(decimal value) =>
        $"{value.ToString("N2", TurkishCulture)} TL";
}
