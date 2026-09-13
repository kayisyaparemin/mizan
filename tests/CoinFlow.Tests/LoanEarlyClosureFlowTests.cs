using CoinFlow.Application.Models;
using CoinFlow.Application.Services;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;
using CoinFlow.Infrastructure.Persistence;

namespace CoinFlow.Tests;

/// <summary>
/// Erken ödemenin uçtan uca yolu: simüle et → uygula → geri al → checkpoint'te
/// ödendi / ödenmedi. Kanonik seed: Burgan 55.777 anapara, 9 × 7.374,59,
/// ilk taksit 18.09.2026; Garanti 190.188, 22 × 14.501,23, ilk taksit 07.09.2026.
/// </summary>
public sealed class LoanEarlyClosureFlowTests
{
    private static readonly DateOnly SeedDate = new(2026, 8, 20);
    private static readonly DateOnly FirstReviewDate = new(2026, 9, 10);

    private static readonly Guid BurganId =
        Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid GarantiId =
        Guid.Parse("20000000-0000-0000-0000-000000000001");

    private static SimulationRequest Closure(
        Guid loanId,
        DateOnly date) => new(
        SimulationScenarioType.LoanEarlyClosure,
        "Krediyi kapat",
        0m,
        date,
        ScenarioId: Guid.NewGuid(),
        LoanId: loanId);

    private static SimulationRequest Partial(
        Guid loanId,
        DateOnly date,
        decimal principal,
        LoanPrepaymentMode mode) => new(
        SimulationScenarioType.LoanPartialPrepayment,
        "Ara ödeme",
        principal,
        date,
        ScenarioId: Guid.NewGuid(),
        LoanId: loanId,
        PrepaymentMode: mode);

    [Fact]
    public async Task SimulatedClosure_RemovesLaterInstallmentsAndReportsTheSaving()
    {
        await WithSeededStore(async (store, service) =>
        {
            var closureDate = new DateOnly(2027, 1, 18);
            var request = Closure(BurganId, closureDate);

            var result = await service.SimulateAsync(request);

            var scenarioItems = result.Scenario
                .SelectMany(x => x.MandatoryItems)
                .ToArray();
            var payoff = Assert.Single(scenarioItems, x =>
                x.PaymentId == request.ScenarioId);
            Assert.Equal(closureDate, payoff.DueDate);
            Assert.DoesNotContain(scenarioItems, x =>
                x.PaymentId == BurganId && x.DueDate > closureDate);

            var impact = Assert.Single(result.LoanImpacts);
            Assert.Equal(BurganId, impact.LoanId);
            Assert.Equal(payoff.Amount, impact.PrepaidAmount);
            Assert.True(impact.InterestSaving > 0m);
            Assert.Equal(closureDate, impact.ScenarioEndDate);
            Assert.Equal(new DateOnly(2027, 5, 18), impact.BaselineEndDate);
            // Senaryo maliyeti kapatma tutarını içerir.
            Assert.Equal(payoff.Amount, result.Risk.TotalScenarioCost);
        });
    }

    [Fact]
    public async Task AppliedClosure_IsIdempotentAndCanBeUndone()
    {
        await WithSeededStore(async (store, service) =>
        {
            var request = Closure(BurganId, new DateOnly(2027, 1, 18));
            var before = await service.GetFuturePeriodsAsync();

            var applied = await service.ApplySimulationAsync(request, confirmed: true);
            var again = await service.ApplySimulationAsync(request, confirmed: true);

            Assert.False(applied.AlreadyApplied);
            Assert.True(again.AlreadyApplied);
            var stored = Assert.Single(await store.GetLoanPrepaymentsAsync());
            Assert.Equal(request.ScenarioId, stored.Id);
            Assert.Contains(
                (await service.GetFuturePeriodsAsync()).SelectMany(x => x.MandatoryItems),
                x => x.PaymentId == request.ScenarioId);

            await service.DeleteLoanPrepaymentAsync(stored.Id);

            Assert.Empty(await store.GetLoanPrepaymentsAsync());
            var restored = await service.GetFuturePeriodsAsync();
            Assert.Equal(
                before.Select(x => x.EndingProjectedSavings),
                restored.Select(x => x.EndingProjectedSavings));
        });
    }

