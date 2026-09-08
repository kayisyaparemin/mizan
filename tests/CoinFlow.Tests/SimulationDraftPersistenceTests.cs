using CoinFlow.Application.Models;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;
using CoinFlow.Infrastructure.Persistence;

namespace CoinFlow.Tests;

/// <summary>
/// Simülatördeki taslak yalnızca bellekte duruyordu: uygulama kapanınca
/// kullanıcının kurduğu deneme kayboluyordu. Geçici planlar bunu çözer.
/// Buradaki testler kaydın gerçekten diske indiğini, alan alan geri
/// geldiğini ve apply akışına dokunmadığını sabitler.
/// </summary>
public sealed class SimulationDraftPersistenceTests
{
    private static readonly DateOnly SeedDate = new(2026, 8, 20);

    /// <summary>Bildirilen sorun: uygulama kapanınca deneme kayboluyordu.</summary>
    [Fact]
    public async Task SavedDraft_SurvivesARestart()
    {
        var path = NewPath();
        try
        {
            await using (var store = NewStore(path))
            {
                var service = TestFactory.Service(store, SeedDate);
                await service.SaveSimulationDraftAsync(
                    "Beyaz eşya denemesi",
                    [new SimulationDraftCondition(CashPurchase(), true)]);
            }

            await using (var reopened = NewStore(path))
            {
                var service = TestFactory.Service(reopened, SeedDate);
                var draft = Assert.Single(
                    await service.GetSimulationDraftsAsync());

                Assert.Equal("Beyaz eşya denemesi", draft.Name);
                Assert.Single(draft.Conditions);
            }
        }
        finally
        {
            Cleanup(path);
        }
    }

    /// <summary>
    /// Her alan tek tek sütuna yazılıyor; biri unutulursa geri yüklenen plan
    /// sessizce başka bir plan olur.
    /// </summary>
    [Fact]
    public async Task SavedDraft_RoundTripsEveryField()
    {
        await WithStore(async store =>
        {
            var service = TestFactory.Service(store, SeedDate);
            var cardId = Guid.NewGuid();
            var scenarioId = Guid.NewGuid();
            var request = new SimulationRequest(
                SimulationScenarioType.CreditCardInstallmentPurchase,
                "Taksitli alışveriş",
                48_000m,
                new DateOnly(2026, 11, 3),
                PaymentCount: 6,
                FirstPaymentDate: new DateOnly(2026, 12, 5),
                CreditCardId: cardId,
                TotalRepaymentAmount: 52_000m,
                NewPaymentAssignmentMode: PaymentAssignmentMode.PreviousPeriod,
                EffectiveSalaryDate: new DateOnly(2027, 1, 10),
                ScenarioId: scenarioId,
                CardPaymentType: CreditCardPaymentType.FullStatement,
                AppliesToAllStatements: true);

            await service.SaveSimulationDraftAsync(
                "Tam kayıt",
                [new SimulationDraftCondition(request, false)]);

            var draft = Assert.Single(
                await service.GetSimulationDraftsAsync());
            var condition = Assert.Single(draft.Conditions);

            // Kapalı koşul kapalı geri gelmeli: kullanıcı bilerek kapatmıştı.
            Assert.False(condition.IsEnabled);
            Assert.Equal(request, condition.Request);
            // ScenarioId apply yolunda entity kimliklerini üretiyor; değişirse
            // aynı plan ikinci kez uygulandığında mükerrer kayıt oluşur.
            Assert.Equal(scenarioId, condition.Request.ScenarioId);
        });
    }

    [Fact]
    public async Task SavedDraft_KeepsConditionOrder()
    {
        await WithStore(async store =>
        {
            var service = TestFactory.Service(store, SeedDate);
            await service.SaveSimulationDraftAsync(
                "Sıralı",
                [
                    new SimulationDraftCondition(CashPurchase("Birinci"), true),
                    new SimulationDraftCondition(CashPurchase("İkinci"), false),
                    new SimulationDraftCondition(CashPurchase("Üçüncü"), true)
                ]);

            var draft = Assert.Single(
                await service.GetSimulationDraftsAsync());

            Assert.Equal(
                new[] { "Birinci", "İkinci", "Üçüncü" },
                draft.Conditions.Select(x => x.Request.Name));
            Assert.Equal(2, draft.EnabledConditionCount);
        });
    }

