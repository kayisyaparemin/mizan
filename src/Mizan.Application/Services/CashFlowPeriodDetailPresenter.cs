using System.Globalization;
using Mizan.Application.Models;
using Mizan.Domain.Calculations;
using Mizan.Domain.Models;

namespace Mizan.Application.Services;

public sealed partial class CashFlowPeriodDetailPresenter
{
    private static readonly CultureInfo TurkishCulture =
        CultureInfo.GetCultureInfo("tr-TR");

    public SalaryPeriodDetailData Build(
        CashFlowPeriodProjection scenario,
        CashFlowPeriodProjection? baseline = null,
        bool isSimulationScenario = false)
    {
        if (baseline is not null && baseline.Period != scenario.Period)
        {
            throw new InvalidOperationException(
                "Mevcut Plan ile Yeni Plan aynı döneme ait olmalıdır.");
        }

        var flow = BuildFlow(scenario);
        var mandatory = BuildMandatoryRows(scenario);
        var interest = BuildInterestRows(scenario);
        var cardInterest = scenario.CardPaymentStatuses
            .Where(x => x.CarryInterest > 0m)
            .Select(x => new DetailMetric(
                x.CardName,
                x.CarryInterest,
                DetailSemanticType.Interest))
            .ToArray();
        var transition = BuildTransitionRows(scenario);
        // Ödeme şekli yalnızca gerçek plan görüntülenirken değiştirilebilir:
        // simülasyon sonucu geçici, kapanmış dönem ise yeniden hesaplanmaz (I6).
        var payments = BuildPayments(scenario, !isSimulationScenario);
        var comparison = baseline is null || isSimulationScenario
            ? []
            : BuildComparisonRows(baseline, scenario);
        var periodNeed = SimulatorProjectionMath.PeriodNeed(scenario);
        var incomeCoverage = scenario.TotalIncome - periodNeed;

        return new SalaryPeriodDetailData(
            scenario.PeriodStart,
            $"{scenario.PeriodStart.ToString("dd MMMM yyyy", TurkishCulture)} Dönemi",
            PaymentWindowText(scenario),
            scenario.CashFlowAllocationMode == CashFlowAllocationMode.PreviousPeriod
                ? "Geçmiş dönemi kapatır"
                : "Gelecek dönemi karşılar",
            scenario.IsStrategyTransition,
            isSimulationScenario,
            Summary(
                "DÖNEM BAŞI",
                scenario.OpeningProjectedBalance,
                scenario.OpeningProjectedBalance < 0m
                    ? DetailSemanticType.Deficit
                    : DetailSemanticType.Projection),
            Summary(
                "BU DÖNEM GEREKEN",
                periodNeed,
                DetailSemanticType.Mandatory),
            Summary(
                incomeCoverage >= 0m
                    ? "GELİRLERDEN KALAN"
                    : "GELİRLERİN KARŞILAMADIĞI",
                Math.Abs(incomeCoverage),
                incomeCoverage >= 0m
                    ? DetailSemanticType.Savings
                    : DetailSemanticType.Deficit),
            IncomeCoverageMessage(scenario, incomeCoverage),
            SimulatorProjectionMath.BuildNeedBreakdown(scenario),
            Summary(
                isSimulationScenario ? "DÖNEM GELİRLERİ" : "GELİR",
                scenario.TotalIncome,
                DetailSemanticType.Income),
            Summary(
                "ZORUNLU",
                scenario.MandatoryOutflow,
                DetailSemanticType.Mandatory),
            Summary(
                "DÖNEM NETİ",
                scenario.EstimatedSurplus,
                scenario.EstimatedSurplus < 0m
                    ? DetailSemanticType.Deficit
                    : DetailSemanticType.Savings),
            Summary(
                "DÖNEM SONU",
                scenario.EndingProjectedBalance,
                scenario.EndingProjectedBalance < 0m
                    ? DetailSemanticType.Deficit
                    : DetailSemanticType.Projection),
            flow,
            scenario.HasCarryOverDeficit
                ? new DetailDeficitCallout(
                    scenario.CarryOverDeficit,
                    scenario.DeficitCoveredThisPeriod,
                    scenario.RemainingCarryOverDeficit,
                    scenario.RecoveredCarryOverDeficit)
                : null,
            mandatory,
            interest,
            cardInterest,
            transition,
            payments,
            comparison,
            BuildDebugRows(scenario));
    }

    private static string IncomeCoverageMessage(
        CashFlowPeriodProjection row,
        decimal incomeCoverage)
    {
        if (incomeCoverage < 0m && row.EndingProjectedBalance >= 0m)
        {
            return $"Bu ay dönem gelirlerin ihtiyacın {Money(Math.Abs(incomeCoverage))} altında kalıyor. Fark dönem başındaki finansal durumundan karşılanıyor.";
        }

        if (row.EndingProjectedBalance < 0m)
        {
            return $"Bu dönem sonunda yaklaşık {Money(Math.Abs(row.EndingProjectedBalance))} finansman açığı oluşuyor.";
        }

        return incomeCoverage > 0m
            ? $"Dönem gelirlerinden {Money(incomeCoverage)} kalıyor."
            : "Dönem gelirleri bu ayki toplam ihtiyacı tam karşılıyor.";
    }

    private static DetailMetric Summary(
        string label,
        decimal amount,
        DetailSemanticType semantic) =>
        new(label, amount, semantic, DecimalPlaces: 0);

