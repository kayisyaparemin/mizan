using System.Globalization;
using System.Reflection;
using CoinFlow.Application.Abstractions;
using CoinFlow.Application.Models;
using CoinFlow.Application.Services;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;
using CoinFlow.Infrastructure.Persistence;

namespace CoinFlow.Tests;

/// <summary>
/// Geçmiş dönemlerin tutulduğu iş kuralları: dönem planının dondurulması,
/// dönem kapanışında gerçekleşenin hesaplanması, plan/gerçek karşılaştırması
/// ve geçmiş sorgusu. Kanonik seed'in yuvarladığı ya da sıfırladığı kalemler
/// (açık faizi, kısmi dönem, sınır günleri) burada bilerek kurulur; beklenen
/// her rakam elle hesaplanmıştır.
/// </summary>
public sealed class PeriodHistoryTests
{
    private static readonly DateOnly Anchor = new(2026, 8, 20);
    private static readonly DateOnly FirstCheckpoint = new(2026, 9, 10);
    private static readonly DateOnly SecondCheckpoint = new(2026, 10, 10);
    private static readonly DateOnly ThirdCheckpoint = new(2026, 11, 10);

    // 20.08 → 10.09 penceresi (20.08 hariç, 10.09 dahil):
    //   gelir     100.000 maaş + 2.500 (25.08)            = 102.500
    //   zorunlu   5.000 (01.09 geçici) + 10.000 (05.09 kredi) = 15.000
    //   büyük     7.000 (10.09)                           =   7.000
    //   yaşam     30.000 × 21 / 31                        =  20.322,58
    //   dönem sonu 50.000 + 102.500 − 15.000 − 7.000 − 20.322,58 = 110.177,42
    private const decimal FirstPlannedIncome = 102_500m;
    private const decimal FirstPlannedMandatory = 15_000m;
    private const decimal FirstPlannedLarge = 7_000m;
    private const decimal FirstPlannedLiving = 20_322.58m;
    private const decimal FirstPlannedEnding = 110_177.42m;

    // ---------------------------------------------------------------
    // Dönem planının dondurulması
    // ---------------------------------------------------------------

    [Fact]
    public void Freeze_KeepsOnlyWindowActivity_AndEndingMatchesHandCalculation()
    {
        var frozen = Freeze(SimplePlan(), Snapshot(Anchor, 50_000m));

        Assert.Equal(Anchor, frozen.PeriodStart);
        Assert.Equal(FirstCheckpoint, frozen.PeriodEnd);
        Assert.Equal(FirstCheckpoint, frozen.ReviewAvailableFrom);
        Assert.Equal(Anchor.AddDays(1), frozen.PaymentWindowStart);
        Assert.Equal(FirstCheckpoint, frozen.PaymentWindowEnd);
        Assert.Equal(50_000m, frozen.OpeningSavings);
        Assert.Equal(FirstPlannedIncome, frozen.PlannedIncome);
        Assert.Equal(10_000m, frozen.PlannedLoanPayments);
        Assert.Equal(5_000m, frozen.PlannedTemporaryPayments);
        Assert.Equal(0m, frozen.PlannedCardPayments);
        Assert.Equal(FirstPlannedMandatory, frozen.PlannedMandatoryPayments);
        Assert.Equal(FirstPlannedLarge, frozen.PlannedLargeExpenses);
        Assert.Equal(FirstPlannedLiving, frozen.PlannedLivingBudget);
        Assert.Equal(0m, frozen.PlannedDeficitInterest);
        Assert.Equal(FirstPlannedEnding, frozen.PlannedEndingSavings);

        // Satırlar tarihe göre sıralı; snapshot günü ve checkpoint sonrası yok.
        Assert.Equal(
            new[]
            {
                (new DateOnly(2026, 9, 1), 5_000m, PlanPaymentSourceType.TemporaryPayment),
                (new DateOnly(2026, 9, 5), 10_000m, PlanPaymentSourceType.Loan),
                (FirstCheckpoint, 7_000m, PlanPaymentSourceType.PlannedLargeExpense)
            },
            frozen.PaymentLines.Select(x =>
                (x.PlannedDate, x.PlannedAmount.GetValueOrDefault(), x.SourceType)));
        Assert.All(frozen.PaymentLines, x => Assert.Equal(frozen.Id, x.PeriodPlanSnapshotId));
        Assert.Equal(
            frozen.PlannedMandatoryPayments + frozen.PlannedLargeExpenses,
            frozen.PaymentLines.Sum(x => x.PlannedAmount.GetValueOrDefault()));
    }

    [Fact]
    public void Freeze_DeficitInterest_RoundsMidpointAwayFromZero()
    {
        // Faiz öncesi dönem sonu −12.345,70 → × %5 = 617,285.
        // Bankacı yuvarlaması 617,28 verirdi; kural AwayFromZero → 617,29.
        var opening = -12_345.70m - (FirstPlannedIncome - FirstPlannedMandatory -
                                     FirstPlannedLarge - FirstPlannedLiving);
        Assert.Equal(-72_523.12m, opening);

        var frozen = Freeze(SimplePlan(), Snapshot(Anchor, opening));

        Assert.Equal(617.29m, frozen.PlannedDeficitInterest);
        Assert.Equal(-12_962.99m, frozen.PlannedEndingSavings);
    }

    [Fact]
    public void Freeze_PositiveOrZeroEnding_HasNoDeficitInterest()
    {
        var breakEven = -(FirstPlannedIncome - FirstPlannedMandatory -
                          FirstPlannedLarge - FirstPlannedLiving);

        var zero = Freeze(SimplePlan(), Snapshot(Anchor, breakEven));
        var oneKuruşShort = Freeze(SimplePlan(), Snapshot(Anchor, breakEven - 0.01m));

        Assert.Equal(0m, zero.PlannedEndingSavings);
        Assert.Equal(0m, zero.PlannedDeficitInterest);
        // 0,01 × %5 = 0,0005 → iki haneye 0,00: açık var ama faizi yuvarlanıp kaybolur.
        Assert.Equal(0m, oneKuruşShort.PlannedDeficitInterest);
        Assert.Equal(-0.01m, oneKuruşShort.PlannedEndingSavings);
    }

    [Theory]
    // İlk kısmi dönem: 30.000 × 21 / 31.
    [InlineData("2026-08-20", 10, "30000", "2026-09-10", "20322.58")]
    // Maaş günündeki snapshot tam bütçe alır.
    [InlineData("2026-09-10", 10, "30000", "2026-10-10", "30000")]
    // Şubat 2027 (28 gün): 15.02 → 10.03 = 23 gün.
    [InlineData("2027-02-15", 10, "28000", "2027-03-10", "23000")]
    // Artık yıl Şubat 2028 (29 gün): 15.02 → 10.03 = 24 gün.
    [InlineData("2028-02-15", 10, "29000", "2028-03-10", "24000")]
    // Maaş günü 31: dönem [31.01, 28.02) = 28 gün, 10.02 → 28.02 = 18 gün.
    [InlineData("2027-02-10", 31, "30000", "2027-02-28", "19285.71")]
    // Maaş günü 31, kırpılmış 28 Şubat maaş günüdür: tam bütçe, sonraki 31 Mart.
    [InlineData("2027-02-28", 31, "30000", "2027-03-31", "30000")]
    // Maaş günü 31, Nisan 30 çeker: 30.04 maaş günüdür, sonraki 31 Mayıs.
    [InlineData("2027-04-30", 31, "30000", "2027-05-31", "30000")]
    // Yarım kuruş: 1.000,01 × 15 / 30 = 500,005 → AwayFromZero 500,01.
    [InlineData("2026-09-25", 10, "1000.01", "2026-10-10", "500.01")]
    // Checkpoint'ten bir gün önce: 30.000 × 1 / 31 = 967,741… → 967,74.
    [InlineData("2026-09-09", 10, "30000", "2026-09-10", "967.74")]
    public void Freeze_LivingBudgetAndReviewDate_FollowSalaryCalendar(
        string snapshotDate,
        int salaryDay,
        string monthlyBudget,
        string expectedReviewDate,
        string expectedBudget)
    {
        var date = DateOnly.Parse(snapshotDate, CultureInfo.InvariantCulture);
        var plan = SimplePlan() with
        {
            Settings = SimplePlan().Settings with
            {
                MonthlyLivingBudget = Money(monthlyBudget)
            }
        };

        var frozen = Freeze(plan, Snapshot(date, 0m, salaryDay));

        Assert.Equal(
            DateOnly.Parse(expectedReviewDate, CultureInfo.InvariantCulture),
            frozen.ReviewAvailableFrom);
        Assert.Equal(frozen.ReviewAvailableFrom, frozen.PeriodEnd);
        Assert.Equal(Money(expectedBudget), frozen.PlannedLivingBudget);
    }

