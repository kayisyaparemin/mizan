using CoinFlow.Domain.Calculations;
using CoinFlow.Infrastructure.Persistence;

namespace CoinFlow.Tests;

/// <summary>
/// Ürün kontratındaki invariant'ların gerçek uygulama akışı üzerinden
/// regression koruması. Component testleri değil; servis katmanından
/// okunan sonuçlar doğrulanır.
/// </summary>
public sealed class ProductContractInvariantTests
{
    private static readonly DateOnly Init = new(2026, 8, 20);

    /// <summary>
    /// P6 / I1 — Dashboard ve 12 Dönem aynı projection sonucunu kullanır.
    /// Ayrı bir hesap motoru veya UI katmanında kopyalanmış formül olamaz.
    /// </summary>
    [Fact]
    public async Task P6_DashboardAndTwelvePeriodShareTheSameProjection()
    {
        await WithSeededService(async service =>
        {
            var dashboard = await service.GetDashboardAsync();
            var periods = await service.GetFuturePeriodsAsync(periodCount: 12);

            Assert.NotNull(dashboard);
            Assert.Equal(
                periods[0].PeriodStart,
                dashboard!.CurrentPeriod.PeriodStart);
            Assert.Equal(
                periods[0].EndingProjectedSavings,
                dashboard.CurrentPeriod.EndingProjectedSavings);
            Assert.Equal(
                periods[0].MandatoryOutflow,
                dashboard.CurrentPeriod.MandatoryOutflow);
            Assert.Equal(
                periods[^1].EndingProjectedSavings,
                dashboard.TwelvePeriodEndingProjectedSavings);
        });
    }

    /// <summary>
    /// §20 — Ana projection ufku 12 maaş dönemidir.
    /// </summary>
    [Fact]
    public async Task Horizon_IsTwelveSalaryPeriods()
    {
        await WithSeededService(async service =>
            Assert.Equal(
                12,
                (await service.GetFuturePeriodsAsync(periodCount: 12)).Count));
    }

    /// <summary>
    /// I2 — Dönemler yarı açıktır: bir dönemin bitişi bir sonrakinin
    /// başlangıcıdır. Boşluk veya çakışma olamaz.
    /// </summary>
    [Fact]
    public async Task I2_PeriodsAreHalfOpenAndContiguous()
    {
        await WithSeededService(async service =>
        {
            var periods = await service.GetFuturePeriodsAsync(periodCount: 12);
            for (var i = 1; i < periods.Count; i++)
            {
                Assert.Equal(periods[i - 1].PeriodEnd, periods[i].PeriodStart);
                Assert.True(periods[i - 1].PeriodStart < periods[i - 1].PeriodEnd);
            }
        });
    }

    /// <summary>
    /// I10 — Ödeme pencereleri boşluk veya mükerrer atama üretmez.
    /// Pencere sonu dahil olduğu için bir sonraki pencere ertesi gün başlar.
    /// </summary>
    [Fact]
    public async Task I10_PaymentWindowsCoverWithoutGapOrOverlap()
    {
        await WithSeededService(async service =>
        {
            var periods = await service.GetFuturePeriodsAsync(periodCount: 12);
            for (var i = 1; i < periods.Count; i++)
            {
                Assert.Equal(
                    periods[i - 1].PaymentWindowEnd.AddDays(1),
                    periods[i].PaymentWindowStart);
            }
        });
    }

    /// <summary>
    /// I7 / §12 — CarryOverDeficit ikinci bir yükümlülük değildir.
    /// Faiz öncesi dönem sonu tam olarak opening + net katkıdır; devreden
    /// açık bu hesapta ikinci kez düşülmez.
    /// </summary>
    [Fact]
    public async Task I7_CarryOverDeficitIsNotSubtractedTwice()
    {
        await WithSeededService(async service =>
        {
            foreach (var period in
                     await service.GetFuturePeriodsAsync(periodCount: 12))
            {
                Assert.Equal(
                    period.OpeningProjectedSavings +
                    period.CurrentPeriodNetContribution,
                    period.EndingProjectedSavingsBeforeDeficitInterest);
            }
        });
    }

    /// <summary>
    /// I8 / I9 — Kart faizi ve açık finansman faizi ayrı state'tir; toplam
    /// ikisinin toplamıdır ve kart faizi aynı dönemin zorunlu ödemesine
    /// tekrar yazılmaz.
    /// </summary>
    [Fact]
    public async Task I8_InterestStatesStaySeparate()
    {
        await WithSeededService(async service =>
        {
            foreach (var period in
                     await service.GetFuturePeriodsAsync(periodCount: 12))
            {
                Assert.Equal(
                    period.CardInterestGenerated +
                    period.DeficitFinancingInterest,
                    period.TotalInterestGenerated);
                // Zorunlu çıkış tam olarak yükümlülük satırlarının toplamıdır;
                // kart ödemeleri bu listeye zaten dahildir, ikinci kez eklenmez.
                Assert.Equal(
                    period.MandatoryItems.Sum(x => x.Amount),
                    period.MandatoryOutflow);
                // I9 — kart carry faizi aynı dönemin zorunlu çıkışına yazılmaz.
                // Kart satırlarının toplamı, projeksiyonun kart ödeme
                // kararlarıyla birebir aynı olmalı; faiz eklenmiş olsaydı
                // bu eşitlik bozulurdu.
                Assert.Equal(
                    period.CardPaymentStatuses.Sum(x => x.Payment ?? 0m),
                    period.MandatoryItems
                        .Where(x => x.Type == ObligationType.CreditCard)
                        .Sum(x => x.Amount));
            }
        });
    }

    private static async Task WithSeededService(
        Func<Application.Services.CoinFlowService, Task> test)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"coinflow-contract-{Guid.NewGuid():N}.db3");
        try
        {
            await using var store = new SqliteCoinFlowStore(path, true, Init);
            var service = TestFactory.Service(store, Init);
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
