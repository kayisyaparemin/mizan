using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CoinFlow.Application.Abstractions;
using CoinFlow.Application.Models;

namespace CoinFlow.Infrastructure.Imports;

public sealed class AkbankAxessStatementParser : BankStatementParserBase
{
    public override string BankName => "Akbank Axess";

    public override bool CanParse(string text)
    {
        var normalized = Normalize(text);
        return ContainsAll(normalized, "AKBANK") &&
               (ContainsAny(normalized, "AXESS", "HESAP OZETI") ||
                ContainsAny(normalized, "KREDI KARTI EKSTRESI"));
    }

    public override CreditCardStatementImportResult Parse(
        string text,
        string sourceDocumentFingerprint) =>
        ParseCore(
            text,
            sourceDocumentFingerprint,
            "Akbank Axess",
            ["HESAP KESIM TARIHI", "EKSTRE KESIM TARIHI", "EKSTRE TARIHI", "KESIM TARIHI"],
            ["SON ODEME TARIHI"],
            ["DONEM BORCU", "EKSTRE BORCU", "EKSTRE TUTARI", "TOPLAM BORC"],
            ["ASGARI ODEME TUTARI", "ASGARI TUTAR", "ASGARI ODEME"],
            ["BIR SONRAKI HESAP KESIM TARIHI", "SONRAKI KESIM TARIHI", "BIR SONRAKI EKSTRE TARIHI"],
            ["BIR SONRAKI SON ODEME TARIHI", "SONRAKI SON ODEME TARIHI"]);
}

public sealed class GarantiBonusStatementParser : BankStatementParserBase
{
    public override string BankName => "Garanti BBVA Bonus";

    public override bool CanParse(string text)
    {
        var normalized = Normalize(text);
        return ContainsAny(normalized, "GARANTI BBVA", "TURKIYE GARANTI BANKASI") &&
               ContainsAny(normalized, "BONUS", "EKSTRE OZETI");
    }

    public override CreditCardStatementImportResult Parse(
        string text,
        string sourceDocumentFingerprint) =>
        ParseCore(
            text,
            sourceDocumentFingerprint,
            "Garanti BBVA Bonus",
            ["HESAP KESIM TARIHI", "EKSTRE TARIHI", "KESIM TARIHI"],
            ["SON ODEME TARIHI"],
            ["DONEM BORCU", "EKSTRE BORCU", "EKSTRE TUTARI", "TOPLAM BORC"],
            ["MIN. ODEME TUTARI", "MINIMUM ODEME TUTARI", "MIN ODEME TUTARI", "ASGARI ODEME TUTARI", "ASGARI TUTAR", "ASGARI ODEME"],
            ["BIR SONRAKI HESAP KESIMINIZ", "BIR SONRAKI HESAP KESIM TARIHI", "BIR SONRAKI EKSTRE TARIHI", "SONRAKI KESIM TARIHI"],
            ["BIR SONRAKI SON ODEME TARIHI", "SONRAKI SON ODEME TARIHI", "SON ODEMENIZ"]);
}

public abstract class BankStatementParserBase : ICreditCardStatementParser
{
    private static readonly TimeSpan RegexTimeout =
        TimeSpan.FromMilliseconds(250);
    private static readonly Regex DateRegex = new(
        @"\b(?<date>\d{1,2}[./-]\d{1,2}[./-]\d{4})\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant |
        RegexOptions.NonBacktracking,
        RegexTimeout);

    private static readonly Regex MoneyRegex = new(
        @"(?<amount>\d{1,3}(?:[.,]\d{3})*(?:[.,]\d{2})|\d+(?:[.,]\d{2}))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant |
        RegexOptions.NonBacktracking,
        RegexTimeout);

    private static readonly Regex Last4Regex = new(
        @"(?:\*{2,}|X{2,}|KART(?:\s+NO)?|CARD(?:\s+NO)?)\s*(?<last4>\d{4})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled |
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        RegexTimeout);

