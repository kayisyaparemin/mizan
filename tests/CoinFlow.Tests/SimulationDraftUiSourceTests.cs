namespace CoinFlow.Tests;

/// <summary>
/// Geçici planların ekran tarafındaki kararları. Test projesi
/// <c>CoinFlow.App</c>'e referans vermediği için bunlar
/// <see cref="SimulatorConditionToggleSourceTests"/> ile aynı desende,
/// kaynak üzerinden sabitlenir.
/// </summary>
public sealed class SimulationDraftUiSourceTests
{
    [Fact]
    public void Simulator_OffersToSaveAndReloadDrafts()
    {
        var page = PageSource();

        Assert.Contains("Geçici Plan Oluştur", page);
        Assert.Contains("SaveDraftCommand", page);
        Assert.Contains("Geçici Planların", page);
        Assert.Contains(
            "BindableLayout.ItemsSource=\"{Binding SavedDrafts}\"",
            page);
        Assert.Contains("LoadDraftCommand", page);
        Assert.Contains("DeleteDraftCommand", page);
    }

    /// <summary>
    /// Bu projede daha önce bir tarih İngilizce ay adıyla çıktı ve yalnız
    /// ekranda görüldü. Kültür açıkça verilmeli.
    /// </summary>
    [Fact]
    public void SavedDraftDate_IsFormattedWithTheTurkishCulture()
    {
        var viewModel = ViewModelSource();

        Assert.Contains(
            "ToString(\"dd MMMM yyyy\", TurkishCulture)",
            viewModel);
    }

    /// <summary>
    /// Yükleme ekrandakinin yerine geçer. Eklemek olsaydı iki planın
    /// koşulları karışır, "bu plan neydi" sorusu cevapsız kalırdı.
    /// </summary>
    [Fact]
    public void LoadingADraft_ReplacesTheDraftOnScreen()
    {
        var viewModel = ViewModelSource();
        var load = viewModel.IndexOf(
            "private async Task LoadDraftAsync(",
            StringComparison.Ordinal);
        Assert.True(load > 0, "LoadDraftAsync bulunamadı.");
        var clear = viewModel.IndexOf(
            "DraftConditions.Clear();",
            load,
            StringComparison.Ordinal);
        var add = viewModel.IndexOf(
            "DraftConditions.Add(CreateConditionView(",
            load,
            StringComparison.Ordinal);
        Assert.True(
            clear > 0 && clear < add,
            "Yükleme önce listeyi temizlemeli, sonra doldurmalı.");
    }

    /// <summary>
    /// Kapalı koşul kapalı kaydedilir ve kapalı geri gelir; kullanıcı onu
    /// bilerek kapatmıştı.
    /// </summary>
    [Fact]
    public void SavedDraft_CarriesTheEnabledStateBothWays()
    {
        var viewModel = ViewModelSource();

        Assert.Contains(
            "new SimulationDraftCondition(\n                        x.Request,\n                        x.IsEnabled)",
            viewModel.Replace("\r\n", "\n"));
        Assert.Contains(
            "CreateConditionView(\n                    condition.Request,\n                    condition.IsEnabled)",
            viewModel.Replace("\r\n", "\n"));
    }

    private static string PageSource() => File.ReadAllText(
        Path.Combine(
            RepositoryRoot(),
            "src", "CoinFlow.App", "Pages", "SimulationPage.xaml"));

    private static string ViewModelSource() => File.ReadAllText(
        Path.Combine(
            RepositoryRoot(),
            "src", "CoinFlow.App", "ViewModels", "SimulationViewModel.cs"));

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
