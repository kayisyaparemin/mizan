using Android.App;
using Android.Runtime;
using Android.Util;
using Mizan.App.Backup;

namespace Mizan.App;

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

        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            Log.Error("Mizan", $"AppDomain UnhandledException: {args.ExceptionObject}");
        };
        AndroidEnvironment.UnhandledExceptionRaiser += (sender, args) =>
        {
            Log.Error("Mizan", $"AndroidEnvironment UnhandledExceptionRaiser: {args.Exception}");
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            Log.Error("Mizan", $"TaskScheduler UnobservedTaskException: {args.Exception}");
            args.SetObserved();
        };

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
