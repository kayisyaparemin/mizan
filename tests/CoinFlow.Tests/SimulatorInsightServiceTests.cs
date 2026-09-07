using CoinFlow.Application.Services;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Tests;

public sealed class SimulatorInsightServiceTests
{
    private readonly SimulatorInsightService _service = new();

    [Fact]
    public void InterestComparison_KeepsCardAndDeficitInterestApart()
    {
        // Kartı erkenden kapatan bir senaryo kart faizini düşürürken parayı
        // erken çıkardığı için açık faizini yükseltebilir. Tek toplam bu ters
        // hareketi gizler; kalemler ayrı kalmalı (I8).
        var rows = SimulatorInsightService.BuildInterestComparison(
            new ProjectionInterestSummary(7_081m, 6_236m),
            new ProjectionInterestSummary(941m, 10_858m));

        Assert.Equal(3, rows.Count);

        var card = rows[0];
        Assert.Equal(-6_140m, card.DifferenceAmount);
        Assert.True(card.IsSaving);
        Assert.False(card.IsExtra);
        Assert.False(card.IsTotal);

        var deficit = rows[1];
        Assert.Equal(4_622m, deficit.DifferenceAmount);
        Assert.True(deficit.IsExtra);
        Assert.StartsWith("+", deficit.Difference);

        var total = rows[2];
        Assert.True(total.IsTotal);
        Assert.Equal(-1_518m, total.DifferenceAmount);
        Assert.True(total.IsSaving);
        Assert.Contains("→", total.Transition);
    }

    [Fact]
    public void CashChart_AlwaysKeepsZeroInsideTheScale()
    {
        // Her iki uç da artıdayken bile sıfır ölçekte kalmalı: grafiğin
        // taşıdığı asıl bilgi sıfır çizgisi.
        var series = SimulatorInsightService.BuildCashChart(
        [
            Row(new DateOnly(2026, 9, 10), opening: 0m, income: 0m, ending: 40_000m),
            Row(new DateOnly(2026, 10, 10), opening: 0m, income: 0m, ending: 90_000m)
        ]);

        Assert.Equal(0m, series.Minimum);
        Assert.Equal(90_000m, series.Maximum);
        Assert.False(series.CrossesZero);
        Assert.False(series.HasScenario);
    }

    [Fact]
    public void CashChart_ReportsTheMonthTheDeficitCloses()
    {
        var series = SimulatorInsightService.BuildCashChart(
        [
            Row(new DateOnly(2026, 9, 10), opening: 0m, income: 0m, ending: -80_000m),
            Row(new DateOnly(2026, 10, 10), opening: 0m, income: 0m, ending: -20_000m),
            Row(new DateOnly(2026, 11, 10), opening: 0m, income: 0m, ending: 15_000m),
            Row(new DateOnly(2026, 12, 10), opening: 0m, income: 0m, ending: 60_000m)
        ]);

        Assert.True(series.CrossesZero);
        Assert.Equal(-80_000m, series.Minimum);
        Assert.Equal(60_000m, series.Maximum);
        Assert.Equal(
            new DateOnly(2026, 11, 10),
            series.FirstNonNegativePoint!.PeriodStart);
    }

    [Fact]
    public void CashChart_WithScenario_TracksBothLinesAndScenarioRecovery()
    {
        var baseline = new[]
        {
            Row(new DateOnly(2026, 9, 10), opening: 0m, income: 0m, ending: -80_000m),
            Row(new DateOnly(2026, 10, 10), opening: 0m, income: 0m, ending: -60_000m)
        };
        var scenario = new[]
        {
            Row(new DateOnly(2026, 9, 10), opening: 0m, income: 0m, ending: 20_000m),
            Row(new DateOnly(2026, 10, 10), opening: 0m, income: 0m, ending: 45_000m)
        };

        var series = SimulatorInsightService.BuildCashChart(baseline, scenario);

        Assert.True(series.HasScenario);
        Assert.Equal(-80_000m, series.Minimum);
        Assert.Equal(45_000m, series.Maximum);
        Assert.Equal(-80_000m, series.Points[0].Baseline);
        Assert.Equal(20_000m, series.Points[0].Scenario);
        // Kapanış senaryo çizgisine göre raporlanır, baza göre değil.
        Assert.Equal(
            new DateOnly(2026, 9, 10),
            series.FirstNonNegativePoint!.PeriodStart);
    }

