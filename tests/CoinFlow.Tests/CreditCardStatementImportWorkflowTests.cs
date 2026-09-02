using System.Diagnostics;
using CoinFlow.Application.Abstractions;
using CoinFlow.Application.Models;
using CoinFlow.Application.Services;
using CoinFlow.Infrastructure.Imports;

namespace CoinFlow.Tests;

public sealed class CreditCardStatementImportWorkflowTests
{
    [Fact]
    public async Task PickerCancel_IsNormalExitAndClearsRunningState()
    {
        var importer = new StubImporter();
        var workflow = Workflow(importer, new StubPicker(null));

        var attempt = await workflow.RunAsync();

        Assert.Equal(
            CreditCardStatementImportOutcome.Cancelled,
            attempt.Outcome);
        Assert.False(workflow.IsRunning);
        Assert.Equal(0, importer.CallCount);
    }

    [Fact]
    public async Task ImportFailure_ClearsRunningStateWithoutResult()
    {
        var diagnostics = new RecordingDiagnostics();
        var workflow = Workflow(
            new StubImporter(
                exception: new InvalidDataException("sensitive")),
            new StubPicker(new StubSelection()),
            diagnostics);

        var attempt = await workflow.RunAsync();

        Assert.Equal(
            CreditCardStatementImportOutcome.Failed,
            attempt.Outcome);
        Assert.Null(attempt.Result);
        Assert.False(workflow.IsRunning);
        Assert.Equal("InvalidDataException", diagnostics.ExceptionType);
    }

    [Fact]
    public async Task DoubleTap_IsRejectedWhilePickerIsOpen()
    {
        var picker = new BlockingPicker();
        var workflow = Workflow(new StubImporter(), picker);
        var first = workflow.RunAsync();
        await picker.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var second = await workflow.RunAsync();

        Assert.Equal(
            CreditCardStatementImportOutcome.AlreadyRunning,
            second.Outcome);
        picker.Release.SetResult(null);
        Assert.Equal(
            CreditCardStatementImportOutcome.Cancelled,
            (await first).Outcome);
        Assert.False(workflow.IsRunning);
    }

    [Fact]
    public async Task PartialParse_IsReturnedForManualPrefill()
    {
        var partial = new CreditCardStatementImportResult
        {
            StatementAmount = 100_804.94m,
            DueDate = new DateOnly(2026, 9, 7),
            Warnings = ["Asgari ödeme bulunamadı."]
        };
        var workflow = Workflow(
            new StubImporter(partial),
            new StubPicker(new StubSelection()));

        var attempt = await workflow.RunAsync();

        Assert.True(attempt.IsCompleted);
        Assert.Same(partial, attempt.Result);
        Assert.Equal(100_804.94m, attempt.Result!.StatementAmount);
        Assert.Equal(new DateOnly(2026, 9, 7), attempt.Result.DueDate);
        Assert.False(attempt.Result.HasRequiredFields);
    }

    [Fact]
    public async Task Watchdog_ReturnsTimedOutAndDoesNotRetryHungImporter()
    {
        var importer = new ReleasableImporter();
        var selection = new StubSelection();
        var workflow = Workflow(
            importer,
            new StubPicker(selection),
            timeout: TimeSpan.FromMilliseconds(75));
        var timer = Stopwatch.StartNew();

        var attempt = await workflow.RunAsync();

        timer.Stop();
        Assert.Equal(
            CreditCardStatementImportOutcome.TimedOut,
            attempt.Outcome);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(1));
        Assert.False(workflow.IsRunning);
        Assert.Equal(1, importer.CallCount);
        Assert.Equal(1, selection.CopyCount);

        var repeated = await workflow.RunAsync();
        Assert.Equal(
            CreditCardStatementImportOutcome.AlreadyRunning,
            repeated.Outcome);
        Assert.Equal(1, importer.CallCount);

