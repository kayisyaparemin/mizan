
namespace Mizan.Application.Models;

public sealed record StatementFieldConfidence(
    bool StatementDate,
    bool DueDate,
    bool StatementAmount,
    bool MinimumPaymentAmount,
    bool NextStatementDate,
    bool NextDueDate);

public sealed record CreditCardStatementImportResult
{
    public string DetectedBank { get; init; } = string.Empty;
    public string? CardLast4 { get; init; }
    public DateOnly? StatementDate { get; init; }
    public DateOnly? DueDate { get; init; }
    public decimal? StatementAmount { get; init; }
    public decimal? MinimumPaymentAmount { get; init; }
    public DateOnly? NextStatementDate { get; init; }
    public DateOnly? NextDueDate { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public decimal Confidence { get; init; }
    public StatementFieldConfidence FieldConfidence { get; init; } =
        new(false, false, false, false, false, false);
    public string? SourceDocumentFingerprint { get; init; }
    public bool TextExtractionSucceeded { get; init; } = true;

    public bool HasRequiredFields =>
        StatementDate is not null &&
        DueDate is not null &&
        StatementAmount is not null &&
        MinimumPaymentAmount is not null;
}

public enum CreditCardStatementImportOutcome
{
    Completed,
    Cancelled,
    TimedOut,
    AlreadyRunning,
    Failed
}

public sealed record CreditCardStatementImportOptions(
    TimeSpan AutomaticImportTimeout)
{
    public static CreditCardStatementImportOptions Default { get; } =
        new(TimeSpan.FromSeconds(15));
}

public sealed record CreditCardStatementImportAttempt(
    CreditCardStatementImportOutcome Outcome,
    CreditCardStatementImportResult? Result = null)
{
    public bool IsCompleted =>
        Outcome == CreditCardStatementImportOutcome.Completed &&
        Result is not null;
}
