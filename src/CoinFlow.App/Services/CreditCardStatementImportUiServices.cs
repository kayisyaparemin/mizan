using System.Diagnostics;
using CoinFlow.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace CoinFlow.App.Services;

public sealed class CreditCardStatementPdfPicker(
    ICreditCardStatementImportDiagnostics diagnostics) :
    ICreditCardStatementPdfPicker
{
    public async Task<ICreditCardStatementPdfSelection?> PickPdfAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var file = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Ekstre PDF seç",
            FileTypes = FilePickerFileType.Pdf
        });
        cancellationToken.ThrowIfCancellationRequested();
        return file is null
            ? null
            : new PickedPdfSelection(file, diagnostics);
    }

    private sealed class PickedPdfSelection(
        FileResult file,
        ICreditCardStatementImportDiagnostics diagnostics) :
        ICreditCardStatementPdfSelection
    {
        private const int MaximumPdfBytes = 32 * 1024 * 1024;

        public async Task<ICreditCardStatementLocalPdf> CopyToLocalCacheAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            diagnostics.CopyStarted();
            var timer = Stopwatch.StartNew();
            var localPath = Path.Combine(
                FileSystem.CacheDirectory,
                $"statement-import-{Guid.NewGuid():N}.pdf");

            try
            {
                await using var source = await file
                    .OpenReadAsync()
                    .ConfigureAwait(false);
                await using var destination = new FileStream(
                    localPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await CopyBoundedAsync(
                        source,
                        destination,
                        cancellationToken)
                    .ConfigureAwait(false);
                await destination
                    .FlushAsync(cancellationToken)
                    .ConfigureAwait(false);
                timer.Stop();
                diagnostics.CopyCompleted(timer.Elapsed);
                return new LocalPdf(localPath, cancellationToken);
            }
            catch
            {
                TryDelete(localPath);
                throw;
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
                        "Statement PDF exceeds the local import size limit.");
                }

                await destination
                    .WriteAsync(
                        buffer.AsMemory(0, bytesRead),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private sealed class LocalPdf : ICreditCardStatementLocalPdf
    {
        private readonly string _path;
        private readonly CancellationTokenRegistration _cleanupRegistration;

        public LocalPdf(string path, CancellationToken cancellationToken)
        {
            _path = path;
            _cleanupRegistration = cancellationToken.Register(
                static state => TryDelete((string)state!),
                path);
        }

        public Stream OpenRead() => new FileStream(
            _path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        public ValueTask DisposeAsync()
        {
            _cleanupRegistration.Dispose();
            TryDelete(_path);
            return ValueTask.CompletedTask;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

public sealed class DevelopmentCreditCardStatementImportDiagnostics(
    ILogger<DevelopmentCreditCardStatementImportDiagnostics> logger)
    : ICreditCardStatementImportDiagnostics
{
    public void ImportStarted() => Log("IMPORT START");

    public void FileSelected() => Log("FILE SELECTED");

    public void CopyStarted() => Log("COPY START");

    public void CopyCompleted(TimeSpan duration) =>
        Log("COPY END", duration.TotalMilliseconds);

    public void ExtractionStarted() => Log("TEXT EXTRACTION START");

    public void ExtractionCompleted(
        TimeSpan duration,
        bool hasUsableText) =>
        Log(
            "TEXT EXTRACTION END",
            duration.TotalMilliseconds,
            hasUsableText ? "usable" : "unusable");

    public void QualityAssessed(bool hasUsableText) =>
        Log("QUALITY", result: hasUsableText ? "GOOD" : "BAD");

    public void OcrFallbackUsed(bool used) =>
        Log("OCR FALLBACK", result: used ? "USED" : "NOT_USED");

    public void ParseStarted() => Log("PARSE START");

    public void ParserSelected(string? parserName) =>
        Log("PARSER SELECTED", result: parserName ?? "none");

    public void ParseCompleted(
        TimeSpan duration,
        bool hasRequiredFields,
        int warningCount) =>
        Log(
            "PARSE END",
            duration.TotalMilliseconds,
            hasRequiredFields ? "required-fields-found" :
                "required-fields-missing",
            warningCount);

    public void PreviewStarted() => Log("PREVIEW START");

    public void ImportCompleted(string outcome, TimeSpan duration) =>
        Log(
            "IMPORT END",
            duration.TotalMilliseconds,
            outcome);

    public void ImportFailed(string exceptionType) =>
        logger.LogWarning(
            "Statement import stage={Stage} exceptionType={ExceptionType} thread={ThreadId} isMainThread={IsMainThread}",
            "IMPORT FAILURE",
            exceptionType,
            Environment.CurrentManagedThreadId,
            MainThread.IsMainThread);

    private void Log(
        string stage,
        double? durationMs = null,
        string? result = null,
        int? warningCount = null) =>
        logger.LogInformation(
            "Statement import stage={Stage} durationMs={DurationMs} result={Result} warningCount={WarningCount} thread={ThreadId} isMainThread={IsMainThread}",
            stage,
            durationMs,
            result,
            warningCount,
            Environment.CurrentManagedThreadId,
            MainThread.IsMainThread);
}

public sealed class NullCreditCardStatementImportDiagnostics :
    ICreditCardStatementImportDiagnostics
{
    public void ImportStarted() { }
    public void FileSelected() { }
    public void CopyStarted() { }
    public void CopyCompleted(TimeSpan duration) { }
    public void ExtractionStarted() { }
    public void ExtractionCompleted(
        TimeSpan duration,
        bool hasUsableText) { }
    public void QualityAssessed(bool hasUsableText) { }
    public void OcrFallbackUsed(bool used) { }
    public void ParseStarted() { }
    public void ParserSelected(string? parserName) { }
    public void ParseCompleted(
        TimeSpan duration,
        bool hasRequiredFields,
        int warningCount) { }
    public void PreviewStarted() { }
    public void ImportCompleted(string outcome, TimeSpan duration) { }
    public void ImportFailed(string exceptionType) { }
}
