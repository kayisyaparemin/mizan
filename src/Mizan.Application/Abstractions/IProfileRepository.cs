using Mizan.Application.Models;

namespace Mizan.Application.Abstractions;

/// <summary>
/// Profillerin listesi ve her profilin verisinin fiziksel yeri.
/// </summary>
public interface IProfileRepository
{
    Task<IReadOnlyList<UserProfile>> GetProfilesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Yeni profili oluşturur ya da mevcut profilin bilgisini günceller.</summary>
    Task SaveProfileAsync(
        UserProfile profile,
        CancellationToken cancellationToken = default);

    /// <summary>Profili ve bütün finans verisini kalıcı olarak siler.</summary>
    Task DeleteProfileAsync(
        Guid profileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Profil özelliğinden önceki sürümün tek veritabanı hâlâ duruyor mu.
    /// </summary>
    bool HasLegacyDatabase { get; }

    /// <summary>
    /// Eski tek veritabanını verilen profilin verisi olarak taşır. Veri
    /// kopyalanmaz, dosya yerinden alınır; eski yerde hiçbir şey kalmaz.
    /// </summary>
    Task AdoptLegacyDatabaseAsync(
        UserProfile profile,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Uygulamanın okuyup yazdığı veritabanını açık profile bağlar.
/// </summary>
public interface IProfileStoreSwitch
{
    Task OpenAsync(Guid profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Açık profilin bağlantısını kapatır. Kapalıyken yapılan her veri çağrısı
    /// hata verir; hiçbir çağrı yanlış profile düşmez.
    /// </summary>
    Task CloseAsync();
}
