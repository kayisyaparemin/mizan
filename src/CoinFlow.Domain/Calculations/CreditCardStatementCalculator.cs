using CoinFlow.Domain.Models;

namespace CoinFlow.Domain.Calculations;

public sealed partial class CreditCardStatementCalculator
{
    public IReadOnlyList<CreditCardStatementProjection> Project(
        CreditCard card,
        int statementCount,
        bool useProjectionFallback = false,
        decimal carryInterestRate = 0.05m)
    {
        if (statementCount < 1)
        {
            return [];
        }

        Validate(card);
        ValidateInterestRate(carryInterestRate);
        var actualStatement = card.CurrentStatement;
        var firstClose = actualStatement?.StatementDate ??
                         card.KnownNextStatementDate ??
                         ResolveStatementCloseOnOrAfter(
                             card.BalanceAsOfDate,
                             card.StatementClosingDay);
        var closeDate = firstClose;
        var assignedCharges = card.Charges
            .Where(x => actualStatement is null ||
                        x.PostingDate > actualStatement.StatementDate)
            .GroupBy(x => ResolveChargeStatementClose(
                card,
                x.PostingDate,
                firstClose))
            .ToDictionary(x => x.Key, x => x.Sum(charge => charge.Amount));
        decimal? carried = actualStatement?.StatementAmount ??
                           card.CarriedBalance;
        var result = new List<CreditCardStatementProjection>(statementCount);

        for (var index = 0; index < statementCount; index++)
        {
            var isActualStatement = actualStatement is not null && index == 0;
            var newCharges = isActualStatement
                ? 0m
                : assignedCharges.GetValueOrDefault(closeDate);
            if (actualStatement is null && index == 0)
            {
                newCharges += card.UnbilledSpending;
            }

            // Devreden bakiyenin faizi, o bakiyenin *girdiği* ekstreye eklenir
            // (gerçek banka davranışı). Kesilmiş ekstrede banka faizi zaten
            // işlemiştir; `StatementAmount` nihai tutardır, üzerine eklenmez.
            var carryInterest = !isActualStatement && carried is > 0m
                ? RoundMoney(carried.Value * carryInterestRate)
                : 0m;
            decimal? statementBalance = carried is null
                ? null
                : carried.Value + carryInterest + newCharges;
            decimal? minimumPayment = isActualStatement
                ? actualStatement!.MinimumPaymentAmount
                : statementBalance is null
                    ? null
                    : RoundMoney(
                        statementBalance.Value * card.MinimumPaymentRate);
            var dueDate = ResolvePaymentDueDate(
                card,
                closeDate,
                isActualStatement);
            var decision = ResolvePayment(
                card,
                dueDate,
                statementBalance,
                minimumPayment,
                isActualStatement,
                useProjectionFallback);
            decimal? carriedAfterPayment = statementBalance is null || decision.Payment is null
                ? null
                : Math.Max(0m, statementBalance.Value - decision.Payment.Value);
            // Faiz artık dönem sonunda kapitalize edilmiyor; kalan anapara
            // olduğu gibi devreder, faizi bir sonraki ekstrede işlenir.
            decimal? nextCarriedBalance = carriedAfterPayment;

            result.Add(new CreditCardStatementProjection(
                closeDate,
                dueDate,
                carried,
                newCharges,
                statementBalance,
                minimumPayment,
                decision.Payment,
                carriedAfterPayment,
                carryInterest,
                nextCarriedBalance,
                carryInterestRate,
                decision.Resolution,
                decision.PaymentType,
                isActualStatement,
                isActualStatement ? actualStatement!.Source : null));

            carried = nextCarriedBalance;
            closeDate = ResolveNextStatementCloseDate(
                card,
                closeDate,
                isActualStatement);
        }

        return result;
    }

