using Mizan.Domain.Models;

namespace Mizan.Domain.Calculations;

public sealed partial class CreditCardStatementCalculator
{
    private static void ValidateInterestRate(decimal rate)
    {
        if (rate is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rate),
                "Kart devreden borç faiz oranı 0 ile 1 arasında olmalıdır.");
        }
    }

    private static void Validate(CreditCard card)
    {
        if (card.BalanceAsOfDate == default)
        {
            throw new InvalidOperationException("Kart bakiye tarihi gereklidir.");
        }

        CalendarRules.ValidateDay(card.StatementClosingDay);
        CalendarRules.ValidateDay(card.PaymentDueDay);
        if (card.MinimumPaymentRate is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(card),
                "Asgari ödeme oranı 0 ile 1 arasında olmalıdır.");
        }

        if (card.CarriedBalance < 0m ||
            card.UnbilledSpending < 0m ||
            card.Charges.Any(x => x.Amount < 0m))
        {
            throw new InvalidOperationException(
                "Kart borç bileşenleri negatif olamaz.");
        }

        if (card.PaymentStrategy == CreditCardPaymentStrategy.FixedAmount &&
            card.FixedPaymentAmount is null or <= 0m)
        {
            throw new InvalidOperationException(
                "Sabit ödeme tercihi için 0'dan büyük bir tutar gereklidir.");
        }

        if (card.ProjectionFallbackStrategy == ProjectionFallbackStrategy.FixedAmount &&
            card.ProjectionFallbackFixedAmount is null or <= 0m)
        {
            throw new InvalidOperationException(
                "Gelecek hesaplamalarda sabit tutar kullanmak için 0'dan büyük bir tutar gereklidir.");
        }

        if (card.PaymentPlans.Any(x =>
                x.PaymentType == CreditCardPaymentType.FixedAmount &&
                x.Amount is null or <= 0m))
        {
            throw new InvalidOperationException(
                "Özel kart ödemesi için 0'dan büyük bir tutar gereklidir.");
        }

        if (card.PaymentPlans.GroupBy(x => x.DueDate).Any(x => x.Count() > 1))
        {
            throw new InvalidOperationException(
                "Aynı son ödeme tarihi için yalnızca bir özel kart planı olabilir.");
        }

        if (card.CurrentStatement is { } statement)
        {
            if (statement.StatementDate == default ||
                statement.DueDate == default)
            {
                throw new InvalidOperationException(
                    "Kesilmiş ekstre için kesim ve son ödeme tarihi gereklidir.");
            }

            if (statement.StatementAmount < 0m ||
                statement.MinimumPaymentAmount < 0m ||
                statement.MinimumPaymentAmount > statement.StatementAmount)
            {
                throw new InvalidOperationException(
                    "Kesilmiş ekstre tutarı ve asgari ödeme geçersiz.");
            }

            if (statement.NextStatementDate is { } nextStatementDate &&
                nextStatementDate <= statement.StatementDate)
            {
                throw new InvalidOperationException(
                    "Bir sonraki kesim tarihi mevcut ekstre tarihinden sonra olmalıdır.");
            }

            if (statement.NextDueDate is { } nextDueDate &&
                nextDueDate <= statement.DueDate)
            {
                throw new InvalidOperationException(
                    "Bir sonraki son ödeme tarihi mevcut son ödeme tarihinden sonra olmalıdır.");
            }
        }

        if (card.CurrentStatement is null &&
            card.CurrentStatementPaymentPlan is not null)
        {
            throw new InvalidOperationException(
                "Kesilmiş ekstre planı için önce ekstre bilgisi gereklidir.");
        }

        if (card is
            {
                CurrentStatement: { } currentStatement,
                CurrentStatementPaymentPlan:
                {
                    Mode: CurrentStatementPaymentMode.Custom
                } currentPlan
            } &&
            (currentPlan.CustomAmount is null or < 0m ||
             currentPlan.CustomAmount > currentStatement.StatementAmount))
        {
            throw new InvalidOperationException(
                "Bu ekstre için özel ödeme tutarı 0 ile ekstre tutarı arasında olmalıdır.");
        }

        if (card.KnownNextDueDate is { } knownDue &&
            (card.KnownNextStatementDate is not { } knownClose ||
             knownDue <= knownClose))
        {
            throw new InvalidOperationException(
                "Bilinen bir sonraki son ödeme tarihi bilinen kesim tarihinden sonra olmalıdır.");
        }
    }
}
