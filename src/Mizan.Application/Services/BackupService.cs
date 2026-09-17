using System.Globalization;
using Mizan.Application.Abstractions;
using Mizan.Application.Models;

namespace Mizan.Application.Services;

public sealed record BackupOptions(string WorkingDirectory, int KeepCount = 7);

/// <summary>
/// Yedekleme kuralları: gün başına bir dosya, değişiklik yoksa yeni dosya
/// yok, en yeni <see cref="BackupOptions.KeepCount"/> yedek saklanır.
/// </summary>
public sealed class BackupService(
    IProfileBackupArchive archive,
    IBackupStorage storage,
    IProfileRepository profiles,
    IClock clock,
    BackupOptions options)
{
    public const string FilePrefix = "Mizan-yedek-";
    public const string FileExtension = ".zip";
    private readonly SemaphoreSlim _lock = new(1, 1);

    public bool HasAccess => storage.HasAccess;

    public string LocationDescription => storage.LocationDescription;

    public Task<bool> RequestAccessAsync() => storage.RequestAccessAsync();

    public Task<BackupState?> GetLastBackupAsync(
        CancellationToken cancellationToken = default) =>
        archive.GetStateAsync(cancellationToken);

    /// <summary>
    /// Gece görevi. Son yedekten beri hiçbir profil değişmediyse dosya
    /// yazmaz: aksi hâlde uygulamanın açılmadığı bir hafta, eski ama farklı
    /// yedekleri aynı içerikli kopyalarla saklama sınırının dışına iterdi.
    /// </summary>
    public Task<BackupResult> BackUpIfChangedAsync(
        CancellationToken cancellationToken = default) =>
        BackUpAsync(onlyIfChanged: true, cancellationToken);

    /// <summary>Ayarlar'daki "Şimdi Yedekle".</summary>
    public Task<BackupResult> BackUpNowAsync(
        CancellationToken cancellationToken = default) =>
        BackUpAsync(onlyIfChanged: false, cancellationToken);

    /// <summary>Klasördeki Mizan yedekleri, en yenisi önce.</summary>
    public async Task<IReadOnlyList<StoredBackup>> ListBackupsAsync(
        CancellationToken cancellationToken = default) =>
        OnlyMizanBackups(await storage.ListAsync(cancellationToken))
            .OrderByDescending(backup => backup.ModifiedAt)
            .ThenByDescending(backup => backup.FileName, StringComparer.Ordinal)
            .ToArray();

    public Task<Stream> OpenBackupAsync(
        string fileName,
        CancellationToken cancellationToken = default) =>
        storage.OpenReadAsync(fileName, cancellationToken);

    /// <summary>Yedeğin içindeki profiller; dosya Mizan yedeği değilse hata.</summary>
    public Task<BackupSummary> ReadBackupAsync(
        Stream source,
        CancellationToken cancellationToken = default) =>
        archive.ReadSummaryAsync(Rewound(source), cancellationToken);

    /// <summary>Klasördeki bir yedeği geri yükler.</summary>
    public async Task<BackupSummary> RestoreAsync(
        string fileName,
        CancellationToken cancellationToken = default)
    {
        await using var source = await storage.OpenReadAsync(fileName, cancellationToken);
        return await RestoreAsync(source, cancellationToken);
    }

    /// <summary>
    /// Uygulama yeni kurulmuşken, hiç profil yokken yedekteki bütün profilleri
    /// olduğu gibi (aynı kimlik ve adla) geri yükler.
    /// </summary>
    public async Task<BackupSummary> RestoreAsync(
        Stream source,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (profiles.HasLegacyDatabase ||
                (await profiles.GetProfilesAsync(cancellationToken)).Count > 0)
            {
                throw new InvalidOperationException(
                    "Yedek yalnız hiç profil yokken geri yüklenebilir.");
            }

            var backup = await archive.ReadSummaryAsync(Rewound(source), cancellationToken);
            await archive.ImportAsync(
                Rewound(source),
                backup.Profiles
                    .Select(profile => new ProfileImport(profile.Id, AsProfile(profile)))
                    .ToArray(),
                cancellationToken);
            return backup;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Profil varken yedekten profil ekler. Mevcut hiçbir profile dokunulmaz:
    /// yedekteki profil telefonda zaten varsa (aynı kimlik) yedekteki hâli
    /// "Ad (14 Eylül yedeği)" adıyla ayrı bir profil olarak eklenir. Ad başka
    /// bir profille çakışırsa sonuna 2, 3… eklenir.
    /// </summary>
    /// <param name="profileIds">Yedekteki profillerden eklenecekler; <c>null</c> hepsi.</param>
    public async Task<BackupImportResult> AddFromBackupAsync(
        Stream source,
        IReadOnlyCollection<Guid>? profileIds,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var backup = await archive.ReadSummaryAsync(Rewound(source), cancellationToken);
            var selected = profileIds is null
                ? backup.Profiles
                : backup.Profiles.Where(profile => profileIds.Contains(profile.Id)).ToArray();
            if (selected.Count == 0 ||
                (profileIds is not null && selected.Count != profileIds.Count))
            {
                throw new InvalidOperationException("Seçilen profil yedekte yok.");
            }

            var existing = await profiles.GetProfilesAsync(cancellationToken);
            var takenNames = existing.Select(profile => profile.Name).ToList();
            var added = new List<ImportedProfile>();
            foreach (var profile in selected)
            {
                var isCopy = existing.Any(current => current.Id == profile.Id);
                var name = ProfileService.UniqueName(
                    isCopy ? CopyName(profile.Name, backup.CreatedAt) : profile.Name,
                    takenNames);
                takenNames.Add(name);
                added.Add(new ImportedProfile(
                    AsProfile(profile) with
                    {
                        Id = isCopy ? Guid.NewGuid() : profile.Id,
                        Name = name,
                        CreatedAt = isCopy ? clock.UtcNow : profile.CreatedAt,
                        LastOpenedAt = isCopy ? null : profile.LastOpenedAt
                    },
                    isCopy));
            }

            await archive.ImportAsync(
                Rewound(source),
                selected
                    .Zip(added, (profile, import) => new ProfileImport(profile.Id, import.Profile))
                    .ToArray(),
                cancellationToken);
            return new BackupImportResult(backup, added);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>"Ayşe (14 Eylül yedeği)"; ad sınırına sığmazsa ad kısaltılır.</summary>
    public static string CopyName(string name, DateTimeOffset backupCreatedAt)
    {
        var suffix =
            $" ({backupCreatedAt.ToLocalTime().ToString("d MMMM", TurkishCulture)} yedeği)";
        var room = UserProfile.MaxNameLength - suffix.Length;
        var trimmed = name.Length <= room
            ? name
            : name[..Math.Max(room - 1, 1)].TrimEnd() + "…";
        return trimmed + suffix;
    }

    private static readonly CultureInfo TurkishCulture = CultureInfo.GetCultureInfo("tr-TR");

    private static UserProfile AsProfile(BackupProfile profile) => new()
    {
        Id = profile.Id,
        Name = profile.Name,
        CreatedAt = profile.CreatedAt,
        LastOpenedAt = profile.LastOpenedAt
    };

    // Yedek iki kez okunur (özet, sonra içe aktarma); akış başa sarılabilmeli.
    private static Stream Rewound(Stream source)
    {
        if (!source.CanSeek)
        {
            throw new ArgumentException(
                "Yedek dosyası konumlanabilir bir akıştan okunmalı.",
                nameof(source));
        }

        source.Position = 0;
        return source;
    }

    public static string FileNameFor(DateOnly date) =>
        FilePrefix +
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
        FileExtension;

    /// <summary>
    /// Silinecek yedekler: en yeni <paramref name="keepCount"/> dosya kalır.
    /// Mizan'ın adlandırmadığı dosyalara dokunulmaz.
    /// </summary>
    public static IReadOnlyList<StoredBackup> SelectForDeletion(
        IEnumerable<StoredBackup> backups,
        int keepCount) =>
        OnlyMizanBackups(backups)
            .OrderByDescending(backup => backup.ModifiedAt)
            .ThenByDescending(backup => backup.FileName, StringComparer.Ordinal)
            .Skip(Math.Max(keepCount, 1))
            .ToArray();

    private static IEnumerable<StoredBackup> OnlyMizanBackups(
        IEnumerable<StoredBackup> backups) =>
        backups.Where(backup =>
            backup.FileName.StartsWith(FilePrefix, StringComparison.Ordinal) &&
            backup.FileName.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase));

    private async Task<BackupResult> BackUpAsync(
        bool onlyIfChanged,
        CancellationToken cancellationToken)
    {
        if (!storage.HasAccess)
        {
            return new BackupResult(BackupOutcome.NoAccess);
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if ((await profiles.GetProfilesAsync(cancellationToken)).Count == 0)
            {
                return new BackupResult(BackupOutcome.NothingToBackUp);
            }

            var fingerprint = await archive.ComputeFingerprintAsync(cancellationToken);
            var previous = await archive.GetStateAsync(cancellationToken);
            if (onlyIfChanged &&
                previous?.Fingerprint == fingerprint &&
                (await storage.ListAsync(cancellationToken))
                .Any(backup => backup.FileName == previous.FileName))
            {
                // Veri aynı ve son yedek hâlâ klasörde. Kullanıcı klasörü
                // silmişse "değişiklik yok" diye yedeksiz kalınmaz.
                return new BackupResult(BackupOutcome.Unchanged, previous);
            }

            // Önce yerel geçici dosyaya yazılır; yarım kalan bir yedek, bugünkü
            // sağlam dosyanın üzerine hiç yazılmaz.
            Directory.CreateDirectory(options.WorkingDirectory);
            var temporaryPath = Path.Combine(
                options.WorkingDirectory,
                $"backup-{Guid.NewGuid():N}.zip");
            try
            {
                await using (var stream = File.Create(temporaryPath))
                {
                    await archive.WriteAsync(stream, cancellationToken);
                }

                var fileName = FileNameFor(clock.Today);
                await storage.SaveAsync(fileName, temporaryPath, cancellationToken);
                await PruneAsync(cancellationToken);

                var state = new BackupState(clock.UtcNow, fileName, fingerprint);
                await archive.SaveStateAsync(state, cancellationToken);
                return new BackupResult(BackupOutcome.Created, state);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task PruneAsync(CancellationToken cancellationToken)
    {
        var stored = await storage.ListAsync(cancellationToken);
        foreach (var backup in SelectForDeletion(stored, options.KeepCount))
        {
            await storage.DeleteAsync(backup.FileName, cancellationToken);
        }
    }
}
