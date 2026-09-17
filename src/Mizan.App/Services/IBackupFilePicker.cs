namespace Mizan.App.Services;

public interface IBackupFilePicker
{
    /// <summary>
    /// Kullanıcıya yedek dosyasını seçtirir ve okumak için açar; vazgeçerse
    /// <c>null</c> döner.
    /// </summary>
    Task<Stream?> PickAndOpenAsync();
}
