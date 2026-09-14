using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoinFlow.Application.Abstractions;
using CoinFlow.Application.Models;
using SQLite;

namespace CoinFlow.Infrastructure.Persistence;

/// <summary>
/// Yedek dosyası bir zip'tir:
/// <c>mizan-backup.json</c> (biçim, tarih, profiller) ve her profil için
/// <c>profiles/{id}/coinflow.db3</c>.
/// </summary>
/// <remarks>
/// Veritabanı dosyası olduğu gibi kopyalanmaz: açık bir profil o anda
/// yazıyorsa kopya yarım bir işlem içerebilir. <c>VACUUM INTO</c> tek bir
/// okuma işlemi içinde tutarlı bir anlık görüntü üretir.
/// </remarks>
public sealed class ProfileBackupArchive(
    FileSystemProfileRepository repository,
    IClock clock) : IProfileBackupArchive
{
    public const int FormatVersion = 1;
    public const string ManifestEntryName = "mizan-backup.json";
    private const string StateFileName = "backup-state.json";
    private const long MaxDatabaseBytes = 512L * 1024 * 1024;

    public async Task<string> ComputeFingerprintAsync(
        CancellationToken cancellationToken = default)
    {
        // Dosyanın değişiklik zamanına değil içeriğine bakılır: store her
        // açılışta ayar satırını aynı değerlerle yeniden yazıyor, zaman
        // damgası "değişti" derdi. Son açılış tarihi de bilinçli olarak
        // dışarıda — yalnız profili açmak yeni bir yedek gerektirmez.
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var profile in (await repository.GetProfilesAsync(cancellationToken))
                     .OrderBy(profile => profile.Id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Append(hash, $"profile|{profile.Id:N}|{profile.Name}");
            var databasePath = repository.GetDatabasePath(profile.Id);
            if (File.Exists(databasePath))
            {
                AppendDatabaseContent(hash, databasePath);
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void AppendDatabaseContent(IncrementalHash hash, string databasePath)
    {
        SQLitePCL.Batteries_V2.Init();
        using var connection = new SQLiteConnection(
            new SQLiteConnectionString(databasePath, SQLiteOpenFlags.ReadOnly, true));
        connection.BusyTimeout = TimeSpan.FromSeconds(10);
        connection.RunInTransaction(() =>
        {
            var tables = connection.QueryScalars<string>(
                "SELECT name FROM sqlite_master " +
                "WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name");
            foreach (var table in tables)
            {
                Append(hash, $"table|{table}");
                var statement = SQLite3.Prepare2(
                    connection.Handle,
                    $"SELECT * FROM \"{table.Replace("\"", "\"\"", StringComparison.Ordinal)}\" ORDER BY rowid");
                try
                {
                    while (SQLite3.Step(statement) == SQLite3.Result.Row)
                    {
                        var columns = SQLite3.ColumnCount(statement);
                        for (var column = 0; column < columns; column++)
                        {
                            Append(hash, SQLite3.ColumnType(statement, column) switch
                            {
                                SQLite3.ColType.Integer => "i" + SQLite3.ColumnInt64(statement, column)
                                    .ToString(CultureInfo.InvariantCulture),
                                SQLite3.ColType.Float => "f" + SQLite3.ColumnDouble(statement, column)
                                    .ToString("R", CultureInfo.InvariantCulture),
                                SQLite3.ColType.Text => "t" + SQLite3.ColumnString(statement, column),
                                SQLite3.ColType.Blob => "b" + Convert.ToBase64String(
                                    SQLite3.ColumnByteArray(statement, column)),
                                _ => "n"
                            });
                        }
                    }
                }
                finally
                {
                    SQLite3.Finalize(statement);
                }
            }
        });
    }

    private static void Append(IncrementalHash hash, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData([0]);
    }

    public async Task<BackupSummary> WriteAsync(
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        var profiles = await repository.GetProfilesAsync(cancellationToken);
        var workDirectory = Path.Combine(
            repository.RootDirectory,
            $".backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDirectory);
        try
        {
            var manifestProfiles = new List<BackupManifestProfile>();
            using var zip = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
            foreach (var profile in profiles.OrderBy(profile => profile.CreatedAt))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var databasePath = repository.GetDatabasePath(profile.Id);
                var hasData = File.Exists(databasePath);
                if (hasData)
                {
                    var snapshotPath = Path.Combine(workDirectory, $"{profile.Id:N}.db3");
                    SnapshotDatabase(databasePath, snapshotPath);
                    zip.CreateEntryFromFile(
                        snapshotPath,
                        DatabaseEntryName(profile.Id),
                        CompressionLevel.Optimal);
                }

                manifestProfiles.Add(new BackupManifestProfile(
                    profile.Id,
                    profile.Name,
                    profile.CreatedAt,
                    profile.LastOpenedAt,
                    hasData));
            }

            var manifest = new BackupManifest(
                FormatVersion,
                clock.UtcNow,
                SqliteCoinFlowStore.CurrentSchemaVersion,
                manifestProfiles);
            var entry = zip.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
            await using (var stream = entry.Open())
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    manifest,
                    BackupJsonContext.Default.BackupManifest,
                    cancellationToken);
            }

            return Summary(manifest);
        }
        finally
        {
            TryDeleteDirectory(workDirectory);
        }
    }

    public async Task<BackupSummary> ReadSummaryAsync(
        Stream source,
        CancellationToken cancellationToken = default)
    {
        using var zip = OpenArchive(source);
        return Summary(await ReadManifestAsync(zip, cancellationToken));
    }

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

    public async Task<BackupState?> GetStateAsync(
        CancellationToken cancellationToken = default)
    {
        var path = StatePath();
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync(
                stream,
                BackupJsonContext.Default.BackupState,
                cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task SaveStateAsync(
        BackupState state,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(repository.RootDirectory);
        var path = StatePath();
        var temporaryPath = path + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                state,
                BackupJsonContext.Default.BackupState,
                cancellationToken);
        }

        File.Move(temporaryPath, path, overwrite: true);
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
            if (schemaVersion > SqliteCoinFlowStore.CurrentSchemaVersion)
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

    private static BackupSummary Summary(BackupManifest manifest) =>
        new(
            manifest.CreatedAt,
            manifest.Profiles
                .Select(profile => new BackupProfile(
                    profile.Id,
                    profile.Name,
                    profile.CreatedAt,
                    profile.LastOpenedAt))
                .ToArray());

    private static string DatabaseEntryName(Guid profileId) =>
        $"profiles/{profileId:N}/{FileSystemProfileRepository.DatabaseFileName}";

    private string StatePath() =>
        Path.Combine(repository.RootDirectory, StateFileName);

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

internal sealed record BackupManifest(
    int Format,
    DateTimeOffset CreatedAt,
    int SchemaVersion,
    IReadOnlyList<BackupManifestProfile> Profiles);

internal sealed record BackupManifestProfile(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastOpenedAt,
    bool HasData);

[JsonSerializable(typeof(BackupManifest))]
[JsonSerializable(typeof(BackupState))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class BackupJsonContext : JsonSerializerContext;
