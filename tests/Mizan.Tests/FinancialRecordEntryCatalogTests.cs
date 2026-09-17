using Mizan.Application.Models;

namespace Mizan.Tests;

/// <summary>
/// Finansal Yapı'nın ekleme alanı simülatördeki plan türü seçimiyle aynı
/// tasarımda: grup çipleri, grup başına en fazla üç açıklamalı kart. Bu testler
/// hiçbir kayıt türünün seçicinin dışında kalmadığını sabitler.
/// </summary>
public sealed class FinancialRecordEntryCatalogTests
{
    [Fact]
    public void EveryGroup_HasOneToThreeOptions_AndEveryOptionHasAGroup()
    {
        foreach (var (group, label) in FinancialRecordEntryCatalog.Groups)
        {
            Assert.False(string.IsNullOrWhiteSpace(label));
            Assert.InRange(
                FinancialRecordEntryCatalog.OptionsIn(group).Count,
                1,
                FinancialRecordEntryCatalog.MaxOptionsPerGroup);
        }

        Assert.Equal(
            FinancialRecordEntryCatalog.Options.Count,
            FinancialRecordEntryCatalog.Groups.Sum(x =>
                FinancialRecordEntryCatalog.OptionsIn(x.Group).Count));
        Assert.Equal(
            FinancialRecordEntryCatalog.Options.Count,
            FinancialRecordEntryCatalog.Options.Select(x => x.Key).Distinct().Count());
    }

    [Fact]
    public void EveryDirectEntryScenario_IsOfferedOnce_WithTheSimulatorOption()
    {
        var direct = SimulationScenarioCatalog.Options
            .Where(x => x.EntryHome == ScenarioEntryHome.SharedForm)
            .ToArray();

        foreach (var scenario in direct)
        {
            var option = Assert.Single(
                FinancialRecordEntryCatalog.Options,
                x => x.Scenario == scenario);
            Assert.Equal(RecordEntryForm.SharedForm, option.Form);
            Assert.Equal(scenario.Title, option.Title);
            Assert.Equal(scenario.Summary, option.Summary);
            Assert.Equal(scenario.Group.ToString(), option.Group.ToString());
        }

        Assert.All(
            FinancialRecordEntryCatalog.Options.Where(x => x.Form == RecordEntryForm.SharedForm),
            x => Assert.Contains(x.Scenario, direct));
    }

    [Fact]
    public void EveryRecordForm_IsReachable()
    {
        foreach (var form in Enum.GetValues<RecordEntryForm>())
        {
            Assert.Contains(FinancialRecordEntryCatalog.Options, x => x.Form == form);
        }

        Assert.All(
            FinancialRecordEntryCatalog.Options.Where(x => x.Form != RecordEntryForm.SharedForm),
            x => Assert.Null(x.Scenario));
    }

    [Fact]
    public void Groups_FollowTheSimulatorOrder_AndDefaultIsCashPayment()
    {
        Assert.Equal(
            new[] { "Harcama", "Borç / Kredi", "Gelir", "Hesap" },
            FinancialRecordEntryCatalog.Groups.Select(x => x.Label));
        Assert.Equal(
            new[] { "Tek seferlik gelir", "Maaş / gelir değişikliği" },
            FinancialRecordEntryCatalog.OptionsIn(RecordEntryGroup.Income).Select(x => x.Title));
        Assert.Equal(
            new[] { RecordEntryForm.CreditCard, RecordEntryForm.Loan, RecordEntryForm.PaymentPlan },
            FinancialRecordEntryCatalog.OptionsIn(RecordEntryGroup.Account).Select(x => x.Form));
        Assert.Same(
            SimulationScenarioCatalog.CashPayment,
            FinancialRecordEntryCatalog.Default.Scenario);
        Assert.Same(
            FinancialRecordEntryCatalog.CreditCard,
            FinancialRecordEntryCatalog.For("credit-card"));
    }
}