    [Theory]
    // Şubat sonu, maaş günü 31: pencere (10.02, 28.02].
    [InlineData("2027-02-10", 31, "2027-02-10|2027-02-11|2027-02-28|2027-03-01", "2027-02-11|2027-02-28")]
    // Artık yıl, maaş günü 29: pencere (01.02, 29.02].
    [InlineData("2028-02-01", 29, "2028-02-01|2028-02-02|2028-02-29|2028-03-01", "2028-02-02|2028-02-29")]
    // Yıl dönümü: pencere (20.12, 10.01].
    [InlineData("2026-12-20", 10, "2026-12-20|2026-12-31|2027-01-10|2027-01-11", "2026-12-31|2027-01-10")]
    public void Freeze_PaymentWindow_ExcludesSnapshotDayAndIncludesCheckpoint(
        string snapshotDate,
        int salaryDay,
        string installmentDates,
        string expectedDates)
    {
        var date = DateOnly.Parse(snapshotDate, CultureInfo.InvariantCulture);
        var planId = Guid.NewGuid();
        var plan = SimplePlan() with
        {
            Loans = [],
            PlannedLargeExpenses = [],
            OtherIncomes = [],
            PaymentPlans =
            [
                new TemporaryPaymentPlan
                {
                    Id = planId,
                    Name = "Sınır",
                    Installments = Dates(installmentDates)
                        .Select((due, index) => new TemporaryPaymentInstallment
                        {
                            PlanId = planId,
                            DueDate = due,
                            Amount = 1_000m + index
                        })
                        .ToArray()
                }
            ]
        };

        var frozen = Freeze(plan, Snapshot(date, 0m, salaryDay));

        Assert.Equal(Dates(expectedDates), frozen.PaymentLines.Select(x => x.PlannedDate));
        Assert.Equal(
            frozen.PaymentLines.Sum(x => x.PlannedAmount.GetValueOrDefault()),
            frozen.PlannedTemporaryPayments);
    }

    [Fact]
    public void Freeze_IncomeUsesSalaryEffectiveOnCheckpoint_NotOnSnapshotDay()
    {
        // Zam 01.09'da başlıyor: 20.08'de dondurulan plan checkpoint'teki
        // (10.09) maaşı kullanır. Zam 11.09'da olsaydı eski maaş kalırdı.
        var plan = SimplePlan() with
        {
            OtherIncomes = [],
            Salaries =
            [
                Salary(100_000m, new DateOnly(2026, 1, 1)),
                Salary(120_000m, new DateOnly(2026, 9, 1))
            ]
        };
        var late = plan with
        {
            Salaries =
            [
                Salary(100_000m, new DateOnly(2026, 1, 1)),
                Salary(120_000m, new DateOnly(2026, 9, 11))
            ]
        };

        Assert.Equal(120_000m, Freeze(plan, Snapshot(Anchor, 0m)).PlannedIncome);
        Assert.Equal(100_000m, Freeze(late, Snapshot(Anchor, 0m)).PlannedIncome);
    }

    [Fact]
    public void Freeze_IgnoresLargeExpensesThatAreNoLongerPlanned()
    {
        var plan = SimplePlan() with
        {
            PlannedLargeExpenses =
            [
                LargeExpense("Planlı", 7_000m, FirstCheckpoint),
                LargeExpense("Vazgeçildi", 9_000m, new DateOnly(2026, 9, 2)) with
                {
                    Status = PlannedExpenseStatus.Cancelled
                }
            ]
        };

        var frozen = Freeze(plan, Snapshot(Anchor, 50_000m));

        Assert.Equal(7_000m, frozen.PlannedLargeExpenses);
        Assert.DoesNotContain(frozen.PaymentLines, x => x.Name == "Vazgeçildi");
        Assert.Equal(FirstPlannedEnding, frozen.PlannedEndingSavings);
    }

    // ---------------------------------------------------------------
    // Plan / gerçek karşılaştırması
    // ---------------------------------------------------------------

    [Fact]
    public void Comparison_WithoutRevision_ComparesEveryCategoryAgainstOriginalPlan()
    {
        var plan = ComparisonPlan();
        var actual = ComparisonActual();

        var comparison = new PlanActualComparisonCalculator()
            .Calculate(plan, null, actual);

        AssertLine(comparison, "Gelir", 102_500m, 105_500m, 3_000m);
        AssertLine(comparison, "Krediler", 10_000m, 12_000m, 2_000m);
        AssertLine(comparison, "Kredi kartları", 4_000m, 4_000m, 0m);
        AssertLine(comparison, "Geçici ödemeler", 5_000m, 0m, -5_000m);
        AssertLine(comparison, "Taksitli ödemeler", 1_000m, 1_000m, 0m);
        AssertLine(comparison, "Diğer planlı ödemeler", 500m, 750m, 250m);
        AssertLine(comparison, "Zorunlu ödemeler", 20_500m, 17_750m, -2_750m);
        AssertLine(comparison, "Büyük ödemeler", 7_000m, 7_000m, 0m);
        AssertLine(comparison, "Yaşam giderleri", 20_322.58m, 24_500m, 4_177.42m);
        // Faiz = kart faizi + açık faizi.
        AssertLine(comparison, "Faiz", 300.25m, 450m, 149.75m);
        AssertLine(comparison, "Plan dışı ödemeler", 0m, 1_200m, 1_200m);
        AssertLine(comparison, "Dönem düzeltmesi", 0m, 850m, 850m);
        Assert.Equal(12, comparison.Lines.Count);

        Assert.Equal(104_877.17m, comparison.PlannedEndingSavings);
        Assert.Equal(111_000m, comparison.ActualEndingSavings);
        Assert.Equal(6_122.83m, comparison.Difference);
        Assert.Equal(
            "Dönem sonu finansal durumun planın 6.122,83 TL üzerinde gerçekleşti. " +
            "En belirgin fark Geçici ödemeler kaleminde 5.000,00 TL oldu.",
            comparison.Summary);
    }

    [Fact]
    public void Comparison_WithRevision_UsesRevisionValuesForEveryPlannedColumn()
    {
        var plan = ComparisonPlan();
        var revision = new PeriodPlanRevision
        {
            PeriodPlanSnapshotId = plan.Id,
            RevisionNumber = 1,
            PlannedIncome = 110_000m,
            PlannedLoanPayments = 11_000m,
            PlannedCardPayments = 4_500m,
            PlannedTemporaryPayments = 6_000m,
            PlannedInstallmentPayments = 1_500m,
            PlannedOtherScheduledPayments = 600m,
            PlannedMandatoryPayments = 23_600m,
            PlannedLargeExpenses = 8_000m,
            PlannedLivingBudget = 21_000m,
            PlannedCardInterest = 100m,
            PlannedDeficitInterest = 50m,
            // PlannedInterest karşılaştırmada kullanılmaz; kart + açık toplanır.
            PlannedInterest = 99_999m,
            PlannedEndingSavings = 107_350m
        };

        var comparison = new PlanActualComparisonCalculator()
            .Calculate(plan, revision, ComparisonActual());

        AssertLine(comparison, "Gelir", 110_000m, 105_500m, -4_500m);
        AssertLine(comparison, "Krediler", 11_000m, 12_000m, 1_000m);
        AssertLine(comparison, "Kredi kartları", 4_500m, 4_000m, -500m);
        AssertLine(comparison, "Geçici ödemeler", 6_000m, 0m, -6_000m);
        AssertLine(comparison, "Taksitli ödemeler", 1_500m, 1_000m, -500m);
        AssertLine(comparison, "Diğer planlı ödemeler", 600m, 750m, 150m);
        AssertLine(comparison, "Zorunlu ödemeler", 23_600m, 17_750m, -5_850m);
        AssertLine(comparison, "Büyük ödemeler", 8_000m, 7_000m, -1_000m);
        AssertLine(comparison, "Yaşam giderleri", 21_000m, 24_500m, 3_500m);
        AssertLine(comparison, "Faiz", 150m, 450m, 300m);
        Assert.Equal(107_350m, comparison.PlannedEndingSavings);
        Assert.Equal(3_650m, comparison.Difference);
        Assert.EndsWith(
            "En belirgin fark Geçici ödemeler kaleminde 6.000,00 TL oldu.",
            comparison.Summary);
    }

    [Fact]
    public void Comparison_Summary_SaysBelowPlan_WithTurkishFormat_OnEnglishDevice()
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var actual = ComparisonActual() with { ConfirmedEndingSavings = 103_642.61m };

            var comparison = new PlanActualComparisonCalculator()
                .Calculate(ComparisonPlan(), null, actual);

            Assert.Equal(-1_234.56m, comparison.Difference);
            Assert.StartsWith(
                "Dönem sonu finansal durumun planın 1.234,56 TL altında gerçekleşti.",
                comparison.Summary);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Comparison_ZeroEndingDifference_UsesSameLevelSentenceEvenIfCategoriesMoved()
    {
        var actual = ComparisonActual() with { ConfirmedEndingSavings = 104_877.17m };

        var comparison = new PlanActualComparisonCalculator()
            .Calculate(ComparisonPlan(), null, actual);

        Assert.Equal(0m, comparison.Difference);
        Assert.Equal(
            "Dönem sonu finansal durumun planla aynı seviyede gerçekleşti.",
            comparison.Summary);
    }

