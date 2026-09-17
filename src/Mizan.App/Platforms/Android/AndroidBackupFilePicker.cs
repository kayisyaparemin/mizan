using Android.App;
using Android.Content;
using Android.Provider;
using Mizan.App.Services;

namespace Mizan.App.Backup;

/// <summary>
/// Yedek başka bir yerdeyse (bilgisayardan kopyalandıysa) sistemin dosya
/// seçicisini <c>Mizan</c> klasöründe açar. MAUI'nin <c>FilePicker</c>'ı
/// başlangıç klasörü alamıyor ve "Son dosyalar"da boş açılıyordu.
/// </summary>
public sealed class AndroidBackupFilePicker : IBackupFilePicker
{
    public async Task<Stream?> PickAndOpenAsync()
    {
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("*/*");
        intent.PutExtra(
            Intent.ExtraMimeTypes,
            ["application/zip", "application/x-zip-compressed", "application/octet-stream"]);
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            intent.PutExtra(
                DocumentsContract.ExtraInitialUri,
                DocumentsContract.BuildDocumentUri(
                    "com.android.externalstorage.documents",
                    $"primary:{AndroidStorageAccess.FolderName}"));
        }

        var (resultCode, data) = await ActivityResults.StartAsync(intent);
        if (resultCode != Result.Ok || data?.Data is not { } uri)
        {
            return null;
        }

        return Platform.AppContext.ContentResolver?.OpenInputStream(uri) ??
               throw new IOException("Seçilen dosya açılamadı.");
    }
}
