namespace CoinFlow.Tests;

public sealed class StatementDateUxSourceTests
{
    [Fact]
    public void StatementFlow_UsesDatePickersAndNoFreeTextNextDates()
    {
        var root = RepositoryRoot();
        var pages = new[]
        {
            "CardControlPage.xaml",
            "CommitmentsPage.xaml",
            "OnboardingPage.xaml"
        }.Select(name => File.ReadAllText(Path.Combine(
            root,
            "src",
            "CoinFlow.App",
            "Pages",
            name)));

        foreach (var page in pages)
        {
            Assert.Contains("<DatePicker", page);
            Assert.DoesNotContain(
                "<Entry Text=\"{Binding CardNextStatementDate}",
                page);
            Assert.DoesNotContain(
                "<Entry Text=\"{Binding CardNextDueDate}",
                page);
            Assert.DoesNotContain(
                "<Entry Text=\"{Binding StatementDraftNextStatementDate}",
                page);
            Assert.DoesNotContain(
                "<Entry Text=\"{Binding StatementDraftNextDueDate}",
                page);
        }
    }

    [Fact]
    public void AppWideDatePicker_UsesTurkishNumericFormat()
    {
        var styles = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "CoinFlow.App",
            "Resources",
            "Styles",
            "Styles.xaml"));

        Assert.Contains(
            "<Setter Property=\"Format\" Value=\"dd.MM.yyyy\" />",
            styles);
    }

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
