using CoinFlow.Domain.Models;
using CoinFlow.Infrastructure.Persistence;

namespace CoinFlow.Tests;

/// <summary>
/// Gözlem defteri: dönem içinde ne olduğunun kalıcı kaydı. Checkpoint'te
/// review'ın girdisi olur ve tüketilir (I15). Snapshot zincirine hiç dokunmaz
/// (I14) — o kısmın regresyonu <see cref="DashboardCurrentBalanceTests"/>.
/// </summary>
public sealed class PeriodObservationTests
{
    private static readonly DateOnly SeedDate = new(2026, 8, 20);
    /// <summary>
    /// Dönemin ilk günleri: hiçbir plan satırının vadesi gelmemiş. Vadesi
    /// geçen satır kendiliğinden ödenmiş sayıldığı için (bkz.
    /// <see cref="DashboardCurrentBalanceTests"/>) buradaki testler elle
    /// işaretlemeyi ölçebilsin diye o tarihten önce durur.
    /// </summary>
    private static readonly DateOnly BeforeAnyDueDate = new(2026, 8, 25);
    private static readonly DateOnly Checkpoint = new(2026, 9, 10);

    [Fact]
    public async Task Observation_SurvivesARestart()
    {
        var path = NewPath();
        try
        {
            await using (var store = NewStore(path))
            {
                await SeedAsync(store);
                var service = TestFactory.Service(store, BeforeAnyDueDate);
                await service.ObserveCurrentBalanceAsync(-81_000m);
            }

            await using (var reopened = NewStore(path))
            {
                var service = TestFactory.Service(reopened, BeforeAnyDueDate);
                var progress = await service.GetPeriodProgressAsync();

                Assert.NotNull(progress);
                Assert.Equal(-81_000m, progress!.ObservedBalance);
                Assert.Equal(BeforeAnyDueDate, progress.Observation!.ObservedOn);
            }
        }
        finally
        {
            Cleanup(path);
        }
    }

    /// <summary>
    /// Ödenmiş işaretlenen satır KALAN listesinden düşer; toplam da düşer.
    /// </summary>
    [Fact]
    public async Task ObservingAPayment_RemovesItFromWhatIsLeft()
    {
        await WithSeededStore(async store =>
        {
            var service = TestFactory.Service(store, BeforeAnyDueDate);
            var before = await service.GetPeriodProgressAsync();
            Assert.NotNull(before);
            var line = before!.RemainingLines[0];
            var beforeCount = before.RemainingLines.Count;
            var beforeTotal = before.RemainingPlannedTotal;

            await service.ObservePaymentAsync(
                line.Id,
                ActualPaymentStatus.Paid,
                line.PlannedAmount ?? 0m);

            var after = await service.GetPeriodProgressAsync();
            Assert.NotNull(after);
            Assert.Equal(beforeCount - 1, after!.RemainingLines.Count);
            Assert.DoesNotContain(after.RemainingLines, x => x.Id == line.Id);
            Assert.Equal(
                beforeTotal - (line.PlannedAmount ?? 0m),
                after.RemainingPlannedTotal);
        });
    }

    [Fact]
    public async Task ObservingAPayment_DoesNotTouchTheFrozenPlan()
    {
        await WithSeededStore(async store =>
        {
            var service = TestFactory.Service(store, BeforeAnyDueDate);
            var progress = await service.GetPeriodProgressAsync();
            var line = progress!.RemainingLines[0];

            await service.ObservePaymentAsync(
                line.Id,
                ActualPaymentStatus.Paid,
                line.PlannedAmount ?? 0m);

            var history = await store.GetFinancialHistoryAsync();
            var plan = Assert.Single(history.Plans);
            // Plan satırı yerinde duruyor; düşen yalnız KALAN görünümü.
            Assert.Contains(plan.PaymentLines, x => x.Id == line.Id);
            Assert.Single(history.Snapshots);
        });
    }

