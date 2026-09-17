namespace Mizan.App.Services;

public interface IUserFeedbackService
{
    Task ShowSuccessAsync(
        string message,
        string title = "Kaydedildi",
        string button = "Tamam");

    Task ShowErrorAsync(
        string message,
        string title = "Kaydedilemedi",
        string button = "Tamam");

    Task<bool> ConfirmAsync(
        string title,
        string message,
        string accept,
        string cancel);

    /// <summary>Tek satır metin ister; vazgeçilirse <c>null</c> döner.</summary>
    Task<string?> PromptAsync(
        string title,
        string message,
        string accept,
        string cancel,
        string placeholder = "",
        int maxLength = -1);

    /// <summary>Seçenek listesi gösterir; seçilen metni ya da <c>null</c> döner.</summary>
    Task<string?> ChooseAsync(
        string title,
        string cancel,
        string? destruction,
        params string[] options);
}