    [Fact]
    public async Task SavingOverAnExistingDraft_UpdatesItInPlace()
    {
        await WithStore(async store =>
        {
            var service = TestFactory.Service(store, SeedDate);
            var first = await service.SaveSimulationDraftAsync(
                "Deneme",
                [new SimulationDraftCondition(CashPurchase(), true)]);

            var updated = await service.SaveSimulationDraftAsync(
                "Deneme",
                [
                    new SimulationDraftCondition(CashPurchase("Bir"), true),
                    new SimulationDraftCondition(CashPurchase("İki"), true)
                ],
                first.Id);

            var draft = Assert.Single(
                await service.GetSimulationDraftsAsync());
            Assert.Equal(first.Id, updated.Id);
            Assert.Equal(2, draft.Conditions.Count);
            // Oluşturma zamanı korunur, güncelleme zamanı ilerler.
            Assert.Equal(first.CreatedAt, draft.CreatedAt);
        });
    }

    [Fact]
    public async Task DeletingADraft_TakesItsConditionsWithIt()
    {
        await WithStore(async store =>
        {
            var service = TestFactory.Service(store, SeedDate);
            var kept = await service.SaveSimulationDraftAsync(
                "Kalan",
                [new SimulationDraftCondition(CashPurchase("Kalan"), true)]);
            var removed = await service.SaveSimulationDraftAsync(
                "Gidecek",
                [new SimulationDraftCondition(CashPurchase("Gidecek"), true)]);

            await service.DeleteSimulationDraftAsync(removed.Id);

            var draft = Assert.Single(
                await service.GetSimulationDraftsAsync());
            Assert.Equal(kept.Id, draft.Id);
            // Silinen planın koşulu geride kalırsa kalan plana yapışırdı.
            Assert.Equal("Kalan", Assert.Single(draft.Conditions).Request.Name);
        });
    }

    [Fact]
    public async Task EmptyNameOrNoConditions_IsRejected()
    {
        await WithStore(async store =>
        {
            var service = TestFactory.Service(store, SeedDate);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.SaveSimulationDraftAsync(
                    "   ",
                    [new SimulationDraftCondition(CashPurchase(), true)]));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.SaveSimulationDraftAsync("Boş", []));
            Assert.Empty(await service.GetSimulationDraftsAsync());
        });
    }

    /// <summary>
    /// Geçici plan bir deneme; uygulanan plan gerçek kayıt. Apply akışı
    /// kaydedilmiş planlara dokunmamalı — kullanıcı aynı denemeyi tekrar
    /// yükleyip üzerine kurabilmeli.
    /// </summary>
    [Fact]
    public async Task ApplyingAPlan_LeavesSavedDraftsAlone()
    {
        await WithStore(async store =>
        {
            var service = TestFactory.Service(store, SeedDate);
            await service.LoadCanonicalDevelopmentDataAsync();
            await service.GetFinancialPlanAsync();

            var request = CashPurchase("Tadilat");
            await service.SaveSimulationDraftAsync(
                "Tadilat denemesi",
                [new SimulationDraftCondition(request, true)]);

            await service.ApplySimulationAsync([request], confirmed: true);

            var draft = Assert.Single(
                await service.GetSimulationDraftsAsync());
            Assert.Equal("Tadilat denemesi", draft.Name);
            Assert.Equal(
                request,
                Assert.Single(draft.Conditions).Request);
            // Apply gerçekten çalışmış olmalı; test kendini kandırmasın.
            Assert.Contains(
                (await service.GetFinancialPlanAsync()).PlannedLargeExpenses,
                x => x.Name == "Tadilat");
        });
    }

    /// <summary>
    /// Şema v11 → v12 yalnız iki tablo ekliyor. Mevcut verinin olduğu gibi
    /// açılması gerekiyor; bu gerçek bir kullanıcının veritabanı.
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
                var service = TestFactory.Service(store, SeedDate);
                await service.LoadCanonicalDevelopmentDataAsync();
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
                // Yeni tablolar hazır ve boş.
                Assert.Empty(await service.GetSimulationDraftsAsync());
            }
        }
        finally
        {
            Cleanup(path);
        }
    }

    private static SimulationRequest CashPurchase(string name = "Tadilat") =>
        new(
            SimulationScenarioType.CashPurchase,
            name,
            120_000m,
            new DateOnly(2027, 3, 15),
            ScenarioId: Guid.NewGuid());

    private static async Task WithStore(Func<SqliteCoinFlowStore, Task> test)
    {
        var path = NewPath();
        try
        {
            await using var store = NewStore(path);
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
        $"coinflow-draft-{Guid.NewGuid():N}.db3");

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