    [Fact]
    public void CashChart_Range_ZoomsWithoutChangingTheVisibleValues()
    {
        var periods = new[]
        {
            Row(new DateOnly(2026, 9, 10), opening: 0m, income: 0m, ending: -80_000m),
            Row(new DateOnly(2026, 10, 10), opening: 0m, income: 0m, ending: -20_000m),
            Row(new DateOnly(2026, 11, 10), opening: 0m, income: 0m, ending: 15_000m),
            Row(new DateOnly(2026, 12, 10), opening: 0m, income: 0m, ending: 500_000m)
        };

        var full = SimulatorInsightService.BuildCashChart(periods);
        var zoomed = SimulatorInsightService.BuildCashChart(periods, take: 2);

        Assert.Equal(4, full.Points.Count);
        Assert.Equal(2, zoomed.Points.Count);
        // Görünen noktalar aynı kalır; yalnızca ölçek pencereye daralır.
        Assert.Equal(full.Points[0].Baseline, zoomed.Points[0].Baseline);
        Assert.Equal(full.Points[1].Baseline, zoomed.Points[1].Baseline);
        Assert.Equal(500_000m, full.Maximum);
        Assert.Equal(0m, zoomed.Maximum);
        Assert.Equal(-80_000m, zoomed.Minimum);
    }

    [Fact]
    public void CashChart_WindowedSeries_CanHideARecoveryTheFullSeriesHas()
    {
        // Başlığın pencereden değil tam seriden okunması gerektiğinin sebebi:
        // 3 aya daraltınca açık kapanmıyormuş gibi görünüyor, oysa plan Kasım'da
        // artıya geçiyor.
        var periods = new[]
        {
            Row(new DateOnly(2026, 9, 10), opening: 0m, income: 0m, ending: -80_000m),
            Row(new DateOnly(2026, 10, 10), opening: 0m, income: 0m, ending: -40_000m),
            Row(new DateOnly(2026, 11, 10), opening: 0m, income: 0m, ending: 25_000m)
        };

        var windowed = SimulatorInsightService.BuildCashChart(periods, take: 2);
        var full = SimulatorInsightService.BuildCashChart(periods);

        Assert.Null(windowed.FirstNonNegativePoint);
        Assert.Equal(
            new DateOnly(2026, 11, 10),
            full.FirstNonNegativePoint!.PeriodStart);
    }

    [Fact]
    public void CashChart_RangeLargerThanData_ReturnsEverything()
    {
        var periods = new[]
        {
            Row(new DateOnly(2026, 9, 10), opening: 0m, income: 0m, ending: 10_000m)
        };

        var series = SimulatorInsightService.BuildCashChart(periods, take: 12);

        Assert.Single(series.Points);
    }

    [Fact]
    public void CashChart_WithoutPeriods_IsEmpty()
    {
        var series = SimulatorInsightService.BuildCashChart([]);

        Assert.False(series.HasData);
        Assert.Null(series.FirstNonNegativePoint);
    }

    [Fact]
    public void CashChart_WithoutScenario_DrawsTheBaselineOnTheScenarioTrack()
    {
        // Hiçbir koşul açık değilken simülatör tek çizgi çiziyor. Çizim
        // tarafı (CashProjectionChartDrawable) HasScenario false iken
        // point.Scenario'yu okuduğu için senaryosuz seride bu alan baz
        // değeri taşımalı; taşımazsa grafik sıfır çizgisine yapışır.
        var series = SimulatorInsightService.BuildCashChart(
        [
            Row(new DateOnly(2026, 9, 10), opening: 0m, income: 0m, ending: -80_000m),
            Row(new DateOnly(2026, 10, 10), opening: 0m, income: 0m, ending: 25_000m)
        ]);

        Assert.False(series.HasScenario);
        Assert.All(series.Points, point =>
            Assert.Equal(point.Baseline, point.Scenario));
        Assert.Equal(-80_000m, series.Minimum);
        Assert.Equal(25_000m, series.Maximum);
        // Açığın kapandığı ay senaryosuz modda da okunabilmeli: başlık bu
        // moda da bakıyor.
        Assert.Equal(
            new DateOnly(2026, 10, 10),
            series.FirstNonNegativePoint!.PeriodStart);
    }

    [Fact]
    public void InterestComparison_CountsFinancingCostAsInterest()
    {
        // 150.000 çekip 180.000 ödüyorsan aradaki 30.000 faizdir. Tabloya
        // katılmazsa kredi, KMH faizini sıfırladığı için faiz düşürüyormuş
        // gibi görünür; oysa toplam yük artıyor.
        var rows = SimulatorInsightService.BuildInterestComparison(
            new ProjectionInterestSummary(7_081m, 6_236m),
            new ProjectionInterestSummary(7_081m, 0m),
            scenarioFinancingCost: 30_000m);

        Assert.Equal(4, rows.Count);

        var financing = rows[2];
        Assert.Equal("Kredi finansman maliyeti", financing.Label);
        Assert.Equal(30_000m, financing.DifferenceAmount);
        Assert.True(financing.IsExtra);

        var total = rows[3];
        Assert.True(total.IsTotal);
        Assert.Equal(23_764m, total.DifferenceAmount);
        Assert.True(total.IsExtra);
        Assert.Contains("37.081,00", total.Transition);
    }