    [Fact]
    public void Comparison_Cause_IgnoresMandatoryTotalAndReconciliationAdjustment()
    {
        // Yalnız "Zorunlu ödemeler" (bir toplam) ve "Dönem düzeltmesi"
        // değişmiş: sebep cümlesi eklenmez.
        var plan = new PeriodPlanSnapshot
        {
            PlannedMandatoryPayments = 10_000m,
            PlannedEndingSavings = 1_000m
        };
        var actual = new PeriodActual
        {
            PeriodPlanSnapshotId = plan.Id,
            ActualMandatoryPayments = 90_000m,
            ReconciliationAdjustment = 50_000m,
            ConfirmedEndingSavings = 1_500m
        };

        var comparison = new PlanActualComparisonCalculator()
            .Calculate(plan, null, actual);

        Assert.Equal(
            "Dönem sonu finansal durumun planın 500,00 TL üzerinde gerçekleşti.",
            comparison.Summary);
    }

    [Fact]
    public void Comparison_Cause_PicksLargestAbsoluteDifference_FirstCategoryWinsTies()
    {
        var plan = new PeriodPlanSnapshot
        {
            PlannedIncome = 10_000m,
            PlannedLivingBudget = 5_000m,
            PlannedEndingSavings = 5_000m
        };
        // Gelir −2.000, yaşam +2.000: eşit büyüklük, listedeki ilk kalem (Gelir).
        var actual = new PeriodActual
        {
            ActualIncome = 8_000m,
            ActualLivingSpend = 7_000m,
            UnplannedPayments = 1_999.99m,
            ConfirmedEndingSavings = 1_000m
        };

        var comparison = new PlanActualComparisonCalculator()
            .Calculate(plan, null, actual);

        Assert.EndsWith(
            "En belirgin fark Gelir kaleminde 2.000,00 TL oldu.",
            comparison.Summary);
    }

    // ---------------------------------------------------------------
    // Dönem kapanışı (SQLite ile uçtan uca)
    // ---------------------------------------------------------------

    [Fact]
    public async Task ReviewContext_SuggestsPlannedEnding_BeforeAnyActualIsEntered()
    {
        await WithSimpleStore(async store =>
        {
            var review = TestFactory.Service(store, FirstCheckpoint);
            var context = await review.GetPeriodReviewContextAsync();

            Assert.Null(context.Actual);
            Assert.Null(context.Comparison);
            Assert.Null(context.Revision);
            Assert.Equal(0, context.RevisionCount);
            Assert.Equal(FirstPlannedEnding, context.SuggestedStartingSavings);
            Assert.Equal(context.OriginalPlan.PlannedEndingSavings, context.SuggestedStartingSavings);
        });
    }

    [Fact]
    public async Task Finalize_RecordsEveryActualAmount_AndDerivesEndingByHand()
    {
        await WithSimpleStore(async store =>
        {
            var review = TestFactory.Service(store, FirstCheckpoint);
            var context = await review.GetPeriodReviewContextAsync();
            var lines = context.OriginalPlan.PaymentLines;
            var temporary = lines.Single(x => x.SourceType == PlanPaymentSourceType.TemporaryPayment);
            var loan = lines.Single(x => x.SourceType == PlanPaymentSourceType.Loan);
            var large = lines.Single(x => x.SourceType == PlanPaymentSourceType.PlannedLargeExpense);
            var draft = new PeriodReviewDraft(
                context.OriginalPlan.Id,
                [
                    new ActualPaymentDraft(temporary.Id, ActualPaymentStatus.Unpaid, 5_000m, new DateOnly(2026, 9, 1)),
                    new ActualPaymentDraft(loan.Id, ActualPaymentStatus.Paid, 12_000m, new DateOnly(2026, 9, 6), "  gecikme faizi  "),
                    new ActualPaymentDraft(large.Id, ActualPaymentStatus.Paid, 7_000m, null)
                ],
                25_000m,
                150m,
                [
                    // Sınır günleri: snapshot'tan bir gün sonra ve checkpoint günü geçerli.
                    new ActualFlowDraft(ActualFlowType.UnplannedIncome, " İade ", " Diğer ", Anchor.AddDays(1), 3_000m),
                    new ActualFlowDraft(ActualFlowType.UnplannedPayment, "Tamir", "Ev", FirstCheckpoint, 1_200m)
                ],
                [
                    new LivingBreakdownDraft("Market", 15_000m),
                    new LivingBreakdownDraft("Ulaşım", 0m)
                ],
                null,
                "  Eylül notu ");

            // 50.000 + 102.500 + 3.000 − (0 + 12.000 + 7.000) − 25.000 − 150 − 1.200
            const decimal derived = 110_150m;
            var preview = await review.PreviewPeriodReviewAsync(draft);
            Assert.Equal(derived, preview.SuggestedStartingSavings);
            Assert.Equal(derived, preview.ConfirmedStartingSavings);
            Assert.Equal(0m, preview.ReconciliationAdjustment);

            var result = await review.FinalizePeriodReviewAsync(
                draft with { ConfirmedStartingSavings = 111_000m });
            var actual = result.Actual;

            Assert.Equal(Anchor, actual.PeriodStart);
            Assert.Equal(FirstCheckpoint, actual.PeriodEnd);
            Assert.Equal(105_500m, actual.ActualIncome);
            Assert.Equal(3_000m, actual.UnplannedIncome);
            Assert.Equal(1_200m, actual.UnplannedPayments);
            Assert.Equal(12_000m, actual.ActualLoanPayments);
            Assert.Equal(0m, actual.ActualTemporaryPayments);
            Assert.Equal(0m, actual.ActualCardPayments);
            Assert.Equal(7_000m, actual.ActualLargeExpenses);
            // Büyük gider zorunlu ödemelere katılmaz.
            Assert.Equal(12_000m, actual.ActualMandatoryPayments);
            Assert.Equal(25_000m, actual.ActualLivingSpend);
            Assert.Equal(150m, actual.ActualInterest);
            Assert.Equal(derived, actual.DerivedEndingSavings);
            Assert.Equal(111_000m, actual.ConfirmedEndingSavings);
            Assert.Equal(850m, actual.ReconciliationAdjustment);
            Assert.Equal("Eylül notu", actual.Note);

            var paidLoan = actual.Payments.Single(x => x.PeriodPlanPaymentLineId == loan.Id);
            Assert.Equal(ActualPaymentStatus.DifferentAmount, paidLoan.Status);
            Assert.Equal(new DateOnly(2026, 9, 6), paidLoan.ActualPaymentDate);
            Assert.Equal(new DateOnly(2026, 9, 5), paidLoan.PlannedDate);
            Assert.Equal(10_000m, paidLoan.PlannedAmount);
            Assert.Equal("gecikme faizi", paidLoan.Note);
            var unpaid = actual.Payments.Single(x => x.PeriodPlanPaymentLineId == temporary.Id);
            Assert.Equal(ActualPaymentStatus.Unpaid, unpaid.Status);
            Assert.Equal(0m, unpaid.ActualAmount);
            Assert.Null(unpaid.ActualPaymentDate);
            var paidLarge = actual.Payments.Single(x => x.PeriodPlanPaymentLineId == large.Id);
            Assert.Equal(ActualPaymentStatus.Paid, paidLarge.Status);
            Assert.Equal(FirstCheckpoint, paidLarge.ActualPaymentDate);

            Assert.Equal(["İade", "Tamir"], actual.Flows.Select(x => x.Name).Order());
            Assert.Equal("Diğer", actual.Flows.Single(x => x.Name == "İade").Category);
            // Sıfır tutarlı yaşam kırılımı saklanmaz.
            Assert.Equal("Market", Assert.Single(actual.LivingBreakdown).Category);

            Assert.Equal(111_000m - FirstPlannedEnding, result.Comparison.Difference);
            Assert.Equal(
                "Dönem sonu finansal durumun planın 822,58 TL üzerinde gerçekleşti. " +
                "En belirgin fark Geçici ödemeler kaleminde 5.000,00 TL oldu.",
                result.Comparison.Summary);
            Assert.Equal(result.Comparison.Summary, actual.ComparisonSummary);

            // Kayıt yeniden okunduğunda aynı rakamlar.
            var stored = Assert.Single(await review.GetHistoryPeriodsAsync());
            Assert.Equal(actual.Id, stored.Actual.Id);
            Assert.Equal(111_000m, stored.Actual.ConfirmedEndingSavings);
            Assert.Equal(850m, stored.Actual.ReconciliationAdjustment);
            Assert.Equal(3, stored.Actual.Payments.Count);
            Assert.Equal(2, stored.Actual.Flows.Count);
            Assert.Equal(15_000m, Assert.Single(stored.Actual.LivingBreakdown).Amount);
            Assert.Equal(result.Comparison.Summary, stored.Comparison.Summary);
            Assert.Equal(result.NewSnapshot.Id, stored.ResultSnapshot.Id);
        });
    }

