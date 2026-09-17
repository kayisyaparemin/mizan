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
    private const string AllProfilesOption = "Hepsi";
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

    private async Task WhenLoadedAsync()
    {
        if (IsLoaded)
        {
            return;
        }

        var loaded = new TaskCompletionSource();
        void OnLoaded(object? sender, EventArgs e)
        {
            Loaded -= OnLoaded;
            loaded.TrySetResult();
        }

        Loaded += OnLoaded;
        await Task.WhenAny(loaded.Task, Task.Delay(500));
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
            "Örn. Ev bütçesi",
            _viewModel.MaxNameLength);
        if (name is not null)
        {
            await _viewModel.CreateAndOpenAsync(name);
        }
    }

    /// <summary>İlk kurulum: seçilen yedekteki bütün profiller geri gelir.</summary>
    private async void OnRestoreClicked(object? sender, EventArgs e)
    {
        if (await InspectChosenBackupAsync() is null)
        {
            return;
        }

        if (await _viewModel.RestorePendingAsync() is { } summary)
        {
            await _feedback.ShowSuccessAsync(
                $"{BackupDate(summary)} tarihli yedekten " +
                $"{summary.Profiles.Count} profil geri geldi: {string.Join(", ", summary.ProfileNames)}.",
                "Geri Yüklendi");
        }
    }

    /// <summary>
    /// Profil varken: yedekten seçilen profil eklenir. Telefonda zaten olan
    /// profile dokunulmaz, yedekteki hâli kopya olarak gelir.
    /// </summary>
    private async void OnAddFromBackupClicked(object? sender, EventArgs e)
    {
        if (await InspectChosenBackupAsync() is not { } backup)
        {
            return;
        }

        var (cancelled, profileIds) = await ChooseProfilesAsync(backup);
        if (cancelled)
        {
            _viewModel.DiscardPending();
            return;
        }

        if (await _viewModel.AddPendingAsync(profileIds) is not { } result)
        {
            return;
        }

        var names = string.Join(", ", result.Added.Select(added => added.Profile.Name));
        var message = result.Added.Count == 1
            ? $"{BackupDate(backup)} tarihli yedekten \"{names}\" profili eklendi."
            : $"{BackupDate(backup)} tarihli yedekten {result.Added.Count} profil eklendi: {names}.";
        var copies = result.Added.Count(added => added.IsCopy);
        if (copies == 1 && result.Added.Count == 1)
        {
            message += " Bu profil telefonda zaten vardı; yedekteki hâli ayrı profil olarak eklendi, mevcut profile dokunulmadı.";
        }
        else if (copies > 0)
        {
            message += " Telefonda zaten olan profillerin yedekteki hâli ayrı profil olarak eklendi; mevcut profillere dokunulmadı.";
        }

        await _feedback.ShowSuccessAsync(message, "Yedekten Eklendi");
    }

    /// <summary>
    /// Kullanıcıya yedeği seçtirir (klasördeki yedekler ya da başka bir dosya)
    /// ve içindekileri okur. Vazgeçilirse <c>null</c>.
    /// </summary>
    private async Task<BackupSummary?> InspectChosenBackupAsync()
    {
        if (await _viewModel.FindBackupsAsync() is not { } backups)
        {
            return null;
        }

        if (backups.Count == 0)
        {
            var pickFile = await _feedback.ConfirmAsync(
                "Yedek Bulunamadı",
                $"{_viewModel.BackupLocation} klasöründe Mizan yedeği yok. Yedek dosyan başka bir yerdeyse kendin seçebilirsin.",
                "Dosya Seç",
                "Vazgeç");
            return pickFile ? await _viewModel.InspectAsync(stored: null) : null;
        }

        var labels = backups
            .Select((backup, index) => index == 0
                ? $"{ProfileSelectionViewModel.BackupLabel(backup)} (en yeni)"
                : ProfileSelectionViewModel.BackupLabel(backup))
            .ToList();
        var choice = await _feedback.ChooseAsync(
            "Hangi yedek?",
            "Vazgeç",
            null,
            [.. labels, OtherFileOption]);
        if (choice == OtherFileOption)
        {
            return await _viewModel.InspectAsync(stored: null);
        }

        return choice is not null && labels.IndexOf(choice) is var index and >= 0
            ? await _viewModel.InspectAsync(backups[index])
            : null;
    }

    /// <summary>Yedekte tek profil varsa sormadan o; birden fazlaysa biri ya da hepsi.</summary>
    private async Task<(bool Cancelled, IReadOnlyCollection<Guid>? ProfileIds)> ChooseProfilesAsync(
        BackupSummary backup)
    {
        if (backup.Profiles.Count == 1)
        {
            return (false, [backup.Profiles[0].Id]);
        }

        var labels = backup.Profiles
            .Select(profile => _viewModel.IsOnDevice(profile.Id)
                ? $"{profile.Name} (telefonda var, kopya eklenir)"
                : profile.Name)
            .ToList();
        var choice = await _feedback.ChooseAsync(
            "Hangi profil eklensin?",
            "Vazgeç",
            null,
            [.. labels, AllProfilesOption]);
        if (choice == AllProfilesOption)
        {
            return (false, null);
        }

        return choice is not null && labels.IndexOf(choice) is var index and >= 0
            ? (false, [backup.Profiles[index].Id])
            : (true, null);
    }

    private static string BackupDate(BackupSummary backup) =>
        backup.CreatedAt.ToLocalTime().ToString("d MMMM yyyy HH:mm", TurkishCulture);

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
            // Alan mevcut adla doldurulmaz; boş kaydetmek adı değiştirmez.
            profile.Name,
            _viewModel.MaxNameLength);
        if (!string.IsNullOrWhiteSpace(name))
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
