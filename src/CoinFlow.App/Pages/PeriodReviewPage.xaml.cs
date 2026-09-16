using CoinFlow.App.Services;
using CoinFlow.App.ViewModels;

namespace CoinFlow.App.Pages;

public partial class PeriodReviewPage : ContentPage
{
    private readonly PeriodReviewWizardViewModel _viewModel;
    private readonly IUserFeedbackService _feedback;
    private readonly INavigationService _navigation;

    public PeriodReviewPage(
        PeriodReviewWizardViewModel viewModel,
        IUserFeedbackService feedback,
        INavigationService navigation)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _feedback = feedback;
        _navigation = navigation;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    protected override bool OnBackButtonPressed()
    {
        _ = TryCloseAsync();
        return true;
    }

    private async void OnCloseClicked(object? sender, EventArgs e) =>
        await TryCloseAsync();

    private async Task TryCloseAsync()
    {
        if (!_viewModel.IsSuccess)
        {
            var close = await _feedback.ConfirmAsync(
                "Güncellemeden çıkılsın mı?",
                "Girdiğin bilgiler henüz kaydedilmedi. Çıkmak istiyor musun?",
                "Çık",
                "Devam et");
            if (!close)
            {
                return;
            }
        }

        await _navigation.PopModalAsync();
    }

    private async void OnViewPlanClicked(object? sender, EventArgs e)
    {
        await _navigation.PopModalAsync();
        await _navigation.NavigateToAsync(NavigationRoutes.FutureMonths);
    }

    private async void OnReturnHomeClicked(object? sender, EventArgs e)
    {
        await _navigation.PopModalAsync();
        await _navigation.NavigateToAsync(NavigationRoutes.Dashboard);
    }
}
