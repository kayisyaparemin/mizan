using System.Text.RegularExpressions;

namespace Mizan.Tests;

/// <summary>
/// Profil akışının uygulama katmanındaki kuralları. Test projesi
/// <c>Mizan.App</c>'e referans vermediği için kaynak üzerinden sabitlenir.
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

    /// <summary>
    /// v1.18.2 açılış güvenliği: MainActivity pencere ve insets ayarlarında
    /// controller null kontrolü ve try-catch koruması olmalıdır.
    /// </summary>
    [Fact]
    public void MainActivity_ProtectsWindowInsetsAndCatchesExceptions()
    {
        var activity = AppSource(Path.Combine("Platforms", "Android", "MainActivity.cs"));

        Assert.Contains("if (controller is not null)", activity);
        Assert.Contains("catch (Exception exception)", activity);
    }

    /// <summary>
    /// v1.18.2 açılış güvenliği: MainApplication genel hata yakalayıcıları (AppDomain,
    /// AndroidEnvironment, TaskScheduler) kaydetmelidir.
    /// </summary>
    [Fact]
    public void MainApplication_RegistersGlobalExceptionHandlers()
    {
        var app = AppSource(Path.Combine("Platforms", "Android", "MainApplication.cs"));

        Assert.Contains("AppDomain.CurrentDomain.UnhandledException", app);
        Assert.Contains("AndroidEnvironment.UnhandledExceptionRaiser", app);
        Assert.Contains("TaskScheduler.UnobservedTaskException", app);
    }

    /// <summary>
    /// v1.18.2 açılış güvenliği: ProfileSelectionPage.OnAppearing gövdesi
    /// koruyucu try-catch bloğu içinde olmalıdır; açılışta fırlayan hiçbir istisna
    /// uygulamayı çökerterek kapatmamalıdır.
    /// </summary>
    [Fact]
    public void ProfileSelectionPage_OnAppearing_HasProtectiveTryCatch()
    {
        var profilePage = AppSource(Path.Combine("Pages", "ProfileSelectionPage.xaml.cs"));

        Assert.Contains("protected override async void OnAppearing()", profilePage);
        Assert.Contains("catch (Exception exception)", profilePage);
        Assert.Contains("_viewModel.MarkBackupAccessOffered();", profilePage);
    }

    /// <summary>
    /// v1.18.2 açılış güvenliği: UserFeedbackService diyalog metotları
    /// pencere/ekran hazır değilken istisna fırlatmak yerine güvenli dönüş yapmalıdır.
    /// </summary>
    [Fact]
    public void UserFeedbackService_HandlesExceptions_WithoutCrashingApp()
    {
        var feedback = AppSource(Path.Combine("Services", "UserFeedbackService.cs"));

        Assert.Contains("try", feedback);
        Assert.Contains("catch", feedback);
        Assert.Contains("return false;", feedback);
    }

    private static string AppSource(string fileName) => File.ReadAllText(
        Path.Combine(RepositoryRoot(), "src", "Mizan.App", fileName));

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
