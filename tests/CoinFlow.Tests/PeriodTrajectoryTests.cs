using CoinFlow.Application.Services;
using CoinFlow.Infrastructure.Persistence;

namespace CoinFlow.Tests;

/// <summary>
/// Gidişat hesabı: "elimdeki tutarla kalan ödemeleri yapınca dönem sonu ve
/// faiz ne olur?" Her parametre plandaki karşılığıyla kıyaslanabilir olmalı.
/// </summary>
/// <remarks>
/// İki gerçek hatadan doğdu. Önce gidişat faizi hiç saymıyordu ve plana tam
/// uyan kullanıcıya faiz kadar kâr gösteriyordu. Sonra faiz plandan sabit
/// alındı — oysa açık faizi gözlenen pozisyonla **değişir** ve kullanıcının
/// görmek istediği tam olarak o değişimdi.
///
/// Kanonik seed'in faizi sıfır olduğu için mevcut testlerin hiçbiri bunları
/// yakalayamıyordu; buradaki senaryolar bilinçli olarak açık durumda kurulur.
/// </remarks>
public sealed class PeriodTrajectoryTests
{
    private static readonly DateOnly SeedDate = new(2026, 8, 20);

    /// <summary>
    /// Asıl regresyon testi. Plana tam uyuyorsan her sütun eşit, her fark
    /// sıfır olmalı; başka her sonuç iki tarafın farklı kalemler içerdiğini
    /// gösterir.
    /// </summary>
    [Fact]
    public async Task OnPlan_EveryParameterMatches()
    {
        await WithDeficitPlan(async (store, plan) =>
        {
            Assert.True(
                plan.PlannedDeficitInterest > 0m,
                "Senaryo faizsiz kurulmuş; test asıl riski ölçmüyor.");

            var elapsed = 2;
            var service = TestFactory.Service(
                store,
                plan.PeriodStart.AddDays(elapsed));
            await service.ObserveCurrentBalanceAsync(
                OnPlanBalance(plan, elapsed));
            var progress = await service.GetPeriodProgressAsync();

            Assert.NotNull(progress);
            Assert.Equal(0m, progress!.BalanceDeviation);
            Assert.Equal(0m, progress.DeficitInterestDeviation);
            Assert.Equal(0m, progress.Deviation);
            Assert.Equal(
                plan.PlannedDeficitInterest,
                progress.ProjectedDeficitInterest);
            Assert.Equal(
                plan.PlannedEndingSavings,
                progress.ProjectedEndingSavings);
        });
    }

    /// <summary>
    /// Kullanıcının asıl istediği: "faiz A oluyor, planda B idi, C fark."
    /// Açık faizi gözlenen pozisyondan yeniden hesaplanır, plandan
    /// kopyalanmaz.
    /// </summary>
    [Fact]
    public async Task WorsePosition_RaisesTheDeficitInterest()
    {
        await WithDeficitPlan(async (store, plan) =>
        {
            var elapsed = 2;
            var service = TestFactory.Service(
                store,
                plan.PeriodStart.AddDays(elapsed));
            var settings = await store.GetSettingsAsync();

            await service.ObserveCurrentBalanceAsync(
                OnPlanBalance(plan, elapsed) - 20_000m);
            var progress = await service.GetPeriodProgressAsync();

            Assert.NotNull(progress);
            // 20.000 daha kötü pozisyon → açığın mutlak değeri 20.000 büyür.
            // ending = before − faiz olduğu için before = ending + faiz.
            var plannedBefore = plan.PlannedEndingSavings +
                                plan.PlannedDeficitInterest;
            var expectedInterest = decimal.Round(
                (Math.Abs(plannedBefore) + 20_000m) *
                settings.DeficitFinancingInterestRate,
                2,
                MidpointRounding.AwayFromZero);
            Assert.Equal(expectedInterest, progress!.ProjectedDeficitInterest);
            Assert.True(
                progress.DeficitInterestDeviation > 0m,
                "Pozisyon kötüleşince faiz artmalı.");
            Assert.Equal(-20_000m, progress.BalanceDeviation);
        });
    }

    /// <summary>
    /// I9 — kart faizi karta kapitalize olur, nakit dönem sonunu değiştirmez.
    /// Motor da (<c>PeriodPlanSnapshotService.Freeze</c>) onu
    /// <c>PlannedEndingSavings</c>'e katmaz; gidişat da katmamalı.
    /// </summary>
    [Fact]
    public async Task CardInterest_DoesNotEnterTheCashEnding()
    {
        await WithDeficitPlan(async (store, plan) =>
        {
            var elapsed = 2;
            var service = TestFactory.Service(
                store,
                plan.PeriodStart.AddDays(elapsed));
            await service.ObserveCurrentBalanceAsync(
                OnPlanBalance(plan, elapsed));
            var progress = await service.GetPeriodProgressAsync();

            Assert.NotNull(progress);
            // Nakit dönem sonu = faiz öncesi − yalnız açık faizi.
            var endingBefore = progress!.ObservedBalance!.Value
                               - progress.RemainingPlannedTotal
                               - progress.RemainingLivingBudget;
            Assert.Equal(
                endingBefore - progress.ProjectedDeficitInterest!.Value,
                progress.ProjectedEndingSavings);
        });
    }

