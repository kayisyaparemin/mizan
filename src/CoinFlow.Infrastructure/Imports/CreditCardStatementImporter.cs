using System.Security.Cryptography;
using CoinFlow.Application.Abstractions;
using CoinFlow.Application.Models;

namespace CoinFlow.Infrastructure.Imports;

public sealed class CreditCardStatementImporter(
    IPdfTextExtractor textExtractor,
    IEnumerable<ICreditCardStatementParser> parsers)
    : ICreditCardStatementImporter
{
    public async Task<CreditCardStatementImportResult> ImportPdfAsync(
        Stream pdf,
        CancellationToken cancellationToken = default)
    {
        using var copy = new MemoryStream();
        await pdf.CopyToAsync(copy, cancellationToken);
        var bytes = copy.ToArray();
        var fingerprint = Convert.ToHexString(SHA256.HashData(bytes));
        copy.Position = 0;

        string text;
        try
        {
            text = await textExtractor.ExtractTextAsync(copy, cancellationToken);
        }
        catch (Exception)
        {
            return Unreadable(fingerprint);
        }

        if (!HasUsableText(text))
        {
            return Unreadable(fingerprint);
        }

        var parser = parsers.FirstOrDefault(x => x.CanParse(text));
        if (parser is null)
        {
            return new CreditCardStatementImportResult
            {
                SourceDocumentFingerprint = fingerprint,
                Confidence = 0m,
                Warnings =
                [
                    "Bu bankanın ekstre düzeni henüz otomatik tanınamadı. Bilgileri elle girebilirsin."
                ]
            };
        }

        return parser.Parse(text, fingerprint);
    }

    private static bool HasUsableText(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 80)
        {
            return false;
        }

        var meaningful = text.Count(char.IsLetterOrDigit);
        return meaningful >= 40 &&
               (decimal)meaningful / Math.Max(1, text.Length) > 0.20m;
    }

    private static CreditCardStatementImportResult Unreadable(
        string fingerprint) => new()
        {
            SourceDocumentFingerprint = fingerprint,
            TextExtractionSucceeded = false,
            Confidence = 0m,
            Warnings =
            [
                "Bu ekstre otomatik okunamadı. Bilgileri elle girebilirsin."
            ]
        };
}
