using System.Globalization;
using CoinFlow.Application.Abstractions;
using CoinFlow.Application.Models;

namespace CoinFlow.Application.Services;

/// <summary>
/// Profil kuralları: ad, açma, silme. Veri izolasyonu burada değil,
/// her profilin ayrı veritabanında sağlanır; bu servis yalnız hangi
/// veritabanının açık olduğunu yönetir.
/// </summary>
public sealed class ProfileService(
    IProfileRepository repository,
    IProfileStoreSwitch storeSwitch,
    IClock clock)
{
    private static readonly CultureInfo TurkishCulture =
        CultureInfo.GetCultureInfo("tr-TR");
    private readonly SemaphoreSlim _lock = new(1, 1);

    public UserProfile? ActiveProfile { get; private set; }

    /// <summary>
    /// Her profil açılışında yenilenir. Ekranlar "bu oturumda bir kez sor"
    /// türü kararlarını buna bağlar; aynı profile geri dönmek yeni oturumdur.
    /// </summary>
    public Guid SessionId { get; private set; }

    /// <summary>
    /// Profilleri oluşturulma sırasıyla döner. Profil öncesi sürümden kalan
    /// veritabanı varsa önce onu <see cref="UserProfile.DefaultName"/>
    /// profiline taşır; kullanıcının verisi ilk açılışta kaybolmaz.
    /// </summary>
    public async Task<IReadOnlyList<UserProfile>> GetProfilesAsync(
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (repository.HasLegacyDatabase)
            {
                var existing = await repository.GetProfilesAsync(
                    cancellationToken);
                await repository.AdoptLegacyDatabaseAsync(
                    new UserProfile
                    {
                        Name = UniqueName(UserProfile.DefaultName, existing),
                        CreatedAt = clock.UtcNow
                    },
                    cancellationToken);
            }

            return Ordered(await repository.GetProfilesAsync(cancellationToken));
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<UserProfile> CreateAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var profiles = await repository.GetProfilesAsync(cancellationToken);
            var profile = new UserProfile
            {
                Name = ValidateName(name, profiles, exceptId: null),
                CreatedAt = clock.UtcNow
            };
            await repository.SaveProfileAsync(profile, cancellationToken);
            return profile;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<UserProfile> RenameAsync(
        Guid profileId,
        string name,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var profiles = await repository.GetProfilesAsync(cancellationToken);
            var profile = Find(profiles, profileId) with
            {
                Name = ValidateName(name, profiles, profileId)
            };
            await repository.SaveProfileAsync(profile, cancellationToken);
            if (ActiveProfile?.Id == profileId)
            {
                ActiveProfile = profile;
            }

            return profile;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DeleteAsync(
        Guid profileId,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var profiles = await repository.GetProfilesAsync(cancellationToken);
            Find(profiles, profileId);
            if (ActiveProfile?.Id == profileId)
            {
                // Açık bağlantının altından dosya silinmez; silme yalnız
                // seçim ekranından, hiçbir profil açık değilken yapılır.
                throw new InvalidOperationException(
                    "Açık olan profil silinemez. Önce profil seçim ekranına dön.");
            }

            if (profiles.Count == 1)
            {
                throw new InvalidOperationException(
                    "Son kalan profil silinemez.");
            }

            await repository.DeleteProfileAsync(profileId, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<UserProfile> OpenAsync(
        Guid profileId,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var profiles = await repository.GetProfilesAsync(cancellationToken);
            var profile = Find(profiles, profileId) with
            {
                LastOpenedAt = clock.UtcNow
            };

            ActiveProfile = null;
            await storeSwitch.OpenAsync(profileId, cancellationToken);
            await repository.SaveProfileAsync(profile, cancellationToken);
            ActiveProfile = profile;
            SessionId = Guid.NewGuid();
            return profile;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task CloseAsync()
    {
        await _lock.WaitAsync();
        try
        {
            ActiveProfile = null;
            await storeSwitch.CloseAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    private static IReadOnlyList<UserProfile> Ordered(
        IEnumerable<UserProfile> profiles) =>
        profiles
            .OrderBy(profile => profile.CreatedAt)
            .ThenBy(profile => profile.Id)
            .ToArray();

    private static UserProfile Find(
        IEnumerable<UserProfile> profiles,
        Guid profileId) =>
        profiles.FirstOrDefault(profile => profile.Id == profileId) ??
        throw new InvalidOperationException("Profil bulunamadı.");

    private static string ValidateName(
        string? name,
        IEnumerable<UserProfile> profiles,
        Guid? exceptId)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new InvalidOperationException("Profil adı boş olamaz.");
        }

        if (trimmed.Length > UserProfile.MaxNameLength)
        {
            throw new InvalidOperationException(
                $"Profil adı en fazla {UserProfile.MaxNameLength} karakter olabilir.");
        }

        if (profiles.Any(profile =>
                profile.Id != exceptId &&
                SameName(profile.Name, trimmed)))
        {
            throw new InvalidOperationException(
                "Bu adla bir profil zaten var.");
        }

        return trimmed;
    }

    private static string UniqueName(
        string name,
        IReadOnlyList<UserProfile> profiles)
    {
        var candidate = name;
        for (var suffix = 2;
             profiles.Any(profile => SameName(profile.Name, candidate));
             suffix++)
        {
            candidate = $"{name} {suffix}";
        }

        return candidate;
    }

    // "İpek" ile "ipek" aynı ad sayılır; Türkçe büyük/küçük harf kuralıyla.
    private static bool SameName(string left, string right) =>
        string.Compare(
            left,
            right,
            TurkishCulture,
            CompareOptions.IgnoreCase) == 0;
}
