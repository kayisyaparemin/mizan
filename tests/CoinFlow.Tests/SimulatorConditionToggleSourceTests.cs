namespace CoinFlow.Tests;

/// <summary>
/// Koşul switch'lerinin kuralları App katmanında yaşıyor; test projesi
/// <c>CoinFlow.App</c>'e referans vermediği için bu kurallar
/// <see cref="StatementDateUxSourceTests"/> ile aynı desende, kaynak üzerinden
/// sabitlenir. Amaç kelime kelime kod dondurmak değil, sessizce geri
/// alınabilecek üç kararı korumak: filtreleme, uygula yolunun aynı listeyi
/// görmesi ve senaryosuz modda karşılaştırmanın gizlenmesi.
/// </summary>
public sealed class SimulatorConditionToggleSourceTests
{
    [Fact]
    public void Calculation_UsesOnlyEnabledConditions()
    {
        var viewModel = SimulationViewModelSource();

        // Filtresiz eski ifade geri gelirse kapalı koşul yeniden hesaba girer.
        Assert.DoesNotContain(
            "DraftConditions.Select(x => x.Request)",
            viewModel);
        Assert.Contains(
            "DraftConditions.Where(x => x.IsEnabled)",
            viewModel);
        Assert.Contains("var requests = EnabledRequests();", viewModel);
    }

    [Fact]
    public void ApplyPlan_SavesTheSameListTheChartWasBuiltFrom()
    {
        var viewModel = SimulationViewModelSource();

        // "Planı Uygula" _lastRequests'i kaydeder; o da EnabledRequests()'ten
        // gelir. Uygulanan plan ile ekranda gösterilen plan ayrışmamalı.
        Assert.Contains("_lastRequests = requests;", viewModel);
        Assert.Contains("_lastRequests,", viewModel);
    }

    [Fact]
    public void ApplyPlan_KeepsDisabledConditionsInTheDraft()
    {
        var viewModel = SimulationViewModelSource();

        // Kapalı koşulu kullanıcı bilerek dışarıda bıraktı; uygulama sonrası
        // tüm listeyi süpürmek onu uyarısız siler.
        Assert.Contains(
            "foreach (var applied in EnabledConditions())",
            viewModel);
        Assert.Contains("DraftConditions.Remove(applied);", viewModel);
    }

    [Fact]
    public void AllConditionsOff_FallsBackToTheBaselineProjection()
    {
        var viewModel = SimulationViewModelSource();

        // Motor koşulsuz simülasyonu reddediyor (SimulationTests
        // Validate_WithoutAnyCondition_IsRejected); ekran bu yüzden
        // SimulateAsync'i hiç çağırmadan baz projeksiyonu gösterir.
        Assert.Contains("PopulateBaselineOnlyAsync", viewModel);
        Assert.Contains("service.GetFuturePeriodsAsync(", viewModel);
        Assert.Contains("IsBaselineOnly = true;", viewModel);
    }

    [Fact]
    public void ConditionRow_HasASwitchBoundToTheEnabledFlag()
    {
        var page = SimulationPageSource();

        Assert.Contains(
            "<Switch IsToggled=\"{Binding IsEnabled, Mode=TwoWay}\"",
            page);
        // Kapalı koşul silinmiş gibi değil, sönük görünmeli.
        Assert.Contains("Opacity=\"{Binding RowOpacity}\"", page);
    }

    [Fact]
    public void ScenarioLessResults_HideTheComparisonSections()
    {
        var page = SimulationPageSource();

        // Senaryo yokken faiz karşılaştırması ve "Planı Uygula" anlamsız;
        // liste ise baz projeksiyonu göstermeye devam eder.
        Assert.Contains("IsVisible=\"{Binding HasScenarioResults}\"", page);
        Assert.Contains("IsVisible=\"{Binding IsBaselineOnly}\"", page);
        Assert.Contains("BindableLayout.ItemsSource=\"{Binding Results}\"", page);
    }

    [Fact]
    public void DraftCondition_IsObservableSoTheSwitchCanWriteBack()
    {
        var model = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "CoinFlow.App",
            "Models",
            "SimulationDraftConditionView.cs"));

        // record'a bool eklemek yetmiyordu: Switch iki yönlü bağlanabilmek
        // için INotifyPropertyChanged istiyor.
        Assert.DoesNotContain("public sealed record", model);
        Assert.Contains(": ObservableObject", model);
        Assert.Contains("private bool isEnabled = true;", model);
    }

    private static string SimulationViewModelSource() => File.ReadAllText(
        Path.Combine(
            RepositoryRoot(),
            "src",
            "CoinFlow.App",
            "ViewModels",
            "SimulationViewModel.cs"));

    private static string SimulationPageSource() => File.ReadAllText(
        Path.Combine(
            RepositoryRoot(),
            "src",
            "CoinFlow.App",
            "Pages",
            "SimulationPage.xaml"));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "CoinFlow.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
               throw new DirectoryNotFoundException(
                   "CoinFlow repository root was not found.");
    }
}
