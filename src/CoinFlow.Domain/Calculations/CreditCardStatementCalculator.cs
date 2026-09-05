using CoinFlow.Domain.Models;

namespace CoinFlow.Domain.Calculations;

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
    decimal CarryInterest,
    decimal? NextCarriedBalance,
    decimal AppliedInterestRate,
    CreditCardPaymentResolution PaymentResolution,
    CreditCardPaymentType? AppliedPaymentType,
    bool IsActualStatement = false,
    CreditCardStatementSource? StatementSource = null)
{
    public bool IsPaymentDetermined => Payment is not null;
    public bool UsesProjectionFallback =>
        PaymentResolution == CreditCardPaymentResolution.ProjectionFallback;
}

public sealed class CreditCardStatementCalculator
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

            decimal? statementBalance = carried is null
                ? null
                : carried.Value + newCharges;
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
            var carryInterest = carriedAfterPayment is > 0m
                ? RoundMoney(carriedAfterPayment.Value * carryInterestRate)
                : 0m;
            decimal? nextCarriedBalance = carriedAfterPayment is null
                ? null
                : carriedAfterPayment.Value + carryInterest;

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

    private sealed record PaymentDecision(
        decimal? Payment,
        CreditCardPaymentResolution Resolution,
        CreditCardPaymentType? PaymentType);
}
