namespace CoinFlow.Tests;

/// <summary>
/// UI/UX yamasının geri alınabilir kararları. Test projesi
/// <c>CoinFlow.App</c>'e referans vermediği için bu kurallar
/// <see cref="SimulatorConditionToggleSourceTests"/> ile aynı desende,
/// kaynak üzerinden sabitlenir. Amaç kodu dondurmak değil; kullanıcının
/// açıkça istediği üç şeyin sessizce geri gelmesini engellemek.
/// </summary>
public sealed class UiPatchSourceTests
{
    [Fact]
    public void Simulator_ShowsAListAgain_NotAChart()
    {
        var page = PageSource("SimulationPage.xaml");

        // Grafik ve yaşam gideri kadranı kaldırıldı; kullanıcı listeyi
        // istedi, "aşağı doğru uzayan tüm aylar".
        Assert.DoesNotContain("GraphicsView", page);
        Assert.DoesNotContain("CashChart", page);
        Assert.DoesNotContain("<Slider", page);
        Assert.Contains("12 Dönem Finansal Görünüm", page);
        Assert.Contains(
            "BindableLayout.ItemsSource=\"{Binding Results}\"",
            page);
    }

    /// <summary>
    /// Kullanıcının sevdiği tek şey koşul switch'leri; grafik giderken
    /// onların yanlışlıkla götürülmediğini burada da doğruluyoruz.
    /// </summary>
    [Fact]
    public void Simulator_KeepsTheConditionSwitches()
    {
        var page = PageSource("SimulationPage.xaml");

        Assert.Contains("<Switch", page);
        Assert.Contains("IsEnabled, Mode=TwoWay", page);
    }

    [Fact]
    public void TwelvePeriods_ScrollWithThePage_NotInsideABox()
    {
        var page = PageSource("FutureMonthsPage.xaml");

        // CollectionView kendi içinde kayıyordu; sayfanın tamamı tek yüzey
        // olarak kaysın diye ScrollView + BindableLayout'a geçildi.
        Assert.DoesNotContain("<CollectionView", page);
        Assert.Contains("<ScrollView>", page);
        Assert.Contains(
            "BindableLayout.ItemsSource=\"{Binding Periods}\"",
            page);
    }

    [Fact]
    public void MainPage_OpensWithTheCurrentBalance()
    {
        var page = PageSource("MainPage.xaml");
        var viewModel = ViewModelSource("DashboardViewModel.cs");

        Assert.Contains("Text=\"{Binding CurrentBalanceInput}\"", page);
        Assert.Contains("SaveCurrentBalanceCommand", page);
        // Çapayı ilerleten yol bu; SaveSettingsAsync'e dönerse tarih
        // güncellemesi sessizce ayrı bir koda bağlanır.
        Assert.Contains(
            "RefreshCurrentFinancialStateAsync(",
            viewModel);
        // Mevcut tutar durum özetinden önce gelmeli: önce bugünü gir.
        Assert.True(
            page.IndexOf("CurrentBalanceInput", StringComparison.Ordinal) <
            page.IndexOf("HeadlineAmount", StringComparison.Ordinal),
            "Mevcut tutar kutusu durum özetinin üstünde olmalı.");
    }

    [Fact]
    public void CurrentBalance_IsNoLongerOfferedInSettings()
    {
        var settings = PageSource("SettingsPage.xaml");

        // İki yerde düzenlenebilmesi hangisinin çapayı ilerlettiğini
        // belirsizleştiriyordu.
        Assert.DoesNotContain("Mevcut Tutar", settings);
        Assert.DoesNotContain("ProjectionStartingSavings", settings);
        // Yaşam gideri ve faiz oranları Ayarlar'da kalır.
        Assert.Contains("MonthlyLivingBudget", settings);
        Assert.Contains("CreditCardCarryInterestRate", settings);
    }

    [Fact]
    public void MainPage_SaysTheSameThingOnce()
    {
        var page = PageSource("MainPage.xaml");
        var viewModel = ViewModelSource("DashboardViewModel.cs");

        // Dönem sonu rakamı üç yerde birden duruyordu: uyarı kutusu, hero
        // ve "Bu dönem nasıl oluşuyor" tablosu. Yalnız hero kaldı.
        Assert.DoesNotContain("Text=\"{Binding EndingSavings}\"", page);
        Assert.DoesNotContain("\"Bu dönem açık veriyor\"", viewModel);
        Assert.Contains("OpenCurrentPeriodDetailCommand", page);
        // Aksiyon bekleyen uyarılar duruyor; kalkan yalnız tekrar edeni.
        Assert.Contains("\"Kart ödeme tercihin eksik\"", viewModel);
        Assert.Contains("\"Geçen dönem kapandı\"", viewModel);
    }

    [Fact]
    public void MainPage_BreaksTheTwelvePeriodInterestIntoItsTwoStates()
    {
        var page = PageSource("MainPage.xaml");

        // I8 sunumda da geçerli: ikisi ters yönde hareket edebiliyor,
        // tek toplam bunu gizler.
        Assert.Contains("{Binding TwelveMonthCardInterest}", page);
        Assert.Contains("{Binding TwelveMonthDeficitInterest}", page);
        Assert.Contains("{Binding TwelveMonthInterest}", page);
    }

    /// <summary>
    /// Kırılım satırları tam sayıya yuvarlanınca ekranda toplanmıyordu
    /// (10.328 + 12.650 = 22.978, motor 22.977). Emülatörde görüldü,
    /// hiçbir teste düşmüyordu.
    /// </summary>
    [Fact]
    public void InterestBreakdowns_AreShownToTheKurus()
    {
        foreach (var name in new[]
                 {
                     "DashboardViewModel.cs",
                     "FutureMonthsViewModel.cs"
                 })
        {
            var viewModel = ViewModelSource(name);
            Assert.DoesNotContain(
                "Interest = Money(interest.CreditCardInterest);",
                viewModel);
            Assert.DoesNotContain(
                "dashboard.TwelvePeriodTotalInterest);",
                viewModel);
        }
    }

    private static string PageSource(string fileName) => File.ReadAllText(
        Path.Combine(
            RepositoryRoot(), "src", "CoinFlow.App", "Pages", fileName));

    private static string ViewModelSource(string fileName) => File.ReadAllText(
        Path.Combine(
            RepositoryRoot(), "src", "CoinFlow.App", "ViewModels", fileName));

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
