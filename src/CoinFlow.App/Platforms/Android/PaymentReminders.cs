using System.Globalization;
using System.Text;
using Android.App;
using Android.Content;
using Android.Util;
using AndroidX.Core.App;
using CoinFlow.App.Services;
using CoinFlow.Application.Models;

namespace CoinFlow.App.Reminders;

/// <summary>
/// Ödeme hatırlatıcılarını AlarmManager ile kurar. Kurulan bildirimler
/// uygulama klasöründe bir dosyada da tutulur: Android telefon yeniden
/// başlayınca alarmları siler, <see cref="PaymentReminderBootReceiver"/>
/// veritabanını açmadan bu dosyadan geri kurar.
/// </summary>
/// <remarks>
/// Kesin alarm izni (SCHEDULE_EXACT_ALARM) istenmez; Android 14'te ayrı kullanıcı
/// izni ister. Android 12+ bildirimi on dakikalık pencerede kurar; daha
/// eski sürümlerde izin gerekmeden tam saatinde.
/// </remarks>
public sealed class AndroidPaymentReminderScheduler : IPaymentReminderScheduler
{
    private static readonly object FileLock = new();

    private static Context Context => Android.App.Application.Context;

    /// <summary>Android 12+ zorunlu kılar; 23 öncesinde bayrak yok.</summary>
    internal static PendingIntentFlags Immutable =>
        OperatingSystem.IsAndroidVersionAtLeast(23)
            ? PendingIntentFlags.Immutable
            : 0;

    public bool AreNotificationsAllowed =>
        NotificationManagerCompat.From(Context).AreNotificationsEnabled();

    public async Task<bool> RequestPermissionAsync()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33) && !AreNotificationsAllowed)
        {
            await Permissions.RequestAsync<Permissions.PostNotifications>();
        }

        return AreNotificationsAllowed;
    }

    public void Replace(Guid profileId, IReadOnlyList<PaymentReminder> reminders)
    {
        lock (FileLock)
        {
            var entries = ScheduledReminderFile.Read(Context);
            foreach (var entry in entries.Where(x => x.ProfileId == profileId))
            {
                Cancel(Context, entry);
            }

            var kept = entries.Where(x => x.ProfileId != profileId).ToList();
            var added = reminders.Select(x => new ScheduledReminder(
                profileId,
                x.Key,
                x.NotifyAt,
                x.Title,
                x.Message)).ToArray();
            foreach (var entry in added)
            {
                Schedule(Context, entry);
            }

            kept.AddRange(added);
            ScheduledReminderFile.Write(Context, kept);
        }
    }

    public void OpenNotificationSettings()
    {
        try
        {
            var intent = OperatingSystem.IsAndroidVersionAtLeast(26)
                ? new Intent(Android.Provider.Settings.ActionAppNotificationSettings)
                    .PutExtra(Android.Provider.Settings.ExtraAppPackage, Context.PackageName)
                : new Intent(
                    Android.Provider.Settings.ActionApplicationDetailsSettings,
                    Android.Net.Uri.Parse($"package:{Context.PackageName}"));
            intent!.AddFlags(ActivityFlags.NewTask);
            Context.StartActivity(intent);
        }
        catch (Exception exception)
        {
            Log.Warn("Mizan", $"Bildirim ayarları açılamadı: {exception}");
        }
    }

    /// <summary>Telefon yeniden başlayınca ya da uygulama güncellenince.</summary>
    public static void RescheduleAll(Context context)
    {
        lock (FileLock)
        {
            var now = DateTime.Now;
            var pending = ScheduledReminderFile.Read(context)
                .Where(x => x.NotifyAt > now)
                .ToList();
            foreach (var entry in pending)
            {
                Schedule(context, entry);
            }

            ScheduledReminderFile.Write(context, pending);
        }
    }

    private static void Schedule(Context context, ScheduledReminder entry)
    {
        if (entry.NotifyAt <= DateTime.Now ||
            context.GetSystemService(Context.AlarmService) is not AlarmManager alarms)
        {
            return;
        }

        var intent = ReceiverIntent(context);
        intent.PutExtra(PaymentReminderReceiver.TitleExtra, entry.Title);
        intent.PutExtra(PaymentReminderReceiver.MessageExtra, entry.Message);
        intent.PutExtra(PaymentReminderReceiver.IdExtra, entry.RequestCode);
        var pending = PendingIntent.GetBroadcast(
            context,
            entry.RequestCode,
            intent,
            PendingIntentFlags.UpdateCurrent | AndroidPaymentReminderScheduler.Immutable);
        if (pending is null)
        {
            return;
        }

        var at = new DateTimeOffset(DateTime.SpecifyKind(entry.NotifyAt, DateTimeKind.Local))
            .ToUnixTimeMilliseconds();
        if (OperatingSystem.IsAndroidVersionAtLeast(31) && !alarms.CanScheduleExactAlarms())
        {
            // Emülatörde ölçüldü: SetAndAllowWhileIdle Android 14'te bir
            // saatlik pencere alıyordu; 09:00 bildirimi 10:00'a kayabilirdi.
            // Kesin alarm izni olmadan en dar pencere 10 dakika.
            alarms.SetWindow(AlarmType.RtcWakeup, at, (long)ReminderWindow.TotalMilliseconds, pending);
        }
        else if (OperatingSystem.IsAndroidVersionAtLeast(23))
        {
            alarms.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, at, pending);
        }
        else
        {
            alarms.SetExact(AlarmType.RtcWakeup, at, pending);
        }
    }

    private static readonly TimeSpan ReminderWindow = TimeSpan.FromMinutes(10);

    private static void Cancel(Context context, ScheduledReminder entry)
    {
        // Eşitlik intent'in bileşeni ve istek koduyla; ekler karşılaştırılmaz.
        var pending = PendingIntent.GetBroadcast(
            context,
            entry.RequestCode,
            ReceiverIntent(context),
            PendingIntentFlags.NoCreate | Immutable);
        if (pending is null)
        {
            return;
        }

        (context.GetSystemService(Context.AlarmService) as AlarmManager)?.Cancel(pending);
        pending.Cancel();
    }

    private static Intent ReceiverIntent(Context context) =>
        new(context, typeof(PaymentReminderReceiver));
}

