using Mizan.Application.Models;
using Mizan.Application.Services;
using Mizan.Domain.Calculations;
using Mizan.Domain.Models;
using Mizan.Infrastructure.Persistence;

namespace Mizan.Tests.Regression;

/// <summary>
/// Mizan'ın temel finansal iş mantığını (Core Business) uçtan uca koruyan
/// ve herhangi bir bileşen bozulduğunda anında alarm veren Regresyon Test Paketi.
/// </summary>
public sealed class CoreBusinessRegressionTests
{
    private static readonly DateOnly SeedDate = new(2026, 8, 20);
    private static readonly Guid AxessCardId = Guid.Parse("40000000-0000-0000-0000-000000000001");

    /// <summary>
    /// REGRESYON 1: Çok Dönemli Nakit Akışı ve Para Korunumu (Conservation of Balance).
    /// 12 dönem boyunca her dönemin kapanış bakiyesi bir sonraki dönemin açılış bakiyesine
    /// kuruşu kuruşuna eşit olmalıdır. Hiçbir dönemde sapma (drift) veya hayali bakiye oluşamaz.
    /// </summary>
    [Fact]
    public async Task Regression_MultiPeriod_CashFlow_ConservationOfBalance_HoldsAcross12Periods()
    {
        await WithCanonicalService(async service =>
        {
            var periods = await service.GetFuturePeriodsAsync(periodCount: 12);
            Assert.Equal(12, periods.Count);

            for (var i = 0; i < periods.Count; i++)
            {
                var period = periods[i];

                // 1. Dönem içi net katkı formülü doğrulaması:
                // Net Katkı = Toplam Gelir - Zorunlu Çıkışlar - Yaşam Gideri
                var expectedNetContribution = period.TotalIncome - period.MandatoryOutflow - period.VariableExpenseAllowance;
                Assert.Equal(expectedNetContribution, period.CurrentPeriodNetContribution);

                // 2. Faiz öncesi dönem sonu bakiye:
                // EndingBeforeInterest = Opening + NetContribution
                var expectedEndingBeforeInterest = period.OpeningProjectedBalance + period.CurrentPeriodNetContribution;
                Assert.Equal(expectedEndingBeforeInterest, period.EndingProjectedBalanceBeforeDeficitInterest);

                // 3. Faiz sonrası nihai dönem sonu:
                // Ending = EndingBeforeInterest - DeficitFinancingInterest
                var expectedEnding = period.EndingProjectedBalanceBeforeDeficitInterest - period.DeficitFinancingInterest;
                Assert.Equal(expectedEnding, period.EndingProjectedBalance);

                // 4. KMH Faiz kuralı: Bakiye pozitifse açık faizi 0 olmalı, negatifse >= 0 olmalı
                if (period.EndingProjectedBalanceBeforeDeficitInterest >= 0m)
                {
                    Assert.Equal(0m, period.DeficitFinancingInterest);
                }
                else
                {
                    Assert.True(period.DeficitFinancingInterest >= 0m);
                }

                // 5. Bir sonraki döneme devir doğrulaması (Zero Drift):
                if (i < periods.Count - 1)
                {
                    var nextPeriod = periods[i + 1];
                    Assert.Equal(period.EndingProjectedBalance, nextPeriod.OpeningProjectedBalance);
                    Assert.Equal(period.PeriodEnd, nextPeriod.PeriodStart);
                }
            }
        });
    }

