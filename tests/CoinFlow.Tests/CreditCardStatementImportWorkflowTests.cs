using CoinFlow.Application.Abstractions;
using CoinFlow.Application.Models;
using CoinFlow.Application.Services;

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
        var workflow = new CreditCardStatementImportWorkflow(
            new StubImporter(exception: new InvalidDataException("sensitive")),
            new StubPicker(new MemoryStream([1, 2, 3])),
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
            new StubPicker(new MemoryStream([1, 2, 3])));

        var attempt = await workflow.RunAsync();

        Assert.True(attempt.IsCompleted);
        Assert.Same(partial, attempt.Result);
        Assert.Equal(100_804.94m, attempt.Result!.StatementAmount);
        Assert.Equal(new DateOnly(2026, 9, 7), attempt.Result.DueDate);
        Assert.False(attempt.Result.HasRequiredFields);
    }

    private static CreditCardStatementImportWorkflow Workflow(
        ICreditCardStatementImporter importer,
        ICreditCardStatementPdfPicker picker) => new(
            importer,
            picker,
            new RecordingDiagnostics());

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

    private sealed class StubPicker(Stream? stream) :
        ICreditCardStatementPdfPicker
    {
        public Task<Stream?> PickPdfAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(stream);
    }

    private sealed class BlockingPicker : ICreditCardStatementPdfPicker
    {
        public TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<Stream?> Release { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Stream?> PickPdfAsync(
            CancellationToken cancellationToken = default)
        {
            Entered.SetResult();
            return Release.Task;
        }
    }

    private sealed class RecordingDiagnostics :
        ICreditCardStatementImportDiagnostics
    {
        public string? ExceptionType { get; private set; }

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
        public void ImportFailed(string exceptionType) =>
            ExceptionType = exceptionType;
    }
}
