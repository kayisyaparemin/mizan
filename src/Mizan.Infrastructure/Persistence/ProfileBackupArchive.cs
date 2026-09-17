using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mizan.Application.Abstractions;
using Mizan.Application.Models;

namespace Mizan.Infrastructure.Persistence;

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
public sealed partial class ProfileBackupArchive(
    FileSystemProfileRepository repository,
    IClock clock) : IProfileBackupArchive
{
    public const int FormatVersion = 1;
    public const string ManifestEntryName = "mizan-backup.json";
    private const string StateFileName = "backup-state.json";
    private const long MaxDatabaseBytes = 512L * 1024 * 1024;

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
                SqliteMizanStore.CurrentSchemaVersion,
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
