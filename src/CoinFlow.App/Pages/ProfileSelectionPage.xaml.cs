using CoinFlow.App.Models;
using CoinFlow.App.Services;
using CoinFlow.App.ViewModels;

namespace CoinFlow.App.Pages;

public partial class ProfileSelectionPage : ContentPage
{
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
