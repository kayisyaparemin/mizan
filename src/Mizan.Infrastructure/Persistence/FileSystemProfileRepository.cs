using System.Text.Json;
using System.Text.Json.Serialization;
using Mizan.Application.Abstractions;
using Mizan.Application.Models;

namespace Mizan.Infrastructure.Persistence;

/// <summary>
/// Her profil kendi klasörüdür:
/// <c>profiles/{id}/coinflow.db3</c> finans verisi,
/// <c>profiles/{id}/profile.json</c> adı ve tarihleri.
/// </summary>
/// <remarks>
/// Merkezi bir profil listesi bilinçli olarak tutulmuyor. Liste klasörlerden
/// okunduğu için "listede var ama verisi yok" ya da "verisi var ama listede
/// yok" durumu oluşamaz. Meta dosyası okunamayan ama veritabanı olan klasör
/// <see cref="UserProfile.DefaultName"/> adıyla yine listelenir: veri,
/// bilgisinin kaybolması yüzünden görünmez olmaz.
/// </remarks>
public sealed class FileSystemProfileRepository : IProfileRepository
{
    public const string DatabaseFileName = "coinflow.db3";
    private const string ProfilesDirectoryName = "profiles";
    private const string MetadataFileName = "profile.json";
    private static readonly string[] SqliteSidecarSuffixes =
        ["-journal", "-wal", "-shm"];

    private readonly string _rootDirectory;

    public FileSystemProfileRepository(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException(
                "Uygulama veri klasörü gereklidir.",
                nameof(rootDirectory));
        }

        _rootDirectory = rootDirectory;
    }

    /// <summary>Profil özelliğinden önceki sürümün veritabanı yolu.</summary>
    public string LegacyDatabasePath =>
        Path.Combine(_rootDirectory, DatabaseFileName);

    public bool HasLegacyDatabase => File.Exists(LegacyDatabasePath);

    public string RootDirectory => _rootDirectory;

    public string GetDatabasePath(Guid profileId) =>
        Path.Combine(ProfileDirectory(profileId), DatabaseFileName);

    public string GetProfileDirectory(Guid profileId) =>
        ProfileDirectory(profileId);

    public async Task<IReadOnlyList<UserProfile>> GetProfilesAsync(
        CancellationToken cancellationToken = default)
    {
        var profilesDirectory = ProfilesDirectory();
        if (!Directory.Exists(profilesDirectory))
        {
            return [];
        }

        var profiles = new List<UserProfile>();
        foreach (var directory in Directory.EnumerateDirectories(profilesDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out var id))
            {
                continue;
            }

            var profile = await ReadMetadataAsync(id, directory, cancellationToken);
            if (profile is not null)
            {
                profiles.Add(profile);
            }
        }

        return profiles;
    }

    public async Task SaveProfileAsync(
        UserProfile profile,
        CancellationToken cancellationToken = default)
    {
        var directory = ProfileDirectory(profile.Id);
        Directory.CreateDirectory(directory);

        // Önce geçici dosyaya yaz, sonra yerine taşı: yazma yarıda kesilirse
        // eski meta dosyası bozulmadan kalır.
        var path = Path.Combine(directory, MetadataFileName);
        var temporaryPath = path + ".tmp";
        var metadata = new ProfileMetadata(
            profile.Name,
            profile.CreatedAt,
            profile.LastOpenedAt);
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                metadata,
                ProfileMetadataJsonContext.Default.ProfileMetadata,
                cancellationToken);
        }

        File.Move(temporaryPath, path, overwrite: true);
    }

    public Task DeleteProfileAsync(
        Guid profileId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = ProfileDirectory(profileId);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        return Task.CompletedTask;
    }

    public async Task AdoptLegacyDatabaseAsync(
        UserProfile profile,
        CancellationToken cancellationToken = default)
    {
        if (!HasLegacyDatabase)
        {
            return;
        }

        var target = GetDatabasePath(profile.Id);
        Directory.CreateDirectory(ProfileDirectory(profile.Id));

        // Yarım kalmış işlem günlükleri veritabanıyla birlikte gitmeli; aksi
        // hâlde SQLite taşınan dosyayı tutarsız bulur. Veritabanı en son
        // taşınır: ondan önce kesilirse eski yerinde tam olarak durur.
        foreach (var suffix in SqliteSidecarSuffixes)
        {
            if (File.Exists(LegacyDatabasePath + suffix))
            {
                File.Move(LegacyDatabasePath + suffix, target + suffix, overwrite: true);
            }
        }

        File.Move(LegacyDatabasePath, target);

        // Taşıma ile meta yazımı arasında kesilirse klasör yine listelenir
        // (ReadMetadataAsync), yalnız adı varsayılan olur.
        await SaveProfileAsync(profile, cancellationToken);
    }

    private async Task<UserProfile?> ReadMetadataAsync(
        Guid id,
        string directory,
        CancellationToken cancellationToken)
    {
        var metadataPath = Path.Combine(directory, MetadataFileName);
        if (File.Exists(metadataPath))
        {
            try
            {
                await using var stream = File.OpenRead(metadataPath);
                var metadata = await JsonSerializer.DeserializeAsync(
                    stream,
                    ProfileMetadataJsonContext.Default.ProfileMetadata,
                    cancellationToken);
                if (metadata is not null &&
                    !string.IsNullOrWhiteSpace(metadata.Name))
                {
                    return new UserProfile
                    {
                        Id = id,
                        Name = metadata.Name,
                        CreatedAt = metadata.CreatedAt,
                        LastOpenedAt = metadata.LastOpenedAt
                    };
                }
            }
            catch (JsonException)
            {
                // Bozuk meta dosyası: veritabanı varsa aşağıda kurtarılır.
            }
        }

        if (!File.Exists(Path.Combine(directory, DatabaseFileName)))
        {
            return null;
        }

        return new UserProfile
        {
            Id = id,
            Name = UserProfile.DefaultName,
            CreatedAt = new DateTimeOffset(
                Directory.GetCreationTimeUtc(directory),
                TimeSpan.Zero)
        };
    }

    private string ProfilesDirectory() =>
        Path.Combine(_rootDirectory, ProfilesDirectoryName);

    private string ProfileDirectory(Guid profileId) =>
        Path.Combine(ProfilesDirectory(), profileId.ToString("N"));
}

internal sealed record ProfileMetadata(
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastOpenedAt);

// Kaynak üretimli serileştirici: Android release derlemesinde kırpma
// (trimming) yansımayla okunan tipleri silebilir, bu yol ondan etkilenmez.
[JsonSerializable(typeof(ProfileMetadata))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class ProfileMetadataJsonContext : JsonSerializerContext;
