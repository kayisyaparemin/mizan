using System.Globalization;
using CoinFlow.App.Models;
using CoinFlow.App.Services;
using CoinFlow.App.ViewModels;
using CoinFlow.Application.Models;

namespace CoinFlow.App.Pages;

public partial class ProfileSelectionPage : ContentPage
{
    private static readonly CultureInfo TurkishCulture = CultureInfo.GetCultureInfo("tr-TR");
    private const string OtherFileOption = "Başka bir dosya seç…";
    private const string RenameOption = "Adını Değiştir";
    private const string DeleteOption = "Sil";
    private readonly ProfileSelectionViewModel _viewModel;
    private readonly IUserFeedbackService _feedback;

    public ProfileSelectionPage(
        ProfileSelectionViewModel viewModel,
        IUserFeedbackService feedback)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _feedback = feedback;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
        if (!_viewModel.ShouldOfferBackupAccess())
        {
            return;
        }

        // Uygulamanın ilk sayfası pencereye bağlanmadan gösterilen uyarı
        // Android'de sessizce düşüyor; emülatörde soru hiç çıkmadı.
        await WhenLoadedAsync();
        var allow = await _feedback.ConfirmAsync(
            "Yedekleme İzni",
            $"Mizan her gece bütün profillerini {_viewModel.BackupLocation} klasörüne yedekler. " +
            "Böylece uygulamayı kaldırıp yeniden kursan da verilerin kaybolmaz. " +
            "Bunun için açılan ekranda \"Tüm dosyalara erişim\" iznini açman gerekiyor.",
            "İzin Ver",
            "Şimdi Değil");
        _viewModel.MarkBackupAccessOffered();
        if (allow)
        {
            await _viewModel.RequestBackupAccessAsync();
        }
    }

    private Task WhenLoadedAsync()
    {
        if (IsLoaded)
        {
            return Task.CompletedTask;
        }

        var loaded = new TaskCompletionSource();
        void OnLoaded(object? sender, EventArgs e)
        {
            Loaded -= OnLoaded;
            loaded.TrySetResult();
        }

        Loaded += OnLoaded;
        return loaded.Task;
    }

    private async void OnProfileTapped(object? sender, TappedEventArgs e)
    {
        if (ProfileOf(sender) is { } profile)
        {
            await _viewModel.OpenAsync(profile);
        }
    }

    private async void OnCreateClicked(object? sender, EventArgs e)
    {
        var name = await _feedback.PromptAsync(
            "Yeni Profil",
            "Profilin adı ne olsun? Yeni profil boş başlar; ilk kurulumla devam edersin.",
            "Oluştur",
            "Vazgeç",
            maxLength: _viewModel.MaxNameLength);
        if (name is not null)
        {
            await _viewModel.CreateAndOpenAsync(name);
        }
    }

    private async void OnRestoreClicked(object? sender, EventArgs e)
    {
        if (await _viewModel.FindBackupsAsync() is not { } backups)
        {
            return;
        }

        BackupSummary? summary;
        if (backups.Count == 0)
        {
            var pickFile = await _feedback.ConfirmAsync(
                "Yedek Bulunamadı",
                $"{_viewModel.BackupLocation} klasöründe Mizan yedeği yok. Yedek dosyan başka bir yerdeyse kendin seçebilirsin.",
                "Dosya Seç",
                "Vazgeç");
            summary = pickFile
                ? await _viewModel.RestoreFromPickedFileAsync()
                : null;
        }
        else
        {
            var labels = backups
                .Select((backup, index) => index == 0
                    ? $"{ProfileSelectionViewModel.BackupLabel(backup)} (en yeni)"
                    : ProfileSelectionViewModel.BackupLabel(backup))
                .ToList();
            var choice = await _feedback.ChooseAsync(
                "Hangi yedekten dönülsün?",
                "Vazgeç",
                null,
                [.. labels, OtherFileOption]);
            if (choice == OtherFileOption)
            {
                summary = await _viewModel.RestoreFromPickedFileAsync();
            }
            else if (choice is not null && labels.IndexOf(choice) is var index and >= 0)
            {
                summary = await _viewModel.RestoreAsync(backups[index]);
            }
            else
            {
                summary = null;
            }
        }

        if (summary is not null)
        {
            await _feedback.ShowSuccessAsync(
                $"{summary.CreatedAt.ToLocalTime().ToString("d MMMM yyyy HH:mm", TurkishCulture)} tarihli yedekten " +
                $"{summary.ProfileNames.Count} profil geri geldi: {string.Join(", ", summary.ProfileNames)}.",
                "Geri Yüklendi");
        }
    }

    private async void OnProfileOptionsClicked(object? sender, EventArgs e)
    {
        if (ProfileOf(sender) is not { } profile)
        {
            return;
        }

        var choice = await _feedback.ChooseAsync(
            profile.Name,
            "Vazgeç",
            _viewModel.CanDeleteProfiles ? DeleteOption : null,
            RenameOption);
        if (choice == RenameOption)
        {
            await RenameAsync(profile);
        }
        else if (choice == DeleteOption)
        {
            await DeleteAsync(profile);
        }
    }

    private async Task RenameAsync(ProfileCardItem profile)
    {
        var name = await _feedback.PromptAsync(
            "Adını Değiştir",
            "Profilin yeni adı:",
            "Kaydet",
            "Vazgeç",
            profile.Name,
            _viewModel.MaxNameLength);
        if (name is not null)
        {
            await _viewModel.RenameAsync(profile, name);
        }
    }

    private async Task DeleteAsync(ProfileCardItem profile)
    {
        var confirmed = await _feedback.ConfirmAsync(
            "Profili Sil",
            $"\"{profile.Name}\" profili ve içindeki bütün finans verileri kalıcı olarak silinecek. Bu işlem geri alınamaz.",
            "Profili Sil",
            "Vazgeç");
        if (confirmed)
        {
            await _viewModel.DeleteAsync(profile);
        }
    }

    private static ProfileCardItem? ProfileOf(object? sender) =>
        (sender as BindableObject)?.BindingContext as ProfileCardItem;
}
