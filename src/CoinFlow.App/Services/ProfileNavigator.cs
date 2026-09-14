using CoinFlow.App.Pages;
using CoinFlow.Application.Services;

namespace CoinFlow.App.Services;

/// <summary>
/// Uygulamanın kökünü profil seçimi ile profilin kendi kabuğu arasında
/// değiştirir.
/// </summary>
/// <remarks>
/// Her profil açılışında <see cref="AppShell"/> yeniden kurulur. Sayfalar ve
/// view model'ler kabukla birlikte doğduğu için önceki profilin ekranlarından
/// hiçbir durum (form, seçim, "bu soruyu sordum" bayrağı) yeni profile taşınmaz.
/// </remarks>
public sealed class ProfileNavigator(
    IServiceProvider services,
    ProfileService profiles)
{
    public async Task OpenAsync(Guid profileId)
    {
        await profiles.OpenAsync(profileId);
        await MainThread.InvokeOnMainThreadAsync(
            () => SetRoot(services.GetRequiredService<AppShell>()));
    }

    public async Task SwitchProfileAsync()
    {
        // Önce ekran değişir, sonra bağlantı kapanır: kapanan profilin
        // sayfaları artık görünmüyorken veritabanı elden alınır.
        await MainThread.InvokeOnMainThreadAsync(
            () => SetRoot(services.GetRequiredService<ProfileSelectionPage>()));
        await profiles.CloseAsync();
    }

    private static void SetRoot(Page page)
    {
        if (Microsoft.Maui.Controls.Application.Current is { } application)
        {
            application.MainPage = page;
        }
    }
}
