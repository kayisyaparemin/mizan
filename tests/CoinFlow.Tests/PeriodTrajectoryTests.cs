using CoinFlow.Application.Services;
using CoinFlow.Infrastructure.Persistence;

namespace CoinFlow.Tests;

/// <summary>
/// Gidişat: "elimdeki tutarla kalan ödemeleri yapınca yaşam gideri havuzumdan
/// ne kalır, KMH faizi ne olur, dönem sonu nereye gider?"
/// </summary>
/// <remarks>
/// Üç hatadan doğdu. (1) Gidişat faizi hiç saymıyordu. (2) Faiz plandan sabit
/// alınıyordu, oysa pozisyonla değişir. (3) Yaşam gideri günlere bölünüyordu,
/// oysa bir **havuz**: 20.000 planlanıp 15.000 harcandıysa 5.000 kalmıştır.
/// Üçüncüsü en sinsisiydi — plana tam uyan kullanıcıya yoktan sapma
/// uyduruyordu.
///
/// Kanonik seed'in faizi sıfır olduğu için senaryolar bilinçli olarak açık
/// durumda kurulur.
/// </remarks>
public sealed class PeriodTrajectoryTests
{
    private static readonly DateOnly SeedDate = new(2026, 8, 20);

    /// <summary>
    /// Asıl regresyon testi: hiç harcama yapılmamışken gidişat planın
    /// aynısını vermeli.
    /// </summary>
    [Fact]
    public async Task WithNothingSpentYet_TheTrajectoryMatchesThePlan()
    {
        await WithDeficitPlan(async (store, plan) =>
        {
            Assert.True(
                plan.PlannedDeficitInterest > 0m,
                "Senaryo faizsiz kurulmuş; test asıl riski ölçmüyor.");

            var service = ServiceAt(store, plan, 2);
            await service.ObserveCurrentBalanceAsync(StartingPosition(plan));
            var progress = await service.GetPeriodProgressAsync();

            Assert.NotNull(progress);
            Assert.Equal(0m, progress!.ObservedLivingSpend);
            Assert.Equal(plan.PlannedLivingBudget, progress.RemainingLivingBudget);
            Assert.Equal(
                plan.PlannedDeficitInterest,
                progress.ProjectedDeficitInterest);
            Assert.Equal(
                plan.PlannedEndingSavings,
                progress.ProjectedEndingSavings);
            Assert.Equal(0m, progress.EndingDeviation);
        });
    }

    /// <summary>
    /// Havuzun **içinde** harcamak dönem sonunu değiştirmez. Planlanan 20.000
    /// iken 15.000 harcandıysa 5.000 kalmıştır; toplam yine 20.000.
    /// Eski günlere bölen model burada yoktan sapma üretiyordu.
    /// </summary>
    [Fact]
    public async Task SpendingInsideTheBudget_DoesNotMoveThePeriodEnding()
    {
        await WithDeficitPlan(async (store, plan) =>
        {
            var spent = 5_000m;
            Assert.True(spent < plan.PlannedLivingBudget);

            var service = ServiceAt(store, plan, 2);
            await service.ObserveCurrentBalanceAsync(
                StartingPosition(plan) - spent);
            var progress = await service.GetPeriodProgressAsync();

            Assert.NotNull(progress);
            Assert.Equal(spent, progress!.ObservedLivingSpend);
            Assert.Equal(
                plan.PlannedLivingBudget - spent,
                progress.RemainingLivingBudget);
            // Havuzdan harcamak toplamı değiştirmez.
            Assert.Equal(
                plan.PlannedEndingSavings,
                progress.ProjectedEndingSavings);
            Assert.Equal(0m, progress.EndingDeviation);
            Assert.Null(progress.LivingOverspend);
        });
    }

    /// <summary>
    /// Havuz aşılınca fazlası dönem sonuna yansır — ve KMH faizi de artar.
    /// </summary>
    [Fact]
    public async Task SpendingBeyondTheBudget_WorsensTheEndingAndTheInterest()
    {
        await WithDeficitPlan(async (store, plan) =>
        {
            var overspend = 5_000m;
            var service = ServiceAt(store, plan, 2);
            await service.ObserveCurrentBalanceAsync(
                StartingPosition(plan) - plan.PlannedLivingBudget - overspend);
            var progress = await service.GetPeriodProgressAsync();

            Assert.NotNull(progress);
            Assert.Equal(0m, progress!.RemainingLivingBudget);
            Assert.Equal(overspend, progress.LivingOverspend);
            // Dönem sonu aşım kadar kötüleşir, üstüne artan faiz biner.
            Assert.True(progress.EndingDeviation < -overspend);
            Assert.True(progress.DeficitInterestDeviation > 0m);
        });
    }

