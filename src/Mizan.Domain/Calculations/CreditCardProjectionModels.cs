using Mizan.Domain.Models;

namespace Mizan.Domain.Calculations;

public enum CreditCardPaymentResolution
{
    Undetermined = 0,
    DueDateOverride = 1,
    GeneralStrategy = 2,
    ProjectionFallback = 3,
    CurrentStatementPlan = 4
}

public sealed record CreditCardStatementProjection(
    DateOnly StatementCloseDate,
    DateOnly PaymentDueDate,
    decimal? OpeningCarriedBalance,
    decimal NewCharges,
    decimal? StatementBalance,
    decimal? MinimumPayment,
    decimal? Payment,
    decimal? CarriedAfterPayment,
    // Bu ekstreye giren devreden bakiyeye işlenen faiz. `StatementBalance`
    // içinde yer alır, ayrı bir nakit çıkışı değildir (I9).
    decimal CarryInterest,
    decimal? NextCarriedBalance,
    decimal AppliedInterestRate,
    CreditCardPaymentResolution PaymentResolution,
    CreditCardPaymentType? AppliedPaymentType,
    bool IsActualStatement = false,
    CreditCardStatementSource? StatementSource = null);
