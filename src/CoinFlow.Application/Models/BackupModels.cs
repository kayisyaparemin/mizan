namespace CoinFlow.Application.Models;

/// <summary>Bir yedeğin içinde ne olduğu.</summary>
public sealed record BackupSummary(
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> ProfileNames);

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
