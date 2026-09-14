using CoinFlow.Application.Models;
using CoinFlow.Application.Services;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;
using CoinFlow.Infrastructure.Persistence;

namespace CoinFlow.Tests;

/// <summary>
/// Finansal Yapı'dan simülatörle aynı formla girilen kayıtlar. Asıl sözleşme
/// paritedir: simüle edip gördüğün 12 dönem, aynı koşulu doğrudan girdikten
/// sonraki 12 dönemle birebir aynıdır. Kanonik seed: çapa 20.08.2026, ilk maaş
/// 10.09.2026.
/// </summary>
public sealed class ScenarioDirectEntryTests
{
    private static readonly DateOnly SeedDate = new(2026, 8, 20);

    private static readonly Guid AxessCardId =
        Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid GarantiId =
        Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid BurganId =
        Guid.Parse("20000000-0000-0000-0000-000000000002");

    private static readonly string[] CaseKeys =
    [
        "cash",
        "card-single",
        "card-installment",
        "recurring",
        "financing",
        "cash-debt",
        "loan-closure",
        "loan-partial",
        "income"
    ];

    public static IEnumerable<object[]> DirectEntryCases =>
        CaseKeys.Select(key => new object[] { key });

    private static SimulationRequest Request(string key) => key switch
    {
        "cash" => new(
            SimulationScenarioType.CashPurchase,
            "Beyaz eşya",
            25_000m,
            new DateOnly(2026, 10, 15),
            ScenarioId: Guid.NewGuid()),
        "card-single" => new(
            SimulationScenarioType.CreditCardSinglePayment,
            "Telefon",
            12_000m,
            new DateOnly(2026, 10, 5),
            CreditCardId: AxessCardId,
            ScenarioId: Guid.NewGuid()),
        "card-installment" => new(
            SimulationScenarioType.CreditCardInstallmentPurchase,
            "Bilgisayar",
            30_000m,
            new DateOnly(2026, 10, 5),
            PaymentCount: 3,
            CreditCardId: AxessCardId,
            ScenarioId: Guid.NewGuid()),
        "recurring" => new(
            SimulationScenarioType.RecurringPayment,
            "Spor salonu",
            1_500m,
            new DateOnly(2026, 10, 1),
            PaymentCount: 6,
            FirstPaymentDate: new DateOnly(2026, 10, 1),
            ScenarioId: Guid.NewGuid()),
        "financing" => new(
            SimulationScenarioType.FinancingLoan,
            "Taşıt kredisi",
            120_000m,
            new DateOnly(2026, 10, 12),
            PaymentCount: 12,
            FirstPaymentDate: new DateOnly(2026, 11, 12),
            TotalRepaymentAmount: 145_000m,
            ScenarioId: Guid.NewGuid()),
        "cash-debt" => new(
            SimulationScenarioType.CashDebt,
            "Aileden borç",
            40_000m,
            new DateOnly(2026, 10, 1),
            PaymentCount: 4,
            FirstPaymentDate: new DateOnly(2026, 11, 1),
            ScenarioId: Guid.NewGuid()),
        "loan-closure" => new(
            SimulationScenarioType.LoanEarlyClosure,
            "Burgan erken kapama",
            0m,
            new DateOnly(2026, 12, 18),
            ScenarioId: Guid.NewGuid(),
            LoanId: BurganId),
        "loan-partial" => new(
            SimulationScenarioType.LoanPartialPrepayment,
            "Garanti ara ödeme",
            50_000m,
            new DateOnly(2027, 1, 7),
            ScenarioId: Guid.NewGuid(),
            LoanId: GarantiId,
            PrepaymentMode: LoanPrepaymentMode.ReduceTerm),
        "income" => new(
            SimulationScenarioType.FutureIncome,
            "Prim",
            20_000m,
            new DateOnly(2026, 11, 20),
            ScenarioId: Guid.NewGuid()),
        _ => throw new ArgumentOutOfRangeException(nameof(key))
    };

    [Fact]
    public void Cases_CoverEveryDirectEntryType()
    {
        var covered = CaseKeys
            .Select(key => Request(key).Type)
            .ToHashSet();
        var expected = SimulationScenarioCatalog.Options
            .Where(x => x.EntryHome == ScenarioEntryHome.SharedForm)
            .SelectMany(x => x.Types)
            // Eski tür: yeni koşul olarak girilmez, yalnız kayıtlı planlarda yaşar.
            .Where(x => x != SimulationScenarioType.FutureOneTimePayment)
            .ToHashSet();

        Assert.Equal(expected, covered);
    }

