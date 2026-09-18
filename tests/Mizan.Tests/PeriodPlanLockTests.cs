using Mizan.Application.Services;
using Mizan.Domain.Models;
using Mizan.Infrastructure.Persistence;

namespace Mizan.Tests;

/// <summary>
/// Dondurulmuş planın kilitli kalması ve dönem içi kart harcaması izolasyonu:
/// Dönem dondurulduktan sonra yapılan harcamalar planı kirletmez;
/// PLANLANAN kolonları kilitli kalır, harcama yalnızca MEVCUT ve GİDİŞAT'a yansır.
/// </summary>
public sealed class PeriodPlanLockTests
{
    private static readonly DateOnly SeedDate = new(2026, 8, 20);
    private static readonly Guid AxessCardId = Guid.Parse("40000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task EnteringCreditCardExpense_KeepsFrozenPlanLocked_AndUpdatesOnlyCurrentTrajectory()
    {
        await WithDeficitPlan(async (store, plan) =>
        {
            var service = ServiceAt(store, plan, 5);
            var startingBalance = StartingPosition(plan) - 10_000m;
            await service.ObserveCurrentBalanceAsync(startingBalance);

            var progressBefore = await service.GetPeriodProgressAsync();
            Assert.NotNull(progressBefore);
            var initialHistory = await store.GetFinancialHistoryAsync();
            var initialRevisionsCount = initialHistory.Revisions.Count;

            var cards = await store.GetCreditCardsAsync();
            var card = cards.First(c => c.Id == AxessCardId);
            var cardBefore = progressBefore!.Cards.First(pc => pc.CardId == card.Id);

            var extraChargeAmount = 2_095m;
            var chargeDate = plan.PeriodStart.AddDays(2);
            var updatedCard = card with
            {
                Charges = [.. card.Charges, new CardCharge
                {
                    Id = Guid.NewGuid(),
                    CreditCardId = card.Id,
                    PostingDate = chargeDate,
                    Amount = extraChargeAmount,
                    Description = "Plansız Harcama"
                }]
            };

            await service.SaveCreditCardAsync(updatedCard);

            // 1. Revizyon üretilmemeli (harcama bir plan revizyonu değildir)
            var historyAfter = await store.GetFinancialHistoryAsync();
            Assert.Equal(initialRevisionsCount, historyAfter.Revisions.Count);

            var progressAfter = await service.GetPeriodProgressAsync();
            Assert.NotNull(progressAfter);

            // 2. Dondurulmuş planın değerleri kilitli kalmalı (değişmemeli)
            Assert.Equal(plan.PlannedEndingBalance, progressAfter!.PlannedEndingBalance);
            Assert.Equal(plan.PlannedDeficitInterest, progressAfter.PlannedDeficitInterest);
            Assert.Equal(plan.PlannedVariableExpenseAllowance, progressAfter.PlannedVariableExpenseAllowance);
            Assert.Equal(plan.PlannedIncome, progressAfter.PlannedIncome);

            // 3. Kart satırında PLANLANAN kilitli kalmalı, MEVCUT güncellenmeli
            var cardAfter = progressAfter.Cards.First(pc => pc.CardId == card.Id);
            Assert.Equal(cardBefore.Planned, cardAfter.Planned);
            Assert.Equal(cardBefore.Current + extraChargeAmount, cardAfter.Current);

            // 4. Gidişat Dönem Sonu (MEVCUT) yeni harcamayı yansıtmalı (sapma görünmeli)
            Assert.NotNull(progressAfter.ProjectedEndingSavings);
            Assert.NotNull(progressBefore.ProjectedEndingSavings);
            Assert.True(progressAfter.ProjectedEndingSavings < progressBefore.ProjectedEndingSavings);

            // 5. KMH faizi MEVCUT değeri artan açık nedeniyle güncellenmeli
            Assert.NotNull(progressAfter.ProjectedDeficitInterest);
            Assert.True(progressAfter.ProjectedDeficitInterest >= progressBefore.ProjectedDeficitInterest);
        });
    }

    [Fact]
    public async Task GetFinancialPlanAsync_DoesNotCreateRevisionsSilently()
    {
        await WithDeficitPlan(async (store, plan) =>
        {
            var service = ServiceAt(store, plan, 2);
            var historyBefore = await store.GetFinancialHistoryAsync();
            var countBefore = historyBefore.Revisions.Count;

            // Sorgu çağrısı yan etki üretmemeli (CQS)
            await service.GetFinancialPlanAsync();
            await service.GetFinancialPlanAsync();

            var historyAfter = await store.GetFinancialHistoryAsync();
            Assert.Equal(countBefore, historyAfter.Revisions.Count);
        });
    }

    private static decimal StartingPosition(PeriodPlanSnapshot plan) =>
        plan.OpeningBalance + plan.PlannedIncome;

    private static MizanService ServiceAt(
        SqliteMizanStore store,
        PeriodPlanSnapshot plan,
        int elapsedDays) =>
        TestFactory.Service(store, plan.PeriodStart.AddDays(elapsedDays));

    private static async Task WithDeficitPlan(
        Func<SqliteMizanStore, PeriodPlanSnapshot, Task> test)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"mizan-lock-{Guid.NewGuid():N}.db3");
        try
        {
            await using var store = new SqliteMizanStore(path, true, SeedDate);
            var seed = TestFactory.Service(store, SeedDate);
            await seed.LoadCanonicalDevelopmentDataAsync();

            // Kartı henüz kesilmiş ekstresi olmayan, harcamaların doğrudan ekstreye yansıdığı duruma getir
            var cards = await store.GetCreditCardsAsync();
            var axess = cards.First(c => c.Id == AxessCardId);
            await store.UpsertCreditCardAsync(axess with
            {
                CurrentStatement = null,
                CurrentStatementPaymentPlan = null,
                PaymentStrategy = CreditCardPaymentStrategy.FullStatement,
                CarriedBalance = 0m,
                UnbilledSpending = 20_000m,
                BalanceAsOfDate = SeedDate,
                StatementClosingDay = 28,
                PaymentDueDay = 5
            });

            await seed.GetFinancialPlanAsync();
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
