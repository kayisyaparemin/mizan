using CoinFlow.Application.Services;
using CoinFlow.Infrastructure.Persistence;

namespace CoinFlow.Tests;

/// <summary>
/// Gidişat hesabının plan ile **kıyaslanabilir** olması. Plan ve gidişat aynı
/// kalemleri içermezse fark rakamı yalan söyler.
/// </summary>
/// <remarks>
/// Bu sınıf gerçek bir hatadan doğdu: gidişat faizi hiç saymıyordu, plan
/// sayıyordu. Plana tam uygun giden kullanıcıya faiz kadar **kâr** gösteriyordu.
/// Kanonik seed'in faizi sıfır olduğu için mevcut testlerin hiçbiri bunu
/// yakalayamadı — buradaki senaryolar bilinçli olarak faizli kurulur.
/// </remarks>
public sealed class PeriodTrajectoryTests
{
    private static readonly DateOnly SeedDate = new(2026, 8, 20);

    /// <summary>
    /// Asıl regresyon testi. Kullanıcı plana tam uyuyorsa fark sıfır olmalı;
    /// başka her sonuç, iki tarafın farklı kalemler içerdiği anlamına gelir.
    /// </summary>
    [Fact]
    public async Task OnPlan_ShowsNoDeviation()
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
            var total = plan.PeriodEnd.DayNumber - plan.PeriodStart.DayNumber;
            // "Tam plana uygun": gelir alınmış, hiç ödeme yapılmamış,
            // yaşam gideri günü gününe planın oranında.
            var onPlan = plan.OpeningSavings
                         + plan.PlannedIncome
                         - decimal.Round(
                             plan.PlannedLivingBudget * elapsed / total,
                             2);

            await service.ObserveCurrentBalanceAsync(onPlan);
            var progress = await service.GetPeriodProgressAsync();

            Assert.NotNull(progress);
            Assert.Equal(0m, progress!.Deviation);
            Assert.Equal(
                plan.PlannedEndingSavings,
                progress.ProjectedEndingSavings);
        });
    }

    /// <summary>
    /// Faiz gözlenen nakit bakiyenin içinde değildir — kart faizi karta
    /// kapitalize olur (I9), açık faizi bir planlama kalemidir. İkisi de
    /// dönem sonuna kadar önümüzdedir, gidişattan düşülmeleri gerekir.
    /// </summary>
    [Fact]
    public async Task Trajectory_SubtractsPlannedInterest()
    {
        await WithDeficitPlan(async (store, plan) =>
        {
            var service = TestFactory.Service(
                store,
                plan.PeriodStart.AddDays(2));
            await service.ObserveCurrentBalanceAsync(0m);
            var progress = await service.GetPeriodProgressAsync();

            Assert.NotNull(progress);
            var interest = plan.PlannedCardInterest +
                           plan.PlannedDeficitInterest;
            Assert.Equal(interest, progress!.PlannedInterest);

            var total = plan.PeriodEnd.DayNumber - plan.PeriodStart.DayNumber;
            var remainingLiving = decimal.Round(
                plan.PlannedLivingBudget * (total - 2) / total,
                2);
            Assert.Equal(
                0m - progress.RemainingPlannedTotal - remainingLiving - interest,
                progress.ProjectedEndingSavings);
        });
    }

    /// <summary>
    /// Planlanandan fazla harcayan kullanıcı eksi fark görmeli. Hatanın
    /// bildirildiği durum tam olarak buydu: harcama fazlaydı, ekran artı
    /// gösteriyordu.
    /// </summary>
    [Fact]
    public async Task Overspending_ShowsANegativeDeviation()
    {
        await WithDeficitPlan(async (store, plan) =>
        {
            var elapsed = 2;
            var service = TestFactory.Service(
                store,
                plan.PeriodStart.AddDays(elapsed));
            var total = plan.PeriodEnd.DayNumber - plan.PeriodStart.DayNumber;
            var onPlan = plan.OpeningSavings
                         + plan.PlannedIncome
                         - decimal.Round(
                             plan.PlannedLivingBudget * elapsed / total,
                             2);

            await service.ObserveCurrentBalanceAsync(onPlan - 5_000m);
            var progress = await service.GetPeriodProgressAsync();

            Assert.NotNull(progress);
            Assert.Equal(-5_000m, progress!.Deviation);
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
            var total = plan.PeriodEnd.DayNumber - plan.PeriodStart.DayNumber;
            var onPlan = plan.OpeningSavings
                         + plan.PlannedIncome
                         - decimal.Round(
                             plan.PlannedLivingBudget * elapsed / total,
                             2);

            await service.ObserveCurrentBalanceAsync(onPlan + 3_000m);
            var progress = await service.GetPeriodProgressAsync();

            Assert.NotNull(progress);
            Assert.Equal(3_000m, progress!.Deviation);
        });
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
