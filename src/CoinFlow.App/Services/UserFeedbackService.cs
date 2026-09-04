using Microsoft.Maui.Controls;

namespace CoinFlow.App.Services;

public sealed class UserFeedbackService : IUserFeedbackService
{
    public Task ShowSuccessAsync(
        string message,
        string title = "Kaydedildi",
        string button = "Tamam") =>
        ShowAlertAsync(title, message, button);

    public Task ShowErrorAsync(
        string message,
        string title = "Kaydedilemedi",
        string button = "Tamam") =>
        ShowAlertAsync(title, message, button);

    public Task<bool> ConfirmAsync(
        string title,
        string message,
        string accept,
        string cancel) =>
        CurrentPage().DisplayAlert(title, message, accept, cancel);

    private static Task ShowAlertAsync(
        string title,
        string message,
        string button) =>
        CurrentPage().DisplayAlert(title, message, button);

    private static Page CurrentPage()
    {
        Page? page = null;
        if (Shell.Current?.CurrentPage is { } shellPage)
        {
            page = shellPage;
        }
        else if (Microsoft.Maui.Controls.Application.Current?.Windows
                     .FirstOrDefault()?.Page is { } windowPage)
        {
            page = windowPage;
        }

        return page is null
            ? throw new InvalidOperationException("Geçerli ekran bulunamadı.")
            : ResolveTopPage(page);
    }

    private static Page ResolveTopPage(Page page)
    {
        // ModalStack is shared across the navigation context, so its top entry
        // is the visible modal regardless of which page we ask for it. Read it
        // once instead of looping: re-selecting the same top modal in a while
        // loop never terminates and pins the UI thread at 100% CPU (ANR).
        var top = page.Navigation.ModalStack.LastOrDefault() ?? page;
        return DescendToVisiblePage(top);
    }

    private static Page DescendToVisiblePage(Page page) => page switch
    {
        NavigationPage { CurrentPage: { } current } =>
            DescendToVisiblePage(current),
        FlyoutPage { Detail: { } detail } =>
            DescendToVisiblePage(detail),
        TabbedPage { CurrentPage: { } current } =>
            DescendToVisiblePage(current),
        _ => page
    };
}
