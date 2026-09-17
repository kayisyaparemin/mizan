namespace Mizan.Application.Models;

/// <summary>
/// Uygulamayı kullanan kişi. Her profilin finans verisi kendi veritabanında
/// durur; profiller arasında hiçbir kayıt paylaşılmaz.
/// </summary>
public sealed record UserProfile
{
    /// <summary>
    /// Profil öncesi sürümden gelen tek veritabanının taşındığı profilin adı.
    /// Adı kaybolmuş (meta dosyası okunamayan) profil de bu adla listelenir.
    /// </summary>
    public const string DefaultName = "Profilim";

    public const int MaxNameLength = 30;

    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = DefaultName;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastOpenedAt { get; init; }
}
