using Android.App;
using Android.Runtime;
using Android.Util;
using CoinFlow.App.Backup;

namespace CoinFlow.App;

[Application]
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override void OnCreate()
    {
        base.OnCreate();
        try
        {
            NightlyBackupJob.EnsureScheduled(this);
        }
        catch (Exception exception)
        {
            // Yedek görevi kurulamadı diye uygulama açılmaz olmamalı.
            Log.Warn("Mizan", $"Gece yedeği kurulamadı: {exception}");
        }
    }
}
