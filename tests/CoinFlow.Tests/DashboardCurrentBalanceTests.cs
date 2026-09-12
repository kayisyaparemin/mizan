using CoinFlow.Application.Services;
using CoinFlow.Infrastructure.Persistence;

namespace CoinFlow.Tests;

/// <summary>
/// Mevcut tutar Ana Sayfa'da bir **gözlemdir** (I14). v1.3.0'da checkpoint
/// ilerletme aletine bağlanmıştı ve bu testler tam tersini doğruluyordu;
/// ölçüldüğünde görüldü ki dönem içinde çağrıldığında donmuş planın
/// penceresi kısalıyor, orijinal plan yetim kalıyor ve plan/gerçek
/// karşılaştırması anlamsızlaşıyor.
///
/// Buradaki testler artık bozulmanın regresyon testidir: gözlem yazmak
/// snapshot zincirine dokunmamalı.
/// </summary>
public sealed class DashboardCurrentBalanceTests
{
    private static readonly DateOnly SeedDate = new(2026, 8, 20);
    private static readonly DateOnly MidPeriod = new(2026, 9, 7);

    /// <summary>
    /// Planın en önemli testi. Ölçülen bozulma buydu: 20.08–10.09 penceresi
    /// 07.09–10.09'a düşüyor, zorunlu 54.823 → 0, ödeme satırı 2 → 0.
    /// </summary>
    [Fact]
    public async Task ObservingMidPeriod_LeavesTheFrozenPlanUntouched()
    {
        await WithSeededStore(async store =>
        {
            var before = await store.GetFinancialHistoryAsync();
            var planBefore = Assert.Single(before.Plans);

            var service = TestFactory.Service(store, MidPeriod);
            await service.ObserveCurrentBalanceAsync(-81_000m);

            var after = await store.GetFinancialHistoryAsync();
            var planAfter = Assert.Single(after.Plans);

            Assert.Equal(planBefore.Id, planAfter.Id);
            Assert.Equal(planBefore.PeriodStart, planAfter.PeriodStart);
            Assert.Equal(planBefore.PeriodEnd, planAfter.PeriodEnd);
            Assert.Equal(
                planBefore.PlannedMandatoryPayments,
                planAfter.PlannedMandatoryPayments);
            Assert.Equal(
                planBefore.PlannedLivingBudget,
                planAfter.PlannedLivingBudget);
            Assert.Equal(
                planBefore.PaymentLines.Count,
                planAfter.PaymentLines.Count);
        });
    }

    [Fact]
    public async Task ObservingMidPeriod_DoesNotCreateASnapshot()
    {
        await WithSeededStore(async store =>
        {
            var before = await store.GetFinancialHistoryAsync();
            var snapshotBefore = Assert.Single(before.Snapshots);

            var service = TestFactory.Service(store, MidPeriod);
            await service.ObserveCurrentBalanceAsync(-81_000m);
            await service.ObserveCurrentBalanceAsync(-40_000m);

            var after = await store.GetFinancialHistoryAsync();
            var snapshotAfter = Assert.Single(after.Snapshots);

            Assert.Equal(snapshotBefore.Id, snapshotAfter.Id);
            Assert.True(snapshotAfter.IsCurrent);
            // Çapa ve başlangıç durumu checkpoint'in malıdır; gözlem onlara
            // dokunmaz.
            Assert.Equal(SeedDate, snapshotAfter.ProjectionAnchorDate);
            Assert.Equal(
                snapshotBefore.ProjectionStartingSavings,
                snapshotAfter.ProjectionStartingSavings);
        });
    }

    [Fact]
    public async Task ObservingMidPeriod_DoesNotMoveTheReviewCheckpoint()
    {
        await WithSeededStore(async store =>
        {
            var before = await store.GetFinancialHistoryAsync();
            var reviewBefore = Assert.Single(before.Plans).ReviewAvailableFrom;

            var service = TestFactory.Service(store, MidPeriod);
            await service.ObserveCurrentBalanceAsync(-81_000m);

            var after = await store.GetFinancialHistoryAsync();
            Assert.Equal(
                reviewBefore,
                Assert.Single(after.Plans).ReviewAvailableFrom);
            // Projeksiyonun çapası da yerinde: 12 Dönem kaymaz (I16).
            var settings = (await service.GetFinancialPlanAsync()).Settings;
            Assert.Equal(SeedDate, settings.ProjectionAnchorDate);
        });
    }

