using Android.App;
using Android.Content;
using Android.Content.PM;
using Mizan.App.Backup;
using Android.OS;
using AndroidX.Core.View;

namespace Mizan.App;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        try
        {
            if (Window is not null)
            {
                Window.SetStatusBarColor(Android.Graphics.Color.ParseColor("#FFCABF"));
                Window.SetNavigationBarColor(Android.Graphics.Color.ParseColor("#FFFACF"));

                var controller = WindowCompat.GetInsetsController(Window, Window.DecorView);
                if (controller is not null)
                {
                    controller.AppearanceLightStatusBars = true;
                    controller.AppearanceLightNavigationBars = true;
                }
            }
        }
        catch (Exception exception)
        {
            Android.Util.Log.Warn("Mizan", $"Pencere stilleri uygulanamadı: {exception}");
        }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        ActivityResults.OnActivityResult(requestCode, resultCode, data);
    }
}
