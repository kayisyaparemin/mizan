using Mizan.App.Pages;
using Mizan.App.ViewModels;
using Mizan.Application.Models;

namespace Mizan.App.Services;

public sealed class MauiNavigationService(IServiceProvider services) : INavigationService
{
    public Task NavigateToAsync(string route, IDictionary<string, object>? parameters = null)
    {
        if (MainThread.IsMainThread)
        {
            if (Shell.Current is null)
            {
                return Task.CompletedTask;
            }

            return parameters is not null
                ? Shell.Current.GoToAsync(route, parameters)
                : Shell.Current.GoToAsync(route);
        }

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
        if (MainThread.IsMainThread)
        {
            if (Shell.Current is null)
            {
                return Task.CompletedTask;
            }

            return Shell.Current.GoToAsync("..");
        }

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
        var page = services.GetRequiredService<OnboardingPage>();
        if (MainThread.IsMainThread)
        {
            if (Shell.Current is null)
            {
                return false;
            }

            await Shell.Current.Navigation.PushModalAsync(new NavigationPage(page));
        }
        else
        {
            var pushed = await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (Shell.Current is null)
                {
                    return false;
                }

                await Shell.Current.Navigation.PushModalAsync(new NavigationPage(page));
                return true;
            });

            if (!pushed)
            {
                return false;
            }
        }

        return await page.Completion;
    }

    public Task OpenPeriodReviewModalAsync()
    {
        if (MainThread.IsMainThread)
        {
            var page = services.GetRequiredService<PeriodReviewPage>();
            return Shell.Current is null
                ? Task.CompletedTask
                : Shell.Current.Navigation.PushModalAsync(new NavigationPage(page));
        }

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
        if (MainThread.IsMainThread)
        {
            return OpenHistoryDetailModalCoreAsync(actualId);
        }

        return MainThread.InvokeOnMainThreadAsync(() => OpenHistoryDetailModalCoreAsync(actualId));
    }

    private async Task OpenHistoryDetailModalCoreAsync(Guid actualId)
    {
        var page = services.GetRequiredService<HistoryDetailPage>();
        await page.LoadAsync(actualId);
        if (Shell.Current is not null)
        {
            await Shell.Current.Navigation.PushModalAsync(new NavigationPage(page));
        }
    }

    public async Task<bool> OpenInitialStrategyModalAsync(
        InitialPaymentStrategySetup setup,
        CommitmentsViewModel viewModel)
    {
        var page = new InitialStrategyPage(setup, viewModel);
        if (MainThread.IsMainThread)
        {
            if (Shell.Current is null)
            {
                return false;
            }

            await Shell.Current.Navigation.PushModalAsync(page);
        }
        else
        {
            var pushed = await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (Shell.Current is null)
                {
                    return false;
                }

                await Shell.Current.Navigation.PushModalAsync(page);
                return true;
            });

            if (!pushed)
            {
                return false;
            }
        }

        await page.Completion;
        return true;
    }

    public Task OpenStrategyChangeModalAsync(SettingsViewModel viewModel)
    {
        if (MainThread.IsMainThread)
        {
            var feedback = services.GetRequiredService<IUserFeedbackService>();
            var page = new StrategyChangePage(viewModel, feedback);
            return Shell.Current is null
                ? Task.CompletedTask
                : Shell.Current.Navigation.PushModalAsync(new NavigationPage(page));
        }

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
        if (MainThread.IsMainThread)
        {
            return Shell.Current?.Navigation?.ModalStack?.Count > 0
                ? Shell.Current.Navigation.PopModalAsync()
                : Task.CompletedTask;
        }

        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (Shell.Current?.Navigation?.ModalStack?.Count > 0)
            {
                await Shell.Current.Navigation.PopModalAsync();
            }
        });
    }
}
