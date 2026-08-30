using System.Text;
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

    private sealed class FixedTextExtractor(string text) : IPdfTextExtractor
    {
        public Task<string> ExtractTextAsync(
            Stream pdf,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(text);
    }
}
