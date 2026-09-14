using CoinFlow.Application.Abstractions;
using CoinFlow.Application.Models;

namespace CoinFlow.Infrastructure.Persistence;

/// <summary>Klasöre yazma izninin platforma özgü kısmı.</summary>
public interface IStorageAccess
{
    bool HasAccess { get; }

    Task<bool> RequestAccessAsync();
}

/// <summary>
/// Yedekleri düz bir klasöre yazar. Android'de bu klasör depolamanın en
/// üstündeki <c>Mizan</c>'dır: uygulama kaldırılınca silinmez ve izin
/// verildiğinde yeniden kurulan uygulama da içindekileri okuyabilir.
/// </summary>
public sealed class FolderBackupStorage(
    string directory,
    string locationDescription,
    IStorageAccess access) : IBackupStorage
{
    public string LocationDescription => locationDescription;

    public bool HasAccess => access.HasAccess;

    public Task<bool> RequestAccessAsync() => access.RequestAccessAsync();

    public async Task SaveAsync(
        string fileName,
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        var target = PathOf(fileName);
        // Önce geçici dosyaya kopyalanır, sonra yerine taşınır: kopyalama
        // yarıda kalırsa aynı günün önceki sağlam yedeği bozulmaz.
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(target)}.tmp");
        try
        {
            await using (var input = File.OpenRead(sourcePath))
            await using (var output = File.Create(temporaryPath))
            {
                await input.CopyToAsync(output, cancellationToken);
            }

            File.Move(temporaryPath, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public Task<IReadOnlyList<StoredBackup>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<StoredBackup> backups = Directory.Exists(directory)
            ? new DirectoryInfo(directory)
                .EnumerateFiles("*.zip")
                .Select(file => new StoredBackup(
                    file.Name,
                    new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero)))
                .ToArray()
            : [];
        return Task.FromResult(backups);
    }

    public Task<Stream> OpenReadAsync(
        string fileName,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream>(File.OpenRead(PathOf(fileName)));

    public Task DeleteAsync(
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var path = PathOf(fileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    // Yalnız dosya adı kullanılır; "../" gibi bir ad klasörün dışına çıkamaz.
    private string PathOf(string fileName) =>
        Path.Combine(directory, Path.GetFileName(fileName));
}
