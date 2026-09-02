using CoinFlow.Application.Abstractions;
using CoinFlow.Application.Models;

namespace CoinFlow.Application.Services;

public sealed class CreditCardStatementImportWorkflow(
    ICreditCardStatementImporter importer,
    ICreditCardStatementPdfPicker picker,
    ICreditCardStatementImportDiagnostics diagnostics)
{
    private int _isRunning;

    public bool IsRunning => Volatile.Read(ref _isRunning) == 1;

    public async Task<CreditCardStatementImportAttempt> RunAsync(
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
        {
            return new CreditCardStatementImportAttempt(
                CreditCardStatementImportOutcome.AlreadyRunning);
        }

        diagnostics.ImportStarted();
        try
        {
            var stream = await picker
                .PickPdfAsync(cancellationToken)
                .ConfigureAwait(false);
            if (stream is null)
            {
                return new CreditCardStatementImportAttempt(
                    CreditCardStatementImportOutcome.Cancelled);
            }

            diagnostics.FileSelected();
            await using (stream.ConfigureAwait(false))
            {
                var result = await importer
                    .ImportPdfAsync(stream, cancellationToken)
                    .ConfigureAwait(false);
                return new CreditCardStatementImportAttempt(
                    CreditCardStatementImportOutcome.Completed,
                    result);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return new CreditCardStatementImportAttempt(
                CreditCardStatementImportOutcome.Cancelled);
        }
        catch (Exception exception)
        {
            diagnostics.ImportFailed(exception.GetType().Name);
            return new CreditCardStatementImportAttempt(
                CreditCardStatementImportOutcome.Failed);
        }
        finally
        {
            Volatile.Write(ref _isRunning, 0);
        }
    }
}
