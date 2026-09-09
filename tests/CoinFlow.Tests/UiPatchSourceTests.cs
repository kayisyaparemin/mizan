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
        // I14 — dönem içi gözlem snapshot zincirine yazmaz. v1.3.0'da bu test
        // tam tersini şart koşuyordu; o zaman doğru sanılan şey ölçüldü ve
        // yanlış çıktı: checkpoint öne çekiliyor, donmuş plan yetim kalıyordu.
        Assert.Contains("ObserveCurrentBalanceAsync(", viewModel);
        Assert.DoesNotContain(
            "RefreshCurrentFinancialStateAsync(",
            viewModel);
        // Mevcut tutar plan bloğundan önce gelmeli: önce bugünü gir.
        Assert.True(
            page.IndexOf("CurrentBalanceInput", StringComparison.Ordinal) <
            page.IndexOf("PlannedEndingText", StringComparison.Ordinal),
            "Mevcut tutar kutusu PLAN bloğunun üstünde olmalı.");
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

    /// <summary>
    /// I16 — Ana Sayfa mevcut dönemin ekranıdır. Başka zaman dilimlerinin
    /// rakamları burada durmaz; her biri kendi ekranının verisidir.
    /// </summary>
    [Fact]
    public void MainPage_OnlyShowsTheCurrentPeriod()
    {
        var page = PageSource("MainPage.xaml");
        var viewModel = ViewModelSource("DashboardViewModel.cs");

        // Gelecek: 12 dönem sonu ve faiz kırılımı 12 Dönem ekranının.
        Assert.DoesNotContain("TwelveMonth", page);
        Assert.DoesNotContain("TwelveMonth", viewModel);
        Assert.DoesNotContain("TightestPeriod", page);
        // Geçmiş: kapanmış dönem özeti Geçmiş ekranının.
        Assert.DoesNotContain("HistorySummary", page);
        Assert.DoesNotContain("StructureSummary", page);
        // Linkler kalır, rakamsız.
        Assert.Contains("OpenFutureMonthsCommand", page);
        Assert.Contains("OpenHistoryCommand", page);
        // Mevcut dönemin kendi verisi burada.
        Assert.Contains("PlannedEndingText", page);
        Assert.Contains("ProjectedEndingText", page);
        Assert.Contains("RemainingLines", page);
    }

    /// <summary>
    /// Ana Sayfa artık rakamlarını gelecek motorundan almıyor. Bu kaçak geri
    /// gelirse mevcut dönem yeniden projeksiyonun render'ına döner.
    /// </summary>
    [Fact]
    public void MainPage_ReadsTheFrozenPlanNotTheProjection()
    {
        var viewModel = ViewModelSource("DashboardViewModel.cs");

        Assert.Contains("GetPeriodProgressAsync()", viewModel);
        Assert.Contains("ApplyProgress(progress)", viewModel);
    }

    [Fact]
    public void MainPage_SaysTheSameThingOnce()
    {
        var page = PageSource("MainPage.xaml");
        var viewModel = ViewModelSource("DashboardViewModel.cs");

        Assert.DoesNotContain("Text=\"{Binding EndingSavings}\"", page);
        Assert.DoesNotContain("\"Bu dönem açık veriyor\"", viewModel);
        Assert.Contains("OpenCurrentPeriodDetailCommand", page);
        // Aksiyon bekleyen uyarılar duruyor; kalkan yalnız tekrar edeni.
        Assert.Contains("\"Kart ödeme tercihin eksik\"", viewModel);
        Assert.Contains("\"Geçen dönem kapandı\"", viewModel);
    }

    /// <summary>
    /// Faiz kırılımı ana sayfadan 12 Dönem'e taşındı (I16); kırılımın kendisi
    /// korunuyor çünkü ikisi ters yönde hareket edebiliyor (I8).
    /// </summary>
    [Fact]
    public void TwelvePeriods_BreakTheInterestIntoItsTwoStates()
    {
        var page = PageSource("FutureMonthsPage.xaml");

        Assert.Contains("{Binding TotalCreditCardInterest}", page);
        Assert.Contains("{Binding TotalDeficitInterest}", page);
        Assert.Contains("{Binding TotalInterestCost}", page);
    }

    /// <summary>
    /// 12 Dönem checkpoint planını gösterir ve kaymaz — ama sapmayı söyler.
    /// Kullanıcı yanlış olduğunu bildiği bir rakama bakıp ekranın susmasıyla
    /// karşılaşmamalı.
    /// </summary>
    [Fact]
    public void TwelvePeriods_DiscloseTheObservedDeviation()
    {
        var page = PageSource("FutureMonthsPage.xaml");
        var viewModel = ViewModelSource("FutureMonthsViewModel.cs");

        Assert.Contains("{Binding DeviationNoticeText}", page);
        Assert.Contains("HasDeviationNotice", page);
        // Gözlem yalnız uyarı üretir; projeksiyona girmez.
        Assert.Contains("GetPeriodProgressAsync()", viewModel);
        Assert.DoesNotContain("ObservedBalance", viewModel);
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
