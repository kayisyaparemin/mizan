using CoinFlow.Domain.Models;

namespace CoinFlow.Domain.Calculations;

public sealed class CreditCardActualPaymentReconciler(
    CreditCardStatementCalculator statementCalculator)
{
    public CreditCard Apply(
        CreditCard card,
        DateOnly paymentDueDate,
        decimal actualPayment,
        decimal carryInterestRate)
    {
        if (actualPayment < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(actualPayment));
        }

        var firstClose = CreditCardStatementCalculator
            .ResolveStatementCloseOnOrAfter(
                card.BalanceAsOfDate,
                card.StatementClosingDay);
        var count = Math.Max(
            24,
            MonthDistance(firstClose, paymentDueDate) + 4);
        var statement = statementCalculator
            .Project(card, count, true, carryInterestRate)
            .SingleOrDefault(x => x.PaymentDueDate == paymentDueDate)
            ?? throw new InvalidOperationException(
                "Kart ödemesinin bağlı olduğu ekstre bulunamadı.");
        var statementBalance = statement.StatementBalance
            ?? throw new InvalidOperationException(
                "Kart ekstresi için borç tutarı hesaplanamadı.");
        var remainingPrincipal = Math.Max(
            0m,
            statementBalance - actualPayment);
        var carryInterest = remainingPrincipal > 0m
            ? decimal.Round(
                remainingPrincipal * carryInterestRate,
                2,
                MidpointRounding.AwayFromZero)
            : 0m;
        // statement.StatementCloseDate is the exact close Project() used for
        // the cycle being settled (identical to CurrentStatement.StatementDate
        // when an actual statement is present). Charges posted on or before it
        // are already reflected in statementBalance/CarriedBalance above and
        // must be dropped, or a later projection would double-count them.
        var remainingCharges = card.Charges
            .Where(charge =>
                charge.PostingDate > statement.StatementCloseDate)
            .ToArray();

        return card with
        {
            CarriedBalance = remainingPrincipal + carryInterest,
            UnbilledSpending = 0m,
            BalanceAsOfDate = statement.StatementCloseDate.AddDays(1),
            Charges = remainingCharges,
            PaymentPlans = card.PaymentPlans
                .Where(x => x.DueDate != paymentDueDate)
                .ToArray(),
            // Settled statement artık gelecekteki projection için authoritative
            // değil; devralınan tek bilgi bankanın bildirdiği bir sonraki
            // kesim/son ödeme tarihidir.
            CurrentStatement = null,
            CurrentStatementPaymentPlan = null,
            KnownNextStatementDate = card.CurrentStatement?.NextStatementDate,
            KnownNextDueDate = card.CurrentStatement?.NextDueDate
        };
    }

    private static int MonthDistance(DateOnly from, DateOnly to) =>
        Math.Max(
            0,
            ((to.Year - from.Year) * 12) + to.Month - from.Month);
}
