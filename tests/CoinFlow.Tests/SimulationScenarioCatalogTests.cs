using CoinFlow.Application.Models;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Tests;

/// <summary>
/// Plan türü kataloğu: simülatörde 13 türlük tek listenin yerine geçen
/// gruplar. Enum değişmediği için kayıtlı geçici planlar etkilenmez; bu testler
/// hiçbir türün kataloğun dışında kalmadığını ve gruplamanın motor türünü
/// doğru çözdüğünü sabitler.
/// </summary>
public sealed class SimulationScenarioCatalogTests
{
    [Fact]
    public void EveryScenarioType_BelongsToExactlyOneOption()
    {
        foreach (var type in Enum.GetValues<SimulationScenarioType>())
        {
            Assert.Single(
                SimulationScenarioCatalog.Options,
                option => option.Types.Contains(type));
            Assert.Same(
                SimulationScenarioCatalog.Options.Single(x => x.Types.Contains(type)),
                SimulationScenarioCatalog.For(type));
        }
    }

    [Fact]
    public void NoGroup_HasMoreThanThreeOptions()
    {
        foreach (var (group, _) in SimulationScenarioCatalog.Groups)
        {
            var count = SimulationScenarioCatalog.OptionsIn(group).Count;
            Assert.InRange(count, 1, SimulationScenarioCatalog.MaxOptionsPerGroup);
        }

        Assert.Equal(
            SimulationScenarioCatalog.Options.Count,
            SimulationScenarioCatalog.Groups.Sum(x =>
                SimulationScenarioCatalog.OptionsIn(x.Group).Count));
    }

    [Fact]
    public void OnlyTypesWithoutTheirOwnScreen_AreEnteredThroughTheSharedForm()
    {
        var elsewhere = SimulationScenarioCatalog.Options
            .Where(x => x.EntryHome != ScenarioEntryHome.SharedForm)
            .SelectMany(x => x.Types)
            .ToHashSet();

        Assert.Equal(
            new HashSet<SimulationScenarioType>
            {
                SimulationScenarioType.SalaryChange,
                SimulationScenarioType.CreditCardPaymentMode,
                SimulationScenarioType.PaymentStrategyChange
            },
            elsewhere);
        Assert.DoesNotContain(
            SimulationScenarioCatalog.OptionsIn(ScenarioGroup.Setting, directEntryOnly: true),
            _ => true);
        Assert.Equal(
            [SimulationScenarioCatalog.OneTimeIncome],
            SimulationScenarioCatalog.OptionsIn(ScenarioGroup.Income, directEntryOnly: true));
    }

    [Theory]
    [InlineData(1, SimulationScenarioType.CreditCardSinglePayment)]
    [InlineData(0, SimulationScenarioType.CreditCardSinglePayment)]
    [InlineData(2, SimulationScenarioType.CreditCardInstallmentPurchase)]
    [InlineData(9, SimulationScenarioType.CreditCardInstallmentPurchase)]
    public void CardSpending_ResolvesByInstallmentCount(
        int count,
        SimulationScenarioType expected) =>
        Assert.Equal(
            expected,
            SimulationScenarioCatalog.Resolve(
                SimulationScenarioCatalog.CardSpending,
                count,
                null));

    [Theory]
    [InlineData(LoanPrepaymentMode.FullClosure, SimulationScenarioType.LoanEarlyClosure)]
    [InlineData(LoanPrepaymentMode.ReduceTerm, SimulationScenarioType.LoanPartialPrepayment)]
    [InlineData(LoanPrepaymentMode.ReduceInstallment, SimulationScenarioType.LoanPartialPrepayment)]
    public void LoanPrepayment_ResolvesByMode(
        LoanPrepaymentMode mode,
        SimulationScenarioType expected) =>
        Assert.Equal(
            expected,
            SimulationScenarioCatalog.Resolve(
                SimulationScenarioCatalog.LoanPrepayment,
                1,
                mode));

    [Fact]
    public void CashPayment_WritesAPlannedExpense_ButAnOldOneTimePaymentKeepsItsType()
    {
        Assert.Equal(
            SimulationScenarioType.CashPurchase,
            SimulationScenarioCatalog.Resolve(
                SimulationScenarioCatalog.CashPayment,
                1,
                null));
        Assert.Equal(
            SimulationScenarioType.CashPurchase,
            SimulationScenarioCatalog.Resolve(
                SimulationScenarioCatalog.CashPayment,
                1,
                null,
                editingType: SimulationScenarioType.CashPurchase));

        // Uygulanmış eski bir koşulun kimliği ödeme planında duruyor; türü
        // değişirse uygulama onu tanımaz ve ikinci kez kaydeder.
        Assert.Equal(
            SimulationScenarioType.FutureOneTimePayment,
            SimulationScenarioCatalog.Resolve(
                SimulationScenarioCatalog.CashPayment,
                1,
                null,
                editingType: SimulationScenarioType.FutureOneTimePayment));
    }

    [Fact]
    public void EveryType_HasItsOwnLabel()
    {
        var labels = Enum.GetValues<SimulationScenarioType>()
            .Select(SimulationScenarioCatalog.TypeText)
            .ToArray();

        Assert.DoesNotContain("Koşul", labels);
        Assert.Equal(labels.Length, labels.Distinct().Count());
    }
}
