using CoinFlow.Application.Models;

namespace CoinFlow.Application.Abstractions;

/// <summary>
/// Bütün profilleri tek bir yedek dosyasına yazar ve geri açar. Uygulama
/// verisinin fiziksel düzenini bilen tek yer burasıdır.
/// </summary>
public interface IProfileBackupArchive
{
    /// <summary>
    /// Profillerin verisi değişmediyse aynı kalan kısa bir özet. Gece yedeği
    /// bununla "değişiklik yoksa yeni dosya yazma" kararını verir.
    /// </summary>
    Task<string> ComputeFingerprintAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Profilleri yedek biçiminde <paramref name="destination"/>'a yazar.</summary>
    Task<BackupSummary> WriteAsync(
        Stream destination,
        CancellationToken cancellationToken = default);

    /// <summary>Yedeğin içindeki profilleri okur; dosya Mizan yedeği değilse hata verir.</summary>
    Task<BackupSummary> ReadSummaryAsync(
        Stream source,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Seçilen profilleri doğrular ve verilen kimlik/adla telefona açar.
    /// Hepsi doğrulanmadan hiçbiri eklenmez; hedef kimlik zaten varsa hata
    /// verir, mevcut hiçbir profile dokunmaz.
    /// </summary>
    Task ImportAsync(
        Stream source,
        IReadOnlyList<ProfileImport> imports,
        CancellationToken cancellationToken = default);

    Task<BackupState?> GetStateAsync(
        CancellationToken cancellationToken = default);

    Task SaveStateAsync(
        BackupState state,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Yedek dosyalarının uygulama dışındaki yeri. Uygulama kaldırılınca
/// uygulamanın kendi klasörü silinir; yedek orada durursa onunla gider.
/// </summary>
public interface IBackupStorage
{
    /// <summary>Kullanıcıya gösterilen yer, örn. "Dahili depolama › Mizan".</summary>
    string LocationDescription { get; }

    /// <summary>Yedek klasörüne yazma izni verilmiş mi.</summary>
    bool HasAccess { get; }

    /// <summary>İzni kullanıcıdan ister; sonunda izin verilmiş mi döner.</summary>
    Task<bool> RequestAccessAsync();

    /// <summary>
    /// Hazır yedek dosyasını depoya kopyalar; aynı adlı dosyanın üzerine
    /// yazar.
    /// </summary>
    Task SaveAsync(
        string fileName,
        string sourcePath,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoredBackup>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(
        string fileName,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string fileName,
        CancellationToken cancellationToken = default);
}