    /// <summary>
    /// REGRESYON 2: Dondurulmuş Plan Dokunulmazlığı (Invariant I16).
    /// Açık dönem başladığında dondurulan plan (openPlan), dönem ortasında yapılan
    /// plansız kredi kartı harcamalarıyla asla kirletilemez.
    /// PLANLANAN bütçe kilitli kalmalı; harcama yalnızca MEVCUT ve GİDİŞAT'ı etkilemelidir.
    /// </summary>
    [Fact]
    public async Task Regression_PeriodPlanLock_MidPeriodExpenses_DoNotMutateFrozenBaseline()
    {
        await WithCanonicalService(async service =>
        {
            var progressBefore = await service.GetPeriodProgressAsync();
            Assert.NotNull(progressBefore);
            var initialPlannedMandatory = progressBefore!.PlannedMandatoryPayments;
            var initialPlannedIncome = progressBefore.PlannedIncome;
            var initialPlannedLiving = progressBefore.PlannedVariableExpenseAllowance;
            var initialPlannedEnding = progressBefore.PlannedEndingBalance;

            var periodsBefore = await service.GetFuturePeriodsAsync(periodCount: 12);
            var period0EndingBefore = periodsBefore[0].EndingProjectedBalance;

            // Dönem ortasında 4.500 TL plansız kredi kartı harcaması yap
            var plan = await service.GetFinancialPlanAsync();
            var card = plan.CreditCards.First(c => c.Id == AxessCardId);
            var midPeriodCharge = 4_500m;

            await service.SaveCreditCardAsync(card with
            {
                Charges = [.. card.Charges, new CardCharge
                {
                    Id = Guid.NewGuid(),
                    CreditCardId = card.Id,
                    PostingDate = progressBefore.PeriodStart.AddDays(3),
                    Amount = midPeriodCharge,
                    Description = "Plansız Market Harcaması"
                }]
            });

            var progressAfter = await service.GetPeriodProgressAsync();
            Assert.NotNull(progressAfter);

            // 1. PLANLANAN kolonları KİLİTLİ KALMALIDIR (Değişemez!)
            Assert.Equal(initialPlannedMandatory, progressAfter!.PlannedMandatoryPayments);
            Assert.Equal(initialPlannedIncome, progressAfter.PlannedIncome);
            Assert.Equal(initialPlannedLiving, progressAfter.PlannedVariableExpenseAllowance);
            Assert.Equal(initialPlannedEnding, progressAfter.PlannedEndingBalance);

            // 2. 12 Dönem projeksiyonundaki Period 0 kilitli kalmalıdır
            var periodsAfter = await service.GetFuturePeriodsAsync(periodCount: 12);
            Assert.Equal(period0EndingBefore, periodsAfter[0].EndingProjectedBalance);

            // 3. GİDİŞAT (Projected Ending) yeni harcamayı yansıtarak sapmayı göstermelidir
            if (progressAfter.ProjectedEndingSavings.HasValue && progressBefore.ProjectedEndingSavings.HasValue)
            {
                Assert.True(progressAfter.ProjectedEndingSavings.Value < progressBefore.ProjectedEndingSavings.Value);
            }
        });
    }

