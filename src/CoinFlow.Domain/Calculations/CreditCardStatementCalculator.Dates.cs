using CoinFlow.Domain.Models;

namespace CoinFlow.Domain.Calculations;

public sealed partial class CreditCardStatementCalculator
{
    public static DateOnly ResolveStatementCloseOnOrAfter(
        DateOnly date,
        int statementClosingDay)
    {
        CalendarRules.ValidateDay(statementClosingDay);
        var closeDate = CalendarRules.ResolveDay(date.Year, date.Month, statementClosingDay);
        return closeDate >= date
            ? closeDate
            : CalendarRules.AddMonthsKeepingDay(closeDate, 1, statementClosingDay);
    }

    public static DateOnly ResolveChargeStatementClose(
        DateOnly postingDate,
        DateOnly firstProjectionClose,
        int statementClosingDay)
    {
        var closeDate = ResolveStatementCloseOnOrAfter(postingDate, statementClosingDay);
        return closeDate < firstProjectionClose ? firstProjectionClose : closeDate;
    }

    private static DateOnly ResolveChargeStatementClose(
        CreditCard card,
        DateOnly postingDate,
        DateOnly firstProjectionClose)
    {
        if (card.CurrentStatement is
            {
                NextStatementDate: { } nextStatementDate
            } currentStatement &&
            postingDate > currentStatement.StatementDate &&
            postingDate <= nextStatementDate)
        {
            return nextStatementDate;
        }

        // Settled statement sonrası CurrentStatement yoktur ama bankanın
        // bildirdiği bir sonraki kesim tarihi hâlâ bilinir (I11). Bu tarihe
        // kadar düşen harcamalar genel kesim gününe değil, bilinen exact
        // tarihe faturalanır. Reconciler, settled ekstreye ait harcamaları
        // zaten düşürdüğü için kalanların tamamı bu pencereye aittir.
        if (card.CurrentStatement is null &&
            card.KnownNextStatementDate is { } knownNextStatementDate &&
            postingDate <= knownNextStatementDate)
        {
            return knownNextStatementDate;
        }

        return ResolveChargeStatementClose(
            postingDate,
            firstProjectionClose,
            card.StatementClosingDay);
    }

    public static DateOnly ResolvePaymentDueDate(
        DateOnly statementCloseDate,
        int paymentDueDay)
    {
        CalendarRules.ValidateDay(paymentDueDay);
        var sameMonth = CalendarRules.ResolveDay(
            statementCloseDate.Year,
            statementCloseDate.Month,
            paymentDueDay);
        return sameMonth > statementCloseDate
            ? sameMonth
            : CalendarRules.AddMonthsKeepingDay(sameMonth, 1, paymentDueDay);
    }

    public static DateOnly ResolveNextStatementDate(
        DateOnly actualStatementDate,
        int statementClosingDay,
        DateOnly? importedExactDate = null)
    {
        CalendarRules.ValidateDay(statementClosingDay);
        return importedExactDate ?? CalendarRules.AddMonthsKeepingDay(
            actualStatementDate,
            1,
            statementClosingDay);
    }

    public static DateOnly ResolveNextDueDate(
        DateOnly nextStatementDate,
        int paymentDueDay,
        DateOnly? importedExactDate = null) =>
        importedExactDate ?? ResolvePaymentDueDate(
            nextStatementDate,
            paymentDueDay);

    private static DateOnly ResolvePaymentDueDate(
        CreditCard card,
        DateOnly statementCloseDate,
        bool isCurrentActualStatement)
    {
        if (card.CurrentStatement is { } statement)
        {
            if (isCurrentActualStatement)
            {
                return statement.DueDate;
            }

            if (statement.NextStatementDate == statementCloseDate &&
                statement.NextDueDate is { } nextDueDate)
            {
                return nextDueDate;
            }
        }
        else if (card.KnownNextStatementDate == statementCloseDate &&
                  card.KnownNextDueDate is { } knownDueDate)
        {
            return knownDueDate;
        }

        return ResolvePaymentDueDate(
            statementCloseDate,
            card.PaymentDueDay);
    }

    private static DateOnly ResolveNextStatementCloseDate(
        CreditCard card,
        DateOnly closeDate,
        bool wasCurrentActualStatement)
    {
        if (wasCurrentActualStatement &&
            card.CurrentStatement?.NextStatementDate is { } nextStatementDate)
        {
            return nextStatementDate;
        }

        return ResolveNextStatementDate(
            closeDate,
            card.StatementClosingDay);
    }
}