    private static IReadOnlyList<DetailMetric> BuildFlow(
        CashFlowPeriodProjection row)
    {
        var result = new List<DetailMetric>
        {
            new(
                "Gelir",
                row.TotalIncome,
                DetailSemanticType.Income,
                ShowPositiveSign: true),
            new(
                "Zorunlu ödemeler",
                -row.MandatoryOutflow,
                DetailSemanticType.Mandatory),
            new(
                "Zorunlular sonrası",
                row.AvailableAfterMandatory,
                DetailSemanticType.Projection,
                IsTotal: true)
        };

        if (row.OpeningProjectedBalance > 0m)
        {
            result.Add(new DetailMetric(
                "Dönem başı durumu",
                row.OpeningProjectedBalance,
                DetailSemanticType.Savings,
                ShowPositiveSign: true));
        }
        else if (row.HasCarryOverDeficit)
        {
            result.Add(new DetailMetric(
                "Devreden açık",
                -row.CarryOverDeficit,
                DetailSemanticType.Deficit));
            result.Add(new DetailMetric(
                "Açık kapatıldıktan sonra kalan",
                row.AvailableAfterCarryOverDeficit,
                DetailSemanticType.Projection));
        }

        if (row.VariableExpenseAllowance > 0m)
        {
            result.Add(new DetailMetric(
                "Tahmini yaşam gideri",
                -row.VariableExpenseAllowance,
                DetailSemanticType.Expense));
        }

        if (row.PlannedLargeCashExpenses > 0m)
        {
            result.Add(new DetailMetric(
                "Planlı büyük ödeme",
                -row.PlannedLargeCashExpenses,
                DetailSemanticType.Expense));
        }

        if (row.DeficitFinancingInterest > 0m)
        {
            result.Add(new DetailMetric(
                "Faiz yükü",
                -row.DeficitFinancingInterest,
                DetailSemanticType.Interest));
        }

        result.Add(new DetailMetric(
            "Dönem sonu tahmini durum",
            row.EndingProjectedBalance,
            row.EndingProjectedBalance < 0m
                ? DetailSemanticType.Deficit
                : DetailSemanticType.Savings,
            IsTotal: true));
        return result;
    }

    private static IReadOnlyList<DetailMetric> BuildMandatoryRows(
        CashFlowPeriodProjection row)
    {
        var candidates = new[]
        {
            new DetailMetric(
                "Krediler",
                row.LoanPayments,
                DetailSemanticType.Mandatory),
            new DetailMetric(
                "Kredi Kartları",
                row.CreditCardPayments,
                DetailSemanticType.Mandatory),
            new DetailMetric(
                "Geçici Ödeme Planları",
                row.TemporaryPayments,
                DetailSemanticType.Mandatory),
            new DetailMetric(
                "Taksit / Finansman",
                row.InstallmentPayments,
                DetailSemanticType.Mandatory),
            new DetailMetric(
                "Diğer Planlı Ödemeler",
                row.OtherScheduledPayments,
                DetailSemanticType.Mandatory)
        };
        return candidates.Where(x => x.Amount != 0m).ToArray();
    }

    private static IReadOnlyList<DetailMetric> BuildInterestRows(
        CashFlowPeriodProjection row)
    {
        if (row.TotalInterestGenerated == 0m)
        {
            return [];
        }

        var result = new List<DetailMetric>();
        if (row.CardInterestGenerated > 0m)
        {
            result.Add(new DetailMetric(
                "Devreden kart borcu faizi",
                row.CardInterestGenerated,
                DetailSemanticType.Interest));
        }

        if (row.DeficitFinancingInterest > 0m)
        {
            result.Add(new DetailMetric(
                "Finansman açığı faiz yükü",
                row.DeficitFinancingInterest,
                DetailSemanticType.Interest));
        }

        result.Add(new DetailMetric(
            "Toplam",
            row.TotalInterestGenerated,
            DetailSemanticType.Interest,
            IsTotal: true));
        return result;
    }

    private static IReadOnlyList<DetailMetric> BuildTransitionRows(
        CashFlowPeriodProjection row)
    {
        if (!row.IsStrategyTransition)
        {
            return [];
        }

        var result = new List<DetailMetric>();
        AddIfNonZero(
            result,
            "Geçmiş düzenden kapanacak",
            row.TransitionCatchUpAmount);
        AddIfNonZero(
            result,
            "Yeni dönem için ayrılacak",
            row.ForwardFundedAmount);
        if (row.MandatoryOutflow != 0m)
        {
            result.Add(new DetailMetric(
                "Toplam geçiş yükü",
                row.MandatoryOutflow,
                DetailSemanticType.Projection,
                IsTotal: true));
        }

        return result;
    }

    private static void AddIfNonZero(
        ICollection<DetailMetric> rows,
        string label,
        decimal amount)
    {
        if (amount != 0m)
        {
            rows.Add(new DetailMetric(
                label,
                amount,
                DetailSemanticType.Projection));
        }
    }


    private static string PaymentWindowText(CashFlowPeriodProjection row)
    {
        var action = row.CashFlowAllocationMode ==
                     CashFlowAllocationMode.PreviousPeriod
            ? "ödemelerini kapatır"
            : "ödemelerini karşılar";
        return $"{row.PaymentWindowStart.ToString("dd MMMM", TurkishCulture)} – " +
               $"{row.PaymentWindowEnd.ToString("dd MMMM", TurkishCulture)} {action}";
    }

    private static string Money(decimal value) =>
        $"{value.ToString("N2", TurkishCulture)} TL";
}
