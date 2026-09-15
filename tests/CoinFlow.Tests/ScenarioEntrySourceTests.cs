using System.Text.RegularExpressions;

namespace CoinFlow.Tests;

/// <summary>
/// Kodda duran ama ekranda açılamayan form yeniden oluşmasın: `89d7f90`'da
/// Finansal Yapı'nın kayıt türü seçicisi kalkınca tek seferlik gelir, taksit
/// ve geçici plan formları sessizce erişilemez kaldı. v1.16.0'dan beri
/// Finansal Yapı'nın ekleme alanı simülatörle aynı tür seçicisini kullanır.
/// App projesi Android'e bağlı olduğu için bu sözleşme kaynaktan okunur.
/// </summary>
public sealed class ScenarioEntrySourceTests
{
    [Fact]
    public void Simulator_UsesTheSharedFormAndTheSharedTypePicker()
    {
        var page = Read("Pages", "SimulationPage.xaml");
        var viewModel = Read("ViewModels", "SimulationViewModel.cs");
        var form = Read("Controls", "ScenarioConditionFormView.xaml");

        Assert.Contains("<controls:ScenarioConditionFormView", page);
        Assert.DoesNotContain("ScenarioTypes", page);
        Assert.DoesNotContain("ScenarioTypes", viewModel);
        Assert.Contains("<controls:EntryTypePickerView />", form);
        Assert.Contains("IsVisible=\"{Binding ShowsTypePicker}\"", form);
    }

    [Fact]
    public void FinancialStructure_UsesTheSameTypePickerInsteadOfAMenu()
    {
        var page = Read("Pages", "CommitmentsPage.xaml");
        var codeBehind = Read("Pages", "CommitmentsPage.xaml.cs");
        var picker = Read("Controls", "EntryTypePickerView.xaml");

        Assert.Contains("<controls:EntryTypePickerView BindingContext=\"{Binding EntryPicker}\" />", page);
        Assert.Contains("<controls:ScenarioConditionFormView", page);
        Assert.Contains("BindingContext=\"{Binding EntryForm}\"", page);
        // Görünürlük BindingContext verilen elemanda değil, dıştaki
        // ContentView'da: yoksa IsVisible EntryPicker'a bakardı.
        Assert.Matches(
            @"<ContentView IsVisible=""\{Binding ShowEntryPicker\}"">\s*<VerticalStackLayout[^>]*>\s*<Label[^>]*/>\s*<controls:EntryTypePickerView",
            page);
        Assert.DoesNotContain("DisplayActionSheet", codeBehind);
        Assert.Contains("_viewModel.StartEntry();", codeBehind);
        Assert.Contains("BindableLayout.ItemsSource=\"{Binding Groups}\"", picker);
        Assert.Contains("BindableLayout.ItemsSource=\"{Binding VisibleOptions}\"", picker);
    }

    [Fact]
    public void FinancialStructure_EveryRecordFormOfTheCatalogHasABuilder()
    {
        var viewModel = Read("ViewModels", "CommitmentsViewModel.cs");

        var mapped = Regex.Matches(viewModel, @"RecordEntryForm\.(\w+) => ""([a-z]+)""")
            .ToDictionary(x => x.Groups[1].Value, x => x.Groups[2].Value);
        var buildable = Regex.Matches(viewModel, "case \"([a-z]+)\":")
            .Select(x => x.Groups[1].Value)
            .ToHashSet();

        Assert.Equal(
            new[] { "CreditCard", "Loan", "PaymentPlan", "Salary" },
            mapped.Keys.Order());
        Assert.Equal(buildable.Order(), mapped.Values.Order());
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
