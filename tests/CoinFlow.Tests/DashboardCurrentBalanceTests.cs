using CoinFlow.Infrastructure.Persistence;

namespace CoinFlow.Tests;

/// <summary>
/// Mevcut tutar Ayarlar'dan Ana Sayfa'ya taşındı ve artık
/// <c>RefreshCurrentFinancialStateAsync</c> üzerinden gidiyor. Kritik nokta
/// rakamın kaydedilmesi değil, **çapanın birlikte ilerlemesi**: "X tarihinde
/// Y param vardı" tek bir cümledir. Çapa geride kalırsa arada ödediğin her şey
/// hâlâ gelecek ödeme olarak planlanır ve çift sayılır.
/// </summary>
public sealed class DashboardCurrentBalanceTests
{
    private static readonly DateOnly SeedDate = new(2026, 8, 20);
    private static readonly DateOnly Today = new(2026, 9, 7);

    [Fact]
    public async Task UpdatingTheCurrentBalance_MovesTheAnchorToToday()
    {
        await WithSeededStore(async store =>
        {
            var service = TestFactory.Service(store, Today);
            var before = (await service.GetFinancialPlanAsync()).Settings;
            Assert.Equal(SeedDate, before.ProjectionAnchorDate);

            await service.RefreshCurrentFinancialStateAsync(-81_000m);

            var after = (await service.GetFinancialPlanAsync()).Settings;
            Assert.Equal(-81_000m, after.ProjectionStartingSavings);
            Assert.Equal(Today, after.ProjectionAnchorDate);
        });
    }

    [Fact]
    public async Task UpdatedBalance_BecomesTheOpeningOfTheNextPeriod()
    {
        await WithSeededStore(async store =>
        {
            var service = TestFactory.Service(store, Today);

            await service.RefreshCurrentFinancialStateAsync(-81_000m);

            var first = (await service.GetFuturePeriodsAsync(periodCount: 1))[0];
            Assert.Equal(-81_000m, first.OpeningProjectedSavings);
            Assert.Equal(Today, first.ProjectionAnchorDate);
        });
    }

    /// <summary>
    /// I5 / I6 — her güncelleme geçmişe bir kayıt düşer; öncekinin üzerine
    /// yazılmaz ve geçmiş yeniden hesaplanmaz.
    /// </summary>
    [Fact]
    public async Task EachUpdate_LeavesTheEarlierSnapshotInHistory()
    {
        await WithSeededStore(async store =>
        {
            var service = TestFactory.Service(store, Today);
            var seeded = (await store.GetFinancialHistoryAsync())
                .Snapshots.Count;

            await service.RefreshCurrentFinancialStateAsync(-81_000m);
            await service.RefreshCurrentFinancialStateAsync(-40_000m);

            var history = await store.GetFinancialHistoryAsync();
            Assert.Equal(seeded + 2, history.Snapshots.Count);
            var current = Assert.Single(history.Snapshots, x => x.IsCurrent);
            Assert.Equal(-40_000m, current.ProjectionStartingSavings);
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
