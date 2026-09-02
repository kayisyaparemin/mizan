using System.Text;
using System.Diagnostics;
using CoinFlow.Application.Abstractions;
using CoinFlow.Application.Models;
using CoinFlow.Infrastructure.Imports;

namespace CoinFlow.Tests;

public sealed class CreditCardStatementImportTests
{
    [Fact]
    public void AxessParser_ReadsAnonymizedStatementTruth()
    {
        var parser = new AkbankAxessStatementParser();
        const string text = """
            AKBANK T.A.S.
            AXESS KREDI KARTI HESAP OZETI
            Kart No ****6822
            HESAP KESIM TARIHI 28.08.2026
            SON ODEME TARIHI 07.09.2026
            DONEM BORCU 100.804,94 TL
            ASGARI ODEME TUTARI 40.321,97 TL
            BIR SONRAKI HESAP KESIM TARIHI 28.09.2026
            BIR SONRAKI SON ODEME TARIHI 08.10.2026
            """;

        var result = parser.Parse(text, "ABC");

        Assert.True(parser.CanParse(text));
        Assert.Equal("Akbank Axess", result.DetectedBank);
        Assert.Equal("6822", result.CardLast4);
        Assert.Equal(new DateOnly(2026, 8, 28), result.StatementDate);
        Assert.Equal(new DateOnly(2026, 9, 7), result.DueDate);
        Assert.Equal(100_804.94m, result.StatementAmount);
        Assert.Equal(40_321.97m, result.MinimumPaymentAmount);
        Assert.Equal(new DateOnly(2026, 9, 28),
            result.NextStatementDate);
        Assert.Equal(new DateOnly(2026, 10, 8), result.NextDueDate);
        Assert.True(result.HasRequiredFields);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void GarantiParser_KeepsExactActualStatementDate()
    {
        var parser = new GarantiBonusStatementParser();
        const string text = """
            GARANTI BBVA
            BONUS EKSTRE OZETI
            Kart No ****1234
            EKSTRE TARIHI 24.08.2026
            SON ODEME TARIHI 03.09.2026
            EKSTRE TUTARI 15.000,00 TL
            ASGARI ODEME TUTARI 6.000,00 TL
            BIR SONRAKI EKSTRE TARIHI 25.09.2026
            BIR SONRAKI SON ODEME TARIHI 05.10.2026
            """;

        var result = parser.Parse(text, "DEF");

        Assert.True(parser.CanParse(text));
        Assert.Equal("Garanti BBVA Bonus", result.DetectedBank);
        Assert.Equal(new DateOnly(2026, 8, 24), result.StatementDate);
        Assert.Equal(15_000m, result.StatementAmount);
        Assert.Equal(6_000m, result.MinimumPaymentAmount);
        Assert.Equal(new DateOnly(2026, 9, 25),
            result.NextStatementDate);
        Assert.Equal(new DateOnly(2026, 10, 5), result.NextDueDate);
    }

    [Fact]
    public async Task Importer_ReturnsLocalFingerprintAndParserResult()
    {
        var importer = new CreditCardStatementImporter(
            new FixedTextExtractor("""
                GARANTI BBVA
                BONUS EKSTRE OZETI
                EKSTRE TARIHI 24.08.2026
                SON ODEME TARIHI 03.09.2026
                EKSTRE TUTARI 15.000,00 TL
                ASGARI ODEME TUTARI 6.000,00 TL
                """),
            [new GarantiBonusStatementParser()]);

        await using var pdf = new MemoryStream(
            Encoding.UTF8.GetBytes("not a real pdf but enough for fingerprint"));
        var result = await importer.ImportPdfAsync(pdf);

        Assert.Equal("Garanti BBVA Bonus", result.DetectedBank);
        Assert.NotNull(result.SourceDocumentFingerprint);
        Assert.Equal(64, result.SourceDocumentFingerprint!.Length);
    }

    [Fact]
    public async Task Importer_DoesNotInventFieldsWhenTextIsUnusable()
    {
        var importer = new CreditCardStatementImporter(
            new FixedTextExtractor("..."),
            [new AkbankAxessStatementParser()]);

        await using var pdf = new MemoryStream([1, 2, 3]);
        var result = await importer.ImportPdfAsync(pdf);

        Assert.False(result.HasRequiredFields);
        Assert.False(result.TextExtractionSucceeded);
        Assert.Contains("otomatik okunamadı", result.Warnings[0]);
    }

    [Fact]
    public async Task Importer_ReturnsImmediatelyWhileSynchronousExtractionRuns()
    {
        var importer = new CreditCardStatementImporter(
            new SlowSynchronousExtractor(),
            [new GarantiBonusStatementParser()]);
        await using var pdf = new MemoryStream([1, 2, 3]);
        var timer = Stopwatch.StartNew();

        var import = importer.ImportPdfAsync(pdf);

        timer.Stop();
        Assert.True(
            timer.Elapsed < TimeSpan.FromMilliseconds(200),
            $"Import call blocked for {timer.Elapsed.TotalMilliseconds:N0} ms.");
        Assert.False(import.IsCompleted);
        Assert.True((await import).HasRequiredFields);
    }

    [Fact]
    public async Task Importer_BoundedPipelineExtractsAndParsesOnlyOnce()
    {
        var extractor = new CountingExtractor();
        var parser = new CountingParser();
        var importer = new CreditCardStatementImporter(
            extractor,
            [parser]);
        await using var pdf = new MemoryStream([1, 2, 3]);

        var result = await importer.ImportPdfAsync(pdf);

        Assert.True(result.HasRequiredFields);
        Assert.Equal(1, extractor.CallCount);
        Assert.Equal(1, parser.CanParseCount);
        Assert.Equal(1, parser.ParseCount);
    }

    [Fact]
    public async Task Importer_ParserFailureReturnsManualFallback()
    {
        var importer = new CreditCardStatementImporter(
            new CountingExtractor(),
            [new ThrowingParser()]);
        await using var pdf = new MemoryStream([1, 2, 3]);

        var result = await importer.ImportPdfAsync(pdf);

        Assert.False(result.HasRequiredFields);
        Assert.Contains("otomatik okunamadı", result.Warnings[0]);
    }

    [Fact]
    public async Task AxessBadEncoding_StopsAfterOneNativeExtraction()
    {
        var extractor = new BadEncodingExtractor();
        var parser = new CountingParser();
        var importer = new CreditCardStatementImporter(
            extractor,
            [parser]);
        await using var pdf = new MemoryStream([1, 2, 3]);

        var result = await importer.ImportPdfAsync(pdf);

        Assert.False(result.HasRequiredFields);
        Assert.False(result.TextExtractionSucceeded);
        Assert.Equal(1, extractor.CallCount);
        Assert.Equal(0, parser.CanParseCount);
        Assert.Equal(0, parser.ParseCount);
    }

    private sealed class FixedTextExtractor(string text) : IPdfTextExtractor
    {
        public Task<string> ExtractTextAsync(
            Stream pdf,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(text);
    }

    private sealed class SlowSynchronousExtractor : IPdfTextExtractor
    {
        public Task<string> ExtractTextAsync(
            Stream pdf,
            CancellationToken cancellationToken = default)
        {
            Thread.Sleep(350);
            return Task.FromResult(ValidText());
        }
    }

    private sealed class CountingExtractor : IPdfTextExtractor
    {
        public int CallCount { get; private set; }

        public Task<string> ExtractTextAsync(
            Stream pdf,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(ValidText());
        }
    }

    private sealed class BadEncodingExtractor : IPdfTextExtractor
    {
        public int CallCount { get; private set; }

        public Task<string> ExtractTextAsync(
            Stream pdf,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new string('\0', 500));
        }
    }

    private class CountingParser : ICreditCardStatementParser
    {
        public string BankName => "Test";
        public int CanParseCount { get; private set; }
        public int ParseCount { get; private set; }

        public virtual bool CanParse(string text)
        {
            CanParseCount++;
            return true;
        }

        public virtual CreditCardStatementImportResult Parse(
            string text,
            string sourceDocumentFingerprint)
        {
            ParseCount++;
            return new CreditCardStatementImportResult
            {
                StatementDate = new DateOnly(2026, 8, 28),
                DueDate = new DateOnly(2026, 9, 7),
                StatementAmount = 100m,
                MinimumPaymentAmount = 40m
            };
        }
    }

    private sealed class ThrowingParser : CountingParser
    {
        public override CreditCardStatementImportResult Parse(
            string text,
            string sourceDocumentFingerprint) =>
            throw new FormatException("statement contents");
    }

    private static string ValidText() => """
        GARANTI BBVA BONUS EKSTRE OZETI
        EKSTRE TARIHI 24.08.2026
        SON ODEME TARIHI 03.09.2026
        EKSTRE TUTARI 15.000,00 TL
        ASGARI ODEME TUTARI 6.000,00 TL
        """;
}
