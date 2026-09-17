using System.Globalization;
using Android.App;
using Android.Content;
using Android.Util;
using AndroidX.Core.App;
using Mizan.App.Services;
using Mizan.Application.Models;
using Mizan.Application.Services;

namespace Mizan.App.Reminders;

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
///
/// Bildirimdeki "Ödedim" / "Ertele" cevabı da veritabanına yazılmaz:
/// <see cref="PaymentReminderActionReceiver"/> cevabı ayrı bir kuyruk
/// dosyasına ekler, uygulama açılınca açık profil onu deftere işler. Alıcı
/// başka bir profil açıkken ya da uygulama kapalıyken de çalışabilir.
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
            var added = reminders.Select(x => ScheduledReminder.From(profileId, x)).ToArray();
            foreach (var entry in added)
            {
                Schedule(Context, entry);
            }

            kept.AddRange(added);
            ScheduledReminderFile.Write(Context, kept);
        }
    }

    public void ShowNow(Guid profileId, PaymentReminder reminder) =>
        PaymentReminderReceiver.Show(Context, ScheduledReminder.From(profileId, reminder));

    public IReadOnlyList<PaymentReminderAnswer> ReadAnswers(Guid profileId)
    {
        lock (FileLock)
        {
            return ReminderAnswerFile.Read(Context)
                .Where(x => x.ProfileId == profileId)
                .Select(x => x.Answer)
                .ToArray();
        }
    }

    public void RemoveAnswers(Guid profileId, int count)
    {
        lock (FileLock)
        {
            var removed = 0;
            var kept = ReminderAnswerFile.Read(Context)
                .Where(x => x.ProfileId != profileId || removed++ >= count)
                .ToArray();
            ReminderAnswerFile.Write(Context, kept);
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

    /// <summary>
    /// Bildirim düğmesine basıldı; veritabanı açılmaz. Cevap kuyruğa eklenir
    /// ve telefondaki alarmlar hemen düzeltilir: "Ödedim" denen ödemenin
    /// kalan bildirimleri iptal edilir, "Ertele" denen için yeniden
    /// hatırlatma kurulur. Uygulama açılınca aynı sonuç defterden yeniden
    /// kurulur (<see cref="Replace"/>).
    /// </summary>
    public static void Answer(Context context, Guid profileId, PaymentReminderAnswer answer)
    {
        lock (FileLock)
        {
            var answers = ReminderAnswerFile.Read(context);
            answers.Add(new QueuedAnswer(profileId, answer));
            ReminderAnswerFile.Write(context, answers);

            var entries = ScheduledReminderFile.Read(context);
            var keys = answer.Payments.Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
            if (answer.Kind == PaymentReminderAnswerKind.Paid)
            {
                var covered = entries
                    .Where(x => x.ProfileId == profileId && x.Payments.Count > 0 &&
                                x.Payments.All(p => keys.Contains(p.Key)))
                    .ToArray();
                foreach (var entry in covered)
                {
                    Cancel(context, entry);
                    entries.Remove(entry);
                }
            }
            else if (answer.SnoozedUntil is { } until)
            {
                var followUps = PaymentReminderPlanner.FollowUps(
                        answer.Payments.Select(x => new PaymentReminderResponse(
                            x.Key,
                            x.Name,
                            x.DueDate,
                            x.Amount,
                            PaymentReminderAnswerKind.Snoozed,
                            answer.AnsweredAt,
                            until)),
                        answer.AnsweredAt)
                    .Select(x => ScheduledReminder.From(profileId, x))
                    .ToArray();
                foreach (var followUp in followUps)
                {
                    entries.RemoveAll(x => x.ProfileId == profileId && x.Key == followUp.Key);
                    Schedule(context, followUp);
                    entries.Add(followUp);
                }
            }

            ScheduledReminderFile.Write(context, entries);
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
        PaymentReminderReceiver.PutExtras(intent, entry);
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
