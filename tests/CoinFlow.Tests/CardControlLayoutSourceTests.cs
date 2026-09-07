namespace CoinFlow.Tests;

/// <summary>
/// Kart sayfası 9 bölüm ve beş ayrı "asgari/tamamı" seçicisiyle kaos
/// hâlindeydi. Seçicilerin dördü farklı kavram — farkı yapan tek şey kapsam:
/// hangi ekstreleri etkiledikleri. Sayfa artık zaman eksenine göre okunuyor.
/// Bu testler o ayrımı ve nadiren değişen ayarların kapalı durmasını sabitler;
/// hiçbir özellik kaldırılmadı, yalnız sunum değişti.
/// </summary>
public sealed class CardControlLayoutSourceTests
{
    [Fact]
    public void Page_ReadsOnATimeAxis()
    {
        var page = PageSource();

        var now = page.IndexOf("Text=\"ŞU AN\"", StringComparison.Ordinal);
        var next = page.IndexOf("Text=\"SIRADAKİ\"", StringComparison.Ordinal);
        var general = page.IndexOf("Text=\"GENEL\"", StringComparison.Ordinal);

        Assert.True(now > 0, "ŞU AN başlığı yok.");
        Assert.True(next > now, "SIRADAKİ, ŞU AN'dan sonra gelmeli.");
        Assert.True(general > next, "GENEL en sonda olmalı.");
    }

    /// <summary>
    /// Kaosun büyük kısmı bu ikisinin sürekli ekranda olmasından geliyordu.
    /// İkisi de nadiren değişir; varsayılan kapalı bir bölümde duruyorlar.
    /// </summary>
    [Fact]
    public void GeneralSettings_SitBehindADisclosure()
    {
        var page = PageSource();
        var viewModel = ViewModelSource();

        Assert.Contains("{Binding AdvancedToggleText}", page);
        Assert.Contains("IsVisible=\"{Binding ShowAdvanced}\"", page);
        Assert.Contains("Kartın varsayılan ödeme şekli", page);
        Assert.Contains("Karar vermediğin ekstrelerde varsayım", page);
        // Varsayılan kapalı: alan initializer almıyor.
        Assert.Contains("private bool showAdvanced;", viewModel);
    }

    [Fact]
    public void ReferenceLists_AreCollapsedByDefault()
    {
        var page = PageSource();
        var viewModel = ViewModelSource();

        Assert.Contains("IsVisible=\"{Binding ShowPreferenceHistory}\"", page);
        Assert.Contains("IsVisible=\"{Binding ShowFutureCharges}\"", page);
        Assert.Contains("private bool showPreferenceHistory;", viewModel);
        Assert.Contains("private bool showFutureCharges;", viewModel);
    }

    /// <summary>
    /// Gelecek ekstreler planın kendisi; referans bilgi değil. Kapatılmaz.
    /// </summary>
    [Fact]
    public void UpcomingStatements_StayOpen()
    {
        var page = PageSource();

        Assert.Contains(
            "IsVisible=\"{Binding HasUpcomingStatements}\"",
            page);
        var section = page.IndexOf(
            "IsVisible=\"{Binding HasUpcomingStatements}\"",
            StringComparison.Ordinal);
        var advanced = page.IndexOf(
            "IsVisible=\"{Binding ShowAdvanced}\"",
            StringComparison.Ordinal);
        Assert.True(
            section < advanced,
            "Gelecek ekstreler gelişmiş bölümün içine düşmemeli.");
    }

    /// <summary>
    /// Sadeleştirme sunumdaydı, özellikte değil: dört karar da yerinde.
    /// </summary>
    [Fact]
    public void EveryPaymentDecision_IsStillReachable()
    {
        var page = PageSource();

        // Kesilmiş ekstrenin kararı
        Assert.Contains("SetCurrentStatementMinimumCommand", page);
        Assert.Contains("SetCurrentStatementFullCommand", page);
        Assert.Contains("ShowCurrentStatementCustomCommand", page);
        // Vadeye özel override
        Assert.Contains("OnUpcomingMinimumClicked", page);
        Assert.Contains("OnUpcomingFullClicked", page);
        Assert.Contains("OnUpcomingClearClicked", page);
        // Kartın genel şekli
        Assert.Contains("PayMinimumCommand", page);
        Assert.Contains("PayFullCommand", page);
        Assert.Contains("AskEachStatementCommand", page);
        // Hesaplama varsayımı
        Assert.Contains("FallbackMinimumCommand", page);
        Assert.Contains("FallbackFullCommand", page);
    }

    /// <summary>
    /// Üç satır da "Asgari" ile bitiyor; ayrımı önek yapıyor. Önek
    /// kaybolursa kullanıcı yine hangisinin ne olduğunu ayırt edemez.
    /// </summary>
    [Fact]
    public void UpcomingStatementLabels_SayWhereTheDecisionCameFrom()
    {
        var viewModel = ViewModelSource();

        Assert.Contains("Bu vade için: {PaymentTypeLabel", viewModel);
        Assert.Contains("Kartın varsayılanı: {PaymentTypeLabel", viewModel);
        Assert.Contains("Karar yok · varsayım: {PaymentTypeLabel", viewModel);
    }

    private static string PageSource() => File.ReadAllText(
        Path.Combine(
            RepositoryRoot(),
            "src", "CoinFlow.App", "Pages", "CardControlPage.xaml"));

    private static string ViewModelSource() => File.ReadAllText(
        Path.Combine(
            RepositoryRoot(),
            "src", "CoinFlow.App", "ViewModels", "CardControlViewModel.cs"));

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