    private static PaymentDecision ResolvePayment(
        CreditCard card,
        DateOnly dueDate,
        decimal? statementBalance,
        decimal? minimumPayment,
        bool isActualStatement,
        bool useProjectionFallback)
    {
        if (isActualStatement &&
            card.CurrentStatementPaymentPlan is { } currentPlan)
        {
            return new PaymentDecision(
                CalculateCurrentStatementPayment(
                    currentPlan,
                    statementBalance,
                    minimumPayment),
                CreditCardPaymentResolution.CurrentStatementPlan,
                ToPaymentType(currentPlan.Mode));
        }

        var paymentOverride = card.PaymentPlans.SingleOrDefault(x => x.DueDate == dueDate);
        if (paymentOverride is not null)
        {
            return new PaymentDecision(
                CalculatePayment(
                    paymentOverride.PaymentType,
                    paymentOverride.Amount,
                    statementBalance,
                    minimumPayment),
                CreditCardPaymentResolution.DueDateOverride,
                paymentOverride.PaymentType);
        }

        var strategyType = ToPaymentType(card.PaymentStrategy);
        if (strategyType is not null)
        {
            return new PaymentDecision(
                CalculatePayment(
                    strategyType.Value,
                    card.FixedPaymentAmount,
                    statementBalance,
                    minimumPayment),
                CreditCardPaymentResolution.GeneralStrategy,
                strategyType);
        }

        var fallbackType = useProjectionFallback
            ? ToPaymentType(card.ProjectionFallbackStrategy)
            : null;
        if (fallbackType is not null)
        {
            return new PaymentDecision(
                CalculatePayment(
                    fallbackType.Value,
                    card.ProjectionFallbackFixedAmount,
                    statementBalance,
                    minimumPayment),
                CreditCardPaymentResolution.ProjectionFallback,
                fallbackType);
        }

        return new PaymentDecision(
            null,
            CreditCardPaymentResolution.Undetermined,
            null);
    }

    private static decimal? CalculateCurrentStatementPayment(
        CurrentStatementPaymentPlan plan,
        decimal? statementBalance,
        decimal? minimumPayment)
    {
        if (statementBalance is null || minimumPayment is null)
        {
            return null;
        }

        var requested = plan.Mode switch
        {
            CurrentStatementPaymentMode.Minimum => minimumPayment.Value,
            CurrentStatementPaymentMode.Full => statementBalance.Value,
            CurrentStatementPaymentMode.Custom =>
                plan.CustomAmount ??
                throw new InvalidOperationException(
                    "Bu ekstre için özel ödeme tutarı gereklidir."),
            _ => throw new ArgumentOutOfRangeException(nameof(plan))
        };

        var rounded = RoundMoney(requested);
        if (rounded < 0m || rounded > statementBalance.Value)
        {
            throw new InvalidOperationException(
                "Bu ekstre için ödeme tutarı 0 ile ekstre tutarı arasında olmalıdır.");
        }

        return rounded;
    }

    private static decimal? CalculatePayment(
        CreditCardPaymentType paymentType,
        decimal? fixedAmount,
        decimal? statementBalance,
        decimal? minimumPayment)
    {
        if (statementBalance is null || minimumPayment is null)
        {
            return null;
        }

        var requested = paymentType switch
        {
            CreditCardPaymentType.Minimum => minimumPayment.Value,
            CreditCardPaymentType.FullStatement => statementBalance.Value,
            CreditCardPaymentType.FixedAmount => Math.Max(
                fixedAmount ?? throw new InvalidOperationException(
                    "Sabit kart ödeme tutarı gereklidir."),
                minimumPayment.Value),
            _ => throw new ArgumentOutOfRangeException(nameof(paymentType))
        };

        return Math.Min(
            statementBalance.Value,
            Math.Max(0m, RoundMoney(requested)));
    }

    private static CreditCardPaymentType? ToPaymentType(
        CreditCardPaymentStrategy strategy) => strategy switch
    {
        CreditCardPaymentStrategy.AskEachStatement => null,
        CreditCardPaymentStrategy.Minimum => CreditCardPaymentType.Minimum,
        CreditCardPaymentStrategy.FullStatement => CreditCardPaymentType.FullStatement,
        CreditCardPaymentStrategy.FixedAmount => CreditCardPaymentType.FixedAmount,
        _ => throw new ArgumentOutOfRangeException(nameof(strategy))
    };

    private static CreditCardPaymentType ToPaymentType(
        CurrentStatementPaymentMode mode) => mode switch
    {
        CurrentStatementPaymentMode.Minimum => CreditCardPaymentType.Minimum,
        CurrentStatementPaymentMode.Full => CreditCardPaymentType.FullStatement,
        CurrentStatementPaymentMode.Custom => CreditCardPaymentType.FixedAmount,
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    private static CreditCardPaymentType? ToPaymentType(
        ProjectionFallbackStrategy strategy) => strategy switch
    {
        ProjectionFallbackStrategy.None => null,
        ProjectionFallbackStrategy.Minimum => CreditCardPaymentType.Minimum,
        ProjectionFallbackStrategy.FullStatement => CreditCardPaymentType.FullStatement,
        ProjectionFallbackStrategy.FixedAmount => CreditCardPaymentType.FixedAmount,
        _ => throw new ArgumentOutOfRangeException(nameof(strategy))
    };

    private static decimal RoundMoney(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

    private sealed record PaymentDecision(
        decimal? Payment,
        CreditCardPaymentResolution Resolution,
        CreditCardPaymentType? PaymentType);
}