    /// <summary>
    /// REGRESYON 3: Kredi Kartı Ödeme ve Ekstre Hesaplama Kuralları.
    /// - Borçtan fazla ödeme planlanamaz (Math.Min).
    /// - Yasal asgari oranın altına inilemez (Math.Max).
    /// - Sabit tutar (FixedAmount) ödeme planında dönem içi ek harcama o dönemin ödemesini artırmaz.
    /// </summary>
    [Fact]
    public void Regression_CreditCard_PaymentCalculation_Respects_LegalAndDebtLimits()
    {
        var calculator = new CreditCardStatementCalculator();
        var cardId = Guid.NewGuid();

        // Kart limiti > 50.000 TL ise yasal asgari oran %40'tır
        var highLimitCard = new CreditCard
        {
            Id = cardId,
            Name = "HighLimit",
            Bank = "Garanti",
            Limit = 100_000m,
            CarriedBalance = 0m,
            UnbilledSpending = 10_000m,
            BalanceAsOfDate = SeedDate,
            StatementClosingDay = 25,
            PaymentDueDay = 5,
            MinimumPaymentRate = 0.40m,
            PaymentStrategy = CreditCardPaymentStrategy.Minimum
        };

        var highProjection = calculator.Project(highLimitCard, 1, false);
        Assert.Single(highProjection);
        // 10.000 TL borcun %40'ı = 4.000 TL asgari
        Assert.Equal(4_000m, highProjection[0].MinimumPayment);
        Assert.Equal(4_000m, highProjection[0].Payment);

        // Kural: Sabit ödeme (FixedAmount) 15.000 TL girilse bile ekstre borcu 10.000 TL ise ödeme 10.000 TL'ye budanır (Math.Min)
        var fixedOverCard = highLimitCard with
        {
            PaymentStrategy = CreditCardPaymentStrategy.FixedAmount,
            FixedPaymentAmount = 15_000m
        };
        var fixedOverProjection = calculator.Project(fixedOverCard, 1, false);
        Assert.Equal(10_000m, fixedOverProjection[0].Payment);

        // Kural: Sabit ödeme 2.000 TL girilse bile asgari 4.000 TL olduğu için yasal asgari 4.000 TL'ye yükseltilir (Math.Max)
        var fixedUnderCard = highLimitCard with
        {
            PaymentStrategy = CreditCardPaymentStrategy.FixedAmount,
            FixedPaymentAmount = 2_000m
        };
        var fixedUnderProjection = calculator.Project(fixedUnderCard, 1, false);
        Assert.Equal(4_000m, fixedUnderProjection[0].Payment);
    }

    /// <summary>
    /// REGRESYON 4: Kredi İtfa Tablosu ve Erken Kapama / Ara Ödeme Regresyonu.
    /// Kredi erken kapama teklifinde (LoanPayoffQuote), erken kapama yapıldığında
    /// faiz tasarrufu pozitif olmalı ve ödenmeyecek taksitler toplamından az tutmalıdır.
    /// </summary>
    [Fact]
    public void Regression_LoanAmortization_EarlyClosure_Calculates_InterestSaving()
    {
        var scheduleCalculator = new LoanScheduleCalculator();
        var amortizationCalculator = new LoanAmortizationCalculator(scheduleCalculator);

        // 100.000 TL kalan anapara, 10.500 TL aylık taksit, 12 ay vade
        var loan = new Loan
        {
            Id = Guid.NewGuid(),
            Name = "İhtiyaç Kredisi",
            Bank = "İş Bankası",
            MonthlyPayment = 10_500m,
            PaymentDay = 15,
            NextPaymentDate = new DateOnly(2026, 9, 15),
            RemainingInstallmentCount = 12,
            RemainingDebt = 100_000m
        };

        var analysis = amortizationCalculator.Analyze(loan);
        Assert.NotNull(analysis.Amortization);
        Assert.True(analysis.Amortization!.MonthlyRate > 0m);

        // 3 taksit ödendikten sonra kalan anapara başlangıçtakinden az olmalıdır
        var principalAfter3 = LoanAmortizationCalculator.PrincipalAfter(analysis.Amortization, 3);
        Assert.True(principalAfter3 < 100_000m);
        Assert.True(principalAfter3 > 0m);

        var quote = amortizationCalculator.PayoffOn(loan, analysis.Amortization, new DateOnly(2026, 8, 25));
        Assert.NotNull(quote);
        Assert.True(quote.HasAnythingToClose);
        Assert.Equal(12, quote.InstallmentsRemoved);

        // Kapatma tutarı kalan anapara civarında olmalı ve toplam taksit tutarından (12 * 10.500 = 126.000) küçük olmalıdır
        Assert.True(quote.Amount < loan.RemainingInstallmentTotal);
        // Faiz tasarrufu pozitif olmalıdır
        Assert.True(quote.InterestSaving > 0m);
    }

