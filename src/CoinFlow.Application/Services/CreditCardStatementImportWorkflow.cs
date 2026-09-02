using CoinFlow.Application.Abstractions;
using CoinFlow.Application.Models;
using System.Diagnostics;

namespace CoinFlow.Application.Services;

public sealed class CreditCardStatementImportWorkflow(
    ICreditCardStatementImporter importer,
    ICreditCardStatementPdfPicker picker,
    ICreditCardStatementImportDiagnostics diagnostics,
    CreditCardStatementImportOptions options)
{
    private int _isRunning;
    private int _hasActivePipeline;

    public bool IsRunning => Volatile.Read(ref _isRunning) == 1;

    public void NotifyPreviewStarted() => diagnostics.PreviewStarted();

    public async Task<CreditCardStatementImportAttempt> RunAsync(
        CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _hasActivePipeline) == 1)
        {
            return new CreditCardStatementImportAttempt(
                CreditCardStatementImportOutcome.AlreadyRunning);
        }

        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
        {
            return new CreditCardStatementImportAttempt(
                CreditCardStatementImportOutcome.AlreadyRunning);
        }

        diagnostics.ImportStarted();
        var importTimer = Stopwatch.StartNew();
        try
        {
            var selection = await picker
                .PickPdfAsync(cancellationToken)
                .ConfigureAwait(false);
            if (selection is null)
            {
                diagnostics.ImportCompleted(
                    nameof(CreditCardStatementImportOutcome.Cancelled),
                    importTimer.Elapsed);
                return new CreditCardStatementImportAttempt(
                    CreditCardStatementImportOutcome.Cancelled);
            }

            diagnostics.FileSelected();
            importTimer.Restart();
            if (Interlocked.CompareExchange(
                    ref _hasActivePipeline,
                    1,
                    0) != 0)
            {
                return new CreditCardStatementImportAttempt(
                    CreditCardStatementImportOutcome.AlreadyRunning);
            }

            var pipelineCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
            var pipeline = Task.Run(
                () => ProcessSelectionWithGuardAsync(
                    selection,
                    pipelineCancellation.Token),
                CancellationToken.None);
            var timeout = Task.Delay(options.AutomaticImportTimeout);
            var userCancellation = cancellationToken.CanBeCanceled
                ? Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                : Task.Delay(Timeout.InfiniteTimeSpan);
            var completed = await Task.WhenAny(
                    pipeline,
                    timeout,
                    userCancellation)
                .ConfigureAwait(false);

            if (pipeline.IsCompleted)
            {
                pipelineCancellation.Dispose();
                var result = await pipeline
                    .ConfigureAwait(false);
                diagnostics.ImportCompleted(
                    nameof(CreditCardStatementImportOutcome.Completed),
                    importTimer.Elapsed);
                return new CreditCardStatementImportAttempt(
                    CreditCardStatementImportOutcome.Completed,
                    result);
            }

            pipelineCancellation.Cancel();
            _ = ObserveLatePipelineAsync(
                pipeline,
                pipelineCancellation,
                diagnostics);

            var outcome = cancellationToken.IsCancellationRequested ||
                          completed == userCancellation
                ? CreditCardStatementImportOutcome.Cancelled
                : CreditCardStatementImportOutcome.TimedOut;
            diagnostics.ImportCompleted(
                outcome.ToString(),
                importTimer.Elapsed);
            return new CreditCardStatementImportAttempt(outcome);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            diagnostics.ImportCompleted(
                nameof(CreditCardStatementImportOutcome.Cancelled),
                importTimer.Elapsed);
            return new CreditCardStatementImportAttempt(
                CreditCardStatementImportOutcome.Cancelled);
        }
        catch (Exception exception)
        {
            diagnostics.ImportFailed(exception.GetType().Name);
            diagnostics.ImportCompleted(
                nameof(CreditCardStatementImportOutcome.Failed),
                importTimer.Elapsed);
            return new CreditCardStatementImportAttempt(
                CreditCardStatementImportOutcome.Failed);
        }
        finally
        {
            Volatile.Write(ref _isRunning, 0);
        }
    }

    private async Task<CreditCardStatementImportResult> ProcessSelectionAsync(
        ICreditCardStatementPdfSelection selection,
        CancellationToken cancellationToken)
    {
        await using var localPdf = await selection
            .CopyToLocalCacheAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var stream = localPdf.OpenRead();
        return await importer
            .ImportPdfAsync(stream, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<CreditCardStatementImportResult>
        ProcessSelectionWithGuardAsync(
            ICreditCardStatementPdfSelection selection,
            CancellationToken cancellationToken)
    {
        try
        {
            return await ProcessSelectionAsync(selection, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _hasActivePipeline, 0);
        }
    }

    private static async Task ObserveLatePipelineAsync(
        Task pipeline,
        CancellationTokenSource cancellation,
        ICreditCardStatementImportDiagnostics diagnostics)
    {
        try
        {
            await pipeline.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            diagnostics.ImportFailed(exception.GetType().Name);
        }
        finally
        {
            cancellation.Dispose();
        }
    }
}