    [Fact]
    public async Task Finalize_OmittedPaymentDraft_CountsAsPaidOnPlannedDateAndAmount()
    {
        await WithSimpleStore(async store =>
        {
            var review = TestFactory.Service(store, FirstCheckpoint);
            var context = await review.GetPeriodReviewContextAsync();

            var result = await review.FinalizePeriodReviewAsync(new PeriodReviewDraft(
                context.OriginalPlan.Id,
                [],
                FirstPlannedLiving,
                0m,
                [],
                [],
                null));

            Assert.All(result.Actual.Payments, payment =>
            {
                Assert.Equal(ActualPaymentStatus.Paid, payment.Status);
                Assert.Equal(payment.PlannedAmount, payment.ActualAmount);
                Assert.Equal(payment.PlannedDate, payment.ActualPaymentDate);
            });
            // Plana birebir uyulduysa fark ve düzeltme sıfırdır.
            Assert.Equal(FirstPlannedEnding, result.Actual.DerivedEndingSavings);
            Assert.Equal(0m, result.Actual.ReconciliationAdjustment);
            Assert.Equal(0m, result.Comparison.Difference);
            Assert.All(result.Comparison.Lines, line =>
                Assert.True(
                    line.Difference == 0m,
                    $"{line.Category}: {line.Difference}"));
        });
    }

    [Fact]
    public async Task Finalize_PaidAtPlannedAmount_StaysPaid_EvenWithDifferentDate()
    {
        await WithSimpleStore(async store =>
        {
            var review = TestFactory.Service(store, FirstCheckpoint);
            var context = await review.GetPeriodReviewContextAsync();
            var loan = context.OriginalPlan.PaymentLines.Single(x =>
                x.SourceType == PlanPaymentSourceType.Loan);

            var result = await review.FinalizePeriodReviewAsync(new PeriodReviewDraft(
                context.OriginalPlan.Id,
                [new ActualPaymentDraft(loan.Id, ActualPaymentStatus.Paid, 10_000m, new DateOnly(2026, 8, 21))],
                FirstPlannedLiving,
                0m,
                [],
                [],
                null));

            var payment = result.Actual.Payments.Single(x => x.PeriodPlanPaymentLineId == loan.Id);
            Assert.Equal(ActualPaymentStatus.Paid, payment.Status);
            Assert.Equal(new DateOnly(2026, 8, 21), payment.ActualPaymentDate);
        });
    }

