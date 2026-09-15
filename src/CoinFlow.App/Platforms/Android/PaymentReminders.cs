using System.Globalization;
using System.Text;
using Android.App;
using Android.Content;
using Android.Util;
using Android.Widget;
using AndroidX.Core.App;
using CommunityToolkit.Mvvm.Messaging;
using CoinFlow.App.Services;
using CoinFlow.Application.Models;
using CoinFlow.Application.Services;

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

/// <summary>Kurulu bir bildirim; profil ve anahtarından sabit istek kodu türer.</summary>
internal sealed record ScheduledReminder(
    Guid ProfileId,
    string Key,
    DateTime NotifyAt,
    string Title,
    string Message,
    IReadOnlyList<PaymentDue> Payments)
{
    public static ScheduledReminder From(Guid profileId, PaymentReminder reminder) =>
        new(profileId, reminder.Key, reminder.NotifyAt, reminder.Title, reminder.Message, reminder.Payments);

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
/// Altıncı sütun bildirimin ödemeleri (v1.17.0); v1.16.0'ın beş sütunlu
/// satırları düğmesiz bildirim olarak okunur.
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
            if (parts.Length is not (5 or 6) ||
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
                    Decode(parts[4]),
                    parts.Length == 6 ? PaymentReminderPayload.DecodePayments(parts[5]) : []));
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
            Encode(x.Message),
            PaymentReminderPayload.EncodePayments(x.Payments))));
        File.Move(temporary, path, overwrite: true);
    }

    private static string PathFor(Context context) =>
        Path.Combine(context.FilesDir!.AbsolutePath, FileName);

    private static string Encode(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static string Decode(string value) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(value));
}

internal sealed record QueuedAnswer(Guid ProfileId, PaymentReminderAnswer Answer);

/// <summary>
/// Bildirim düğmelerinden gelen, henüz deftere işlenmemiş cevaplar. Biçim
/// <see cref="PaymentReminderPayload.EncodeAnswer"/>; okunamayan satır atlanır.
/// </summary>
internal static class ReminderAnswerFile
{
    private const string FileName = "payment-reminder-answers.txt";

    public static List<QueuedAnswer> Read(Context context)
    {
        var path = PathFor(context);
        if (!File.Exists(path))
        {
            return [];
        }

        var result = new List<QueuedAnswer>();
        foreach (var line in File.ReadAllLines(path))
        {
            if (PaymentReminderPayload.TryDecodeAnswer(line, out var profileId, out var answer))
            {
                result.Add(new QueuedAnswer(profileId, answer));
            }
        }

        return result;
    }

    public static void Write(Context context, IEnumerable<QueuedAnswer> answers)
    {
        var path = PathFor(context);
        var temporary = path + ".tmp";
        File.WriteAllLines(
            temporary,
            answers.Select(x => PaymentReminderPayload.EncodeAnswer(x.ProfileId, x.Answer)));
        File.Move(temporary, path, overwrite: true);
    }

