using CoinFlow.App.Pages;
using CoinFlow.App.Services;
using CoinFlow.Application.Services;
using Microsoft.Maui.Controls.Shapes;

namespace CoinFlow.App;

public sealed class AppShell : Shell
{
    public const string PeriodDetailRoute = "salary-period-detail";
    public const string OnboardingRoute = "onboarding";
    public const string CardControlRoute = "card-control";

    public AppShell(IServiceProvider services)
    {
        Routing.RegisterRoute(
            PeriodDetailRoute,
            typeof(SalaryPeriodDetailPage));
        Routing.RegisterRoute(
            OnboardingRoute,
            typeof(OnboardingPage));
        Routing.RegisterRoute(
            CardControlRoute,
            typeof(CardControlPage));
        FlyoutBehavior = FlyoutBehavior.Flyout;
        Shell.SetNavBarIsVisible(this, true);

        Items.Add(CreateFlyoutItem(
            "Ana Sayfa",
            "dashboard",
            "dashboard-content",
            () => services.GetRequiredService<MainPage>()));
        Items.Add(CreateFlyoutItem(
            "12 Dönem",
            "projection",
            "future-months-content",
            () => services.GetRequiredService<FutureMonthsPage>()));
        Items.Add(CreateFlyoutItem(
            "Simülatör",
            "simulation",
            "simulation-content",
            () => services.GetRequiredService<SimulationPage>()));
        Items.Add(CreateFlyoutItem(
            "Finansal Yapı",
            "commitments",
            "commitments-content",
            () => services.GetRequiredService<CommitmentsPage>()));
        Items.Add(CreateFlyoutItem(
            "Geçmiş",
            "history",
            "history-content",
            () => services.GetRequiredService<HistoryPage>()));
        Items.Add(CreateFlyoutItem(
            "Ayarlar",
            "settings",
            "settings-content",
            () => services.GetRequiredService<SettingsPage>()));

        FlyoutHeader = CreateProfileHeader(
            services.GetRequiredService<ProfileService>().ActiveProfile?.Name ??
            string.Empty);
        Items.Add(new MenuItem
        {
            Text = "Profil Değiştir",
            Command = new Command(async () =>
                await services.GetRequiredService<ProfileNavigator>()
                    .SwitchProfileAsync())
        });
    }

    private static View CreateProfileHeader(string profileName)
    {
        var resources = Microsoft.Maui.Controls.Application.Current?.Resources;
        return new Border
        {
            BackgroundColor = Resource<Color>(resources, "FlyoutHeaderSurface"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 0 },
            Padding = new Thickness(20, 28, 20, 18),
            Content = new VerticalStackLayout
            {
                Spacing = 2,
                Children =
                {
                    new Label
                    {
                        Text = "PROFİL",
                        Style = Resource<Style>(resources, "Eyebrow")
                    },
                    new Label
                    {
                        Text = profileName,
                        Style = Resource<Style>(resources, "SectionTitle")
                    }
                }
            }
        };
    }

    private static T? Resource<T>(ResourceDictionary? resources, string key)
        where T : class =>
        resources is not null && resources.TryGetValue(key, out var value)
            ? value as T
            : null;

    private static FlyoutItem CreateFlyoutItem(
        string title,
        string route,
        string contentRoute,
        Func<Page> factory)
    {
        var item = new FlyoutItem
        {
            Title = title,
            Route = route
        };
        item.Items.Add(new ShellContent
        {
            Title = title,
            Route = contentRoute,
            ContentTemplate = new DataTemplate(factory)
        });
        return item;
    }
}
