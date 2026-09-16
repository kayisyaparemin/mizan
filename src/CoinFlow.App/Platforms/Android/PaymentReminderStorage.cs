using System.Globalization;
using System.Text;
using Android.Content;
using CoinFlow.Application.Models;
using CoinFlow.Application.Services;

namespace CoinFlow.App.Reminders;

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
