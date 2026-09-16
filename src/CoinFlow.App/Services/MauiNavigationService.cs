using CoinFlow.App.Pages;
using CoinFlow.App.ViewModels;
using CoinFlow.Application.Models;

namespace CoinFlow.App.Services;

public sealed class MauiNavigationService(IServiceProvider services) : INavigationService
{
    public Task NavigateToAsync(string route, IDictionary<string, object>? parameters = null)
    {
        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (Shell.Current is null)
            {
                return Task.CompletedTask;
            }

            return parameters is not null
                ? Shell.Current.GoToAsync(route, parameters)
                : Shell.Current.GoToAsync(route);
        });
    }

    public Task NavigateBackAsync()
    {
        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (Shell.Current is null)
            {
                return Task.CompletedTask;
            }

            return Shell.Current.GoToAsync("..");
        });
    }

    public async Task<bool> OpenOnboardingModalAsync()
    {
        return await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var page = services.GetRequiredService<OnboardingPage>();
            if (Shell.Current is null)
            {
                return false;
            }

            await Shell.Current.Navigation.PushModalAsync(new NavigationPage(page));
            return await page.Completion;
        });
    }

    public Task OpenPeriodReviewModalAsync()
    {
        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var page = services.GetRequiredService<PeriodReviewPage>();
            if (Shell.Current is null)
            {
                return;
            }

            await Shell.Current.Navigation.PushModalAsync(new NavigationPage(page));
        });
    }

    public Task OpenHistoryDetailModalAsync(Guid actualId)
    {
        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var page = services.GetRequiredService<HistoryDetailPage>();
            await page.LoadAsync(actualId);
            if (Shell.Current is null)
            {
                return;
            }

            await Shell.Current.Navigation.PushModalAsync(new NavigationPage(page));
        });
    }

    public async Task<bool> OpenInitialStrategyModalAsync(
        InitialPaymentStrategySetup setup,
        CommitmentsViewModel viewModel)
    {
        return await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var page = new InitialStrategyPage(setup, viewModel);
            if (Shell.Current is null)
            {
                return false;
            }

            await Shell.Current.Navigation.PushModalAsync(page);
            await page.Completion;
            return true;
        });
    }

    public Task OpenStrategyChangeModalAsync(SettingsViewModel viewModel)
    {
        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var feedback = services.GetRequiredService<IUserFeedbackService>();
            var page = new StrategyChangePage(viewModel, feedback);
            if (Shell.Current is null)
            {
                return;
            }

            await Shell.Current.Navigation.PushModalAsync(new NavigationPage(page));
        });
    }

    public Task PopModalAsync()
    {
        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (Shell.Current?.Navigation?.ModalStack?.Count > 0)
            {
                await Shell.Current.Navigation.PopModalAsync();
            }
        });
    }
}
