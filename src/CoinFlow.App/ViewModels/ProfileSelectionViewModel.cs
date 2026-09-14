using System.Collections.ObjectModel;
using System.Globalization;
using CoinFlow.App.Models;
using CoinFlow.App.Services;
using CoinFlow.Application.Models;
using CoinFlow.Application.Services;

namespace CoinFlow.App.ViewModels;

public sealed class ProfileSelectionViewModel(
    ProfileService profiles,
    ProfileNavigator navigator) : ViewModelBase
{
    public ObservableCollection<ProfileCardItem> Profiles { get; } = [];

    public int MaxNameLength => UserProfile.MaxNameLength;

    /// <summary>Son kalan profil silinemez; seçenek hiç gösterilmez.</summary>
    public bool CanDeleteProfiles => Profiles.Count > 1;

    public Task LoadAsync() => RunAsync(ReloadAsync);

    public Task OpenAsync(ProfileCardItem profile) =>
        RunAsync(() => navigator.OpenAsync(profile.Id));

    /// <summary>
    /// Yeni profil oluşturulur oluşturulmaz açılır: boş profil ilk kurulumla
    /// başlar, tıpkı uygulamanın yeni yüklendiği gibi.
    /// </summary>
    public Task CreateAndOpenAsync(string name) =>
        RunAsync(async () =>
        {
            var profile = await profiles.CreateAsync(name);
            await navigator.OpenAsync(profile.Id);
        });

    public Task RenameAsync(ProfileCardItem profile, string name) =>
        RunAsync(async () =>
        {
            await profiles.RenameAsync(profile.Id, name);
            await ReloadAsync();
        });

    public Task DeleteAsync(ProfileCardItem profile) =>
        RunAsync(async () =>
        {
            await profiles.DeleteAsync(profile.Id);
            await ReloadAsync();
        });

    private async Task ReloadAsync()
    {
        var list = await profiles.GetProfilesAsync();
        Profiles.Clear();
        foreach (var item in list)
        {
            Profiles.Add(ToCard(item));
        }

        OnPropertyChanged(nameof(CanDeleteProfiles));
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            SetStatus(string.Empty);
            await action();
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static ProfileCardItem ToCard(UserProfile profile) =>
        new(
            profile.Id,
            profile.Name,
            StringInfo.GetNextTextElement(profile.Name).ToUpper(TurkishCulture),
            profile.LastOpenedAt is { } openedAt
                ? $"Son açılış {openedAt.ToLocalTime().ToString("d MMMM yyyy", TurkishCulture)}"
                : "Henüz açılmadı");
}