/// <summary>Kurulu bir bildirim; profil ve anahtarından sabit istek kodu türer.</summary>
internal sealed record ScheduledReminder(
    Guid ProfileId,
    string Key,
    DateTime NotifyAt,
    string Title,
    string Message)
{
    /// <summary>
    /// FNV-1a: .NET string hash kodu süreç başına rastgele; yeniden başlatmadan
    /// sonra aynı alarmı iptal edebilmek için kod sabit olmalı.
    /// </summary>
    public int RequestCode
    {
        get
        {
            unchecked
            {
                var hash = 2166136261u;
                foreach (var value in Encoding.UTF8.GetBytes($"{ProfileId:N}/{Key}"))
                {
                    hash = (hash ^ value) * 16777619u;
                }

                return (int)hash;
            }
        }
    }
}

/// <summary>
/// Satır başına bir bildirim, sekmeyle ayrılmış; metinler Base64. Reflection
/// kullanan JSON serileştirmesi kırpılan Release derlemesinde risk taşır.
/// </summary>
internal static class ScheduledReminderFile
{
    private const string FileName = "payment-reminders.txt";
    private const char Separator = '\t';

    public static List<ScheduledReminder> Read(Context context)
    {
        var path = PathFor(context);
        if (!File.Exists(path))
        {
            return [];
        }

        var result = new List<ScheduledReminder>();
        foreach (var line in File.ReadAllLines(path))
        {
            var parts = line.Split(Separator);
            if (parts.Length != 5 ||
                !Guid.TryParse(parts[0], out var profileId) ||
                !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks))
            {
                continue;
            }

            try
            {
                result.Add(new ScheduledReminder(
                    profileId,
                    parts[1],
                    new DateTime(ticks, DateTimeKind.Local),
                    Decode(parts[3]),
                    Decode(parts[4])));
            }
            catch (FormatException)
            {
                // Bozuk satır atlanır; bir sonraki eşitlemede yeniden yazılır.
            }
        }

