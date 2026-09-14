using Android;
using Android.Content;
using Android.Content.PM;
using AndroidX.Core.Content;
using CoinFlow.Infrastructure.Persistence;
using AndroidEnvironment = Android.OS.Environment;
using AndroidSettings = Android.Provider.Settings;
using AndroidUri = Android.Net.Uri;

namespace CoinFlow.App.Backup;

/// <summary>
/// Depolamanın en üstündeki <c>Mizan</c> klasörüne yazma izni.
/// </summary>
/// <remarks>
/// Android 11 ve üstünde uygulamalar kendi klasörlerinin dışına ancak
/// "Tüm dosyalara erişim" izniyle yazabilir; bu izin bir diyalogla değil,
/// sistem ayarındaki tek bir anahtarla verilir. Android 10 ve altında klasik
/// depolama izni yeterlidir.
/// </remarks>
public sealed class AndroidStorageAccess : IStorageAccess
{
    // Geliştirme sürümü aynı telefonda kararlı sürümle yan yana kurulabiliyor;
    // aynı klasörü paylaşsalar aynı günün yedeği birbirinin üzerine yazılırdı.
#if COINFLOW_DEV_BUILD
    public const string FolderName = "Mizan Dev";
#else
    public const string FolderName = "Mizan";
#endif

    public static string FolderPath
    {
        get
        {
#pragma warning disable CS0618, CA1422 // Kök klasörün yolu için hâlâ tek genel API.
            var root = AndroidEnvironment.ExternalStorageDirectory?.AbsolutePath ??
                       "/storage/emulated/0";
#pragma warning restore CS0618, CA1422
            return Path.Combine(root, FolderName);
        }
    }

    public bool HasAccess => OperatingSystem.IsAndroidVersionAtLeast(30)
        ? AndroidEnvironment.IsExternalStorageManager
        : ContextCompat.CheckSelfPermission(
            Platform.AppContext,
            Manifest.Permission.WriteExternalStorage) == Permission.Granted;

    public async Task<bool> RequestAccessAsync()
    {
        if (HasAccess)
        {
            return true;
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            try
            {
                await ActivityResults.StartAsync(new Intent(
                    AndroidSettings.ActionManageAppAllFilesAccessPermission,
                    AndroidUri.Parse($"package:{Platform.AppContext.PackageName}")));
            }
            catch (ActivityNotFoundException)
            {
                // Bazı üreticiler uygulamaya özel ekranı açmıyor; genel liste.
                await ActivityResults.StartAsync(new Intent(
                    AndroidSettings.ActionManageAllFilesAccessPermission));
            }

            return HasAccess;
        }

        var status = await MainThread.InvokeOnMainThreadAsync(
            () => Permissions.RequestAsync<Permissions.StorageWrite>());
        return status == PermissionStatus.Granted;
    }
}
