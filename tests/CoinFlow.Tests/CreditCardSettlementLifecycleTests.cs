using CoinFlow.Application.Models;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;
using CoinFlow.Infrastructure.Persistence;

namespace CoinFlow.Tests;

/// <summary>
/// §23 — Kredi kartı settlement zincirinin UÇTAN UCA doğrulaması.
///
/// Mevcut CreditCardStatementSettlementTests reconciler ve calculator'ı
/// doğrudan çağırır; bu dosya aynı davranışı gerçek akışta doğrular:
/// seed → snapshot → frozen plan → review → actual → FinalizePeriodReviewAsync
/// → reconciliation → yeni canonical state → YENİ PROJECTION.
///
/// Sorulan soru "reconciler doğru hesapladı mı" değil,
/// "hesapladığı şey projection'a yansıdı mı" sorusudur.
/// </summary>
public sealed class CreditCardSettlementLifecycleTests
{
    private static readonly DateOnly InitialDate = new(2026, 8, 20);
    private static readonly DateOnly FirstReviewDate = new(2026, 9, 10);

    private const decimal StatementAmount = 100_804.94m;
    private const decimal MinimumPayment = 40_321.97m;
    private static readonly DateOnly StatementClose = new(2026, 8, 28);
    private static readonly DateOnly StatementDue = new(2026, 9, 7);
    // Bankanın bildirdiği kesin tarihler; genel kesim/vade gününden farklı.
    private static readonly DateOnly BankNextClose = new(2026, 9, 28);
    private static readonly DateOnly BankNextDue = new(2026, 10, 8);
    // Seed kartında 28.09.2026 tarihli bilinen gelecek harcama.
    private const decimal KnownFutureCharge = 15_538.36m;

    /// <summary>
    /// Seed verisinin §23'te tarif edilen kart durumuyla aynı olduğunu
    /// doğrular. Bu tutmazsa aşağıdaki senaryolar başka bir şeyi ölçer.
    /// </summary>
    [Fact]
    public async Task Seed_MatchesScenarioPreconditions()
    {
        await WithStore(async store =>
        {
            var service = TestFactory.Service(store, InitialDate);
            await service.LoadCanonicalDevelopmentDataAsync();
            var card = Assert.Single(
                (await service.GetFinancialPlanAsync()).CreditCards);

            Assert.NotNull(card.CurrentStatement);
            var statement = card.CurrentStatement!;
            Assert.Equal(StatementAmount, statement.StatementAmount);
            Assert.Equal(MinimumPayment, statement.MinimumPaymentAmount);
            Assert.Equal(StatementClose, statement.StatementDate);
            Assert.Equal(StatementDue, statement.DueDate);
            Assert.Equal(BankNextClose, statement.NextStatementDate);
            Assert.Equal(BankNextDue, statement.NextDueDate);
        });
    }

    /// <summary>
    /// §23 ana kontrolü: A (tam ödeme) ve B (kısmi ödeme) finalize sonrası
    /// AYNI projection'ı üretmemelidir. Bug döneminde ikisi birebir aynıydı.
    /// </summary>
    [Fact]
    public async Task FullAndPartialPayment_ProduceDifferentProjections()
    {
        var full = await RunScenarioAsync(StatementAmount);
        var partial = await RunScenarioAsync(50_000m);

        Assert.NotEqual(
            full.NextStatementBalance,
            partial.NextStatementBalance);
        Assert.NotEqual(full.CarriedBalance, partial.CarriedBalance);
        Assert.NotEqual(
            full.TwelvePeriodCardInterest,
            partial.TwelvePeriodCardInterest);
    }

    /// <summary>
    /// A — tam ödeme: ödenmiş principal taşınmaz, üzerinden faiz üretilmez.
    /// Sonraki ekstre yalnız gerçekten bilinen gelecek harcamayı içerir.
    /// (12 dönemlik kart faizi sıfır DEĞİLDİR; gelecek harcamalardan doğar.)
    /// </summary>
    [Fact]
    public async Task FullPayment_CarriesNothingFromSettledStatement()
    {
        var result = await RunScenarioAsync(StatementAmount);

        Assert.Equal(0m, result.CarriedBalance);
        Assert.Equal(0m, result.NextStatementOpeningCarry);
        // Sonraki ekstrenin borcu yalnız 28.09'da düşen harcama kadar.
        Assert.Equal(KnownFutureCharge, result.NextStatementBalance);
        Assert.NotEqual(StatementAmount, result.NextStatementBalance);
    }

