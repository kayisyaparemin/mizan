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
