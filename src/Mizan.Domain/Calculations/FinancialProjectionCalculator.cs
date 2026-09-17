using Mizan.Domain.Models;

namespace Mizan.Domain.Calculations;

public sealed class FinancialProjectionCalculator(
    CashFlowPeriodCalculator CashFlowPeriodCalculator,
    IncomeProjectionCalculator incomeProjectionCalculator,
    CreditCardStatementCalculator cardStatementCalculator,
    MandatoryPaymentCalculator mandatoryPaymentCalculator,
    CashFlowAllocationPlanner allocationPlanner,
    PaymentAllocationStrategyResolver allocationResolver)
{
    public IReadOnlyList<CashFlowPeriodProjection> Calculate(
        FinancialPlan plan,
        DateOnly asOf,
        int periodCount = 12,
        DateOnly? firstSalaryDate = null) =>
        CalculatePlan(plan, asOf, periodCount, firstSalaryDate).Periods;

    public FinancialProjectionResult CalculatePlan(
        FinancialPlan plan,
        DateOnly asOf,
        int periodCount = 12,
        DateOnly? firstSalaryDate = null)
    {
        Validate(plan);
        var anchor = plan.Settings.ProjectionAnchorDate == default
            ? asOf
            : plan.Settings.ProjectionAnchorDate;
        var firstPeriodStart = firstSalaryDate ??
                          CashFlowPeriodCalculator.GetFirstPeriodStartOnOrAfter(
                              anchor,
                              plan.Settings.IncomeDay);
        ValidateProjectionBoundary(anchor, firstPeriodStart, plan.Settings.IncomeDay);
        var periods = BuildPeriods(
            firstPeriodStart,
            plan.Settings.IncomeDay,
            periodCount);
        allocationResolver.ValidateHistory(
            plan.PaymentAssignmentStrategies,
            plan.Settings.IncomeDay,
            firstPeriodStart);

        var cardBundle = BuildCardPayments(
            plan.CreditCards,
            periods[^1].End,
            plan.Settings.CreditCardCarryInterestRate);
        var obligations = mandatoryPaymentCalculator
            .BuildObligations(
                plan.Loans,
                plan.LoanPrepayments,
                plan.PaymentPlans,
                cardBundle.Obligations)
            .Concat(BuildLargeExpenseObligations(plan.PlannedLargeExpenses))
            .ToArray();
        var allocationPlan = allocationPlanner.Plan(
            periods,
            obligations,
            anchor,
            plan.Settings.IncomeDay,
            plan.PaymentAssignmentStrategies);
        var statuses = AssignCardStatuses(cardBundle.Statuses, allocationPlan);
        var result = new List<CashFlowPeriodProjection>(periods.Count);
        var OpeningBalance = plan.Settings.ProjectionOpeningBalance;

        foreach (var period in periods)
        {
            var budget = allocationPlan.Budgets.Single(x =>
                x.PeriodDate == period.Start);
            // Projeksiyon ufku ilk maaş gününde başlar, ama çapa ondan önce
            // olabilir. `[çapa, ilk maaş)` aralığına düşen tek seferlik gelir
            // hiçbir dönemle eşleşmediği için sessizce kayboluyordu; ilk döneme
            // yazıyoruz. Açılış bakiyesine eklemekle aritmetik olarak aynıdır,
            // ama kalem `IncomeItems` içinde görünür kalır.
            var income = incomeProjectionCalculator.Calculate(
                period,
                plan.Salaries,
                plan.OtherIncomes,
                period.Start == firstPeriodStart ? anchor : null);
            var mandatory = mandatoryPaymentCalculator.Summarize(budget.Items);
            var availableAfterMandatory = income.TotalIncome - mandatory.Total;
            var largeExpenses = budget.Items
                .Where(x => x.Type == ObligationType.PlannedLargeExpense)
                .Select(item => plan.PlannedLargeExpenses.Single(x =>
                    x.Id == item.PaymentId))
                .OrderBy(x => x.ExactDate)
                .ThenBy(x => x.Name)
                .ToArray();
            var largeExpenseTotal = largeExpenses.Sum(x => x.Amount);
            var estimatedSurplus = availableAfterMandatory -
                                   plan.Settings.MonthlyVariableExpenseAllowance -
                                   largeExpenseTotal;
            var periodStatuses = statuses
                .Where(x => x.AssignedPeriodDate == period.Start)
                .ToArray();
            var cardInterest = periodStatuses.Sum(x => x.CarryInterest);
            var endingBeforeDeficitInterest = OpeningBalance + estimatedSurplus;
            var deficitInterest = endingBeforeDeficitInterest < 0m
                ? RoundMoney(
                    Math.Abs(endingBeforeDeficitInterest) *
                    plan.Settings.DeficitFinancingInterestRate)
                : 0m;
            var endingBalance = endingBeforeDeficitInterest - deficitInterest;

            result.Add(new CashFlowPeriodProjection(
                period.Start,
                period.End,
                income.PrimaryIncome,
                income.OtherIncome,
                income.TotalIncome,
                mandatory.LoanPayments,
                mandatory.CreditCardPayments,
                mandatory.TemporaryPayments,
                mandatory.InstallmentPayments,
                mandatory.OtherScheduledPayments,
                mandatory.Total,
                availableAfterMandatory,
                plan.Settings.MonthlyVariableExpenseAllowance,
                estimatedSurplus,
                largeExpenseTotal,
                OpeningBalance,
                endingBalance,
                periodStatuses.Any(x =>
                    x.Resolution == CreditCardPaymentResolution.ProjectionFallback),
                periodStatuses.Any(x =>
                    x.Resolution == CreditCardPaymentResolution.Undetermined),
                availableAfterMandatory < 0m ||
                estimatedSurplus < 0m ||
                endingBalance < 0m,
                income.Items,
                mandatory.Items,
                largeExpenses,
                periodStatuses,
                budget.Mode,
                budget.CoverageStart,
                budget.CoverageEnd,
                budget.IsStrategyTransition,
                budget.IsInitialSnapshotPeriod,
                budget.NormalMandatoryAmount,
                budget.TransitionCatchUpAmount,
                budget.ForwardFundedAmount,
                anchor,
                endingBeforeDeficitInterest,
                deficitInterest,
                cardInterest,
                plan.Settings.DeficitFinancingInterestRate));
            OpeningBalance = endingBalance;
        }

        return new FinancialProjectionResult(result, allocationPlan, statuses);
    }

    private static IReadOnlyList<CashFlowPeriod> BuildPeriods(
        DateOnly firstPeriodStart,
        int IncomeDay,
        int count)
    {
        if (count is < 1 or > 60)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count),
                "Dönem sayısı 1 ile 60 arasında olmalıdır.");
        }

        return Enumerable.Range(0, count)
            .Select(index => new CashFlowPeriod(
                CalendarRules.AddMonthsKeepingDay(
                    firstPeriodStart, index, IncomeDay),
                CalendarRules.AddMonthsKeepingDay(
                    firstPeriodStart, index + 1, IncomeDay)))
            .ToArray();
    }

    private CardPaymentBundle BuildCardPayments(
        IEnumerable<CreditCard> cards,
        DateOnly horizonEnd,
        decimal carryInterestRate)
    {
        var obligations = new List<ObligationItem>();
        var statuses = new List<CreditCardPaymentProjectionStatus>();

        foreach (var card in cards)
        {
            var firstClose = CreditCardStatementCalculator
                .ResolveStatementCloseOnOrAfter(
                    card.BalanceAsOfDate,
                    card.StatementClosingDay);
            var statementCount = Math.Max(
                2,
                MonthDistance(firstClose, horizonEnd) + 3);
            var cardName = $"{card.Bank} {card.Name}".Trim();

            foreach (var statement in cardStatementCalculator
                         .Project(
                             card,
                             statementCount,
                             useProjectionFallback: true,
                             carryInterestRate)
                         .Where(x => x.PaymentDueDate < horizonEnd))
            {
                statuses.Add(new CreditCardPaymentProjectionStatus(
                    card.Id,
                    cardName,
                    statement.StatementCloseDate,
                    statement.PaymentDueDate,
                    statement.StatementBalance,
                    statement.MinimumPayment,
                    statement.Payment,
                    statement.OpeningCarriedBalance,
                    statement.NewCharges,
                    statement.CarriedAfterPayment,
                    statement.CarryInterest,
                    statement.NextCarriedBalance,
                    statement.AppliedInterestRate,
                    statement.PaymentResolution,
                    statement.AppliedPaymentType));

                if (statement.Payment is decimal payment)
                {
                    obligations.Add(new ObligationItem(
                        cardName,
                        ObligationType.CreditCard,
                        statement.PaymentDueDate,
                        payment,
                        IsEstimate: statement.PaymentResolution ==
                                    CreditCardPaymentResolution.ProjectionFallback,
                        Detail: statement.PaymentResolution switch
                        {
                            CreditCardPaymentResolution.ProjectionFallback =>
                                "Gelecek hesaplama tercihi",
                            CreditCardPaymentResolution.DueDateOverride =>
                                "Ekstre ödeme planı",
                            _ => "Kart ödeme tercihi"
                        },
                        PaymentId: card.Id));
                }
            }
        }

        return new CardPaymentBundle(obligations, statuses);
    }

    private static IEnumerable<ObligationItem> BuildLargeExpenseObligations(
        IEnumerable<PlannedLargeExpense> expenses) => expenses
        .Where(x => x.Status == PlannedExpenseStatus.Planned)
        .Select(x => new ObligationItem(
            x.Name,
            ObligationType.PlannedLargeExpense,
            x.ExactDate,
            x.Amount,
            Detail: x.Note,
            PaymentId: x.Id));

    private static IReadOnlyList<CreditCardPaymentProjectionStatus>
        AssignCardStatuses(
            IEnumerable<CreditCardPaymentProjectionStatus> statuses,
            CashFlowAllocationPlan allocationPlan) => statuses
        .Select(status =>
        {
            var budget = allocationPlan.FindBudget(status.PaymentDueDate);
            if (budget is not null)
            {
                return status with
                {
                    AssignedPeriodDate = budget.PeriodDate,
                    PaymentBeforePeriodStart =
                        status.PaymentDueDate < budget.PeriodDate,
                    ActiveMode = budget.Mode,
                    AssignmentReason = budget.ResolveReason(
                        status.PaymentDueDate)
                };
            }

            var isPreFirst = allocationPlan.IsPreFirstPeriodDate(
                status.PaymentDueDate);
            return status with
            {
                ActiveMode = isPreFirst
                    ? allocationPlan.Budgets[0].Mode
                    : null,
                AssignmentReason = isPreFirst
                    ? PaymentAllocationReason.PreFirstPeriodUpcoming
                    : null,
                IsPreFirstPeriodObligation = isPreFirst
            };
        })
        .ToArray();

    private static void Validate(FinancialPlan plan)
    {
        CalendarRules.ValidateDay(plan.Settings.IncomeDay);
        if (plan.Settings.MonthlyVariableExpenseAllowance < 0m)
        {
            throw new InvalidOperationException(
                "Aylık tahmini yaşam bütçesi negatif olamaz.");
        }

        ValidateInterestRate(
            plan.Settings.CreditCardCarryInterestRate,
            "Kredi kartı devreden borç faiz oranı");
        ValidateInterestRate(
            plan.Settings.DeficitFinancingInterestRate,
            "Finansman açığı faiz oranı");

        if (plan.Salaries.Any(x => x.Amount < 0m) ||
            plan.OtherIncomes.Any(x => x.Amount < 0m))
        {
            throw new InvalidOperationException("Gelir tutarı negatif olamaz.");
        }

        if (plan.PlannedLargeExpenses.Any(x => x.Amount < 0m))
        {
            throw new InvalidOperationException(
                "Planlanan büyük harcama negatif olamaz.");
        }
    }

    private void ValidateProjectionBoundary(
        DateOnly anchor,
        DateOnly firstPeriodStart,
        int IncomeDay)
    {
        if (CashFlowPeriodCalculator.GetPeriod(firstPeriodStart, IncomeDay).Start !=
            firstPeriodStart)
        {
            throw new InvalidOperationException(
                "Projection başlangıç dönemi geçerli bir dönem tarihi olmalıdır.");
        }

        var earliest = CashFlowPeriodCalculator.GetFirstPeriodStartOnOrAfter(
            anchor,
            IncomeDay);
        if (firstPeriodStart < earliest)
        {
            throw new InvalidOperationException(
                "Projection başlangıç dönemi planlama anchor tarihinden önce olamaz.");
        }
    }

    private static int MonthDistance(DateOnly from, DateOnly to) =>
        ((to.Year - from.Year) * 12) + to.Month - from.Month;

    private static decimal RoundMoney(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static void ValidateInterestRate(decimal rate, string name)
    {
        if (rate is < 0m or > 1m)
        {
            throw new InvalidOperationException(
                $"{name} %0 ile %100 arasında olmalıdır.");
        }
    }

    private sealed record CardPaymentBundle(
        IReadOnlyList<ObligationItem> Obligations,
        IReadOnlyList<CreditCardPaymentProjectionStatus> Statuses);
}
