using System.Text.RegularExpressions;

namespace CoinFlow.Tests;

/// <summary>
/// Profil akışının uygulama katmanındaki kuralları. Test projesi
/// <c>CoinFlow.App</c>'e referans vermediği için kaynak üzerinden sabitlenir.
/// </summary>
public sealed class ProfileStartupSourceTests
{
    /// <summary>
    /// Emülatörde yakalandı: seçim sayfası <c>App</c> yapıcısına parametre
    /// olarak gelince XAML'i uygulama kaynakları yüklenmeden çözülüyor ve
    /// açılışta "StaticResource not found for key Eyebrow" ile çöküyordu.
    /// </summary>
    [Fact]
    public void App_BuildsTheProfilePicker_AfterItsResourcesAreLoaded()
    {
        var app = AppSource("App.xaml.cs");

        Assert.DoesNotMatch(new Regex(@"public App\([^)]*Page\b"), app);
        var initialize = app.IndexOf("InitializeComponent()", StringComparison.Ordinal);
        var picker = app.IndexOf("GetRequiredService<ProfileSelectionPage>()", StringComparison.Ordinal);
        Assert.True(initialize >= 0 && picker > initialize,
            "Profil seçimi InitializeComponent'ten sonra oluşturulmalı.");
    }

    /// <summary>
    /// Kabuk singleton kalırsa sayfaları ve view model'leri önceki profilden
    /// yeni profile taşınır; "yeni yüklenmiş gibi" sözü bozulur.
    /// </summary>
    [Fact]
    public void Shell_IsRebuiltForEveryProfile()
    {
        var program = AppSource("MauiProgram.cs");

        Assert.Contains("AddTransient<AppShell>()", program);
        Assert.DoesNotContain("AddSingleton<AppShell>", program);
        // Veritabanı yolu tek dosyaya sabitlenmez; profil başına çözülür.
        Assert.DoesNotContain("\"coinflow.db3\"", program);
        Assert.Contains("GetDatabasePath(profileId)", program);
    }

    [Fact]
    public void Menu_OffersProfileSwitch()
    {
        var shell = AppSource("AppShell.cs");

        Assert.Contains("Profil Değiştir", shell);
        Assert.Contains("SwitchProfileAsync", shell);
    }

    /// <summary>
    /// v1.18.0 Clean Architecture refactor sonrasında yakalanan ANR hatası:
    /// UserFeedbackService zaten UI thread'indeyken DisplayAlert'i InvokeOnMainThreadAsync
    /// içine sardığında Android dispatcher kuyruğunda kilitleniyordu (ANR).
    /// </summary>
    [Fact]
    public void UserFeedbackService_ChecksIsMainThread_ToPreventAnrDeadlock()
    {
        var feedback = AppSource(Path.Combine("Services", "UserFeedbackService.cs"));

        Assert.Contains("MainThread.IsMainThread", feedback);
        Assert.Contains("Application.Current?.MainPage", feedback);
    }

    /// <summary>
    /// Modal tamamlama bekleyişi (page.Completion) InvokeOnMainThreadAsync
    /// içinde kalırsa UI mesaj kuyruğu kullanıcı etkileşimi bitene kadar
    /// kilitlenir. Bu bekleme InvokeOnMainThreadAsync dışında yapılmalıdır.
    /// </summary>
    [Fact]
    public void MauiNavigationService_DoesNotBlockMainThread_OnModalCompletion()
    {
        var nav = AppSource(Path.Combine("Services", "MauiNavigationService.cs"));

        // Modal completion TCS bekleyişleri InvokeOnMainThreadAsync lambda'sı içinde değil, metodun en sonunda olmalıdır.
        Assert.Contains("return await page.Completion;", nav);
        Assert.Contains("await page.Completion;\r\n        return true;", nav.Replace("\r\n", "\n").Replace("\n", "\r\n"));
        Assert.DoesNotContain("MainThread.InvokeOnMainThreadAsync(async () =>\r\n        {\r\n            var page = services.GetRequiredService<OnboardingPage>();", nav.Replace("\r\n", "\n").Replace("\n", "\r\n"));
    }

    /// <summary>
    /// ProfileSelectionPage açılışında Loaded olayı kaçırılırsa veya düşerse
    /// WhenLoadedAsync sonsuz beklememeli, zaman aşımı koruması taşımalıdır.
    /// </summary>
    [Fact]
    public void ProfileSelectionPage_WhenLoadedAsync_HasTimeoutSafeguard()
    {
        var profilePage = AppSource(Path.Combine("Pages", "ProfileSelectionPage.xaml.cs"));

        Assert.Contains("Task.WhenAny(loaded.Task, Task.Delay(500))", profilePage);
    }

    private static string AppSource(string fileName) => File.ReadAllText(
        Path.Combine(RepositoryRoot(), "src", "CoinFlow.App", fileName));

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
