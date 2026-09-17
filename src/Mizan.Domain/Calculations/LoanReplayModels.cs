using Mizan.Domain.Models;

namespace Mizan.Domain.Calculations;

public enum LoanPaymentKind
{
    Installment,
    EarlyClosure,
    PartialPrepayment
}

/// <summary>
/// Kredinin gelecekteki tek bir ödemesi. Taksitlerde <see cref="SourceId"/>
/// kredinin, erken ödemelerde <see cref="LoanPrepayment"/>'ın kimliğidir.
/// </summary>
public sealed record LoanScheduledPayment(
    DateOnly Date,
    decimal Amount,
    LoanPaymentKind Kind,
    Guid SourceId,
    bool IsFinal);

/// <summary>
/// Olayların kredi üstünde oynatılmış hâli.
/// </summary>
/// <param name="StateAfterEvent">
/// Her olaydan hemen sonraki kanonik kredi: o güne kadar vadesi gelen
/// taksitler ödenmiş, olay işlenmiş. Checkpoint'te ödenen erken ödeme
/// krediye bu hâliyle yazılır.
/// </param>
/// <param name="IgnoredBecauseUnquotable">
/// Kredinin faizi türetilemediği için olaylar oynatılamadı; ödeme listesi
/// olaysız taksitlerden ibaret.
/// </param>
public sealed record LoanReplay(
    Loan Loan,
    IReadOnlyList<LoanScheduledPayment> Payments,
    IReadOnlyDictionary<Guid, Loan> StateAfterEvent,
    IReadOnlyDictionary<Guid, decimal> PrincipalBeforeEvent,
    bool IgnoredBecauseUnquotable)
{
    public decimal Total => Payments.Sum(x => x.Amount);

    public DateOnly? LastPaymentDate =>
        Payments.Count == 0 ? null : Payments[^1].Date;
}