    [Fact]
    public async Task ObservingAPaymentOutsideThePlan_IsRejected()
    {
        await WithSeededStore(async store =>
        {
            var service = TestFactory.Service(store, BeforeAnyDueDate);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ObservePaymentAsync(
                    Guid.NewGuid(),
                    ActualPaymentStatus.Paid,
                    1_000m));
        });
    }

    /// <summary>
    /// I15 — checkpoint'te review kullanıcının dönem boyunca girdiğiyle
    /// dolu gelir; sıfırdan veri girişi değil onaydır.
    /// </summary>
    [Fact]
    public async Task AtTheCheckpoint_TheReviewDraftArrivesPreFilled()
    {
        await WithSeededStore(async store =>
        {
            var midService = TestFactory.Service(store, BeforeAnyDueDate);
            var progress = await midService.GetPeriodProgressAsync();
            var line = progress!.RemainingLines[0];
            await midService.ObservePaymentAsync(
                line.Id,
                ActualPaymentStatus.DifferentAmount,
                12_345m);
            await midService.ObserveCurrentBalanceAsync(-81_000m);

            var service = TestFactory.Service(store, Checkpoint);
            var draft = await service.GetObservedReviewDraftAsync(
                progress.PeriodPlanSnapshotId);

            Assert.NotNull(draft);
            Assert.Equal(-81_000m, draft!.ConfirmedStartingSavings);
            var payment = Assert.Single(draft.Payments);
            Assert.Equal(line.Id, payment.PeriodPlanPaymentLineId);
            Assert.Equal(ActualPaymentStatus.DifferentAmount, payment.Status);
            Assert.Equal(12_345m, payment.ActualAmount);
        });
    }

    [Fact]
    public async Task WithoutAnObservation_TheReviewDraftIsNull()
    {
        await WithSeededStore(async store =>
        {
            var service = TestFactory.Service(store, Checkpoint);
            var progress = await service.GetPeriodProgressAsync();

            Assert.Null(await service.GetObservedReviewDraftAsync(
                progress!.PeriodPlanSnapshotId));
        });
    }

    /// <summary>
    /// Şema v12 → v13 üç tablo ekler; mevcut veri olduğu gibi açılmalı.
    /// </summary>
    [Fact]
    public async Task ReopeningAnExistingDatabase_KeepsThePlanIntact()
    {
        var path = NewPath();
        try
        {
            decimal savings;
            DateOnly anchor;
            await using (var store = NewStore(path))
            {
                var service = await SeedAsync(store);
                var settings = (await service.GetFinancialPlanAsync()).Settings;
                savings = settings.ProjectionStartingSavings;
                anchor = settings.ProjectionAnchorDate;
            }

            await using (var reopened = NewStore(path))
            {
                var service = TestFactory.Service(reopened, SeedDate);
                var plan = await service.GetFinancialPlanAsync();

                Assert.Equal(savings, plan.Settings.ProjectionStartingSavings);
                Assert.Equal(anchor, plan.Settings.ProjectionAnchorDate);
                Assert.NotEmpty(plan.Salaries);
                Assert.Single(plan.CreditCards);
                Assert.Single((await reopened.GetFinancialHistoryAsync()).Plans);
            }
        }
        finally
        {
            Cleanup(path);
        }
    }

    private static async Task<Application.Services.CoinFlowService> SeedAsync(
        SqliteCoinFlowStore store)
    {
        var seed = TestFactory.Service(store, SeedDate);
        await seed.LoadCanonicalDevelopmentDataAsync();
        await seed.GetFinancialPlanAsync();
        return seed;
    }

    private static async Task WithSeededStore(
        Func<SqliteCoinFlowStore, Task> test)
    {
        var path = NewPath();
        try
        {
            await using var store = NewStore(path);
            await SeedAsync(store);
            await test(store);
        }
        finally
        {
            Cleanup(path);
        }
    }

    private static SqliteCoinFlowStore NewStore(string path) =>
        new(path, true, SeedDate);

    private static string NewPath() => Path.Combine(
        Path.GetTempPath(),
        $"coinflow-observation-{Guid.NewGuid():N}.db3");

    private static void Cleanup(string path)
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
