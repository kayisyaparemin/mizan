using CoinFlow.App.Models;
using CoinFlow.App.ViewModels;

namespace CoinFlow.App.Pages;

public partial class OnboardingPage : ContentPage
{
    private readonly TaskCompletionSource<bool> _completion = new();
    private readonly OnboardingViewModel _viewModel;
    private readonly INavigationService _navigation;
    private bool _isClosing;

    public OnboardingPage(
        OnboardingViewModel viewModel,
        INavigationService navigation)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _navigation = navigation;
        _viewModel.Completed += OnCompleted;
    }

    public Task<bool> Completion => _completion.Task;

    private async void OnCompleted(bool completed)
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        _completion.TrySetResult(completed);
        _viewModel.Completed -= OnCompleted;
        if (Navigation.ModalStack.Count > 0)
        {
            await _navigation.PopModalAsync();
        }
        else
        {
            await _navigation.NavigateToAsync(NavigationRoutes.Dashboard);
        }
    }

    private void OnSkipClicked(object? sender, EventArgs e) =>
        _viewModel.Dismiss();

    private void OnRemoveDraftLineClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: FinancialRecordLine line })
        {
            _viewModel.RemoveDraftLine(line);
        }
    }

    protected override bool OnBackButtonPressed()
    {
        _viewModel.Dismiss();
        return true;
    }
}
