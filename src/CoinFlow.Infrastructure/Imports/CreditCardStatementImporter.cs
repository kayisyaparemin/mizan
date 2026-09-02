using System.Security.Cryptography;
using System.Diagnostics;
using CoinFlow.Application.Abstractions;
using CoinFlow.Application.Models;

namespace CoinFlow.Infrastructure.Imports;

public sealed class CreditCardStatementImporter : ICreditCardStatementImporter
{
    private const int MaximumPdfBytes = 32 * 1024 * 1024;
    private readonly IPdfTextExtractor _textExtractor;
    private readonly IReadOnlyList<ICreditCardStatementParser> _parsers;
    private readonly ICreditCardStatementImportDiagnostics _diagnostics;

    public CreditCardStatementImporter(
        IPdfTextExtractor textExtractor,
        IEnumerable<ICreditCardStatementParser> parsers,
        ICreditCardStatementImportDiagnostics diagnostics)
    {
        _textExtractor = textExtractor;
        _parsers = parsers.ToArray();
        _diagnostics = diagnostics;
    }

    public CreditCardStatementImporter(
        IPdfTextExtractor textExtractor,
        IEnumerable<ICreditCardStatementParser> parsers)
        : this(textExtractor, parsers, NullDiagnostics.Instance)
    {
    }

    public Task<CreditCardStatementImportResult> ImportPdfAsync(
        Stream pdf,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        return Task.Run(
            () => ImportCoreAsync(pdf, cancellationToken),
            cancellationToken);
    }

    private async Task<CreditCardStatementImportResult> ImportCoreAsync(
        Stream pdf,
        CancellationToken cancellationToken)
    {
        using var copy = new MemoryStream();
        await CopyBoundedAsync(pdf, copy, cancellationToken)
            .ConfigureAwait(false);
        var bytes = copy.ToArray();
        var fingerprint = Convert.ToHexString(SHA256.HashData(bytes));
        copy.Position = 0;

        string text;
        var extractionTimer = Stopwatch.StartNew();
        try
        {
            text = await _textExtractor
                .ExtractTextAsync(copy, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            extractionTimer.Stop();
            _diagnostics.ExtractionCompleted(
                extractionTimer.Elapsed,
                hasUsableText: false);
            _diagnostics.OcrFallbackUsed(used: false);
            _diagnostics.ImportFailed(exception.GetType().Name);
            return Unreadable(fingerprint);
        }

        extractionTimer.Stop();
        var hasUsableText = HasUsableText(text);
        _diagnostics.ExtractionCompleted(
            extractionTimer.Elapsed,
            hasUsableText);
        _diagnostics.OcrFallbackUsed(used: false);
        if (!hasUsableText)
        {
            return Unreadable(fingerprint);
        }

        var parser = _parsers.FirstOrDefault(x => x.CanParse(text));
        _diagnostics.ParserSelected(parser?.BankName);
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

        var parseTimer = Stopwatch.StartNew();
        try
        {
            var result = parser.Parse(text, fingerprint);
            parseTimer.Stop();
            _diagnostics.ParseCompleted(
                parseTimer.Elapsed,
                result.HasRequiredFields,
                result.Warnings.Count);
            return result;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            parseTimer.Stop();
            _diagnostics.ParseCompleted(
                parseTimer.Elapsed,
                hasRequiredFields: false,
                warningCount: 1);
            _diagnostics.ImportFailed(exception.GetType().Name);
            return Unreadable(fingerprint);
        }
    }

    private static async Task CopyBoundedAsync(
        Stream source,
        Stream destination,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        var totalBytes = 0;
        int bytesRead;
        while ((bytesRead = await source
                   .ReadAsync(buffer, cancellationToken)
                   .ConfigureAwait(false)) > 0)
        {
            totalBytes += bytesRead;
            if (totalBytes > MaximumPdfBytes)
            {
                throw new InvalidDataException(
                    "Statement PDF exceeds the supported local import size.");
            }

            await destination
                .WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken)
                .ConfigureAwait(false);
        }
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

    private sealed class NullDiagnostics :
        ICreditCardStatementImportDiagnostics
    {
        public static NullDiagnostics Instance { get; } = new();

        public void ImportStarted() { }
        public void FileSelected() { }
        public void ExtractionCompleted(
            TimeSpan duration,
            bool hasUsableText) { }
        public void OcrFallbackUsed(bool used) { }
        public void ParserSelected(string? parserName) { }
        public void ParseCompleted(
            TimeSpan duration,
            bool hasRequiredFields,
            int warningCount) { }
        public void ImportFailed(string exceptionType) { }
    }
}
