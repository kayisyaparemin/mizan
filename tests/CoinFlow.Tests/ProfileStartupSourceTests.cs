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
