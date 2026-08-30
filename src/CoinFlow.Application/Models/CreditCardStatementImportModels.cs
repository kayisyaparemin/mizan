using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Models;

public sealed record StatementFieldConfidence(
    bool StatementDate,
    bool DueDate,
    bool StatementAmount,
    bool MinimumPaymentAmount,
    bool NextStatementDate,
    bool NextDueDate);

public sealed record ImportedFutureInstallment(
    string Description,
    DateOnly PostingDate,
    decimal Amount,
    int RemainingCount,
    decimal Confidence);

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
    public decimal? ReportedInterestRate { get; init; }
    public IReadOnlyList<ImportedFutureInstallment> FutureInstallments { get; init; } = [];
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

    public CreditCardStatement ToStatement(Guid creditCardId, DateTimeOffset now) =>
        new()
        {
            CreditCardId = creditCardId,
            StatementDate = StatementDate ??
                throw new InvalidOperationException("Kesim tarihi bulunamadı."),
            DueDate = DueDate ??
                throw new InvalidOperationException("Son ödeme tarihi bulunamadı."),
            StatementAmount = StatementAmount ??
                throw new InvalidOperationException("Ekstre tutarı bulunamadı."),
            MinimumPaymentAmount = MinimumPaymentAmount ??
                throw new InvalidOperationException("Asgari ödeme bulunamadı."),
            NextStatementDate = NextStatementDate,
            NextDueDate = NextDueDate,
            Source = CreditCardStatementSource.PdfImport,
            SourceDocumentFingerprint = SourceDocumentFingerprint,
            ImportedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
}
