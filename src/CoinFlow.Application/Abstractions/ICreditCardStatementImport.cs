using CoinFlow.Application.Models;

namespace CoinFlow.Application.Abstractions;

public interface IPdfTextExtractor
{
    Task<string> ExtractTextAsync(
        Stream pdf,
        CancellationToken cancellationToken = default);
}

public interface ICreditCardStatementParser
{
    string BankName { get; }
    bool CanParse(string text);
    CreditCardStatementImportResult Parse(
        string text,
        string sourceDocumentFingerprint);
}

public interface ICreditCardStatementImporter
{
    Task<CreditCardStatementImportResult> ImportPdfAsync(
        Stream pdf,
        CancellationToken cancellationToken = default);
}
