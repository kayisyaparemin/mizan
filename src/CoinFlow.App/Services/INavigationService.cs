using CoinFlow.Application.Models;
using CoinFlow.App.ViewModels;

namespace CoinFlow.App.Services;

public interface INavigationService
{
    Task NavigateToAsync(string route, IDictionary<string, object>? parameters = null);
    Task NavigateBackAsync();
    Task<bool> OpenOnboardingModalAsync();
    Task OpenPeriodReviewModalAsync();
    Task OpenHistoryDetailModalAsync(Guid actualId);
    Task<bool> OpenInitialStrategyModalAsync(InitialPaymentStrategySetup setup, CommitmentsViewModel viewModel);
    Task OpenStrategyChangeModalAsync(SettingsViewModel viewModel);
    Task PopModalAsync();
}
