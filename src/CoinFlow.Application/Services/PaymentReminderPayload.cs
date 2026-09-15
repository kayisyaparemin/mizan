using System.Globalization;
using System.Text;
using CoinFlow.Application.Models;

namespace CoinFlow.Application.Services;

/// <summary>
/// Bildirimin taşıdığı ödemeler ve bildirimden gelen cevaplar için düz metin
/// biçimi. Android tarafı veritabanını açmadan cevabı dosyaya yazar, uygulama
/// açılınca buradan okur.
/// </summary>
/// <remarks>
/// Reflection kullanan JSON serileştirmesi kırpılan Release derlemesinde risk
/// taşır; hatırlatıcı dosyası da aynı gerekçeyle düz metin. Metinler Base64,
/// ayırıcılar kontrol karakteri: ödeme adında sekme ya da satır sonu olsa da
/// biçim bozulmaz.
/// </remarks>
public static class PaymentReminderPayload
{
    private const char FieldSeparator = '';
    private const char PaymentSeparator = '';
    private const char AnswerSeparator = '\t';
    private const string DateFormat = "yyyyMMdd";
    private const string TimeFormat = "yyyyMMddHHmm";

    public static string EncodePayments(IEnumerable<PaymentDue> payments) =>
        Base64(string.Join(
            PaymentSeparator,
            payments.Select(x => string.Join(
                FieldSeparator,
                x.Key,
                x.Name,
                x.DueDate.ToString(DateFormat, CultureInfo.InvariantCulture),
                x.Amount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty))));

    /// <summary>Bozuk ya da eksik parça atlanır; hiçbir şey okunamazsa boş liste.</summary>
    public static IReadOnlyList<PaymentDue> DecodePayments(string? value)
    {
        if (string.IsNullOrEmpty(value) || !TryUnbase64(value, out var text))
        {
            return [];
        }

        var result = new List<PaymentDue>();
        foreach (var part in text.Split(PaymentSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = part.Split(FieldSeparator);
            if (fields.Length != 4 ||
                fields[0].Length == 0 ||
                !DateOnly.TryParseExact(fields[2], DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                continue;
            }

            decimal? amount = null;
            if (fields[3].Length > 0)
            {
                if (!decimal.TryParse(fields[3], NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
                {
                    continue;
                }

                amount = parsed;
            }

            result.Add(new PaymentDue(fields[0], fields[1], date, amount));
        }

        return result;
    }

    /// <summary>Cevap kuyruğunun bir satırı: profil · tür · zaman · erteleme · ödemeler.</summary>
    public static string EncodeAnswer(Guid profileId, PaymentReminderAnswer answer) =>
        string.Join(
            AnswerSeparator,
            profileId.ToString("N"),
            answer.Kind == PaymentReminderAnswerKind.Paid ? "odendi" : "ertelendi",
            answer.AnsweredAt.ToString(TimeFormat, CultureInfo.InvariantCulture),
            answer.SnoozedUntil?.ToString(TimeFormat, CultureInfo.InvariantCulture) ?? string.Empty,
            EncodePayments(answer.Payments));

    public static bool TryDecodeAnswer(
        string line,
        out Guid profileId,
        out PaymentReminderAnswer answer)
    {
        profileId = Guid.Empty;
        answer = null!;
        var parts = line.Split(AnswerSeparator);
        if (parts.Length != 5 ||
            !Guid.TryParseExact(parts[0], "N", out profileId) ||
            !TryTime(parts[2], out var answeredAt))
        {
            return false;
        }

        PaymentReminderAnswerKind kind;
        switch (parts[1])
        {
            case "odendi":
                kind = PaymentReminderAnswerKind.Paid;
                break;
            case "ertelendi":
                kind = PaymentReminderAnswerKind.Snoozed;
                break;
            default:
                return false;
        }

        DateTime? snoozedUntil = null;
        if (parts[3].Length > 0)
        {
            if (!TryTime(parts[3], out var until))
            {
                return false;
            }

            snoozedUntil = until;
        }

        var payments = DecodePayments(parts[4]);
        if (payments.Count == 0)
        {
            return false;
        }

        answer = new PaymentReminderAnswer(kind, answeredAt, snoozedUntil, payments);
        return true;
    }

    private static bool TryTime(string value, out DateTime time) =>
        DateTime.TryParseExact(
            value,
            TimeFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out time);

    private static string Base64(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static bool TryUnbase64(string value, out string text)
    {
        try
        {
            text = Encoding.UTF8.GetString(Convert.FromBase64String(value));
            return true;
        }
        catch (FormatException)
        {
            text = string.Empty;
            return false;
        }
    }
}
