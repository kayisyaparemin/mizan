using CoinFlow.App.Services;
using CoinFlow.App.ViewModels;
using CoinFlow.Application.Services;

namespace CoinFlow.App.Pages;

public partial class MainPage : ContentPage
{
    // Dönem güncelleme sorusu profil oturumu başına bir kez sorulur. Başka
    // profile geçip geri dönmek yeni oturumdur; soru yeniden gelir.
    private static Guid _reviewPromptSession;
    private bool _onboardingPromptHandled;
    private readonly DashboardViewModel _viewModel;
    private readonly IUserFeedbackService _feedback;
    private readonly IServiceProvider _services;

    public MainPage(
        DashboardViewModel viewModel,
        IUserFeedbackService feedback,
        IServiceProvider services)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _feedback = feedback;
        _services = services;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
        if (_viewModel.ShouldShowOnboarding && !_onboardingPromptHandled)
        {
            _onboardingPromptHandled = true;
            var page = _services.GetRequiredService<OnboardingPage>();
            await Navigation.PushModalAsync(new NavigationPage(page));
            if (await page.Completion)
            {
                await _viewModel.LoadAsync();
            }

            return;
        }

        var session = _services.GetRequiredService<ProfileService>().SessionId;
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