    // Bazı bankalar (ör. Garanti) tarihleri "24 Ağustos 2026" biçiminde,
    // ay adıyla yazıyor. Metin Normalize() ile büyük harf + ASCII'ye dönüştüğü
    // için ay adları da büyük/ASCII olur (Ağustos -> AGUSTOS, Eylül -> EYLUL).
    private static readonly Regex MonthNameDateRegex = new(
        @"(?<day>\d{1,2})\s+(?<month>OCAK|SUBAT|MART|NISAN|MAYIS|HAZIRAN|TEMMUZ|AGUSTOS|EYLUL|EKIM|KASIM|ARALIK)\s+(?<year>\d{4})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant |
        RegexOptions.NonBacktracking,
        RegexTimeout);

    private static readonly IReadOnlyDictionary<string, int> TurkishMonths =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["OCAK"] = 1,
            ["SUBAT"] = 2,
            ["MART"] = 3,
            ["NISAN"] = 4,
            ["MAYIS"] = 5,
            ["HAZIRAN"] = 6,
            ["TEMMUZ"] = 7,
            ["AGUSTOS"] = 8,
            ["EYLUL"] = 9,
            ["EKIM"] = 10,
            ["KASIM"] = 11,
            ["ARALIK"] = 12
        };

    public abstract string BankName { get; }
    public abstract bool CanParse(string text);

    public abstract CreditCardStatementImportResult Parse(
        string text,
        string sourceDocumentFingerprint);

    protected static CreditCardStatementImportResult ParseCore(
        string text,
        string sourceDocumentFingerprint,
        string bankName,
        IReadOnlyList<string> statementDateLabels,
        IReadOnlyList<string> dueDateLabels,
        IReadOnlyList<string> statementAmountLabels,
        IReadOnlyList<string> minimumPaymentLabels,
        IReadOnlyList<string> nextStatementDateLabels,
        IReadOnlyList<string> nextDueDateLabels)
    {
        var normalized = Normalize(text);
        var statementDate = FindDate(normalized, statementDateLabels);
        var dueDate = FindDate(normalized, dueDateLabels);
        var statementAmount = FindMoney(normalized, statementAmountLabels);
        var minimum = FindMoney(normalized, minimumPaymentLabels);
        var nextStatementDate = FindDate(normalized, nextStatementDateLabels);
        var nextDueDate = FindDate(normalized, nextDueDateLabels);
        var fieldConfidence = new StatementFieldConfidence(
            statementDate is not null,
            dueDate is not null,
            statementAmount is not null,
            minimum is not null,
            nextStatementDate is not null,
            nextDueDate is not null);
        var warnings = MissingWarnings(fieldConfidence).ToArray();
        var requiredCount = new[]
        {
            fieldConfidence.StatementDate,
            fieldConfidence.DueDate,
            fieldConfidence.StatementAmount,
            fieldConfidence.MinimumPaymentAmount
        }.Count(x => x);
        var optionalCount = new[]
        {
            fieldConfidence.NextStatementDate,
            fieldConfidence.NextDueDate
        }.Count(x => x);

        return new CreditCardStatementImportResult
        {
            DetectedBank = bankName,
            CardLast4 = FindLast4(normalized),
            StatementDate = statementDate,
            DueDate = dueDate,
            StatementAmount = statementAmount,
            MinimumPaymentAmount = minimum,
            NextStatementDate = nextStatementDate,
            NextDueDate = nextDueDate,
            SourceDocumentFingerprint = sourceDocumentFingerprint,
            FieldConfidence = fieldConfidence,
            Confidence = decimal.Round(
                ((requiredCount / 4m) * 0.80m) +
                ((optionalCount / 2m) * 0.20m),
                2,
                MidpointRounding.AwayFromZero),
            Warnings = warnings
        };
    }

    protected static string Normalize(string text)
    {
        var upper = text
            .Replace('\u00A0', ' ')
            .ToUpperInvariant();
        var builder = new StringBuilder(upper.Length);
        var previousHorizontalSpace = false;
        foreach (var character in upper)
        {
            var normalized = character switch
            {
                'Ç' => 'C',
                'Ğ' => 'G',
                'İ' => 'I',
                'I' => 'I',
                'ı' => 'I',
                'Ö' => 'O',
                'Ş' => 'S',
                'Ü' => 'U',
                _ => character
            };
            var isHorizontalSpace = normalized is ' ' or '\t';
            if (!isHorizontalSpace || !previousHorizontalSpace)
            {
                builder.Append(isHorizontalSpace ? ' ' : normalized);
            }

            previousHorizontalSpace = isHorizontalSpace;
        }

        return builder.ToString();
    }

    protected static bool ContainsAll(
        string text,
        params string[] markers) => markers.All(text.Contains);

    protected static bool ContainsAny(
        string text,
        params string[] markers) => markers.Any(text.Contains);

    private static DateOnly? FindDate(
        string text,
        IEnumerable<string> labels)
    {
        foreach (var label in labels)
        {
            foreach (var window in WindowsAfterLabel(text, label, 120))
            {
                if (TryFindDateInWindow(window, out var date))
                {
                    return date;
                }
            }
        }

        return null;
    }

    private static bool TryFindDateInWindow(string window, out DateOnly date)
    {
        var numeric = DateRegex.Match(window);
        if (numeric.Success &&
            TryParseDate(numeric.Groups["date"].Value, out date))
        {
            return true;
        }

        var named = MonthNameDateRegex.Match(window);
        if (named.Success &&
            TurkishMonths.TryGetValue(named.Groups["month"].Value, out var month) &&
            int.TryParse(named.Groups["day"].Value, out var day) &&
            int.TryParse(named.Groups["year"].Value, out var year) &&
            year is >= 2000 and <= 2100 &&
            day >= 1 &&
            day <= DateTime.DaysInMonth(year, month))
        {
            date = new DateOnly(year, month, day);
            return true;
        }

        date = default;
        return false;
    }

    private static decimal? FindMoney(
        string text,
        IEnumerable<string> labels)
    {
        foreach (var label in labels)
        {
            foreach (var window in WindowsAfterLabel(text, label, 140))
            {
                var match = MoneyRegex.Match(window);
                if (match.Success &&
                    TryParseMoney(match.Groups["amount"].Value, out var amount))
                {
                    return amount;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> WindowsAfterLabel(
        string text,
        string label,
        int length)
    {
        var index = -1;
        while ((index = text.IndexOf(
                   label,
                   index + 1,
                   StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var start = index + label.Length;
            yield return text.Substring(
                start,
                Math.Min(length, text.Length - start));
        }
    }

    private static string? FindLast4(string text)
    {
        var match = Last4Regex.Match(text);
        return match.Success ? match.Groups["last4"].Value : null;
    }

    private static bool TryParseDate(string value, out DateOnly date) =>
        DateOnly.TryParseExact(
            value.Replace('-', '.').Replace('/', '.'),
            "d.M.yyyy",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);

    private static bool TryParseMoney(string value, out decimal amount)
    {
        var cleaned = value
            .Replace("TL", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("TRY", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
        var lastComma = cleaned.LastIndexOf(',');
        var lastDot = cleaned.LastIndexOf('.');
        char? decimalSeparator = null;
        if (lastComma >= 0 || lastDot >= 0)
        {
            var candidate = lastComma > lastDot ? ',' : '.';
            var separatorIndex = Math.Max(lastComma, lastDot);
            var digitCountAfterSeparator = cleaned.Length - separatorIndex - 1;
            decimalSeparator = digitCountAfterSeparator == 2
                ? candidate
                : null;
        }

        var normalized = cleaned
            .Replace(".", string.Empty)
            .Replace(",", string.Empty);
        if (decimalSeparator is { } separator)
        {
            var separatorIndex = cleaned.LastIndexOf(separator);
            var whole = cleaned[..separatorIndex]
                .Replace(".", string.Empty)
                .Replace(",", string.Empty);
            var fraction = cleaned[(separatorIndex + 1)..];
            normalized = $"{whole}.{fraction}";
        }

        return decimal.TryParse(
            normalized,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out amount);
    }

    private static IEnumerable<string> MissingWarnings(
        StatementFieldConfidence confidence)
    {
        if (!confidence.StatementDate)
        {
            yield return "Kesim tarihi bulunamadı.";
        }

        if (!confidence.DueDate)
        {
            yield return "Son ödeme tarihi bulunamadı.";
        }

        if (!confidence.StatementAmount)
        {
            yield return "Ekstre tutarı bulunamadı.";
        }

        if (!confidence.MinimumPaymentAmount)
        {
            yield return "Asgari ödeme bulunamadı.";
        }
    }
}
