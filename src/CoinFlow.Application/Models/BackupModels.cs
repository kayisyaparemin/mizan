namespace CoinFlow.Application.Models;

/// <summary>Yedekteki bir profil.</summary>
public sealed record BackupProfile(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastOpenedAt);

/// <summary>Bir yedeğin içinde ne olduğu.</summary>
public sealed record BackupSummary(
    DateTimeOffset CreatedAt,
    IReadOnlyList<BackupProfile> Profiles)
{
    public IReadOnlyList<string> ProfileNames =>
        Profiles.Select(profile => profile.Name).ToArray();
}

/// <summary>
/// Yedekteki <paramref name="SourceId"/> profili telefona
/// <paramref name="Target"/> olarak açılır (kimlik ve ad farklı olabilir).
/// </summary>
public sealed record ProfileImport(Guid SourceId, UserProfile Target);

/// <summary>Yedekten eklenen bir profil.</summary>
/// <param name="IsCopy">
/// Aynı profil telefonda zaten vardı; yedekteki hâli ayrı bir profil olarak eklendi.
/// </param>
public sealed record ImportedProfile(UserProfile Profile, bool IsCopy);

public sealed record BackupImportResult(
    BackupSummary Backup,
    IReadOnlyList<ImportedProfile> Added);

/// <summary>Bu kurulumun aldığı son yedek.</summary>
public sealed record BackupState(
    DateTimeOffset BackedUpAt,
    string FileName,
    string Fingerprint);

/// <summary>Yedek deposundaki bir dosya.</summary>
public sealed record StoredBackup(
    string FileName,
    DateTimeOffset ModifiedAt);

public enum BackupOutcome
{
    Created,
    /// <summary>Son yedekten beri hiçbir profilde değişiklik yok.</summary>
    Unchanged,
    /// <summary>Yedeklenecek profil yok.</summary>
    NothingToBackUp,
    /// <summary>Yedek klasörüne erişim izni verilmemiş.</summary>
    NoAccess
}

public sealed record BackupResult(
    BackupOutcome Outcome,
    BackupState? State = null);