    /// <summary>
    /// Gözlem defteri açık plan başına tektir; ikinci gözlem üzerine yazar.
    /// </summary>
    [Fact]
    public async Task RepeatedObservations_KeepASingleLedger()
    {
        await WithSeededStore(async store =>
        {
            var service = TestFactory.Service(store, MidPeriod);
            await service.ObserveCurrentBalanceAsync(-81_000m);
            var second = await service.ObserveCurrentBalanceAsync(-40_000m);

            var progress = await service.GetPeriodProgressAsync();

            Assert.NotNull(progress);
            Assert.Equal(-40_000m, progress!.ObservedBalance);
            Assert.Equal(second.Id, progress.Observation!.Id);
            Assert.Equal(MidPeriod, progress.Observation.ObservedOn);
        });
    }

    /// <summary>
    /// Gözlem yoksa gidişat hesaplanmaz. Rakam uydurmaktansa bloğu hiç
    /// göstermemek doğrudur.
    /// </summary>
    [Fact]
    public async Task WithoutAnObservation_ThereIsNoTrajectory()
    {
        await WithSeededStore(async store =>
        {
            var service = TestFactory.Service(store, MidPeriod);
            var progress = await service.GetPeriodProgressAsync();

            Assert.NotNull(progress);
            Assert.False(progress!.HasObservation);
            Assert.Null(progress.ObservedBalance);
            Assert.Null(progress.ProjectedEndingSavings);
            Assert.Null(progress.EndingDeviation);
            // Plan tarafı yine de dolu.
            Assert.NotEqual(0m, progress.PlannedEndingSavings);
        });
    }

    /// <summary>
    /// Ana Sayfa'nın kendi verisi: donmuş planın penceresi ve kalan satırları.
    /// Gelecek projeksiyonundan değil, dönemin kendi planından gelir (I16).
    /// </summary>
    [Fact]
    public async Task Progress_ComesFromTheFrozenPlanWindow()
    {
        await WithSeededStore(async store =>
        {
            var service = TestFactory.Service(store, MidPeriod);
            var progress = await service.GetPeriodProgressAsync();
            var plan = Assert.Single(
                (await store.GetFinancialHistoryAsync()).Plans);

            Assert.NotNull(progress);
            Assert.Equal(plan.PeriodStart, progress!.PeriodStart);
            Assert.Equal(plan.PeriodEnd, progress.PeriodEnd);
            Assert.Equal(plan.PlannedEndingSavings, progress.PlannedEndingSavings);
            Assert.Equal(18, progress.ElapsedDays);
            Assert.Equal(21, progress.TotalDays);
            Assert.False(progress.IsClosable);
        });
    }

    /// <summary>
    /// KALAN yalnız vadesi **gelmemiş** satırları taşır. Karta ekstre
    /// kesilmeden ödeme yapılmaz ve vade günü gelen ödeme yapılır; planın
    /// kendi varsayımı budur. Kullanıcıdan ayrıca "ödedim" demesini istemek
    /// gereksiz — o kayıt zaten sonraki maaş döneminin review'ında tutulur.
    /// </summary>
    [Fact]
    public async Task RemainingLines_DropALineOnceItsDueDatePasses()
    {
        await WithSeededStore(async store =>
        {
            var plan = Assert.Single(
                (await store.GetFinancialHistoryAsync()).Plans);
            var firstDue = plan.PaymentLines.Min(x => x.PlannedDate);

            var before = await TestFactory
                .Service(store, firstDue.AddDays(-1))
                .GetPeriodProgressAsync();
            var after = await TestFactory
                .Service(store, firstDue)
                .GetPeriodProgressAsync();

            Assert.Equal(plan.PaymentLines.Count, before!.RemainingLines.Count);
            Assert.DoesNotContain(
                after!.RemainingLines,
                x => x.PlannedDate <= firstDue);
            Assert.True(
                after.RemainingPlannedTotal < before.RemainingPlannedTotal,
                "Vadesi geçen satır kalan toplamdan da düşmeli.");
        });
    }

    [Fact]
    public async Task AtTheCheckpoint_ThePeriodBecomesClosable()
    {
        await WithSeededStore(async store =>
        {
            var service = TestFactory.Service(store, new DateOnly(2026, 9, 10));
            var progress = await service.GetPeriodProgressAsync();

            Assert.NotNull(progress);
            Assert.True(progress!.IsClosable);
        });
    }

    private static async Task WithSeededStore(
        Func<SqliteCoinFlowStore, Task> test)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"coinflow-balance-{Guid.NewGuid():N}.db3");
        try
        {
            await using var store = new SqliteCoinFlowStore(path, true, SeedDate);
            var seed = TestFactory.Service(store, SeedDate);
            await seed.LoadCanonicalDevelopmentDataAsync();
            await seed.GetFinancialPlanAsync();
            await test(store);
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