        return result;
    }

    public static void Write(Context context, IEnumerable<ScheduledReminder> entries)
    {
        var path = PathFor(context);
        var temporary = path + ".tmp";
        File.WriteAllLines(temporary, entries.Select(x => string.Join(
            Separator,
            x.ProfileId.ToString("N"),
            x.Key,
            x.NotifyAt.Ticks.ToString(CultureInfo.InvariantCulture),
            Encode(x.Title),
            Encode(x.Message))));
        File.Move(temporary, path, overwrite: true);
    }

    private static string PathFor(Context context) =>
        Path.Combine(context.FilesDir!.AbsolutePath, FileName);

    private static string Encode(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static string Decode(string value) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(value));
}

/// <summary>Alarm çaldığında bildirimi gösterir.</summary>
[BroadcastReceiver(
    Name = "com.coinflow.mobile.PaymentReminderReceiver",
    Exported = false)]
public sealed class PaymentReminderReceiver : BroadcastReceiver
{
    public const string ChannelId = "payment-reminders";
    public const string TitleExtra = "title";
    public const string MessageExtra = "message";
    public const string IdExtra = "id";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null)
        {
            return;
        }

        try
        {
            EnsureChannel(context);
            var title = intent.GetStringExtra(TitleExtra) ?? "Ödeme günü";
            var message = intent.GetStringExtra(MessageExtra) ?? string.Empty;
            var builder = new NotificationCompat.Builder(context, ChannelId)
                .SetSmallIcon(Resource.Mipmap.appicon)!
                .SetContentTitle(title)!
                .SetContentText(message)!
                .SetStyle(new NotificationCompat.BigTextStyle().BigText(message))!
                .SetPriority(NotificationCompat.PriorityHigh)!
                .SetCategory(NotificationCompat.CategoryReminder)!
                .SetAutoCancel(true)!;
            if (context.PackageManager?.GetLaunchIntentForPackage(context.PackageName!) is { } launch)
            {
                launch.AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop);
                builder.SetContentIntent(PendingIntent.GetActivity(
                    context,
                    0,
                    launch,
                    PendingIntentFlags.UpdateCurrent | AndroidPaymentReminderScheduler.Immutable));
            }

            NotificationManagerCompat.From(context).Notify(
                intent.GetIntExtra(IdExtra, 0),
                builder.Build());
        }
        catch (Exception exception)
        {
            // İzin geri alınmışsa bildirim düşer; uygulama çökmez.
            Log.Warn("Mizan", $"Ödeme hatırlatıcısı gösterilemedi: {exception}");
        }
    }

    public static void EnsureChannel(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26) ||
            context.GetSystemService(Context.NotificationService) is not NotificationManager manager)
        {
            return;
        }

        manager.CreateNotificationChannel(new NotificationChannel(
            ChannelId,
            "Ödeme hatırlatıcısı",
            NotificationImportance.High)
        {
            Description = "Kredi, kart ve planlı ödemelerin vadesi geldiğinde"
        });
    }
}

/// <summary>Telefon yeniden başlayınca ya da uygulama güncellenince alarmları geri kurar.</summary>
[BroadcastReceiver(
    Name = "com.coinflow.mobile.PaymentReminderBootReceiver",
    Exported = true)]
[IntentFilter(new[] { Intent.ActionBootCompleted, Intent.ActionMyPackageReplaced })]
public sealed class PaymentReminderBootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null ||
            (intent?.Action != Intent.ActionBootCompleted &&
             intent?.Action != Intent.ActionMyPackageReplaced))
        {
            return;
        }

        try
        {
            AndroidPaymentReminderScheduler.RescheduleAll(context);
        }
        catch (Exception exception)
        {
            Log.Warn("Mizan", $"Ödeme hatırlatıcıları geri kurulamadı: {exception}");
        }
    }
}