    /// <summary>
    /// REGRESYON 5: Eksi Bakiye ve KMH Faiz Tahakkuku Dinamiği.
    /// Hesap bakiyesi negatife düştüğünde, eksi bakiye üzerinden günlük KMH faizi
    /// dinamik olarak dönem sonu tahminine dahil edilir. Dönem içi plansız harcama açığı artırdığında
    /// hesaplanan KMH faizi de kesinlikle artmalıdır.
    /// </summary>
    [Fact]
    public async Task Regression_Deficit_KMH_Interest_IncreasesDynamically_WithDeficitGrowth()
    {
        await WithCanonicalService(async service =>
        {
            // Bakiyeyi eksiye düşür (-50.000 TL gözlem)
            await service.ObserveCurrentBalanceAsync(-50_000m);

            var progressInitial = await service.GetPeriodProgressAsync();
            Assert.NotNull(progressInitial);
            Assert.NotNull(progressInitial!.ProjectedDeficitInterest);
            var initialDeficitInterest = progressInitial.ProjectedDeficitInterest!.Value;
            Assert.True(initialDeficitInterest > 0m, "Eksi bakiye durumunda KMH faizi sıfırdan büyük olmalıdır.");

            // Dönem içinde 20.000 TL ek plansız harcama yap (Açığı -70.000 TL'ye derinleştir)
            await service.ObserveCurrentBalanceAsync(-70_000m);

            var progressDeeper = await service.GetPeriodProgressAsync();
            Assert.NotNull(progressDeeper);
            Assert.NotNull(progressDeeper!.ProjectedDeficitInterest);
            var deeperDeficitInterest = progressDeeper.ProjectedDeficitInterest!.Value;

            // Artan açık karşısında KMH faizi de KESİNLİKLE artmalıdır
            Assert.True(
                deeperDeficitInterest > initialDeficitInterest,
                $"Açık büyüdüğünde KMH faizi artmalıdır! Önceki: {initialDeficitInterest}, Yeni: {deeperDeficitInterest}");
        });
    }

    /// <summary>
    /// REGRESYON 6: 12 Aylık Finansal Hedef Birikim ve Erişilebilirlik (Target Reachability).
    /// Hedef birikim tutarına hangi ayda ulaşılacağı projeksiyon zinciri üzerinden
    /// kuruşu kuruşuna tespit edilmelidir.
    /// </summary>
    [Fact]
    public async Task Regression_TargetReachability_AccuratelyIdentifies_GoalPeriod()
    {
        await WithCanonicalService(async service =>
        {
            var periods = await service.GetFuturePeriodsAsync(periodCount: 12);
            var maxBalance = periods.Max(p => p.EndingProjectedBalance);

            if (maxBalance > 0m)
            {
                // Ulaşılabilir bir hedef belirle (en yüksek bakiyenin yarısı)
                var achievableTarget = decimal.Round(maxBalance / 2m, 0);
                var reachability = await service.FindTargetReachabilityAsync(achievableTarget);

                Assert.True(reachability.IsReached);
                if (reachability.FirstReachedPeriod is not null)
                {
                    Assert.True(reachability.FirstReachedPeriod.EndingProjectedBalance >= achievableTarget);
                }

                // Ulaşılamaz bir hedef belirle (mevcut projeksiyonun çok üstünde)
                var impossibleTarget = maxBalance + 10_000_000m;
                var unreachable = await service.FindTargetReachabilityAsync(impossibleTarget);
                Assert.False(unreachable.IsReached);
            }
        });
    }

    private static async Task WithCanonicalService(
        Func<MizanService, Task> test)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"mizan-regression-{Guid.NewGuid():N}.db3");
        try
        {
            await using var store = new SqliteMizanStore(path, true, SeedDate);
            var service = TestFactory.Service(store, SeedDate);
            await service.LoadCanonicalDevelopmentDataAsync();
            await service.GetFinancialPlanAsync();
            await test(service);
        }
        finally
        {
            foreach (var suffix in new[] { string.Empty, "-shm", "-wal" })
            {
                if (File.Exists(path + suffix))
                {
                    File.Delete(path + suffix);
                }
            }
        }
    }
}
