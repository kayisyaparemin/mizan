using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.Content;
using AndroidX.Core.View;

namespace CoinFlow.App;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        if (Window is null)
        {
            return;
        }

        Window.SetStatusBarColor(new Android.Graphics.Color(ContextCompat.GetColor(this, Resource.Color.colorPrimaryDark)));
        Window.SetNavigationBarColor(new Android.Graphics.Color(ContextCompat.GetColor(this, Resource.Color.navigationBar)));

        var controller = WindowCompat.GetInsetsController(Window, Window.DecorView);
        controller.AppearanceLightStatusBars = false;
        controller.AppearanceLightNavigationBars = true;
    }
}