    [Theory]
    [MemberData(nameof(DirectEntryCases))]
    public async Task DirectEntry_ProducesExactlyTheSimulatedProjection(string key)
    {
        await WithSeededStore(async (_, service) =>
        {
            var request = Request(key);
            var before = await service.GetFuturePeriodsAsync();
            var simulated = await service.SimulateAsync(request);

            // Parite ancak kıyas aynı zeminden başlıyorsa anlamlı.
            Assert.Equal(
                before.Select(x => x.EndingProjectedSavings),
                simulated.Baseline.Select(x => x.EndingProjectedSavings));
            Assert.NotEqual(
                simulated.Baseline.Select(x => x.EndingProjectedSavings),
                simulated.Scenario.Select(x => x.EndingProjectedSavings));

            var result = await service.AddRecordFromScenarioAsync(request);
            var after = await service.GetFuturePeriodsAsync();

            Assert.False(result.AlreadyApplied);
            Assert.Equal(
                simulated.Scenario.Select(x => x.PeriodStart),
                after.Select(x => x.PeriodStart));
            Assert.Equal(
                simulated.Scenario.Select(x => x.EndingProjectedSavings),
                after.Select(x => x.EndingProjectedSavings));
            Assert.Equal(
                simulated.Scenario.Select(x => x.MandatoryOutflow),
                after.Select(x => x.MandatoryOutflow));
        });
    }

    [Fact]
    public async Task SameEntryTwice_DoesNotDuplicateTheRecord()
    {
        await WithSeededStore(async (_, service) =>
        {
            var request = Request("cash-debt");

            await service.AddRecordFromScenarioAsync(request);
            var second = await service.AddRecordFromScenarioAsync(request);

            Assert.True(second.AlreadyApplied);
            var plan = await service.GetFinancialPlanAsync();
            Assert.Single(plan.PaymentPlans, x => x.Id == request.ScenarioId);
        });
    }

    [Fact]
    public async Task CardInstallments_AreWrittenToTheCard()
    {
        await WithSeededStore(async (_, service) =>
        {
            var request = Request("card-installment");

            var result = await service.AddRecordFromScenarioAsync(request);

            Assert.Equal(SimulationApplyDestination.CreditCard, result.Destination);
            var card = (await service.GetFinancialPlanAsync()).CreditCards
                .Single(x => x.Id == AxessCardId);
            var charges = card.Charges
                .Where(x => x.Description.StartsWith("Bilgisayar", StringComparison.Ordinal))
                .OrderBy(x => x.PostingDate)
                .ToArray();
            Assert.Equal(3, charges.Length);
            Assert.Equal(30_000m, charges.Sum(x => x.Amount));
            Assert.Equal(request.ScenarioId, charges[0].Id);
        });
    }

    [Fact]
    public async Task Financing_WritesThePrincipalAndTheRepayment()
    {
        await WithSeededStore(async (_, service) =>
        {
            var request = Request("financing");

            await service.AddRecordFromScenarioAsync(request);

            var plan = await service.GetFinancialPlanAsync();
            var income = Assert.Single(plan.OtherIncomes, x => x.Id == request.ScenarioId);
            Assert.Equal(120_000m, income.Amount);
            var repayment = Assert.Single(plan.PaymentPlans, x => x.Id == request.ScenarioId);
            Assert.Equal(PaymentPlanKind.Installment, repayment.Kind);
            Assert.Equal(12, repayment.Installments.Count);
            Assert.Equal(145_000m, repayment.Installments.Sum(x => x.Amount));
        });
    }

    [Fact]
    public async Task TypesWithTheirOwnScreen_AreRejected()
    {
        await WithSeededStore(async (_, service) =>
        {
            var salary = new SimulationRequest(
                SimulationScenarioType.SalaryChange,
                "Zam",
                140_000m,
                new DateOnly(2026, 12, 1),
                ScenarioId: Guid.NewGuid());
            var cardMode = new SimulationRequest(
                SimulationScenarioType.CreditCardPaymentMode,
                "Tamamını öde",
                0m,
                new DateOnly(2026, 10, 5),
                CreditCardId: AxessCardId,
                ScenarioId: Guid.NewGuid(),
                CardPaymentType: CreditCardPaymentType.FullStatement);
            var before = await service.GetFinancialPlanAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AddRecordFromScenarioAsync(salary));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AddRecordFromScenarioAsync(cardMode));

            var after = await service.GetFinancialPlanAsync();
            Assert.Equal(before.Salaries.Count, after.Salaries.Count);
            Assert.Equal(
                before.CreditCards.Single(x => x.Id == AxessCardId).PaymentPlans.Count,
                after.CreditCards.Single(x => x.Id == AxessCardId).PaymentPlans.Count);
        });
    }

    [Fact]
    public async Task IncomeBeforeTheAnchor_IsRejectedLikeInTheSimulator()
    {
        await WithSeededStore(async (_, service) =>
        {
            var request = new SimulationRequest(
                SimulationScenarioType.FutureIncome,
                "Eski prim",
                5_000m,
                new DateOnly(2026, 8, 1),
                ScenarioId: Guid.NewGuid());

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AddRecordFromScenarioAsync(request));
            Assert.DoesNotContain(
                (await service.GetFinancialPlanAsync()).OtherIncomes,
                x => x.Id == request.ScenarioId);
        });
    }

    private static async Task WithSeededStore(
        Func<SqliteCoinFlowStore, CoinFlowService, Task> test)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"coinflow-direct-entry-{Guid.NewGuid():N}.db3");
        try
        {
            await using var store = new SqliteCoinFlowStore(path, true, SeedDate);
            var service = TestFactory.Service(store, SeedDate);
            await service.LoadCanonicalDevelopmentDataAsync();
            await service.GetFinancialPlanAsync();
            await test(store, service);
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