    [Fact]
    public async Task ClosureOfALoanWithoutPrincipal_IsRejectedWithGuidance()
    {
        await WithSeededStore(async (store, service) =>
        {
            var burgan = (await service.GetFinancialPlanAsync()).Loans
                .Single(x => x.Id == BurganId);
            await service.SaveLoanAsync(burgan with { RemainingDebt = null });

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.SimulateAsync(Closure(BurganId, new DateOnly(2027, 1, 18))));

            Assert.Contains("kalan anaparası", error.Message);
        });
    }

    /// <summary>
    /// Dönem içinde uygulanan kapama plan revizyonuna girer; checkpoint'te
    /// ödendi işaretlenince kredi kapanır ve olay tüketilir.
    /// </summary>
    [Fact]
    public async Task PaidClosureAtTheCheckpoint_ClosesTheLoan()
    {
        await WithSeededStore(async (store, service) =>
        {
            var request = Closure(BurganId, new DateOnly(2026, 9, 5));
            await service.ApplySimulationAsync(request, confirmed: true);

            var review = TestFactory.Service(store, FirstReviewDate);
            var context = await review.GetPeriodReviewContextAsync();
            Assert.Contains(FinalLines(context), x =>
                x.SourceEntityId == request.ScenarioId);

            await review.FinalizePeriodReviewAsync(PaidDraft(context));

            var burgan = (await review.GetFinancialPlanAsync()).Loans
                .Single(x => x.Id == BurganId);
            Assert.False(burgan.IsActive);
            Assert.Equal(0, burgan.RemainingInstallmentCount);
            Assert.Empty(await store.GetLoanPrepaymentsAsync());
            Assert.DoesNotContain(
                (await review.GetFuturePeriodsAsync()).SelectMany(x => x.MandatoryItems),
                x => x.PaymentId == BurganId);
        });
    }

    /// <summary>K6 — ödenmeyen erken ödeme gönüllü bir karardı; iptal olur.</summary>
    [Fact]
    public async Task UnpaidClosureAtTheCheckpoint_IsCancelled()
    {
        await WithSeededStore(async (store, service) =>
        {
            var request = Closure(BurganId, new DateOnly(2026, 9, 5));
            await service.ApplySimulationAsync(request, confirmed: true);

            var review = TestFactory.Service(store, FirstReviewDate);
            var context = await review.GetPeriodReviewContextAsync();
            var draft = PaidDraft(context);
            var closureLine = FinalLines(context)
                .Single(x => x.SourceEntityId == request.ScenarioId);
            draft = draft with
            {
                Payments = draft.Payments.Select(x =>
                    x.PeriodPlanPaymentLineId == closureLine.Id
                        ? x with
                        {
                            Status = ActualPaymentStatus.Unpaid,
                            ActualAmount = 0m,
                            ActualPaymentDate = null
                        }
                        : x).ToArray()
            };

            await review.FinalizePeriodReviewAsync(draft);

            var burgan = (await review.GetFinancialPlanAsync()).Loans
                .Single(x => x.Id == BurganId);
            Assert.True(burgan.IsActive);
            Assert.Equal(9, burgan.RemainingInstallmentCount);
            Assert.Empty(await store.GetLoanPrepaymentsAsync());
            Assert.Contains(
                (await review.GetFuturePeriodsAsync()).SelectMany(x => x.MandatoryItems),
                x => x.PaymentId == BurganId);
        });
    }

    /// <summary>
    /// Vade kısaltan ara ödeme ödenince kanonik kredi daha az taksit ve küçük
    /// son taksit taşır; aynı dönemdeki normal taksit de üstüne işlenir.
    /// </summary>
    [Fact]
    public async Task PaidReduceTermPrepayment_ShortensTheCanonicalLoan()
    {
        await WithSeededStore(async (store, service) =>
        {
            var request = Partial(
                GarantiId,
                new DateOnly(2026, 9, 5),
                50_000m,
                LoanPrepaymentMode.ReduceTerm);
            await service.ApplySimulationAsync(request, confirmed: true);

            var review = TestFactory.Service(store, FirstReviewDate);
            var context = await review.GetPeriodReviewContextAsync();
            await review.FinalizePeriodReviewAsync(PaidDraft(context));

            var garanti = (await review.GetFinancialPlanAsync()).Loans
                .Single(x => x.Id == GarantiId);
            Assert.True(garanti.IsActive);
            Assert.True(garanti.RemainingInstallmentCount < 21);
            Assert.NotNull(garanti.FinalPaymentAmount);
            Assert.True(garanti.FinalPaymentAmount < garanti.MonthlyPayment);
            Assert.Equal(14_501.23m, garanti.MonthlyPayment);
            Assert.True(garanti.RemainingDebt < 190_188m - 50_000m);
            Assert.Equal(
                0.0504m,
                new LoanAmortizationCalculator(new LoanScheduleCalculator())
                    .Analyze(garanti).Amortization!.MonthlyRate,
                4);
        });
    }

    [Fact]
    public async Task LoanConditions_SurviveADraftRoundTrip()
    {
        await WithSeededStore(async (store, service) =>
        {
            var closure = Closure(BurganId, new DateOnly(2027, 1, 18));
            var partial = Partial(
                GarantiId,
                new DateOnly(2026, 12, 7),
                30_000m,
                LoanPrepaymentMode.ReduceInstallment);

            await service.SaveSimulationDraftAsync(
                "Kredi denemesi",
                [
                    new SimulationDraftCondition(closure, true),
                    new SimulationDraftCondition(partial, false)
                ]);

            var draft = Assert.Single(await service.GetSimulationDraftsAsync());
            Assert.Equal(closure, draft.Conditions[0].Request);
            Assert.Equal(partial, draft.Conditions[1].Request);
        });
    }

    private static IReadOnlyList<PeriodPlanPaymentLine> FinalLines(
        PeriodReviewContext context) =>
        context.Revision?.PaymentLines.Count > 0
            ? context.Revision.PaymentLines
            : context.OriginalPlan.PaymentLines;

    private static PeriodReviewDraft PaidDraft(PeriodReviewContext context) => new(
        context.OriginalPlan.Id,
        FinalLines(context).Select(line =>
            new ActualPaymentDraft(
                line.Id,
                line.PlannedAmount is null
                    ? ActualPaymentStatus.Unpaid
                    : ActualPaymentStatus.Paid,
                line.PlannedAmount.GetValueOrDefault(),
                line.PlannedAmount is null ? null : line.PlannedDate))
            .ToArray(),
        30_000m,
        context.Revision?.PlannedDeficitInterest ??
        context.OriginalPlan.PlannedDeficitInterest,
        [],
        [],
        null);

    private static async Task WithSeededStore(
        Func<SqliteCoinFlowStore, CoinFlowService, Task> test)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"coinflow-loan-closure-{Guid.NewGuid():N}.db3");
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
