using System.Text.RegularExpressions;
using Sentry;

namespace Mizan.App.Services;

/// <summary>
/// Finansal Gizlilik Filtresi (PII Filter):
/// Kullanıcının hesap bakiyesi, maaşı veya özel finansal rakamlarının
/// telemetri ve hata raporlarında düz metin olarak dışarı sızmasını engeller.
/// </summary>
public static class SentryPiiMasker
{
    private static readonly Regex AmountPattern = new(
        @"(?:\b\d{1,3}(?:\.\d{3})*(?:,\d+)?\s*(?:TL|TRY|₺|\$|€)\b)|(?:\b(?:TL|TRY|₺|\$|€)\s*\d{1,3}(?:\.\d{3})*(?:,\d+)?\b)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static SentryEvent? FilterSensitiveData(SentryEvent sentryEvent)
    {
        if (sentryEvent.Message?.Formatted is { } messageText)
        {
            sentryEvent.Message = new SentryMessage
            {
                Formatted = AmountPattern.Replace(messageText, "[TUTAR_MASKEYE_ALINDI]")
            };
        }

        return sentryEvent;
    }
}