    [Fact]
    public async Task Overspending_ShowsANegativeDeviation()
    {
        await WithDeficitPlan(async (store, plan) =>
        {
            var elapsed = 2;
            var service = TestFactory.Service(
                store,
                plan.PeriodStart.AddDays(elapsed));

            await service.ObserveCurrentBalanceAsync(
                OnPlanBalance(plan, elapsed) - 5_000m);
            var progress = await service.GetPeriodProgressAsync();

            Assert.NotNull(progress);
            Assert.Equal(-5_000m, progress!.BalanceDeviation);
            // Dönem sonu farkı harcamadan **büyüktür**: açık faizi de artar.
            Assert.True(
                progress.Deviation < -5_000m,
                "Kötüleşen pozisyon faizi de artırdığı için dönem sonu farkı " +
                "harcama farkından büyük olmalı.");
        });
    }

    [Fact]
    public async Task UnderSpending_ShowsAPositiveDeviation()
    {
        await WithDeficitPlan(async (store, plan) =>
        {
            var elapsed = 2;
            var service = TestFactory.Service(
                store,
                plan.PeriodStart.AddDays(elapsed));

            await service.ObserveCurrentBalanceAsync(
                OnPlanBalance(plan, elapsed) + 3_000m);
            var progress = await service.GetPeriodProgressAsync();

            Assert.NotNull(progress);
            Assert.Equal(3_000m, progress!.BalanceDeviation);
            Assert.True(progress.Deviation > 3_000m);
            Assert.True(
                progress.DeficitInterestDeviation < 0m,
                "İyileşen pozisyon faizi düşürmeli.");
        });
    }

    /// <summary>
    /// Dönem sonu artıya geçerse açık faizi hiç doğmaz.
    /// </summary>
    [Fact]
    public async Task PositionTurningPositive_RemovesTheDeficitInterest()
    {
        await WithDeficitPlan(async (store, plan) =>
        {
            var elapsed = 2;
            var service = TestFactory.Service(
                store,
                plan.PeriodStart.AddDays(elapsed));

            await service.ObserveCurrentBalanceAsync(500_000m);
            var progress = await service.GetPeriodProgressAsync();

            Assert.NotNull(progress);
            Assert.Equal(0m, progress!.ProjectedDeficitInterest);
            Assert.True(progress.ProjectedEndingSavings > 0m);
        });
    }

    /// <summary>
    /// "Şu an" satırının plan sütunu: planın bugün beklediği bakiye.
    /// </summary>
    [Fact]
    public async Task ExpectedBalanceToday_FollowsThePlansOwnPace()
    {
        await WithDeficitPlan(async (store, plan) =>
        {
            var elapsed = 2;
            var service = TestFactory.Service(
                store,
                plan.PeriodStart.AddDays(elapsed));
            await service.ObserveCurrentBalanceAsync(0m);
            var progress = await service.GetPeriodProgressAsync();

            Assert.NotNull(progress);
            Assert.Equal(
                OnPlanBalance(plan, elapsed),
                progress!.ExpectedBalanceToday);
        });
    }

    /// <summary>
    /// Plana tam uygun bakiye: gelir alınmış, bugüne kadar vadesi gelen
    /// satırlar ödenmiş, yaşam gideri günü gününe harcanmış.
    /// </summary>
    private static decimal OnPlanBalance(
        Domain.Models.PeriodPlanSnapshot plan,
        int elapsed)
    {
        var total = plan.PeriodEnd.DayNumber - plan.PeriodStart.DayNumber;
        var dueSoFar = plan.PaymentLines
            .Where(x => x.PlannedDate <= plan.PeriodStart.AddDays(elapsed))
            .Sum(x => x.PlannedAmount ?? 0m);
        var remainingLiving = decimal.Round(
            plan.PlannedLivingBudget * (total - elapsed) / total,
            2,
            MidpointRounding.AwayFromZero);
        return plan.OpeningSavings
               + plan.PlannedIncome
               - dueSoFar
               - (plan.PlannedLivingBudget - remainingLiving);
    }

    /// <summary>
    /// Dönem sonu açık verdiğinde finansman açığı faizi doğar; senaryoyu
    /// bilinçli olarak o duruma kurar.
    /// </summary>
    private static async Task WithDeficitPlan(
        Func<SqliteCoinFlowStore, Domain.Models.PeriodPlanSnapshot, Task> test)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"coinflow-trajectory-{Guid.NewGuid():N}.db3");
        try
        {
            await using var store = new SqliteCoinFlowStore(path, true, SeedDate);
            var seed = TestFactory.Service(store, SeedDate);
            await seed.LoadCanonicalDevelopmentDataAsync();
            await seed.GetFinancialPlanAsync();
            // Checkpoint işlemi; dönem başında çağrılıyor, I14 ihlali değil.
            await seed.RefreshCurrentFinancialStateAsync(-150_000m);

            var history = await store.GetFinancialHistoryAsync();
            var plan = PeriodProgressService.ResolveOpenPlan(history)
                       ?? throw new InvalidOperationException(
                           "Açık dönem planı kurulamadı.");
            await test(store, plan);
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
