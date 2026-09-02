namespace CoinFlow.Tests;

public sealed class StatementImportArchitectureSourceTests
{
    [Fact]
    public void PdfExtractor_ReadsOnlyHeaderPage()
    {
        var source = ReadSource(
            "src",
            "CoinFlow.Infrastructure",
            "Imports",
            "PdfPigPdfTextExtractor.cs");

        Assert.Contains("document.GetPage(1).Text", source);
        Assert.DoesNotContain("document.GetPages()", source);
        Assert.DoesNotContain("MaximumPageCount", source);
    }

    [Fact]
    public void ImportPath_HasNoSynchronousAsyncBridges()
    {
        var files = new[]
        {
            ReadSource("src", "CoinFlow.Application", "Services",
                "CreditCardStatementImportWorkflow.cs"),
            ReadSource("src", "CoinFlow.Infrastructure", "Imports",
                "CreditCardStatementImporter.cs"),
            ReadSource("src", "CoinFlow.App", "Services",
                "CreditCardStatementImportUiServices.cs")
        };

        foreach (var source in files)
        {
            Assert.DoesNotContain(".Result", source);
            Assert.DoesNotContain(".Wait()", source);
            Assert.DoesNotContain("GetAwaiter().GetResult()", source);
        }
    }

    [Fact]
    public void ImportUx_ExposesCancellationOnEveryEntryScreen()
    {
        var pages = new[]
        {
            "CardControlPage.xaml",
            "CommitmentsPage.xaml",
            "OnboardingPage.xaml"
        };

        foreach (var page in pages)
        {
            var source = ReadSource(
                "src",
                "CoinFlow.App",
                "Pages",
                page);
            Assert.Contains("CancelStatementImportCommand", source);
        }
    }

    private static string ReadSource(params string[] path) =>
        File.ReadAllText(Path.Combine([RepositoryRoot(), .. path]));

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
