namespace CoinFlow.Tests;

/// <summary>
/// Yaşam gideri slider'ının App katmanındaki kuralları. Motor tarafı
/// <c>SimulationTests.LivingBudgetOverride_*</c> ile davranışsal olarak
/// kapsanıyor; burada yalnızca test projesinin ulaşamadığı ekran kararları
/// sabitleniyor (test projesi <c>CoinFlow.App</c>'e referans vermiyor).
/// </summary>
public sealed class SimulatorLivingBudgetSourceTests
{
    [Fact]
    public void BothCalculationPaths_CarryTheOverride()
    {
        var viewModel = SimulationViewModelSource();

        // Senaryolu ve senaryosuz yollar aynı yaşam giderini kullanmazsa
        // son switch'i kapatmak eğriyi sebepsiz zıplatır.
        Assert.Contains(
            "monthlyLivingBudgetOverride: livingBudget",
            viewModel);
        Assert.Equal(
            2,
            viewModel.Split("monthlyLivingBudgetOverride: livingBudget").Length - 1);
    }

    [Fact]
    public void Slider_IsNotACondition_ButALiveControlUnderTheChart()
    {
        var page = SimulationPageSource();

        Assert.Contains(
            "Value=\"{Binding LivingBudget, Mode=TwoWay}\"",
            page);
        Assert.Contains(
            "Maximum=\"{Binding LivingBudgetMaximum}\"",
            page);
        // Grafiğin altında: oynatınca eğrinin ve başlığın kayması görülebilmeli.
        Assert.True(
            page.IndexOf("<Slider", StringComparison.Ordinal) >
            page.IndexOf("x:Name=\"CashChartView\"", StringComparison.Ordinal),
            "Slider grafiğin altında durmalı.");
    }

    [Fact]
    public void Slider_OffersAWayBackToTheSavedValue()
    {
        var page = SimulationPageSource();
        var viewModel = SimulationViewModelSource();

        // Kalıcı olmayan bir kadranda geri dönüş yolu yoksa kullanıcı
        // denemekten çekinir.
        Assert.Contains("ResetLivingBudgetCommand", page);
        Assert.Contains("IsVisible=\"{Binding IsLivingBudgetChanged}\"", page);
        Assert.Contains("private void ResetLivingBudget()", viewModel);
    }

    [Fact]
    public void ApplyConfirmation_DisclosesAnOverriddenLivingBudget()
    {
        var viewModel = SimulationViewModelSource();

        // Slider kaydedilmiyor. Kaydırılmış bir zeminde görülen sonucu
        // uygulayan kişi gerçekte başka bir eğri elde eder; onay bunu demeli.
        Assert.Contains("LivingBudgetApplyNote()", viewModel);
        Assert.Contains(
            "BuildApplyConfirmation(requests) + LivingBudgetApplyNote()",
            viewModel);
        Assert.Contains("Slider kaydedilmez", viewModel);
    }

    [Fact]
    public void ReturningToThePage_RestoresTheSavedLivingBudget()
    {
        var viewModel = SimulationViewModelSource();

        // Slider kalıcı değil; sayfaya dönünce kayıtlı değeri göstermeli,
        // yoksa ekran olmayan bir ayarı varmış gibi sunar.
        Assert.Contains(
            "SetLivingBudgetFromPlan(plan.Settings.MonthlyLivingBudget);",
            viewModel);
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
