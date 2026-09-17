using System.IO.Compression;
using System.Text.Json;
using Mizan.Application.Models;
using SQLite;

namespace Mizan.Infrastructure.Persistence;

public sealed partial class ProfileBackupArchive
{
    public async Task ImportAsync(
        Stream source,
        IReadOnlyList<ProfileImport> imports,
        CancellationToken cancellationToken = default)
    {
        if (imports.Count == 0)
        {
            throw new InvalidOperationException("Eklenecek profil seçilmedi.");
        }

        if (imports.Select(import => import.Target.Id).Distinct().Count() != imports.Count)
        {
            throw new InvalidOperationException("Aynı profil iki kez eklenemez.");
        }

        var stagingRoot = Path.Combine(
            repository.RootDirectory,
            $".restore-{Guid.NewGuid():N}");
        var moved = new List<string>();
        try
        {
            var staging = new FileSystemProfileRepository(stagingRoot);
            using (var zip = OpenArchive(source))
            {
                var manifest = await ReadManifestAsync(zip, cancellationToken);
                foreach (var import in imports)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var profile = manifest.Profiles.FirstOrDefault(
                                      candidate => candidate.Id == import.SourceId) ??
                                  throw Corrupt("Seçilen profil yedekte yok.");
                    if (profile.HasData)
                    {
                        var target = staging.GetDatabasePath(import.Target.Id);
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        ExtractDatabase(zip, profile, target);
                        ValidateDatabase(target, profile.Name);
                    }

                    await staging.SaveProfileAsync(import.Target, cancellationToken);
                }
            }

            // Her şey doğrulandıktan sonra yerine taşınır. Hedef klasör varsa
            // hiçbir şeyin üzerine yazılmaz; taşıma yarıda kalırsa taşınanlar
            // geri alınır.
            if (imports.Any(import => Directory.Exists(
                    repository.GetProfileDirectory(import.Target.Id))))
            {
                throw new InvalidOperationException(
                    "Eklenecek profil telefonda zaten var.");
            }

            foreach (var import in imports)
            {
                var target = repository.GetProfileDirectory(import.Target.Id);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                Directory.Move(staging.GetProfileDirectory(import.Target.Id), target);
                moved.Add(target);
            }

            moved.Clear();
        }
        finally
        {
            foreach (var directory in moved)
            {
                TryDeleteDirectory(directory);
            }

            TryDeleteDirectory(stagingRoot);
        }
    }

    private static void SnapshotDatabase(string databasePath, string snapshotPath)
    {
        SQLitePCL.Batteries_V2.Init();
        using var connection = new SQLiteConnection(
            new SQLiteConnectionString(databasePath, SQLiteOpenFlags.ReadOnly, true));
        connection.BusyTimeout = TimeSpan.FromSeconds(10);
        connection.Execute(
            $"VACUUM INTO '{snapshotPath.Replace("'", "''", StringComparison.Ordinal)}'");
    }

    private static void ExtractDatabase(
        ZipArchive zip,
        BackupManifestProfile profile,
        string target)
    {
        var entry = zip.GetEntry(DatabaseEntryName(profile.Id)) ??
                    throw Corrupt($"\"{profile.Name}\" profilinin verisi yedekte yok.");
        if (entry.Length > MaxDatabaseBytes)
        {
            throw Corrupt($"\"{profile.Name}\" profilinin verisi beklenenden büyük.");
        }

        try
        {
            entry.ExtractToFile(target);
        }
        catch (InvalidDataException exception)
        {
            throw Corrupt($"\"{profile.Name}\" profilinin verisi okunamadı.", exception);
        }
    }

    private static void ValidateDatabase(string path, string profileName)
    {
        try
        {
            SQLitePCL.Batteries_V2.Init();
            using var connection = new SQLiteConnection(
                new SQLiteConnectionString(path, SQLiteOpenFlags.ReadOnly, true));
            var check = connection.ExecuteScalar<string>("PRAGMA quick_check");
            if (!string.Equals(check, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw Corrupt($"\"{profileName}\" profilinin verisi bozuk.");
            }

            var schemaVersion = connection.ExecuteScalar<int>(
                "SELECT SchemaVersion FROM settings LIMIT 1");
            if (schemaVersion > SqliteMizanStore.CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    "Bu yedek Mizan'ın daha yeni bir sürümünden alınmış. Önce uygulamayı güncelle.");
            }
        }
        catch (SQLiteException exception)
        {
            throw Corrupt($"\"{profileName}\" profilinin verisi okunamadı.", exception);
        }
    }

    private static ZipArchive OpenArchive(Stream source)
    {
        try
        {
            return new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException exception)
        {
            throw NotABackup(exception);
        }
    }

    private static async Task<BackupManifest> ReadManifestAsync(
        ZipArchive zip,
        CancellationToken cancellationToken)
    {
        var entry = zip.GetEntry(ManifestEntryName) ?? throw NotABackup();
        BackupManifest? manifest;
        try
        {
            await using var stream = entry.Open();
            manifest = await JsonSerializer.DeserializeAsync(
                stream,
                BackupJsonContext.Default.BackupManifest,
                cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            throw NotABackup(exception);
        }

        if (manifest?.Profiles is null)
        {
            throw NotABackup();
        }

        if (manifest.Format > FormatVersion)
        {
            throw new InvalidOperationException(
                "Bu yedek Mizan'ın daha yeni bir sürümünden alınmış. Önce uygulamayı güncelle.");
        }

        if (manifest.Profiles.Count == 0)
        {
            throw Corrupt("Yedekte hiç profil yok.");
        }

        if (manifest.Profiles.Any(profile =>
                profile.Id == Guid.Empty || string.IsNullOrWhiteSpace(profile.Name)) ||
            manifest.Profiles.Select(profile => profile.Id).Distinct().Count() !=
            manifest.Profiles.Count)
        {
            throw Corrupt("Yedekteki profil listesi bozuk.");
        }

        return manifest;
    }

    private static InvalidOperationException NotABackup(Exception? inner = null) =>
        new("Bu dosya bir Mizan yedeği değil.", inner);

    private static InvalidOperationException Corrupt(
        string message,
        Exception? inner = null) =>
        new($"Yedek geri yüklenemedi: {message}", inner);

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
