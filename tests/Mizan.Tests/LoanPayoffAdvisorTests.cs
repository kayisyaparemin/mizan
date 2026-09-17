using Mizan.Application.Services;
using Mizan.Domain.Calculations;
using Mizan.Domain.Models;

namespace Mizan.Tests;

/// <summary>
/// "Şu ayda kapatabilirsin" önerisi. Kural (kullanıcı kararı): önerilen ayda
/// hiçbir dönemde açık baz çizgiden büyük değil ve toplamda kazanç var —
/// ve daha erken hiçbir taksit günü bu iki koşulu sağlamıyor.
/// </summary>
public sealed class LoanPayoffAdvisorTests
{
    private static readonly DateOnly AsOf = new(2026, 8, 20);

    private static readonly FinancialProjectionCalculator Projection =
        TestFactory.ProjectionCalculator();

    private static readonly LoanPayoffAdvisor Advisor =
        TestFactory.LoanPayoffAdvisor(Projection);

    private static FinancialPlan PlanWithPrincipals(
        decimal startingSavings = 0m,
        decimal deficitRate = 0.05m)
    {
        var plan = TestFactory.CanonicalPlan();
        return plan with
        {
            Settings = plan.Settings with
            {
                ProjectionOpeningBalance = startingSavings,
                DeficitFinancingInterestRate = deficitRate
            },
            Loans = plan.Loans
                .Select(loan => loan with
                {
                    RemainingDebt = loan.Bank == "Garanti BBVA"
                        ? 190_188m
                        : 55_777m
                })
                .ToArray()
        };
    }

    /// <summary>
    /// Önerilen gün kuralı sağlıyor, bir önceki taksit günü sağlamıyor.
    /// Kanonik plan önce açık veriyor, sonra toparlanıyor; öneri o yüzden
    /// ilk aylarda olamaz.
    /// </summary>
    [Fact]
    public void RecommendedDate_IsTheEarliestThatPassesBothRules()
    {
        var plan = PlanWithPrincipals();

        var advice = Advisor.Advise(plan, AsOf);

        var recommended = advice
            .Where(x => x.Status == LoanPayoffAdviceStatus.Recommended)
            .ToArray();
        Assert.NotEmpty(recommended);
        foreach (var item in recommended)
        {
            var loan = plan.Loans.Single(x => x.Id == item.LoanId);
            Assert.True(Passes(plan, loan, item.Date!.Value),
                $"{item.LoanName} {item.Date} kuralı sağlamıyor.");
            var previous = CalendarRules.AddMonthsKeepingDay(
                item.Date.Value,
                -1,
                loan.PaymentDay);
            if (previous >= loan.NextPaymentDate && previous >= AsOf)
            {
                Assert.False(Passes(plan, loan, previous),
                    $"{item.LoanName} için {previous} daha erken uygun.");
            }

            Assert.True(item.PayoffAmount > 0m);
            Assert.True(item.InterestSaving > 0m);
            Assert.True(item.NetGain > 0m);
        }
    }

    /// <summary>
    /// Bol nakitle hiçbir dönem açık vermez; kapatmak her zaman kazançlıdır.
    /// Öneri ilk taksit günü olmalı.
    /// </summary>
    [Fact]
    public void WithAmpleCash_TheFirstInstallmentDayIsRecommended()
    {
        var plan = PlanWithPrincipals(startingSavings: 5_000_000m);

        var advice = Advisor.Advise(plan, AsOf);

        var burgan = Assert.Single(advice, x => x.LoanName.Contains("Burgan"));
        Assert.Equal(LoanPayoffAdviceStatus.Recommended, burgan.Status);
        Assert.Equal(new DateOnly(2026, 9, 18), burgan.Date);
    }

    /// <summary>
    /// Dipsiz açıkta, KMH faizi %5 iken krediyi kapatmak için gereken para
    /// hep açıktan gelir: ya açığı büyütür ya da kazandırmaz. Öneri olmamalı.
    /// </summary>
    [Fact]
    public void InADeepDeficit_NoMonthIsRecommended()
    {
        var plan = PlanWithPrincipals(startingSavings: -2_000_000m);

        var advice = Advisor.Advise(plan, AsOf);

        Assert.Equal(2, advice.Count);
        Assert.All(advice, x =>
            Assert.NotEqual(LoanPayoffAdviceStatus.Recommended, x.Status));
    }

    [Fact]
    public void LoanWithoutPrincipal_AsksForIt()
    {
        var plan = TestFactory.CanonicalPlan();

        var advice = Advisor.Advise(plan, AsOf);

        Assert.Equal(2, advice.Count);
        Assert.All(advice, x =>
            Assert.Equal(LoanPayoffAdviceStatus.NeedsPrincipal, x.Status));
    }

    [Fact]
    public void LoanAlreadyBeingClosed_IsNotAdvisedAgain()
    {
        var plan = PlanWithPrincipals(startingSavings: 5_000_000m);
        var burgan = plan.Loans.Single(x => x.Bank == "Burgan Bank");
        plan = plan with
        {
            LoanPrepayments =
            [
                new LoanPrepayment
                {
                    LoanId = burgan.Id,
                    Date = new DateOnly(2027, 1, 18),
                    Mode = LoanPrepaymentMode.FullClosure
                }
            ]
        };

        var advice = Advisor.Advise(plan, AsOf);

        var item = Assert.Single(advice, x => x.LoanId == burgan.Id);
        Assert.Equal(LoanPayoffAdviceStatus.AlreadyClosing, item.Status);
        Assert.Equal(new DateOnly(2027, 1, 18), item.Date);
    }

    /// <summary>
    /// Kuralın bağımsız uygulaması: advisor'ın kodunu değil, tanımı ölçer.
    /// </summary>
    private static bool Passes(FinancialPlan plan, Loan loan, DateOnly date)
    {
        var simulation = new SimulationCalculator(
            Projection,
            new InstallmentScheduleCalculator(),
            TestFactory.LoanScheduleBuilder());
        var request = new SimulationRequest(
            SimulationScenarioType.LoanEarlyClosure,
            "Kapat",
            0m,
            date,
            ScenarioId: Guid.NewGuid(),
            LoanId: loan.Id);
        SimulationResult result;
        try
        {
            result = simulation.Calculate(plan, AsOf, request);
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        var noNewDeficit = result.Rows.All(row =>
            Math.Max(0m, -row.Scenario.EndingProjectedBalance) <=
            Math.Max(0m, -row.Baseline.EndingProjectedBalance) + 0.01m);
        var builder = TestFactory.LoanScheduleBuilder();
        var baselineAll = builder.Replay(loan, plan.LoanPrepayments).Total;
        var scenarioPlan = simulation.BuildScenarioPlan(plan, request);
        var scenarioAll = builder.Replay(loan, scenarioPlan.LoanPrepayments).Total;
        decimal InPeriods(IReadOnlyList<CashFlowPeriodProjection> periods) => periods
            .SelectMany(x => x.MandatoryItems)
            .Where(x => x.Type == ObligationType.Loan &&
                        (x.PaymentId == loan.Id || x.PaymentId == request.ScenarioId))
            .Sum(x => x.Amount);
        var beyond = (baselineAll - InPeriods(result.Baseline)) -
                     (scenarioAll - InPeriods(result.Scenario));
        var gain = result.Scenario[^1].EndingProjectedBalance -
                   result.Baseline[^1].EndingProjectedBalance + beyond;
        return noNewDeficit && gain > 0m;
    }
}