    [Theory]
    [InlineData("2026-08-20", false)]
    [InlineData("2026-08-21", true)]
    [InlineData("2026-09-10", true)]
    [InlineData("2026-09-11", false)]
    public async Task Finalize_UnplannedFlowDate_MustBeInsideSnapshotExclusiveCheckpointInclusiveWindow(
        string flowDate,
        bool accepted)
    {
        // Bugün 20.09: checkpoint'ten sonraki bir gün de henüz gelecekte değil,
        // ama dönemin dışında olduğu için reddedilir.
        await WithSimpleStore(async store =>
        {
            var review = TestFactory.Service(store, new DateOnly(2026, 9, 20));
            var context = await review.GetPeriodReviewContextAsync();
            var draft = new PeriodReviewDraft(
                context.OriginalPlan.Id,
                [],
                FirstPlannedLiving,
                0m,
                [
                    new ActualFlowDraft(
                        ActualFlowType.UnplannedPayment,
                        "Sınır",
                        "Diğer",
                        DateOnly.Parse(flowDate, CultureInfo.InvariantCulture),
                        100m)
                ],
                [],
                null);

            if (accepted)
            {
                var result = await review.FinalizePeriodReviewAsync(draft);
                Assert.Equal(FirstPlannedEnding - 100m, result.Actual.DerivedEndingSavings);
            }
            else
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    review.FinalizePeriodReviewAsync(draft));
                Assert.Empty((await store.GetFinancialHistoryAsync()).Actuals);
            }
        });
    }

    [Theory]
    [InlineData("2026-08-20", false)]
    [InlineData("2026-08-21", true)]
    [InlineData("2026-09-10", true)]
    [InlineData("2026-09-11", false)]
    public async Task Finalize_ActualPaymentDate_MustBeInsideReviewWindow(
        string paymentDate,
        bool accepted)
    {
        await WithSimpleStore(async store =>
        {
            var review = TestFactory.Service(store, new DateOnly(2026, 9, 20));
            var context = await review.GetPeriodReviewContextAsync();
            var loan = context.OriginalPlan.PaymentLines.Single(x =>
                x.SourceType == PlanPaymentSourceType.Loan);
            var draft = new PeriodReviewDraft(
                context.OriginalPlan.Id,
                [
                    new ActualPaymentDraft(
                        loan.Id,
                        ActualPaymentStatus.Paid,
                        10_000m,
                        DateOnly.Parse(paymentDate, CultureInfo.InvariantCulture))
                ],
                FirstPlannedLiving,
                0m,
                [],
                [],
                null);

            if (accepted)
            {
                await review.FinalizePeriodReviewAsync(draft);
                Assert.Single((await store.GetFinancialHistoryAsync()).Actuals);
            }
            else
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    review.FinalizePeriodReviewAsync(draft));
                Assert.Empty((await store.GetFinancialHistoryAsync()).Actuals);
            }
        });
    }

    [Fact]
    public async Task Preview_DoesNotValidateDates_ButFinalizeDoes()
    {
        // Önizleme sihirbaz dolarken çağrılır; tarih hatası kullanıcı kaydederken
        // söylenir. Önizleme de hiçbir şey yazmaz.
        await WithSimpleStore(async store =>
        {
            var review = TestFactory.Service(store, FirstCheckpoint);
            var context = await review.GetPeriodReviewContextAsync();
            var draft = new PeriodReviewDraft(
                context.OriginalPlan.Id,
                [],
                FirstPlannedLiving,
                0m,
                [new ActualFlowDraft(ActualFlowType.UnplannedIncome, "Erken", "Diğer", Anchor, 500m)],
                [],
                null);

            var preview = await review.PreviewPeriodReviewAsync(draft);

            Assert.Equal(FirstPlannedEnding + 500m, preview.SuggestedStartingSavings);
            Assert.Empty((await store.GetFinancialHistoryAsync()).Actuals);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                review.FinalizePeriodReviewAsync(draft));
        });
    }

    [Fact]
    public async Task Preview_RejectsInvalidAmounts()
    {
        await WithSimpleStore(async store =>
        {
            var review = TestFactory.Service(store, FirstCheckpoint);
            var context = await review.GetPeriodReviewContextAsync();
            var loan = context.OriginalPlan.PaymentLines.Single(x =>
                x.SourceType == PlanPaymentSourceType.Loan);
            var valid = new PeriodReviewDraft(
                context.OriginalPlan.Id,
                [],
                10_000m,
                0m,
                [],
                [],
                null);
            ActualFlowDraft Flow(string name, decimal amount) =>
                new(ActualFlowType.UnplannedPayment, name, "Diğer", FirstCheckpoint, amount);
            ActualPaymentDraft Payment(ActualPaymentStatus status, decimal amount) =>
                new(loan.Id, status, amount, FirstCheckpoint);

            var invalid = new[]
            {
                valid with { ActualLivingSpend = -0.01m },
                valid with { ActualInterest = -0.01m },
                valid with { LivingBreakdown = [new LivingBreakdownDraft("Market", 10_000.01m)] },
                valid with
                {
                    LivingBreakdown =
                    [
                        new LivingBreakdownDraft("Market", 6_000m),
                        new LivingBreakdownDraft("Ulaşım", 4_000.01m)
                    ]
                },
                valid with { LivingBreakdown = [new LivingBreakdownDraft("Market", -1m)] },
                valid with { Flows = [Flow("Sıfır", 0m)] },
                valid with { Flows = [Flow("Eksi", -5m)] },
                valid with { Flows = [Flow("   ", 5m)] },
                valid with { Payments = [Payment(ActualPaymentStatus.Paid, 0m)] },
                valid with { Payments = [Payment(ActualPaymentStatus.DifferentAmount, 0m)] },
                valid with { Payments = [Payment(ActualPaymentStatus.Paid, -1m)] }
            };

            foreach (var draft in invalid)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    review.PreviewPeriodReviewAsync(draft));
            }

            // Sınırdaki geçerli değerler: kırılım toplamı tam yaşam gideri,
            // ödenmedi satırında girilen tutar yok sayılır.
            var boundary = await review.PreviewPeriodReviewAsync(valid with
            {
                LivingBreakdown =
                [
                    new LivingBreakdownDraft("Market", 6_000m),
                    new LivingBreakdownDraft("Ulaşım", 4_000m)
                ],
                Payments = [Payment(ActualPaymentStatus.Unpaid, 99_999m)]
            });
            // 50.000 + 102.500 − (5.000 + 0 + 7.000) − 10.000
            Assert.Equal(130_500m, boundary.SuggestedStartingSavings);
        });
    }

    [Fact]
    public async Task Finalize_IsRejectedBeforeCheckpoint_AndSecondTimeForSamePlan()
    {
        await WithSimpleStore(async store =>
        {
            var early = TestFactory.Service(store, FirstCheckpoint.AddDays(-1));
            var context = await early.GetPeriodReviewContextAsync();
            var draft = new PeriodReviewDraft(context.OriginalPlan.Id, [], 0m, 0m, [], [], null);

            Assert.False((await early.GetPeriodReviewAvailabilityAsync()).IsDue);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                early.FinalizePeriodReviewAsync(draft));

            var onTime = TestFactory.Service(store, FirstCheckpoint);
            await onTime.FinalizePeriodReviewAsync(draft);
            var historyAfterFirst = await store.GetFinancialHistoryAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                onTime.FinalizePeriodReviewAsync(draft));
            var historyAfterRetry = await store.GetFinancialHistoryAsync();
            Assert.Single(historyAfterRetry.Actuals);
            Assert.Equal(historyAfterFirst.Snapshots.Count, historyAfterRetry.Snapshots.Count);
            Assert.Equal(historyAfterFirst.Plans.Count, historyAfterRetry.Plans.Count);
        });
    }

    [Fact]
    public async Task Finalize_NewSnapshotAndPlan_StartOnCheckpointWithConfirmedAmount()
    {
        await WithSimpleStore(async store =>
        {
            var initialSnapshot = Assert.Single((await store.GetFinancialHistoryAsync()).Snapshots);
            var review = TestFactory.Service(store, FirstCheckpoint);
            var context = await review.GetPeriodReviewContextAsync();

            var result = await review.FinalizePeriodReviewAsync(new PeriodReviewDraft(
                context.OriginalPlan.Id,
                [],
                FirstPlannedLiving,
                0m,
                [],
                [],
                95_000m));

            var snapshot = result.NewSnapshot;
            Assert.Equal(FirstCheckpoint, snapshot.SnapshotDate);
            Assert.Equal(FirstCheckpoint, snapshot.ProjectionAnchorDate);
            Assert.Equal(SecondCheckpoint, snapshot.NextReviewDate);
            Assert.Equal(95_000m, snapshot.ProjectionStartingSavings);
            Assert.Equal(initialSnapshot.Id, snapshot.PreviousSnapshotId);
            Assert.Equal(FinancialSnapshotSource.MonthlyUpdate, snapshot.Source);
            Assert.True(snapshot.IsCurrent);
            Assert.Equal(FirstPlannedEnding - 95_000m, -result.Actual.ReconciliationAdjustment);

            // İkinci pencere (10.09, 10.10]: 11.09 kalemleri artık bu dönemde.
            // Snapshot günündeki (20.08) geçici 4.000 ve büyük gider 2.000 hiçbir
            // plana girmedi, ödenmedi: yeni dönemin ilk gününe (11.09) devreder.
            //   gelir   100.000 (10.10 maaşı) + 3.000 (11.09)             = 103.000
            //   zorunlu 4.000 + 6.000 (11.09 geçici) + 10.000 (05.10 kredi) = 20.000
            //   büyük   2.000 + 4.000 (11.09)                              =   6.000
            //   yaşam   30.000 (tam dönem)
            //   sonu    95.000 + 103.000 − 20.000 − 6.000 − 30.000         = 142.000
            var plan = result.NewPlan;
            Assert.Equal(FirstCheckpoint, plan.PeriodStart);
            Assert.Equal(SecondCheckpoint, plan.PeriodEnd);
            Assert.Equal(SecondCheckpoint, plan.ReviewAvailableFrom);
            Assert.Equal(snapshot.Id, plan.FinancialSnapshotId);
            Assert.Equal(95_000m, plan.OpeningSavings);
            Assert.Equal(103_000m, plan.PlannedIncome);
            Assert.Equal(20_000m, plan.PlannedMandatoryPayments);
            Assert.Equal(6_000m, plan.PlannedLargeExpenses);
            Assert.Equal(30_000m, plan.PlannedLivingBudget);
            Assert.Equal(142_000m, plan.PlannedEndingSavings);
            Assert.Equal(
                new[]
                {
                    new DateOnly(2026, 9, 11),
                    new DateOnly(2026, 9, 11),
                    new DateOnly(2026, 9, 11),
                    new DateOnly(2026, 9, 11),
                    new DateOnly(2026, 10, 5)
                },
                plan.PaymentLines.Select(x => x.PlannedDate));

            // Açık dönemin projeksiyonu da onaylanan tutardan başlar.
            var future = await review.GetFuturePeriodsAsync(periodCount: 1);
            Assert.Equal(95_000m, future[0].OpeningProjectedSavings);
            Assert.Equal(SecondCheckpoint, future[0].PeriodStart);
        });
    }

    [Fact]
    public async Task Finalize_PaidInstrumentsAdvance_UnpaidOnesMoveToFirstDayOfNextPeriod()
    {
        await WithSimpleStore(async store =>
        {
            var review = TestFactory.Service(store, FirstCheckpoint);
            var context = await review.GetPeriodReviewContextAsync();
            var lines = context.OriginalPlan.PaymentLines;
            var temporary = lines.Single(x => x.SourceType == PlanPaymentSourceType.TemporaryPayment);
            var loan = lines.Single(x => x.SourceType == PlanPaymentSourceType.Loan);
            var large = lines.Single(x => x.SourceType == PlanPaymentSourceType.PlannedLargeExpense);

            await review.FinalizePeriodReviewAsync(new PeriodReviewDraft(
                context.OriginalPlan.Id,
                [
                    new ActualPaymentDraft(temporary.Id, ActualPaymentStatus.Unpaid, 0m, null),
                    new ActualPaymentDraft(loan.Id, ActualPaymentStatus.Paid, 10_000m, null),
                    new ActualPaymentDraft(large.Id, ActualPaymentStatus.Unpaid, 0m, null)
                ],
                FirstPlannedLiving,
                0m,
                [],
                [],
                null));

            var after = await review.GetFinancialPlanAsync();
            var paidLoan = Assert.Single(after.Loans);
            Assert.Equal(new DateOnly(2026, 10, 5), paidLoan.NextPaymentDate);
            Assert.Equal(5, paidLoan.RemainingInstallmentCount);
            Assert.True(paidLoan.IsActive);

            // Ödenmeyen her şey yeni dönemin ilk gününe (11.09) taşınır: 01.09
            // taksiti ve hiçbir plana girmemiş snapshot günü (20.08) taksiti.
            var installments = Assert.Single(after.PaymentPlans).Installments
                .OrderBy(x => x.Amount)
                .ToArray();
            Assert.Equal(
                new[]
                {
                    (new DateOnly(2026, 9, 11), 4_000m, false),
                    (new DateOnly(2026, 9, 11), 5_000m, false),
                    (new DateOnly(2026, 9, 11), 6_000m, false)
                },
                installments.Select(x => (x.DueDate, x.Amount, x.IsPaid)));

            var unpaidExpense = after.PlannedLargeExpenses.Single(x => x.Id == large.SourceEntityId);
            Assert.Equal(PlannedExpenseStatus.Planned, unpaidExpense.Status);
            Assert.Equal(7_000m, unpaidExpense.Amount);
            Assert.Equal(new DateOnly(2026, 9, 11), unpaidExpense.ExactDate);
        });
    }

    [Fact]
    public async Task UnpaidObligations_AppearInNextFrozenPlan_AndCanBeClosedThere()
    {
        // Hata: ödenmeyen kalem checkpoint gününe (10.09) taşınıyordu; yeni
        // dönemin planı (10.09, 10.10] penceresini okuduğu için 14.501 TL'lik
        // borç Ana Sayfa planında yoktu. Geçici ödeme ve büyük gider her
        // checkpoint'te yeniden taşınıyor, hiçbir review'da kapatılamıyordu.
        await WithSimpleStore(async store =>
        {
            var september = TestFactory.Service(store, FirstCheckpoint);
            var first = await september.GetPeriodReviewContextAsync();
            var result = await september.FinalizePeriodReviewAsync(new PeriodReviewDraft(
                first.OriginalPlan.Id,
                first.OriginalPlan.PaymentLines
                    .Select(x => new ActualPaymentDraft(x.Id, ActualPaymentStatus.Unpaid, 0m, null))
                    .ToArray(),
                FirstPlannedLiving,
                0m,
                [],
                [],
                null));

            var carried = new DateOnly(2026, 9, 11);
            var next = result.NewPlan;
            // 11.09: devreden kredi taksiti 10.000, geçici 4.000 (20.08) + 5.000
            // (01.09) + 6.000 (kendi tarihi), büyük gider 2.000 (20.08) + 7.000
            // (10.09) + 4.000 (kendi tarihi). 05.10: kredinin sıradaki taksiti.
            Assert.Equal(
                new[]
                {
                    (carried, PlanPaymentSourceType.Loan, 10_000m),
                    (carried, PlanPaymentSourceType.TemporaryPayment, 4_000m),
                    (carried, PlanPaymentSourceType.TemporaryPayment, 5_000m),
                    (carried, PlanPaymentSourceType.TemporaryPayment, 6_000m),
                    (carried, PlanPaymentSourceType.PlannedLargeExpense, 2_000m),
                    (carried, PlanPaymentSourceType.PlannedLargeExpense, 4_000m),
                    (carried, PlanPaymentSourceType.PlannedLargeExpense, 7_000m),
                    (new DateOnly(2026, 10, 5), PlanPaymentSourceType.Loan, 10_000m)
                },
                next.PaymentLines
                    .OrderBy(x => x.PlannedDate)
                    .ThenBy(x => x.SourceType)
                    .ThenBy(x => x.PlannedAmount)
                    .Select(x => (x.PlannedDate, x.SourceType, x.PlannedAmount.GetValueOrDefault())));
            Assert.Equal(35_000m, next.PlannedMandatoryPayments);
            Assert.Equal(13_000m, next.PlannedLargeExpenses);
            // 20.08 → 10.09 hiç ödeme yapılmadı: 50.000 + 102.500 − 20.322,58.
            Assert.Equal(132_177.42m, next.OpeningSavings);
            // 132.177,42 + 103.000 − 35.000 − 13.000 − 30.000
            Assert.Equal(157_177.42m, next.PlannedEndingSavings);

            // Bir sonraki checkpoint'te hepsi ödendi olarak kapanır.
            var october = TestFactory.Service(store, SecondCheckpoint);
            var second = await october.GetPeriodReviewContextAsync();
            Assert.Equal(next.Id, second.OriginalPlan.Id);
            await october.FinalizePeriodReviewAsync(new PeriodReviewDraft(
                second.OriginalPlan.Id,
                second.OriginalPlan.PaymentLines
                    .Select(x => new ActualPaymentDraft(x.Id, ActualPaymentStatus.Paid, x.PlannedAmount!.Value, carried))
                    .ToArray(),
                30_000m,
                0m,
                [],
                [],
                null));

            var closed = await october.GetFinancialPlanAsync();
            var loan = Assert.Single(closed.Loans);
            // Devreden ve sıradaki taksit ödendi: 6 → 4, sonraki 05.11.
            Assert.Equal(4, loan.RemainingInstallmentCount);
            Assert.Equal(new DateOnly(2026, 11, 5), loan.NextPaymentDate);
            Assert.All(Assert.Single(closed.PaymentPlans).Installments, x => Assert.True(x.IsPaid));
            Assert.All(closed.PlannedLargeExpenses, x =>
                Assert.Equal(PlannedExpenseStatus.Completed, x.Status));
            var third = (await store.GetFinancialHistoryAsync()).Plans
                .Single(x => x.PeriodStart == SecondCheckpoint);
            Assert.Equal(
                new[] { new DateOnly(2026, 11, 5) },
                third.PaymentLines.Select(x => x.PlannedDate));
        });
    }

    [Fact]
    public async Task ThreeConsecutivePeriods_ChainDatesOpeningsAndHistoryOrder()
    {
        await WithSimpleStore(async store =>
        {
            var confirmedEndings = new[] { 100_000m, 120_500.50m, 90_250.25m };
            var checkpoints = new[] { FirstCheckpoint, SecondCheckpoint, ThirdCheckpoint };
            var expectedStarts = new[] { Anchor, FirstCheckpoint, SecondCheckpoint };
            var expectedOpenings = new[] { 50_000m, 100_000m, 120_500.50m };

            for (var index = 0; index < checkpoints.Length; index++)
            {
                var review = TestFactory.Service(store, checkpoints[index]);
                Assert.True((await review.GetPeriodReviewAvailabilityAsync()).IsDue);
                var context = await review.GetPeriodReviewContextAsync();
                Assert.Equal(expectedStarts[index], context.OriginalPlan.PeriodStart);
                Assert.Equal(checkpoints[index], context.OriginalPlan.PeriodEnd);
                Assert.Equal(expectedOpenings[index], context.OriginalPlan.OpeningSavings);
                Assert.Equal(expectedOpenings[index], context.Snapshot.ProjectionStartingSavings);

                await review.FinalizePeriodReviewAsync(new PeriodReviewDraft(
                    context.OriginalPlan.Id,
                    [],
                    context.OriginalPlan.PlannedLivingBudget,
                    0m,
                    [],
                    [],
                    confirmedEndings[index]));

                Assert.False((await review.GetPeriodReviewAvailabilityAsync()).IsDue);
            }

            var last = TestFactory.Service(store, ThirdCheckpoint);
            var history = await last.GetHistoryPeriodsAsync();

            // En yeni dönem önce.
            Assert.Equal(
                new[] { SecondCheckpoint, FirstCheckpoint, Anchor },
                history.Select(x => x.OriginalPlan.PeriodStart));
            Assert.Equal(
                new[] { ThirdCheckpoint, SecondCheckpoint, FirstCheckpoint },
                history.Select(x => x.Actual.PeriodEnd));
            Assert.Equal(
                confirmedEndings.Reverse(),
                history.Select(x => x.Actual.ConfirmedEndingSavings));
            // Her dönemin sonucu bir sonrakinin kaynağıdır.
            Assert.Equal(history[1].ResultSnapshot.Id, history[0].Actual.SourceFinancialSnapshotId);
            Assert.Equal(history[2].ResultSnapshot.Id, history[1].Actual.SourceFinancialSnapshotId);
            Assert.All(history, x =>
                Assert.Equal(x.Actual.PeriodEnd, x.ResultSnapshot.SnapshotDate));

            var snapshots = (await store.GetFinancialHistoryAsync()).Snapshots;
            Assert.Equal(4, snapshots.Count);
            var current = Assert.Single(snapshots, x => x.IsCurrent);
            Assert.Equal(ThirdCheckpoint, current.SnapshotDate);
            Assert.Equal(new DateOnly(2026, 12, 10), current.NextReviewDate);

            var summary = await last.GetHistorySummaryAsync();
            Assert.NotNull(summary);
            Assert.Equal(3, summary.PeriodCount);
            Assert.Equal(history.Sum(x => x.Comparison.PlannedEndingSavings), summary.Planned);
            Assert.Equal(310_750.75m, summary.Actual);
            Assert.Equal(summary.Actual - summary.Planned, summary.Difference);
        });
    }

    [Fact]
    public async Task SalaryDay31_ChainsThroughFebruaryAndThirtyDayMonths()
    {
        var plan = SimplePlan() with
        {
            Settings = SimplePlan().Settings with
            {
                SalaryDay = 31,
                ProjectionAnchorDate = new DateOnly(2027, 2, 5),
                ProjectionStartingSavings = 10_000m
            },
            Loans = [],
            PaymentPlans = [],
            OtherIncomes = [],
            PlannedLargeExpenses = [],
            PaymentAssignmentStrategies =
            [
                new PaymentAssignmentStrategy
                {
                    Mode = PaymentAssignmentMode.UpcomingPeriod,
                    EffectiveFromSalaryDate = new DateOnly(2027, 1, 31),
                    CreatedAt = new DateTimeOffset(2027, 2, 5, 0, 0, 0, TimeSpan.Zero)
                }
            ]
        };

        await WithStore(plan, new DateOnly(2027, 2, 5), async store =>
        {
            var checkpoints = new[]
            {
                new DateOnly(2027, 2, 28),
                new DateOnly(2027, 3, 31),
                new DateOnly(2027, 4, 30)
            };
            // İlk dönem [31.01, 28.02) 28 gün, 05.02 → 28.02 = 23 gün:
            // 30.000 × 23 / 28 = 24.642,857… → 24.642,86. Sonrakiler tam.
            var expectedBudgets = new[] { 24_642.86m, 30_000m, 30_000m };
            var expectedStarts = new[] { new DateOnly(2027, 2, 5), checkpoints[0], checkpoints[1] };

            for (var index = 0; index < checkpoints.Length; index++)
            {
                var review = TestFactory.Service(store, checkpoints[index]);
                var context = await review.GetPeriodReviewContextAsync();
                Assert.Equal(expectedStarts[index], context.OriginalPlan.PeriodStart);
                Assert.Equal(checkpoints[index], context.OriginalPlan.PeriodEnd);
                Assert.Equal(expectedBudgets[index], context.OriginalPlan.PlannedLivingBudget);
                Assert.False((await TestFactory.Service(store, checkpoints[index].AddDays(-1))
                    .GetPeriodReviewAvailabilityAsync()).IsDue);

                var result = await review.FinalizePeriodReviewAsync(new PeriodReviewDraft(
                    context.OriginalPlan.Id,
                    [],
                    context.OriginalPlan.PlannedLivingBudget,
                    0m,
                    [],
                    [],
                    null));

                Assert.Equal(checkpoints[index], result.NewSnapshot.SnapshotDate);
                // Her dönem 100.000 gelir, yalnız yaşam gideri çıkar.
                Assert.Equal(
                    context.OriginalPlan.OpeningSavings + 100_000m - expectedBudgets[index],
                    result.Actual.ConfirmedEndingSavings);
            }

            var current = Assert.Single(
                (await store.GetFinancialHistoryAsync()).Snapshots,
                x => x.IsCurrent);
            Assert.Equal(new DateOnly(2027, 4, 30), current.SnapshotDate);
            Assert.Equal(new DateOnly(2027, 5, 31), current.NextReviewDate);
            Assert.Equal(10_000m + 300_000m - 84_642.86m, current.ProjectionStartingSavings);
        });
    }

    [Fact]
    public async Task LateFinalization_StampsCheckpointNotToday_AndNextReviewKeepsCalendar()
    {
        await WithSimpleStore(async store =>
        {
            // Birinci dönem ekim başında, 25 gün gecikmeyle kapatılıyor.
            var late = TestFactory.Service(store, new DateOnly(2026, 10, 5));
            var context = await late.GetPeriodReviewContextAsync();
            var result = await late.FinalizePeriodReviewAsync(new PeriodReviewDraft(
                context.OriginalPlan.Id,
                [],
                FirstPlannedLiving,
                0m,
                [],
                [],
                null));

            Assert.Equal(FirstCheckpoint, result.NewSnapshot.SnapshotDate);
            Assert.Equal(SecondCheckpoint, result.NewSnapshot.NextReviewDate);
            Assert.Equal(new DateOnly(2026, 10, 5), DateOnly.FromDateTime(result.Actual.FinalizedAtUtc.UtcDateTime));
            // Yeni dönem hâlâ açık: 05.10'da vadesi gelmedi, 10.10'da gelir.
            Assert.False((await late.GetPeriodReviewAvailabilityAsync()).IsDue);
            var due = await TestFactory.Service(store, SecondCheckpoint).GetPeriodReviewAvailabilityAsync();
            Assert.True(due.IsDue);
            Assert.Equal(FirstCheckpoint, due.PendingPlan!.PeriodStart);
        });
    }

    // ---------------------------------------------------------------
    // Geçmiş sorgusu: revizyon seçimi tarih sınırı
    // ---------------------------------------------------------------

    [Fact]
    public async Task HistoryQuery_UsesLatestRevisionCreatedOnOrBeforeCheckpointUtcDate()
    {
        var source = new FinancialSnapshot { SnapshotDate = Anchor, ProjectionStartingSavings = 50_000m };
        var result = new FinancialSnapshot { SnapshotDate = FirstCheckpoint };
        var plan = new PeriodPlanSnapshot
        {
            FinancialSnapshotId = source.Id,
            PeriodStart = Anchor,
            PeriodEnd = FirstCheckpoint,
            ReviewAvailableFrom = FirstCheckpoint,
            PlannedEndingSavings = 1_000m
        };
        PeriodPlanRevision Revision(int number, DateTimeOffset createdAt, decimal ending) => new()
        {
            PeriodPlanSnapshotId = plan.Id,
            RevisionNumber = number,
            CreatedAtUtc = createdAt,
            PlannedEndingSavings = ending
        };
        var revisions = new[]
        {
            Revision(1, new DateTimeOffset(2026, 8, 25, 9, 0, 0, TimeSpan.Zero), 2_000m),
            // Checkpoint gününün son saniyesi (UTC): geçerli.
            Revision(2, new DateTimeOffset(2026, 9, 10, 23, 59, 59, TimeSpan.Zero), 3_000m),
            // Yerel 11.09 01:00 (+03:00) = UTC 10.09 22:00: UTC tarihine göre geçerli
            // ve yukarıdakinden önce oluşturuldu.
            Revision(3, new DateTimeOffset(2026, 9, 11, 1, 0, 0, TimeSpan.FromHours(3)), 4_000m),
            // UTC 11.09: checkpoint'ten sonra, final plana girmez.
            Revision(4, new DateTimeOffset(2026, 9, 11, 0, 0, 0, TimeSpan.Zero), 5_000m)
        };
        var actual = new PeriodActual
        {
            PeriodPlanSnapshotId = plan.Id,
            SourceFinancialSnapshotId = source.Id,
            ResultFinancialSnapshotId = result.Id,
            PeriodStart = Anchor,
            PeriodEnd = FirstCheckpoint,
            ConfirmedEndingSavings = 3_500m
        };
        var service = new HistoryQueryService(
            FakeHistoryStore(new FinancialHistoryData(
                [source, result],
                [plan],
                revisions,
                [actual])),
            new PlanActualComparisonCalculator());

        var period = Assert.Single(await service.GetPeriodsAsync());

        Assert.Equal(3, period.RevisionCount);
        Assert.Equal(2, period.Revision!.RevisionNumber);
        Assert.Equal(3_000m, period.Comparison.PlannedEndingSavings);
        Assert.Equal(500m, period.Comparison.Difference);
        Assert.Same(result, period.ResultSnapshot);
    }

    [Fact]
    public async Task HistoryQuery_SameCreatedAt_BreaksTieByRevisionNumber()
    {
        var source = new FinancialSnapshot();
        var result = new FinancialSnapshot();
        var plan = new PeriodPlanSnapshot
        {
            FinancialSnapshotId = source.Id,
            ReviewAvailableFrom = FirstCheckpoint
        };
        var createdAt = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var service = new HistoryQueryService(
            FakeHistoryStore(new FinancialHistoryData(
                [source, result],
                [plan],
                [
                    new PeriodPlanRevision { PeriodPlanSnapshotId = plan.Id, RevisionNumber = 7, CreatedAtUtc = createdAt, PlannedEndingSavings = 7m },
                    new PeriodPlanRevision { PeriodPlanSnapshotId = plan.Id, RevisionNumber = 2, CreatedAtUtc = createdAt, PlannedEndingSavings = 2m }
                ],
                [
                    new PeriodActual
                    {
                        PeriodPlanSnapshotId = plan.Id,
                        SourceFinancialSnapshotId = source.Id,
                        ResultFinancialSnapshotId = result.Id
                    }
                ])),
            new PlanActualComparisonCalculator());

        var period = Assert.Single(await service.GetPeriodsAsync());

        Assert.Equal(7, period.Revision!.RevisionNumber);
        Assert.Equal(7m, period.Comparison.PlannedEndingSavings);
    }

    [Fact]
    public async Task HistoryQuery_RecentSummary_TakesNewestPeriodsOnly()
    {
        var periods = new[]
        {
            (Start: new DateOnly(2026, 6, 10), Planned: 1_000m, Actual: 900m),
            (Start: new DateOnly(2026, 9, 10), Planned: 4_000m, Actual: 4_400m),
            (Start: new DateOnly(2026, 7, 10), Planned: 2_000m, Actual: 2_050.50m),
            (Start: new DateOnly(2026, 8, 10), Planned: 3_000m, Actual: 2_999.99m)
        };
        var snapshots = new List<FinancialSnapshot>();
        var plans = new List<PeriodPlanSnapshot>();
        var actuals = new List<PeriodActual>();
        foreach (var item in periods)
        {
            var result = new FinancialSnapshot { SnapshotDate = item.Start.AddMonths(1) };
            var plan = new PeriodPlanSnapshot
            {
                PeriodStart = item.Start,
                PeriodEnd = item.Start.AddMonths(1),
                ReviewAvailableFrom = item.Start.AddMonths(1),
                PlannedEndingSavings = item.Planned
            };
            snapshots.Add(result);
            plans.Add(plan);
            actuals.Add(new PeriodActual
            {
                PeriodPlanSnapshotId = plan.Id,
                ResultFinancialSnapshotId = result.Id,
                PeriodStart = item.Start,
                ConfirmedEndingSavings = item.Actual
            });
        }

        var service = new HistoryQueryService(
            FakeHistoryStore(new FinancialHistoryData(snapshots, plans, [], actuals)),
            new PlanActualComparisonCalculator());

        Assert.Equal(
            new[]
            {
                new DateOnly(2026, 9, 10),
                new DateOnly(2026, 8, 10),
                new DateOnly(2026, 7, 10),
                new DateOnly(2026, 6, 10)
            },
            (await service.GetPeriodsAsync()).Select(x => x.OriginalPlan.PeriodStart));

        var recent = await service.GetRecentSummaryAsync(2);
        Assert.NotNull(recent);
        Assert.Equal(2, recent.PeriodCount);
        Assert.Equal(7_000m, recent.Planned);
        Assert.Equal(7_399.99m, recent.Actual);
        Assert.Equal(399.99m, recent.Difference);

        var all = await service.GetRecentSummaryAsync(10);
        Assert.Equal(4, all!.PeriodCount);
        Assert.Equal(10_000m, all.Planned);
        Assert.Equal(10_350.49m, all.Actual);
        Assert.Equal(350.49m, all.Difference);
    }

    [Fact]
    public async Task HistoryQuery_WithoutActuals_HasNoSummary()
    {
        var service = new HistoryQueryService(
            FakeHistoryStore(new FinancialHistoryData([], [], [], [])),
            new PlanActualComparisonCalculator());

        Assert.Empty(await service.GetPeriodsAsync());
        Assert.Null(await service.GetRecentSummaryAsync());
    }

    // ---------------------------------------------------------------
    // Kurulum
    // ---------------------------------------------------------------

    /// <summary>
    /// Elle hesaplanabilen plan: kart yok (kart faizi yuvarlaması
    /// rakamlara karışmasın), her kaynaktan pencerenin iki yanına düşen bir kalem var.
    /// </summary>
    private static FinancialPlan SimplePlan()
    {
        var temporaryId = Guid.Parse("a0000000-0000-0000-0000-000000000001");
        return new FinancialPlan
        {
            Settings = new UserSettings
            {
                SalaryDay = 10,
                MonthlyLivingBudget = 30_000m,
                ProjectionStartingSavings = 50_000m,
                ProjectionAnchorDate = Anchor
            },
            Salaries = [Salary(100_000m, new DateOnly(2026, 1, 1))],
            OtherIncomes =
            [
                Income(1_000m, Anchor),
                Income(2_500m, new DateOnly(2026, 8, 25)),
                Income(3_000m, new DateOnly(2026, 9, 11))
            ],
            Loans =
            [
                new Loan
                {
                    Id = Guid.Parse("a0000000-0000-0000-0000-000000000002"),
                    Name = "İhtiyaç",
                    Bank = "Banka",
                    MonthlyPayment = 10_000m,
                    PaymentDay = 5,
                    NextPaymentDate = new DateOnly(2026, 9, 5),
                    RemainingInstallmentCount = 6
                }
            ],
            PaymentPlans =
            [
                new TemporaryPaymentPlan
                {
                    Id = temporaryId,
                    Name = "Geçici",
                    Kind = PaymentPlanKind.Temporary,
                    Installments =
                    [
                        Installment(temporaryId, Anchor, 4_000m),
                        Installment(temporaryId, new DateOnly(2026, 9, 1), 5_000m),
                        Installment(temporaryId, new DateOnly(2026, 9, 11), 6_000m)
                    ]
                }
            ],
            PlannedLargeExpenses =
            [
                LargeExpense("Snapshot günü", 2_000m, Anchor),
                LargeExpense("Checkpoint günü", 7_000m, FirstCheckpoint),
                LargeExpense("Sonraki dönem", 4_000m, new DateOnly(2026, 9, 11))
            ],
            PaymentAssignmentStrategies =
            [
                new PaymentAssignmentStrategy
                {
                    Mode = PaymentAssignmentMode.UpcomingPeriod,
                    EffectiveFromSalaryDate = FirstCheckpoint,
                    CreatedAt = new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero)
                }
            ]
        };
    }

    private static PeriodPlanSnapshot ComparisonPlan() => new()
    {
        PeriodStart = Anchor,
        PeriodEnd = FirstCheckpoint,
        ReviewAvailableFrom = FirstCheckpoint,
        PlannedIncome = 102_500m,
        PlannedLoanPayments = 10_000m,
        PlannedCardPayments = 4_000m,
        PlannedTemporaryPayments = 5_000m,
        PlannedInstallmentPayments = 1_000m,
        PlannedOtherScheduledPayments = 500m,
        PlannedMandatoryPayments = 20_500m,
        PlannedLargeExpenses = 7_000m,
        PlannedLivingBudget = 20_322.58m,
        PlannedCardInterest = 250.25m,
        PlannedDeficitInterest = 50m,
        PlannedEndingSavings = 104_877.17m
    };

    private static PeriodActual ComparisonActual() => new()
    {
        ActualIncome = 105_500m,
        ActualLoanPayments = 12_000m,
        ActualCardPayments = 4_000m,
        ActualTemporaryPayments = 0m,
        ActualInstallmentPayments = 1_000m,
        ActualOtherScheduledPayments = 750m,
        ActualMandatoryPayments = 17_750m,
        ActualLargeExpenses = 7_000m,
        ActualLivingSpend = 24_500m,
        ActualInterest = 450m,
        UnplannedIncome = 3_000m,
        UnplannedPayments = 1_200m,
        DerivedEndingSavings = 110_150m,
        ConfirmedEndingSavings = 111_000m,
        ReconciliationAdjustment = 850m
    };

    private static void AssertLine(
        PlanActualComparison comparison,
        string category,
        decimal planned,
        decimal actual,
        decimal difference)
    {
        var line = Assert.Single(comparison.Lines, x => x.Category == category);
        Assert.Equal(planned, line.Planned);
        Assert.Equal(actual, line.Actual);
        Assert.Equal(difference, line.Difference);
    }

    private static PeriodPlanSnapshot Freeze(FinancialPlan source, FinancialSnapshot snapshot)
    {
        var plan = source with
        {
            Settings = source.Settings with
            {
                ProjectionAnchorDate = snapshot.SnapshotDate,
                ProjectionStartingSavings = snapshot.ProjectionStartingSavings,
                SalaryDay = snapshot.SalaryDay
            },
            // Düzen kaydı o maaş gününün takviminde bir tarih olmalı.
            PaymentAssignmentStrategies = source.PaymentAssignmentStrategies
                .Select(x => x with
                {
                    EffectiveFromSalaryDate = new SalaryPeriodCalculator()
                        .GetPeriod(snapshot.SnapshotDate, snapshot.SalaryDay)
                        .Start
                })
                .ToArray()
        };
        return new PeriodPlanSnapshotService(
            TestFactory.ProjectionCalculator(),
            new SalaryPeriodCalculator(),
            new SalaryResolver()).Freeze(plan, snapshot, snapshot.CreatedAtUtc);
    }

    private static FinancialSnapshot Snapshot(
        DateOnly date,
        decimal startingSavings,
        int salaryDay = 10) => new()
    {
        SnapshotDate = date,
        ProjectionAnchorDate = date,
        NextReviewDate = new SalaryPeriodCalculator().GetNextReviewDate(date, salaryDay),
        ProjectionStartingSavings = startingSavings,
        SalaryDay = salaryDay,
        Source = FinancialSnapshotSource.Initial,
        IsCurrent = true,
        CreatedAtUtc = new DateTimeOffset(date.Year, date.Month, date.Day, 12, 0, 0, TimeSpan.Zero)
    };

    private static SalaryScheduleEntry Salary(decimal amount, DateOnly effective) => new()
    {
        Amount = amount,
        EffectiveDate = effective,
        Description = "Maaş"
    };

    private static OneTimeIncome Income(decimal amount, DateOnly date) => new()
    {
        Amount = amount,
        ExactDate = date,
        Description = $"Gelir {date:dd.MM}"
    };

    private static TemporaryPaymentInstallment Installment(Guid planId, DateOnly due, decimal amount) => new()
    {
        PlanId = planId,
        DueDate = due,
        Amount = amount
    };

    private static PlannedLargeExpense LargeExpense(string name, decimal amount, DateOnly date) => new()
    {
        Name = name,
        Amount = amount,
        ExactDate = date
    };

    private static decimal Money(string value) =>
        decimal.Parse(value, CultureInfo.InvariantCulture);

    private static DateOnly[] Dates(string value) => value
        .Split('|')
        .Select(x => DateOnly.Parse(x, CultureInfo.InvariantCulture))
        .ToArray();

    private static ICoinFlowStore FakeHistoryStore(FinancialHistoryData history)
    {
        var store = DispatchProxy.Create<ICoinFlowStore, HistoryOnlyStore>();
        ((HistoryOnlyStore)(object)store).History = history;
        return store;
    }

    public class HistoryOnlyStore : DispatchProxy
    {
        public FinancialHistoryData History { get; set; } = new([], [], [], []);

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(ICoinFlowStore.GetFinancialHistoryAsync)
                ? Task.FromResult(History)
                : throw new NotSupportedException(targetMethod?.Name);
    }

    private static Task WithSimpleStore(Func<SqliteCoinFlowStore, Task> test) =>
        WithStore(SimplePlan(), Anchor, test);

    private static async Task WithStore(
        FinancialPlan plan,
        DateOnly today,
        Func<SqliteCoinFlowStore, Task> test)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"coinflow-history-{Guid.NewGuid():N}.db3");
        try
        {
            await using var store = new SqliteCoinFlowStore(path, true, today);
            await store.InitializeAsync();
            await store.SaveSettingsAsync(plan.Settings);
            foreach (var item in plan.Salaries)
                await store.UpsertSalaryAsync(item);
            foreach (var item in plan.OtherIncomes)
                await store.UpsertOtherIncomeAsync(item);
            foreach (var item in plan.Loans)
                await store.UpsertLoanAsync(item);
            foreach (var item in plan.PaymentPlans)
                await store.UpsertPaymentPlanAsync(item);
            foreach (var item in plan.PlannedLargeExpenses)
                await store.UpsertPlannedLargeExpenseAsync(item);
            foreach (var item in plan.PaymentAssignmentStrategies)
                await store.UpsertPaymentAssignmentStrategyAsync(item);

            // İlk okuma çapadaki snapshot'ı ve donmuş planı oluşturur.
            await TestFactory.Service(store, today).GetFinancialPlanAsync();
            await test(store);
        }
        finally
        {
            foreach (var candidate in new[] { path, path + "-shm", path + "-wal" })
            {
                if (File.Exists(candidate))
                {
                    File.Delete(candidate);
                }
            }
        }
    }
}