    /// <summary>
    /// Tam ödeme, kısmi ödemeye göre kesinlikle daha az faiz yükü üretmeli.
    /// </summary>
    [Fact]
    public async Task FullPayment_ProducesLessInterestThanPartialPayment()
    {
        var full = await RunScenarioAsync(StatementAmount);
        var partial = await RunScenarioAsync(50_000m);

        Assert.True(
            full.TwelvePeriodCardInterest < partial.TwelvePeriodCardInterest,
            $"Tam ödeme faizi ({full.TwelvePeriodCardInterest}) kısmi ödeme " +
            $"faizinden ({partial.TwelvePeriodCardInterest}) küçük olmalı.");
    }

    /// <summary>
    /// I11 — settlement sonrası CurrentStatement kalmaz ama bankanın bildirdiği
    /// kesim tarihine düşen harcama genel kesim gününe kaymamalıdır.
    /// Genel kural 25 olduğu için hata durumunda harcama 25.10'a kayar ve
    /// 28.09 ekstresi boş görünürdü.
    /// </summary>
    [Fact]
    public async Task SettledCard_BillsChargesOnKnownBankCloseDate()
    {
        var result = await RunScenarioAsync(StatementAmount);

        Assert.Equal(BankNextClose, result.NextStatementCloseDate);
        Assert.Equal(KnownFutureCharge, result.NextStatementNewCharges);
    }

    /// <summary>
    /// B — kısmi ödeme: kalan principal projection'da görünür.
    /// 100.804,94 - 50.000 = 50.804,94 kalan, üzerine %5 carry faizi.
    /// </summary>
    [Fact]
    public async Task PartialPayment_CarriesOnlyRemainderIntoNextProjection()
    {
        var result = await RunScenarioAsync(50_000m);

        var expectedPrincipal = StatementAmount - 50_000m;
        var expectedInterest = decimal.Round(
            expectedPrincipal * 0.05m,
            2,
            MidpointRounding.AwayFromZero);
        var expectedCarry = expectedPrincipal + expectedInterest;

        Assert.Equal(expectedCarry, result.CarriedBalance);
        Assert.Equal(expectedCarry, result.NextStatementOpeningCarry);
        // Ödenmiş tutar tekrar borç olarak taşınmamalı.
        Assert.True(result.CarriedBalance < StatementAmount);
    }

    /// <summary>
    /// Ödenmiş ekstre artık authoritative outstanding statement değildir,
    /// ve ekstreye bağlı ödeme planı da sızmaz.
    /// </summary>
    [Fact]
    public async Task SettledStatement_IsRetiredFromCanonicalState()
    {
        var result = await RunScenarioAsync(StatementAmount);

        Assert.True(result.CurrentStatementIsNull);
        Assert.True(result.CurrentStatementPaymentPlanIsNull);
        Assert.Equal(StatementClose.AddDays(1), result.BalanceAsOfDate);
    }

    /// <summary>
    /// I11 — bankanın bildirdiği kesin tarihler settlement'ta kaybolmaz.
    /// Genel kural 25/5 olduğu için kayıp olsaydı 25.09 / 05.10 görürdük.
    /// </summary>
    [Fact]
    public async Task Settlement_PreservesKnownExactBankDates()
    {
        var result = await RunScenarioAsync(StatementAmount);

        Assert.Equal(BankNextClose, result.KnownNextStatementDate);
        Assert.Equal(BankNextDue, result.KnownNextDueDate);
        Assert.Equal(BankNextClose, result.NextStatementCloseDate);
        Assert.Equal(BankNextDue, result.NextStatementDueDate);
    }