    [Fact]
    public void InterestComparison_OmitsFinancingRowWhenThereIsNoLoan()
    {
        var rows = SimulatorInsightService.BuildInterestComparison(
            new ProjectionInterestSummary(7_081m, 6_236m),
            new ProjectionInterestSummary(941m, 10_858m));

        Assert.Equal(3, rows.Count);
        Assert.DoesNotContain(
            rows,
            x => x.Label == "Kredi finansman maliyeti");
    }

    [Fact]
    public void InterestComparison_SaysUnchangedInsteadOfZero()
    {
        // Fark sütununda "0,00 TL" yazmak "bu faiz yok" diye okunuyordu.
        var rows = SimulatorInsightService.BuildInterestComparison(
            new ProjectionInterestSummary(7_324.30m, 0m),
            new ProjectionInterestSummary(7_324.30m, 0m));

        Assert.All(rows, row =>
        {
            Assert.Equal(0m, row.DifferenceAmount);
            Assert.Equal("Değişmiyor", row.Difference);
            Assert.False(row.IsSaving);
            Assert.False(row.IsExtra);
        });
        Assert.Equal("7.324,30 TL → 7.324,30 TL", rows[0].Transition);
    }

    [Fact]
    public void PeriodNeed_ComposesCashRequirementsWithoutOpeningState()
    {
        var row = Row(
            new DateOnly(2027, 3, 10),
            opening: 50_000m,
            income: 100_000m,
            loan: 10_000m,
            creditCard: 20_000m,
            temporary: 5_000m,
            installment: 7_000m,
            other: 3_000m,
            living: 30_000m,
            large: 15_000m,
            deficitInterest: 1_000m,
            ending: 59_000m);

        var view = _service.Build([row]).Periods[0];

        Assert.Equal(91_000m, view.NeedTotal);
        Assert.Equal(9_000m, view.IncomeCoverage);
        Assert.Equal("Gelirlerden kalan", view.CoverageLabel);
        Assert.DoesNotContain(
            view.NeedBreakdown,
            x => x.Label.Contains("Dönem başı", StringComparison.Ordinal));
        Assert.Equal(91_000m,
            view.NeedBreakdown.Single(x => x.Label == "Toplam").Amount);
    }

