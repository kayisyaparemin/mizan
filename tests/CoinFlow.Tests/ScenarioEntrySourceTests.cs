using System.Text.RegularExpressions;

namespace CoinFlow.Tests;

/// <summary>
/// Kodda duran ama ekranda açılamayan form yeniden oluşmasın: `89d7f90`'da
/// Finansal Yapı'nın kayıt türü seçicisi kalkınca tek seferlik gelir, taksit
/// ve geçici plan formları sessizce erişilemez kaldı. App projesi Android'e
/// bağlı olduğu için bu sözleşme kaynaktan okunur.
/// </summary>
public sealed class ScenarioEntrySourceTests
{
    [Fact]
    public void Simulator_UsesTheSharedFormInsteadOfOneLongPicker()
    {
        var page = Read("Pages", "SimulationPage.xaml");
        var viewModel = Read("ViewModels", "SimulationViewModel.cs");

        Assert.Contains("<controls:ScenarioConditionFormView", page);
        Assert.DoesNotContain("ScenarioTypes", page);
        Assert.DoesNotContain("ScenarioTypes", viewModel);
    }

    [Fact]
    public void FinancialStructure_HostsTheSharedFormAndOffersEveryGroup()
    {
        var page = Read("Pages", "CommitmentsPage.xaml");
        var codeBehind = Read("Pages", "CommitmentsPage.xaml.cs");

        Assert.Contains("<controls:ScenarioConditionFormView", page);
        Assert.Contains("BindingContext=\"{Binding EntryForm}\"", page);
        foreach (var group in new[] { "Spending", "Debt", "Income" })
        {
            Assert.Contains($"StartScenarioEntry(ScenarioGroup.{group})", codeBehind);
        }
    }

    [Fact]
    public void FinancialStructure_EveryRecordFormIsReachableFromTheAddMenu()
    {
        var codeBehind = Read("Pages", "CommitmentsPage.xaml.cs");
        var viewModel = Read("ViewModels", "CommitmentsViewModel.cs");

        var offered = Regex.Matches(codeBehind, "StartAdd\\(\"([a-z]+)\"\\)")
            .Select(x => x.Groups[1].Value)
            .ToHashSet();
        var buildable = Regex.Matches(viewModel, "case \"([a-z]+)\":")
            .Select(x => x.Groups[1].Value)
            .ToHashSet();

        Assert.NotEmpty(buildable);
        Assert.Subset(offered, buildable);
    }

    private static string Read(string folder, string name) =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "CoinFlow.App",
            folder,
            name));

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
