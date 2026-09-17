using Mizan.App.Services;
using Mizan.App.ViewModels;
using Mizan.Application.Services;

namespace Mizan.App.Pages;

public partial class MainPage : ContentPage
{
    // Dönem güncelleme sorusu profil oturumu başına bir kez sorulur. Başka
    // profile geçip geri dönmek yeni oturumdur; soru yeniden gelir.
    private static Guid _reviewPromptSession;
    private bool _onboardingPromptHandled;
    private readonly DashboardViewModel _viewModel;
    private readonly IUserFeedbackService _feedback;
    private readonly INavigationService _navigation;
    private readonly ProfileService _profiles;

    public MainPage(
        DashboardViewModel viewModel,
        IUserFeedbackService feedback,
        INavigationService navigation,
        ProfileService profiles)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _feedback = feedback;
        _navigation = navigation;
        _profiles = profiles;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
        if (_viewModel.ShouldShowOnboarding && !_onboardingPromptHandled)
        {
            _onboardingPromptHandled = true;
            if (await _navigation.OpenOnboardingModalAsync())
            {
                await _viewModel.LoadAsync();
            }

            return;
        }

        var session = _profiles.SessionId;
        if (_viewModel.HasPendingReview && _reviewPromptSession != session)
        {
            _reviewPromptSession = session;
            var start = await _feedback.ConfirmAsync(
                "Geçen dönemi güncelleyelim mi?",
                "Bu dönem için bir plan oluşturmuştuk. Ödemelerin ve dönem harcamaların netleştiyse gerçekte ne olduğunu kaydedebiliriz.",
                "Hadi Kaydedelim",
                "Daha Sonra");
            if (start)
            {
                await _viewModel.OpenPeriodReviewAsync();
            }
        }
    }
}