    /// <summary>
    /// Vadesi geçen plan satırı ödenmiş sayılır; kullanıcıdan ayrıca
    /// işaretlemesi istenmez. Karta ekstre kesilmeden ödeme yapılmaz ve vade
    /// günü gelen ödeme yapılır — planın kendi varsayımı da budur.
    /// </summary>
    [Fact]
    public async Task LinesPastTheirDueDate_CountAsPaid()
    {
        await WithDeficitPlan(async (store, plan) =>
        {
            var lastDue = plan.PaymentLines.Max(x => x.PlannedDate);
            var after = lastDue.DayNumber - plan.PeriodStart.DayNumber + 1;
            var service = ServiceAt(store, plan, after);
            // Bütün satırlar ödenmiş, hiç yaşam gideri harcanmamış pozisyon.
            var paid = plan.PaymentLines.Sum(x => x.PlannedAmount ?? 0m);
            await service.ObserveCurrentBalanceAsync(
                StartingPosition(plan) - paid);
            var progress = await service.GetPeriodProgressAsync();

            Assert.NotNull(progress);
            Assert.Empty(progress!.RemainingLines);
            Assert.Equal(0m, progress.RemainingPlannedTotal);
            Assert.Equal(0m, progress.ObservedLivingSpend);
            Assert.Equal(
                plan.PlannedEndingSavings,
                progress.ProjectedEndingSavings);
        });
    }

    /// <summary>
    /// I9 — kart faizi karta kapitalize olur, nakit dönem sonuna girmez.
    /// Motor da (<c>PeriodPlanSnapshotService.Freeze</c>) onu katmıyor.
    /// </summary>
    [Fact]
    public async Task CardInterest_DoesNotEnterTheCashEnding()
    {
        await WithDeficitPlan(async (store, plan) =>
        {
            var service = ServiceAt(store, plan, 2);
            await service.ObserveCurrentBalanceAsync(StartingPosition(plan));
            var progress = await service.GetPeriodProgressAsync();

            Assert.NotNull(progress);
            var endingBefore = progress!.ObservedBalance!.Value
                               - progress.RemainingPlannedTotal
                               - progress.RemainingLivingBudget!.Value;
            Assert.Equal(
                endingBefore - progress.ProjectedDeficitInterest!.Value,
                progress.ProjectedEndingSavings);
        });
    }

    [Fact]
    public async Task PositionTurningPositive_RemovesTheDeficitInterest()
    {
        await WithDeficitPlan(async (store, plan) =>
        {
            var service = ServiceAt(store, plan, 2);
            await service.ObserveCurrentBalanceAsync(500_000m);
            var progress = await service.GetPeriodProgressAsync();

            Assert.NotNull(progress);
            Assert.Equal(0m, progress!.ProjectedDeficitInterest);
            Assert.True(progress.ProjectedEndingSavings > 0m);
            // Planda faiz vardı; satır kaybolmaz, iyileşmeyi göstermesi
            // gerekiyor. Alan yalnız iki taraf da sıfırken hiç açılmaz.
            Assert.True(progress.HasDeficitFinancing);
        });
    }

    /// <summary>
    /// Kart satırları plandaki ödeme ile kartın şu anki hâlini yan yana
    /// koyar; dönemde kart yoksa bölüm hiç üretilmez.
    /// </summary>
    [Fact]
    public async Task CardRows_PairThePlanWithTheCurrentCard()
    {
        await WithDeficitPlan(async (store, plan) =>
        {
            var service = ServiceAt(store, plan, 2);
            await service.ObserveCurrentBalanceAsync(StartingPosition(plan));
            var progress = await service.GetPeriodProgressAsync();

            Assert.NotNull(progress);
            var cardLines = plan.PaymentLines
                .Count(x => x.SourceType ==
                            Domain.Models.PlanPaymentSourceType.CreditCard);
            Assert.Equal(cardLines, progress!.Cards.Count);
            Assert.All(progress.Cards, card =>
                Assert.True(card.Planned > 0m));
        });
    }

    /// <summary>Gözlem yoksa hiçbir gidişat rakamı üretilmez.</summary>
    [Fact]
    public async Task WithoutAnObservation_NoTrajectoryIsProduced()
    {
        await WithDeficitPlan(async (store, plan) =>
        {
            var service = ServiceAt(store, plan, 2);
            var progress = await service.GetPeriodProgressAsync();

            Assert.NotNull(progress);
            Assert.Null(progress!.ObservedLivingSpend);
            Assert.Null(progress.RemainingLivingBudget);
            Assert.Null(progress.ProjectedDeficitInterest);
            Assert.Null(progress.ProjectedEndingSavings);
        });
    }

    /// <summary>Dönem başı pozisyonu: açılış + dönemin geliri.</summary>
    private static decimal StartingPosition(
        Domain.Models.PeriodPlanSnapshot plan) =>
        plan.OpeningSavings + plan.PlannedIncome;

    private static Application.Services.CoinFlowService ServiceAt(
        SqliteCoinFlowStore store,
        Domain.Models.PeriodPlanSnapshot plan,
        int elapsedDays) =>
        TestFactory.Service(store, plan.PeriodStart.AddDays(elapsedDays));

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
