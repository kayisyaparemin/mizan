namespace Mizan.Tests;

/// <summary>
/// Yedeğin Android tarafında test projesinin çalıştıramadığı ama bozulunca
/// sessizce yedeksiz bırakan kuralları. Kaynak üzerinden sabitlenir.
/// </summary>
public sealed class BackupAppSourceTests
{
    [Fact]
    public void Manifest_DeclaresWhatTheBackupNeeds()
    {
        var manifest = AndroidSource("AndroidManifest.xml");

        // Kök Mizan klasörü: Android 11+ tüm dosyalara erişim, 10 ve altı klasik izin.
        Assert.Contains("android.permission.MANAGE_EXTERNAL_STORAGE", manifest);
        Assert.Contains("android.permission.WRITE_EXTERNAL_STORAGE", manifest);
        Assert.Contains("android:requestLegacyExternalStorage=\"true\"", manifest);
        // Kalıcı (persisted) gece görevi bu izin olmadan kurulamaz.
        Assert.Contains("android.permission.RECEIVE_BOOT_COMPLETED", manifest);
    }

    /// <summary>
    /// Android kalıcı görevi sınıf adıyla saklar. Ad derlemeye göre değişen
    /// üretilmiş bir ad olursa güncellemeden sonra gece görevi bulunamaz.
    /// </summary>
    [Fact]
    public void NightlyJob_HasAStableJavaName_AndIsScheduledOnStart()
    {
        var job = AndroidSource("NightlyBackupJob.cs");
        var application = AndroidSource("MainApplication.cs");

        Assert.Contains("Name = \"com.coinflow.mobile.NightlyBackupJob\"", job);
        Assert.Contains("android.permission.BIND_JOB_SERVICE", job);
        Assert.Contains("SetPersisted(true)", job);
        Assert.Contains("NightlyBackupJob.EnsureScheduled(this)", application);
    }

    [Fact]
    public void PickerAndPermissionScreens_ReportBackToTheApp()
    {
        var activity = AndroidSource("MainActivity.cs");

        Assert.Contains("ActivityResults.OnActivityResult(requestCode, resultCode, data)", activity);
    }

    [Fact]
    public void Backups_GoToTheRootMizanFolder_NotInsideTheApp()
    {
        var program = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "Mizan.App", "MauiProgram.cs"));
        var access = AndroidSource("AndroidStorageAccess.cs");

        Assert.Contains("new FolderBackupStorage(", program);
        Assert.Contains("AndroidStorageAccess.FolderPath", program);
        Assert.DoesNotContain("AppDataDirectory, \"Mizan", program);
        Assert.Contains("public const string FolderName = \"Mizan\"", access);
        // Geliştirme sürümü kararlı sürümün yedeklerinin üzerine yazmaz.
        Assert.Contains("public const string FolderName = \"Mizan Dev\"", access);
    }

    private static string AndroidSource(string fileName) => File.ReadAllText(
        Path.Combine(RepositoryRoot(), "src", "Mizan.App", "Platforms", "Android", fileName));

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
