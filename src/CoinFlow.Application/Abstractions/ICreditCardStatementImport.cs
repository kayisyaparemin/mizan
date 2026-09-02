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
    Task<ICreditCardStatementPdfSelection?> PickPdfAsync(
        CancellationToken cancellationToken = default);
}

public interface ICreditCardStatementPdfSelection
{
    Task<ICreditCardStatementLocalPdf> CopyToLocalCacheAsync(
        CancellationToken cancellationToken = default);
}

public interface ICreditCardStatementLocalPdf : IAsyncDisposable
{
    Stream OpenRead();
}

public interface ICreditCardStatementImportDiagnostics
{
    void ImportStarted();
    void FileSelected();
    void CopyStarted();
    void CopyCompleted(TimeSpan duration);
    void ExtractionStarted();
    void ExtractionCompleted(TimeSpan duration, bool hasUsableText);
    void QualityAssessed(bool hasUsableText);
    void OcrFallbackUsed(bool used);
    void ParseStarted();
    void ParserSelected(string? parserName);
    void ParseCompleted(
        TimeSpan duration,
        bool hasRequiredFields,
        int warningCount);
    void PreviewStarted();
    void ImportCompleted(string outcome, TimeSpan duration);
    void ImportFailed(string exceptionType);
}
