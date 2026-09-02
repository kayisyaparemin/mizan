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

public interface ICreditCardStatementPdfPicker
{
    Task<Stream?> PickPdfAsync(
        CancellationToken cancellationToken = default);
}

public interface ICreditCardStatementImportDiagnostics
{
    void ImportStarted();
    void FileSelected();
    void ExtractionCompleted(TimeSpan duration, bool hasUsableText);
    void OcrFallbackUsed(bool used);
    void ParserSelected(string? parserName);
    void ParseCompleted(
        TimeSpan duration,
        bool hasRequiredFields,
        int warningCount);
    void ImportFailed(string exceptionType);
}