        importer.Release.SetResult(new CreditCardStatementImportResult());
        await selection.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, importer.CallCount);
    }

    [Fact]
    public async Task Cancellation_ReturnsPromptlyAndLateWorkerCleansLocalCopy()
    {
        var importer = new ReleasableImporter();
        var selection = new StubSelection();
        var workflow = Workflow(
            importer,
            new StubPicker(selection),
            timeout: TimeSpan.FromSeconds(10));
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(75));

        var attempt = await workflow.RunAsync(cancellation.Token);

        Assert.Equal(
            CreditCardStatementImportOutcome.Cancelled,
            attempt.Outcome);
        Assert.False(workflow.IsRunning);
        Assert.Equal(1, importer.CallCount);

        importer.Release.SetResult(new CreditCardStatementImportResult());
        await selection.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task LocalCopy_IsOpenedOnceAndDisposedAfterSuccess()
    {
        var selection = new StubSelection();
        var workflow = Workflow(
            new StubImporter(),
            new StubPicker(selection));

        var attempt = await workflow.RunAsync();

        Assert.Equal(
            CreditCardStatementImportOutcome.Completed,
            attempt.Outcome);
        Assert.Equal(1, selection.CopyCount);
        Assert.Equal(1, selection.OpenCount);
        Assert.True(selection.Disposed.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task SyntheticHeaderPipeline_ReportsBoundedStageTimings()
    {
        var diagnostics = new RecordingDiagnostics();
        var importer = new CreditCardStatementImporter(
            new DelayedTextExtractor(),
            [new GarantiBonusStatementParser()],
            diagnostics);
        var selection = new MeasuredSelection(diagnostics);
        var workflow = Workflow(
            importer,
            new StubPicker(selection),
            diagnostics);

        var attempt = await workflow.RunAsync();

        Assert.True(attempt.Result!.HasRequiredFields);
        Assert.Equal(1, selection.CopyCount);
        Assert.False(diagnostics.OcrUsed);
        Assert.True(diagnostics.CopyDuration > TimeSpan.Zero);
        Assert.True(diagnostics.ExtractionDuration > TimeSpan.Zero);
        Assert.True(diagnostics.ParseDuration >= TimeSpan.Zero);
        Assert.True(diagnostics.TotalDuration > TimeSpan.Zero);
        Console.WriteLine(
            "PIPELINE_TIMINGS copy={0:N1}ms extraction={1:N1}ms " +
            "quality=<1ms render=0ms ocr=0ms parse={2:N1}ms total={3:N1}ms",
            diagnostics.CopyDuration.TotalMilliseconds,
            diagnostics.ExtractionDuration.TotalMilliseconds,
            diagnostics.ParseDuration.TotalMilliseconds,
            diagnostics.TotalDuration.TotalMilliseconds);
    }

    private static CreditCardStatementImportWorkflow Workflow(
        ICreditCardStatementImporter importer,
        ICreditCardStatementPdfPicker picker,
        RecordingDiagnostics? diagnostics = null,
        TimeSpan? timeout = null) => new(
            importer,
            picker,
            diagnostics ?? new RecordingDiagnostics(),
            new CreditCardStatementImportOptions(
                timeout ?? TimeSpan.FromSeconds(15)));

    private sealed class StubImporter(
        CreditCardStatementImportResult? result = null,
        Exception? exception = null) : ICreditCardStatementImporter
    {
        public int CallCount { get; private set; }

        public Task<CreditCardStatementImportResult> ImportPdfAsync(
            Stream pdf,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return exception is null
                ? Task.FromResult(result ??
                    new CreditCardStatementImportResult())
                : Task.FromException<CreditCardStatementImportResult>(
                    exception);
        }
    }

    private sealed class ReleasableImporter :
        ICreditCardStatementImporter
    {
        public int CallCount { get; private set; }
        public TaskCompletionSource<CreditCardStatementImportResult> Release
            { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<CreditCardStatementImportResult> ImportPdfAsync(
            Stream pdf,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Release.Task;
        }
    }

    private sealed class StubPicker(
        ICreditCardStatementPdfSelection? selection) :
        ICreditCardStatementPdfPicker
    {
        public Task<ICreditCardStatementPdfSelection?> PickPdfAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(selection);
    }

    private sealed class BlockingPicker : ICreditCardStatementPdfPicker
    {
        public TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<ICreditCardStatementPdfSelection?> Release
            { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ICreditCardStatementPdfSelection?> PickPdfAsync(
            CancellationToken cancellationToken = default)
        {
            Entered.SetResult();
            return Release.Task;
        }
    }

    private sealed class StubSelection :
        ICreditCardStatementPdfSelection,
        ICreditCardStatementLocalPdf
    {
        public int CopyCount { get; private set; }
        public int OpenCount { get; private set; }
        public TaskCompletionSource Disposed { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ICreditCardStatementLocalPdf> CopyToLocalCacheAsync(
            CancellationToken cancellationToken = default)
        {
            CopyCount++;
            return Task.FromResult<ICreditCardStatementLocalPdf>(this);
        }

        public Stream OpenRead()
        {
            OpenCount++;
            return new MemoryStream([1, 2, 3]);
        }

        public ValueTask DisposeAsync()
        {
            Disposed.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MeasuredSelection(
        RecordingDiagnostics diagnostics) :
        ICreditCardStatementPdfSelection,
        ICreditCardStatementLocalPdf
    {
        public int CopyCount { get; private set; }

        public async Task<ICreditCardStatementLocalPdf> CopyToLocalCacheAsync(
            CancellationToken cancellationToken = default)
        {
            CopyCount++;
            diagnostics.CopyStarted();
            var timer = Stopwatch.StartNew();
            await Task.Delay(20, cancellationToken);
            diagnostics.CopyCompleted(timer.Elapsed);
            return this;
        }

        public Stream OpenRead() => new MemoryStream([1, 2, 3]);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class DelayedTextExtractor : IPdfTextExtractor
    {
        public async Task<string> ExtractTextAsync(
            Stream pdf,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(30, cancellationToken);
            return """
                GARANTI BBVA BONUS EKSTRE OZETI
                EKSTRE TARIHI 24.08.2026
                SON ODEME TARIHI 03.09.2026
                EKSTRE TUTARI 15.000,00 TL
                ASGARI ODEME TUTARI 6.000,00 TL
                """;
        }
    }

    private sealed class RecordingDiagnostics :
        ICreditCardStatementImportDiagnostics
    {
        public string? ExceptionType { get; private set; }
        public TimeSpan CopyDuration { get; private set; }
        public TimeSpan ExtractionDuration { get; private set; }
        public TimeSpan ParseDuration { get; private set; }
        public TimeSpan TotalDuration { get; private set; }
        public bool OcrUsed { get; private set; }

        public void ImportStarted() { }
        public void FileSelected() { }
        public void CopyStarted() { }
        public void CopyCompleted(TimeSpan duration) =>
            CopyDuration = duration;
        public void ExtractionStarted() { }
        public void ExtractionCompleted(
            TimeSpan duration,
            bool hasUsableText) => ExtractionDuration = duration;
        public void QualityAssessed(bool hasUsableText) { }
        public void OcrFallbackUsed(bool used) => OcrUsed = used;
        public void ParseStarted() { }
        public void ParserSelected(string? parserName) { }
        public void ParseCompleted(
            TimeSpan duration,
            bool hasRequiredFields,
            int warningCount) => ParseDuration = duration;
        public void PreviewStarted() { }
        public void ImportCompleted(string outcome, TimeSpan duration) =>
            TotalDuration = duration;
        public void ImportFailed(string exceptionType) =>
            ExceptionType = exceptionType;
    }
}