    /// <summary>
    /// I6 — finalize edilen dönemin donmuş planı ve actual'ı sonradan
    /// değişmez; yeni snapshot checkpoint tarihine yazılır.
    /// </summary>
    [Fact]
    public async Task Finalization_WritesSnapshotAtCheckpointAndKeepsHistory()
    {
        await WithStore(async store =>
        {
            await FinalizeAsync(store, StatementAmount);
            var history = await store.GetFinancialHistoryAsync();

            var actual = Assert.Single(history.Actuals);
            Assert.Equal(FirstReviewDate, actual.PeriodEnd);

            var current = history.Snapshots.Single(x => x.IsCurrent);
            // Cihaz tarihi değil, planın checkpoint tarihi esas alınır.
            Assert.Equal(FirstReviewDate, current.SnapshotDate);
            Assert.Equal(2, history.Snapshots.Count);
        });
    }

    private static async Task<ScenarioResult> RunScenarioAsync(
        decimal actualCardPayment)
    {
        ScenarioResult? captured = null;
        await WithStore(async store =>
        {
            var service = await FinalizeAsync(store, actualCardPayment);
            var card = Assert.Single(
                (await service.GetFinancialPlanAsync()).CreditCards);
            var plan = await service.GetFinancialPlanAsync();

            // Canonical state'ten değil, gerçek projection motorundan oku.
            var projection = new CreditCardStatementCalculator().Project(
                card,
                2,
                true,
                plan.Settings.CreditCardCarryInterestRate);
            var next = projection[0];
            var periods = await service.GetFuturePeriodsAsync(periodCount: 12);

            captured = new ScenarioResult(
                card.CarriedBalance,
                card.BalanceAsOfDate,
                card.CurrentStatement is null,
                card.CurrentStatementPaymentPlan is null,
                card.KnownNextStatementDate,
                card.KnownNextDueDate,
                next.StatementCloseDate,
                next.PaymentDueDate,
                next.OpeningCarriedBalance ?? 0m,
                next.StatementBalance ?? 0m,
                next.NewCharges,
                next.CarryInterest,
                periods.Sum(x => x.CardInterestGenerated));
        });

        return captured ?? throw new InvalidOperationException(
            "Senaryo sonucu üretilemedi.");
    }

    private static async Task<Application.Services.CoinFlowService>
        FinalizeAsync(SqliteCoinFlowStore store, decimal actualCardPayment)
    {
        var initial = TestFactory.Service(store, InitialDate);
        await initial.LoadCanonicalDevelopmentDataAsync();
        await initial.GetFinancialPlanAsync();

        var review = TestFactory.Service(store, FirstReviewDate);
        var context = await review.GetPeriodReviewContextAsync();
        var lines = context.OriginalPlan.PaymentLines;
        var cardLine = lines
            .Where(x => x.SourceType == PlanPaymentSourceType.CreditCard)
            .OrderBy(x => x.PlannedDate)
            .First();
        Assert.Equal(StatementDue, cardLine.PlannedDate);

        var draft = new PeriodReviewDraft(
            context.OriginalPlan.Id,
            lines.Select(line => new ActualPaymentDraft(
                    line.Id,
                    line.PlannedAmount is null
                        ? ActualPaymentStatus.Unpaid
                        : line.Id == cardLine.Id
                            ? ActualPaymentStatus.DifferentAmount
                            : ActualPaymentStatus.Paid,
                    line.Id == cardLine.Id
                        ? actualCardPayment
                        : line.PlannedAmount.GetValueOrDefault(),
                    line.PlannedAmount is null ? null : line.PlannedDate))
                .ToArray(),
            context.OriginalPlan.PlannedLivingBudget,
            context.OriginalPlan.PlannedDeficitInterest,
            [],
            [],
            null);

        await review.FinalizePeriodReviewAsync(draft);
        return review;
    }

    private sealed record ScenarioResult(
        decimal CarriedBalance,
        DateOnly BalanceAsOfDate,
        bool CurrentStatementIsNull,
        bool CurrentStatementPaymentPlanIsNull,
        DateOnly? KnownNextStatementDate,
        DateOnly? KnownNextDueDate,
        DateOnly NextStatementCloseDate,
        DateOnly NextStatementDueDate,
        decimal NextStatementOpeningCarry,
        decimal NextStatementBalance,
        decimal NextStatementNewCharges,
        decimal NextCarryInterest,
        decimal TwelvePeriodCardInterest);

    private static async Task WithStore(
        Func<SqliteCoinFlowStore, Task> test)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"coinflow-settlement-{Guid.NewGuid():N}.db3");
        try
        {
            await using var store = new SqliteCoinFlowStore(
                path,
                true,
                InitialDate);
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