    [Fact]
    public void IncomeInsufficient_DoesNotBecomeFinancingDeficitWhenOpeningCoversGap()
    {
        var row = Row(
            new DateOnly(2027, 3, 10),
            opening: 50_000m,
            income: 100_000m,
            mandatory: 90_000m,
            living: 30_000m,
            ending: 30_000m);

        var summary = _service.Build([row]);
        var view = summary.Periods[0];

        Assert.Equal(120_000m, view.NeedTotal);
        Assert.Equal(-20_000m, view.IncomeCoverage);
        Assert.Equal("Gelirlerin karşılamadığı", view.CoverageLabel);
        Assert.NotNull(summary.FirstIncomeInsufficientPeriod);
        Assert.Null(summary.FirstDeficitPeriod);
        Assert.Contains(
            summary.NarrativeInsights,
            x => x.Contains("dönem başı durumundan", StringComparison.Ordinal));
        Assert.DoesNotContain(
            summary.NarrativeInsights,
            x => x.Contains("Mevcut", StringComparison.OrdinalIgnoreCase) ||
                 x.Contains("Yeni Plan", StringComparison.OrdinalIgnoreCase) ||
                 x.Contains("fark", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FinancingDeficit_ReportsRecoveryInsideHorizon()
    {
        var september = Row(
            new DateOnly(2026, 9, 10),
            opening: 0m,
            income: 50_000m,
            mandatory: 40_000m,
            living: 35_000m,
            deficitInterest: 1_250m,
            ending: -26_250m);
        var october = Row(
            new DateOnly(2026, 10, 10),
            opening: -26_250m,
            income: 70_000m,
            mandatory: 20_000m,
            living: 40_000m,
            ending: -16_250m);
        var november = Row(
            new DateOnly(2026, 11, 10),
            opening: -16_250m,
            income: 80_000m,
            mandatory: 10_000m,
            living: 30_000m,
            ending: 23_750m);

        var summary = _service.Build([september, october, november]);

        Assert.Equal(september.PeriodStart,
            summary.FirstDeficitPeriod?.Projection.PeriodStart);
        Assert.Equal(november.PeriodStart,
            summary.DeficitRecoveryPeriod?.Projection.PeriodStart);
        Assert.Contains(
            summary.NarrativeInsights,
            x => x.Contains("finansman açığı oluşuyor", StringComparison.Ordinal));
        Assert.Contains(
            summary.NarrativeInsights,
            x => x.Contains("kapanması bekleniyor", StringComparison.Ordinal));
    }

    [Fact]
    public void HighestNeedLowestEndingAndRecovery_AreDeterministicAndDeduplicated()
    {
        var january = Row(
            new DateOnly(2027, 1, 10),
            opening: 70_000m,
            income: 100_000m,
            mandatory: 40_000m,
            living: 30_000m,
            ending: 100_000m);
        var february = Row(
            new DateOnly(2027, 2, 10),
            opening: 100_000m,
            income: 100_000m,
            mandatory: 95_000m,
            living: 45_000m,
            ending: 60_000m);
        var march = Row(
            new DateOnly(2027, 3, 10),
            opening: 60_000m,
            income: 100_000m,
            mandatory: 40_000m,
            living: 30_000m,
            ending: 90_000m);
        var april = Row(
            new DateOnly(2027, 4, 10),
            opening: 90_000m,
            income: 100_000m,
            mandatory: 30_000m,
            living: 30_000m,
            ending: 130_000m);

        var summary = _service.Build([january, february, march, april]);

        Assert.Equal(february.PeriodStart,
            summary.HighestNeedPeriod.Projection.PeriodStart);
        Assert.Equal(february.PeriodStart,
            summary.LowestEndingPeriod.Projection.PeriodStart);
        Assert.Equal(march.PeriodStart,
            summary.BurdenReliefPeriod?.Projection.PeriodStart);
        Assert.All(summary.Periods,
            period => Assert.True(period.InsightChips.Count <= 2));
    }

    [Fact]
    public void RepresentativeTimelineStory_DoesNotNarrateScenarioInputs()
    {
        var plan = TestFactory.CanonicalPlan();
        var card = Assert.Single(plan.CreditCards);
        var calculator = new SimulationCalculator(
            TestFactory.ProjectionCalculator(),
            new InstallmentScheduleCalculator());
        var result = calculator.Calculate(
            plan,
            new DateOnly(2026, 8, 20),
            new SimulationRequest(
                SimulationScenarioType.CreditCardInstallmentPurchase,
                "Beyaz eşya",
                120_000m,
                new DateOnly(2026, 9, 24),
                9,
                CreditCardId: card.Id));

        var summary = _service.Build(result.Scenario);
        var text = string.Join(" ", summary.NarrativeInsights);

        Assert.DoesNotContain("ekledin", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("taksit ek", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Mevcut Plan", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Yeni Plan", text, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(summary.NarrativeInsights);
        Assert.NotEmpty(summary.Periods);
    }

    private static SalaryPeriodProjection Row(
        DateOnly start,
        decimal opening,
        decimal income,
        decimal mandatory = 0m,
        decimal loan = 0m,
        decimal creditCard = 0m,
        decimal temporary = 0m,
        decimal installment = 0m,
        decimal other = 0m,
        decimal living = 0m,
        decimal large = 0m,
        decimal deficitInterest = 0m,
        decimal ending = 0m)
    {
        var mandatoryTotal = mandatory == 0m
            ? loan + creditCard + temporary + installment + other
            : mandatory;
        var end = CalendarRules.AddMonthsKeepingDay(start, 1, 10);
        return new SalaryPeriodProjection(
            start,
            end,
            income,
            0m,
            income,
            loan,
            creditCard,
            temporary,
            installment,
            other,
            mandatoryTotal,
            income - mandatoryTotal,
            living,
            income - mandatoryTotal - living - large,
            large,
            opening,
            ending,
            IsEstimatedCardPayment: false,
            HasUndeterminedCardPayment: false,
            HasDeficit: ending < 0m,
            IncomeItems: [],
            MandatoryItems: [],
            LargeExpenseItems: [],
            CardPaymentStatuses: [],
            PaymentAssignmentMode: PaymentAssignmentMode.UpcomingPeriod,
            PaymentWindowStart: start,
            PaymentWindowEnd: end.AddDays(-1),
            EndingProjectedSavingsBeforeDeficitInterest:
                ending + deficitInterest,
            DeficitFinancingInterest: deficitInterest);
    }
}
