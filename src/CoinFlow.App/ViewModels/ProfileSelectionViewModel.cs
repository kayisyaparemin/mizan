using System.Collections.ObjectModel;
using System.Globalization;
using CoinFlow.App.Models;
using CoinFlow.App.Services;
using CoinFlow.Application.Models;
using CoinFlow.Application.Services;

namespace CoinFlow.App.ViewModels;

public sealed class ProfileSelectionViewModel(
    ProfileService profiles,
    ProfileNavigator navigator,
    BackupService backup,
    IBackupFilePicker filePicker) : ViewModelBase
{
    private const long MaxBackupBytes = 1024L * 1024 * 1024;
    private const string AccessPromptShownKey = "backup.access-prompt-shown";
    private bool _loaded;

    public ObservableCollection<ProfileCardItem> Profiles { get; } = [];

    public int MaxNameLength => UserProfile.MaxNameLength;

    /// <summary>Son kalan profil silinemez; seçenek hiç gösterilmez.</summary>
    public bool CanDeleteProfiles => Profiles.Count > 1;

    public bool HasProfiles => Profiles.Count > 0;

    /// <summary>
    /// Hiç profil yok: uygulama yeni kurulmuş. Kullanıcı yedekten dönmek ile
    /// temiz başlamak arasında seçer.
    /// </summary>
    public bool IsFirstRun => _loaded && Profiles.Count == 0;

    public string BackupLocation => backup.LocationDescription;

    public Task LoadAsync() => RunAsync(ReloadAsync);

    /// <summary>
    /// Yedek klasörüne izin verilmemişse bu kurulumda bir kez sorulur.
    /// Uygulama kaldırılınca bu bilgi de silinir; yeniden kurulumda yine sorulur.
    /// </summary>
    public bool ShouldOfferBackupAccess() =>
        !backup.HasAccess && !Preferences.Default.Get(AccessPromptShownKey, false);

    /// <summary>Soru ekranda gösterildikten sonra çağrılır.</summary>
    public void MarkBackupAccessOffered() =>
        Preferences.Default.Set(AccessPromptShownKey, true);

    public Task<bool> RequestBackupAccessAsync() => backup.RequestAccessAsync();

    public Task OpenAsync(ProfileCardItem profile) =>
        RunAsync(() => navigator.OpenAsync(profile.Id));

    /// <summary>
    /// Yeni profil oluşturulur oluşturulmaz açılır: boş profil ilk kurulumla
    /// başlar, tıpkı uygulamanın yeni yüklendiği gibi.
    /// </summary>
    public Task CreateAndOpenAsync(string name) =>
        RunAsync(async () =>
        {
            var profile = await profiles.CreateAsync(name);
            await navigator.OpenAsync(profile.Id);
        });

    public Task RenameAsync(ProfileCardItem profile, string name) =>
        RunAsync(async () =>
        {
            await profiles.RenameAsync(profile.Id, name);
            await ReloadAsync();
        });

    public Task DeleteAsync(ProfileCardItem profile) =>
        RunAsync(async () =>
        {
            await profiles.DeleteAsync(profile.Id);
            await ReloadAsync();
        });

    /// <summary>
    /// Mizan klasöründeki yedekler, en yenisi önce. İzin yoksa önce ister;
    /// izin verilmezse <c>null</c> döner.
    /// </summary>
    public async Task<IReadOnlyList<StoredBackup>?> FindBackupsAsync()
    {
        IReadOnlyList<StoredBackup>? backups = null;
        await RunAsync(async () =>
        {
            if (!backup.HasAccess && !await backup.RequestAccessAsync())
            {
                SetStatus(
                    $"Yedekleri okuyabilmem için dosya erişim izni gerekiyor ({backup.LocationDescription}).");
                return;
            }

            backups = await backup.ListBackupsAsync();
        });

        return backups;
    }

    public static string BackupLabel(StoredBackup stored) =>
        stored.ModifiedAt.ToLocalTime().ToString("d MMMM yyyy, HH:mm", TurkishCulture);

    public async Task<BackupSummary?> RestoreAsync(StoredBackup stored)
    {
        BackupSummary? summary = null;
        await RunAsync(async () =>
        {
            summary = await backup.RestoreAsync(stored.FileName);
            await ReloadAsync();
        });

        return summary;
    }

    /// <summary>
    /// Yedek başka bir yerdeyse dosyayı kullanıcı seçer. Vazgeçilir ya da
    /// hata olursa <c>null</c> döner (hata durum satırına yazılır).
    /// </summary>
    public async Task<BackupSummary?> RestoreFromPickedFileAsync()
    {
        BackupSummary? summary = null;
        await RunAsync(async () =>
        {
            await using var source = await filePicker.PickAndOpenAsync();
            if (source is null)
            {
                return;
            }

            // Seçiciden gelen akış konumlanamayabilir; zip okumak için önce
            // yerel bir kopyaya alınır.
            var localPath = Path.Combine(
                FileSystem.CacheDirectory,
                $"restore-{Guid.NewGuid():N}.zip");
            try
            {
                await using (var destination = File.Create(localPath))
                {
                    await source.CopyToAsync(destination);
                    if (destination.Length > MaxBackupBytes)
                    {
                        throw new InvalidOperationException(
                            "Bu dosya bir Mizan yedeği olamayacak kadar büyük.");
                    }
                }

                await using var backupStream = File.OpenRead(localPath);
                summary = await backup.RestoreAsync(backupStream);
                await ReloadAsync();
            }
            finally
            {
                if (File.Exists(localPath))
                {
                    File.Delete(localPath);
                }
            }
        });

        return summary;
    }

    private async Task ReloadAsync()
    {
        var list = await profiles.GetProfilesAsync();
        Profiles.Clear();
        foreach (var item in list)
        {
            Profiles.Add(ToCard(item));
        }

        _loaded = true;
        OnPropertyChanged(nameof(CanDeleteProfiles));
        OnPropertyChanged(nameof(HasProfiles));
        OnPropertyChanged(nameof(IsFirstRun));
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            SetStatus(string.Empty);
            await action();
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static ProfileCardItem ToCard(UserProfile profile) =>
        new(
            profile.Id,
            profile.Name,
            StringInfo.GetNextTextElement(profile.Name).ToUpper(TurkishCulture),
            profile.LastOpenedAt is { } openedAt
                ? $"Son açılış {openedAt.ToLocalTime().ToString("d MMMM yyyy", TurkishCulture)}"
                : "Henüz açılmadı");
}
