using CoinFlow.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace CoinFlow.App.Services;

public sealed class CreditCardStatementPdfPicker :
    ICreditCardStatementPdfPicker
{
    public async Task<Stream?> PickPdfAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var file = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Ekstre PDF seç",
            FileTypes = FilePickerFileType.Pdf
        });
        cancellationToken.ThrowIfCancellationRequested();
        return file is null ? null : await file.OpenReadAsync();
    }
}

public sealed class DevelopmentCreditCardStatementImportDiagnostics(
    ILogger<DevelopmentCreditCardStatementImportDiagnostics> logger)
    : ICreditCardStatementImportDiagnostics
{
    public void ImportStarted() =>
        logger.LogInformation("Statement import started");

    public void FileSelected() =>
        logger.LogInformation("Statement file selected");

    public void ExtractionCompleted(
        TimeSpan duration,
        bool hasUsableText) =>
        logger.LogInformation(
            "Statement extraction completed in {DurationMs} ms; usable text: {HasUsableText}",
            duration.TotalMilliseconds,
            hasUsableText);

    public void OcrFallbackUsed(bool used) =>
        logger.LogInformation(
            "Statement OCR fallback used: {OcrFallbackUsed}",
            used);

    public void ParserSelected(string? parserName) =>
        logger.LogInformation(
            "Statement parser selected: {ParserName}",
            parserName ?? "none");

    public void ParseCompleted(
        TimeSpan duration,
        bool hasRequiredFields,
        int warningCount) =>
        logger.LogInformation(
            "Statement parse completed in {DurationMs} ms; required fields: {HasRequiredFields}; warnings: {WarningCount}",
            duration.TotalMilliseconds,
            hasRequiredFields,
            warningCount);

    public void ImportFailed(string exceptionType) =>
        logger.LogWarning(
            "Statement import failed with {ExceptionType}",
            exceptionType);
}

public sealed class NullCreditCardStatementImportDiagnostics :
    ICreditCardStatementImportDiagnostics
{
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
