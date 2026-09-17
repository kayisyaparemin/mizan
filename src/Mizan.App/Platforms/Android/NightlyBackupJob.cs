using Android.App;
using Android.App.Job;
using Android.Content;
using Android.Util;
using Mizan.Application.Services;

namespace Mizan.App.Backup;

/// <summary>
/// Her gece 23:30 civarında bütün profillerin yedeğini alır. O günden beri
/// hiçbir profil değişmediyse dosya yazılmaz (<see cref="BackupService"/>).
/// </summary>
/// <remarks>
/// Android görevi tam dakikasında çalıştırmayı garanti etmez; telefon uyku
/// modundaysa bir sonraki bakım aralığına kayar. Bu yüzden görev 23:30'dan
/// önce başlamaz, en geç üç saat içinde çalışır. Tek seferlik bir görevdir;
/// her çalışmanın sonunda ertesi gece için yeniden kurulur. Telefon yeniden
/// başlayınca Android görevi kendisi geri kurar (persisted).
/// </remarks>
[Service(
    Name = "com.coinflow.mobile.NightlyBackupJob",
    Permission = "android.permission.BIND_JOB_SERVICE",
    Exported = true)]
public sealed class NightlyBackupJob : JobService
{
    public const int JobId = 7301;
    private static readonly TimeSpan RunAt = new(23, 30, 0);
    private static readonly TimeSpan Window = TimeSpan.FromHours(3);
    private CancellationTokenSource? _cancellation;

    public static void EnsureScheduled(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(29) ||
            Scheduler(context) is not { } scheduler ||
            scheduler.GetPendingJob(JobId) is not null)
        {
            return;
        }

        Schedule(context, scheduler);
    }

    public override bool OnStartJob(JobParameters? parameters)
    {
        _cancellation = new CancellationTokenSource();
        var cancellationToken = _cancellation.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var backup = IPlatformApplication.Current?.Services
                    .GetService<BackupService>();
                if (backup is not null)
                {
                    var result = await backup.BackUpIfChangedAsync(cancellationToken);
                    Log.Info("Mizan", $"Gece yedeği: {result.Outcome}");
                }
            }
            catch (Exception exception)
            {
                Log.Warn("Mizan", $"Gece yedeği alınamadı: {exception}");
            }
            finally
            {
                // Aynı kimlikle kurmak çalışan görevi durdurur; önce bitir.
                JobFinished(parameters, false);
                if (Scheduler(this) is { } scheduler)
                {
                    Schedule(this, scheduler);
                }
            }
        });

        return true;
    }

    public override bool OnStopJob(JobParameters? parameters)
    {
        _cancellation?.Cancel();
        return false;
    }

    private static void Schedule(Context context, JobScheduler scheduler)
    {
        var now = DateTime.Now;
        var next = now.Date + RunAt;
        if (next <= now.AddMinutes(1))
        {
            next = next.AddDays(1);
        }

        var delay = (long)(next - now).TotalMilliseconds;
        var job = new JobInfo.Builder(
                JobId,
                new ComponentName(context, Java.Lang.Class.FromType(typeof(NightlyBackupJob))))
            .SetMinimumLatency(delay)!
            .SetOverrideDeadline(delay + (long)Window.TotalMilliseconds)!
            .SetPersisted(true)!
            .Build()!;
        scheduler.Schedule(job);
    }

    private static JobScheduler? Scheduler(Context context) =>
        context.GetSystemService(JobSchedulerService) as JobScheduler;
}