    private static string PathFor(Context context) =>
        Path.Combine(context.FilesDir!.AbsolutePath, FileName);
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
    public const string ProfileExtra = "profile";
    public const string PaymentsExtra = "payments";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null)
        {
            return;
        }

        Guid.TryParseExact(intent.GetStringExtra(ProfileExtra), "N", out var profileId);
        Show(
            context,
            intent.GetStringExtra(TitleExtra) ?? "Ödeme günü",
            intent.GetStringExtra(MessageExtra) ?? string.Empty,
            intent.GetIntExtra(IdExtra, 0),
            profileId,
            intent.GetStringExtra(PaymentsExtra));
    }

    internal static void PutExtras(Intent intent, ScheduledReminder entry)
    {
        intent.PutExtra(TitleExtra, entry.Title);
        intent.PutExtra(MessageExtra, entry.Message);
        intent.PutExtra(IdExtra, entry.RequestCode);
        intent.PutExtra(ProfileExtra, entry.ProfileId.ToString("N"));
        intent.PutExtra(PaymentsExtra, PaymentReminderPayload.EncodePayments(entry.Payments));
    }

    internal static void Show(Context context, ScheduledReminder entry) =>
        Show(
            context,
            entry.Title,
            entry.Message,
            entry.RequestCode,
            entry.ProfileId,
            PaymentReminderPayload.EncodePayments(entry.Payments));

    private static void Show(
        Context context,
        string title,
        string message,
        int id,
        Guid profileId,
        string? payments)
    {
        try
        {
            EnsureChannel(context);
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

            // v1.16.0'dan kalan, ödemesi yazılmamış alarmlar düğmesiz çıkar.
            var dues = PaymentReminderPayload.DecodePayments(payments);
            if (profileId != Guid.Empty && dues.Count > 0)
            {
                builder.AddAction(
                    0,
                    dues.Count == 1 ? "Ödedim" : "Hepsini ödedim",
                    PaymentReminderActionReceiver.Intent(context, PaymentReminderActionReceiver.PaidAction, id, profileId, payments!));
                builder.AddAction(
                    0,
                    "Ertele",
                    PaymentReminderActionReceiver.Intent(context, PaymentReminderActionReceiver.SnoozeAction, id, profileId, payments!));
            }

            NotificationManagerCompat.From(context).Notify(id, builder.Build());
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

/// <summary>
/// Bildirimdeki "Ödedim" ve "Ertele" düğmeleri. Veritabanını açmaz: cevabı
/// kuyruğa yazar, alarmları düzeltir, bildirimi kapatır ve kısa bir onay
/// gösterir. Uygulama açıksa ekranlara haber verir.
/// </summary>
[BroadcastReceiver(
    Name = "com.coinflow.mobile.PaymentReminderActionReceiver",
    Exported = false)]
public sealed class PaymentReminderActionReceiver : BroadcastReceiver
{
    public const string PaidAction = "com.coinflow.mobile.reminder.PAID";
    public const string SnoozeAction = "com.coinflow.mobile.reminder.SNOOZE";

    /// <summary>
    /// İki düğme aynı istek kodunu kullanır; PendingIntent'leri eylem adı
    /// ayırır, alarmınkinden de bileşen (alıcı sınıfı) ayırır.
    /// </summary>
    internal static PendingIntent Intent(
        Context context,
        string action,
        int notificationId,
        Guid profileId,
        string payments)
    {
        var intent = new Intent(context, typeof(PaymentReminderActionReceiver))
            .SetAction(action)!
            .PutExtra(PaymentReminderReceiver.IdExtra, notificationId)!
            .PutExtra(PaymentReminderReceiver.ProfileExtra, profileId.ToString("N"))!
            .PutExtra(PaymentReminderReceiver.PaymentsExtra, payments)!;
        return PendingIntent.GetBroadcast(
            context,
            notificationId,
            intent,
            PendingIntentFlags.UpdateCurrent | AndroidPaymentReminderScheduler.Immutable)!;
    }

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null ||
            intent?.Action is not (PaidAction or SnoozeAction) ||
            !Guid.TryParseExact(intent.GetStringExtra(PaymentReminderReceiver.ProfileExtra), "N", out var profileId))
        {
            return;
        }

        try
        {
            var payments = PaymentReminderPayload.DecodePayments(
                intent.GetStringExtra(PaymentReminderReceiver.PaymentsExtra));
            if (payments.Count == 0)
            {
                return;
            }

            var now = DateTime.Now;
            var paid = intent.Action == PaidAction;
            var until = paid ? (DateTime?)null : PaymentReminderPlanner.SnoozeUntil(now);
            AndroidPaymentReminderScheduler.Answer(
                context,
                profileId,
                new PaymentReminderAnswer(
                    paid ? PaymentReminderAnswerKind.Paid : PaymentReminderAnswerKind.Snoozed,
                    now,
                    until,
                    payments));
            NotificationManagerCompat.From(context).Cancel(
                intent.GetIntExtra(PaymentReminderReceiver.IdExtra, 0));
            Toast.MakeText(
                context,
                paid
                    ? "Tebrikler! Ödendi olarak kaydedildi."
                    : $"Ertelendi. {PaymentReminderPlanner.SnoozeText(until!.Value, now)}",
                ToastLength.Long)?.Show();
            WeakReferenceMessenger.Default.Send(new PaymentReminderAnsweredMessage());
        }
        catch (Exception exception)
        {
            Log.Warn("Mizan", $"Hatırlatıcı cevabı kaydedilemedi: {exception}");
        }
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
