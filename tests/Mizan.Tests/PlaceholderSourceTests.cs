using System.Text.RegularExpressions;

namespace Mizan.Tests;

/// <summary>
/// Metin kutuları kendiliğinden dolmaz; örnek ve varsayılan değer
/// placeholder'dadır. Önceden "Beyaz eşya · 120000 · 9 taksit" gibi örnekler
/// ya da "10 / 12 / 25 / 40" gibi günler dolu geliyordu ve fark edilmeden
/// kayıt olarak kaydedilebiliyordu. App projesi Android'e bağlı olduğu için
/// sözleşme kaynaktan okunur.
/// </summary>
public sealed class PlaceholderSourceTests
{
    private static readonly string[] Pages =
    [
        "CardControlPage.xaml",
        "CommitmentsPage.xaml",
        "FutureMonthsPage.xaml",
        "MainPage.xaml",
        "OnboardingPage.xaml",
        "PeriodReviewPage.xaml",
        "SettingsPage.xaml",
        "SimulationPage.xaml",
        "StrategyChangePage.xaml"
    ];

    [Fact]
    public void EveryEntry_HasAPlaceholder()
    {
        var files = Pages
            .Select(page => (page, Read("Pages", page)))
            .Append(("ScenarioConditionFormView.xaml", Read("Controls", "ScenarioConditionFormView.xaml")));

        foreach (var (name, xaml) in files)
        {
            foreach (Match entry in Regex.Matches(xaml, "<Entry\\b[^>]*>"))
            {
                Assert.True(
                    entry.Value.Contains("Placeholder=", StringComparison.Ordinal),
                    $"{name}: {entry.Value}");
            }
        }
    }

    [Theory]
    [InlineData("ViewModels", "ScenarioConditionForm.cs", "Controls", "ScenarioConditionFormView.xaml")]
    [InlineData("ViewModels", "CommitmentsViewModel.cs", "Pages", "CommitmentsPage.xaml")]
    [InlineData("ViewModels", "OnboardingViewModel.cs", "Pages", "OnboardingPage.xaml")]
    [InlineData("ViewModels", "PeriodReviewWizardViewModel.cs", "Pages", "PeriodReviewPage.xaml")]
    public void EntryBoundFields_StartEmpty(
        string viewModelFolder,
        string viewModel,
        string pageFolder,
        string page)
    {
        var source = Read(viewModelFolder, viewModel);
        var bound = Regex.Matches(
                Read(pageFolder, page),
                "<Entry\\b[^>]*Text=\"\\{Binding (\\w+)\\}\"")
            .Select(x => char.ToLowerInvariant(x.Groups[1].Value[0]) + x.Groups[1].Value[1..])
            .ToHashSet();
        Assert.NotEmpty(bound);

        foreach (Match field in Regex.Matches(
                     source,
                     "\\[ObservableProperty\\] private string (\\w+) = \"([^\"]*)\";"))
        {
            Assert.False(
                bound.Contains(field.Groups[1].Value) && field.Groups[2].Value.Length > 0,
                $"{viewModel}: {field.Groups[1].Value} = \"{field.Groups[2].Value}\"");
        }
    }

    [Fact]
    public void ScenarioForm_NeitherPrefillsSamplesNorWritesTheLoanNameIntoTheField()
    {
        var form = Read("ViewModels", "ScenarioConditionForm.cs");

        Assert.DoesNotContain("\"Beyaz eşya\"", form);
        Assert.DoesNotContain("\"Yeni koşul\"", form);
        Assert.DoesNotContain("PaymentCount = \"1\"", form);
        // Kredinin adı placeholder'da görünür, boş ad o adla kaydedilir.
        Assert.DoesNotContain("Name = IsPartialPrepayment", form);
        Assert.Contains("string.IsNullOrWhiteSpace(Name) ? NameSuggestion", form);
    }

    [Fact]
    public void StatementImport_DoesNotGuessTheCardName()
    {
        foreach (var viewModel in new[] { "CommitmentsViewModel.cs", "OnboardingViewModel.cs" })
        {
            var source = Read("ViewModels", viewModel);
            Assert.DoesNotContain("\"Axess\"", source);
            Assert.DoesNotContain("\"Bonus\"", source);
        }
    }

    [Fact]
    public void ReviewWizard_EmptyFieldsMeanPlannedValues()
    {
        var wizard = Read("ViewModels", "PeriodReviewWizardViewModel.cs");

        Assert.Contains("item.PlannedAmountValue ??", wizard);
        Assert.Contains("? _plannedLiving", wizard);
        Assert.Contains("? _plannedInterest", wizard);
        Assert.Contains("!string.IsNullOrWhiteSpace(CurrentStartingSavings)", wizard);
        Assert.DoesNotContain("ActualAmount = line.PlannedAmount", wizard);
    }

    [Fact]
    public void PromptDialog_UsesPlaceholderInsteadOfInitialValue()
    {
        var service = Read("Services", "UserFeedbackService.cs");

        Assert.Contains("placeholder: placeholder", service);
        Assert.DoesNotContain("initialValue", service);
    }

    private static string Read(string folder, string name) =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "Mizan.App",
            folder,
            name));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "Mizan.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
               throw new DirectoryNotFoundException(
                   "Mizan repository root was not found.");
    }
}
