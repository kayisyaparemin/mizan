using Mizan.Domain.Models;

namespace Mizan.Domain.Calculations;

public sealed record CreditCardPaymentProjectionStatus(
    Guid CardId,
    string CardName,
    DateOnly StatementCloseDate,
    DateOnly PaymentDueDate,
    decimal? StatementBalance,
    decimal? MinimumPayment,
    decimal? Payment,
    decimal? OpeningCarriedBalance,
    decimal NewCharges,
    decimal? CarriedPrincipalAfterPayment,
    decimal CarryInterest,
    decimal? NextCarriedBalance,
    decimal AppliedInterestRate,
    CreditCardPaymentResolution Resolution,
    CreditCardPaymentType? PaymentType,
    DateOnly AssignedPeriodDate = default,
    bool PaymentBeforePeriodStart = false,
    CashFlowAllocationMode? ActiveMode = null,
    PaymentAllocationReason? AssignmentReason = null,
    bool IsPreFirstPeriodObligation = false);

public sealed record CashFlowPeriodProjection(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal PrimaryIncome,
    decimal OtherIncome,
    decimal TotalIncome,
    decimal LoanPayments,
    decimal CreditCardPayments,
    decimal TemporaryPayments,
    decimal InstallmentPayments,
    decimal OtherScheduledPayments,
    decimal MandatoryOutflow,
    decimal AvailableAfterMandatory,
    decimal VariableExpenseAllowance,
    decimal EstimatedSurplus,
    decimal PlannedLargeCashExpenses,
    decimal OpeningProjectedBalance,
    decimal EndingProjectedBalance,
    bool IsEstimatedCardPayment,
    bool HasUndeterminedCardPayment,
    bool HasDeficit,
    IReadOnlyList<IncomeProjectionItem> IncomeItems,
    IReadOnlyList<ObligationItem> MandatoryItems,
    IReadOnlyList<PlannedLargeExpense> LargeExpenseItems,
    IReadOnlyList<CreditCardPaymentProjectionStatus> CardPaymentStatuses,
    CashFlowAllocationMode CashFlowAllocationMode =
        CashFlowAllocationMode.UpcomingPeriod,
    DateOnly PaymentWindowStart = default,
    DateOnly PaymentWindowEnd = default,
    bool IsStrategyTransition = false,
    bool IsInitialSnapshotPeriod = false,
    decimal NormalMandatoryAmount = 0m,
    decimal TransitionCatchUpAmount = 0m,
    decimal ForwardFundedAmount = 0m,
    DateOnly ProjectionAnchorDate = default,
    decimal EndingProjectedBalanceBeforeDeficitInterest = 0m,
    decimal DeficitFinancingInterest = 0m,
    decimal CardInterestGenerated = 0m,
    decimal AppliedDeficitInterestRate = 0m)
{
    public CashFlowPeriod Period => new(PeriodStart, PeriodEnd);
    public decimal CarryOverDeficit => OpeningProjectedBalance < 0m
        ? Math.Abs(OpeningProjectedBalance)
        : 0m;
    public decimal AvailableAfterCarryOverDeficit =>
        AvailableAfterMandatory - CarryOverDeficit;
    public decimal CurrentPeriodNetContribution =>
        EstimatedSurplus;
    public decimal DeficitPrincipal =>
        EndingProjectedBalanceBeforeDeficitInterest < 0m
            ? Math.Abs(EndingProjectedBalanceBeforeDeficitInterest)
            : 0m;
    public decimal TotalInterestGenerated =>
        CardInterestGenerated + DeficitFinancingInterest;
    public decimal DeficitCoveredThisPeriod => CarryOverDeficit == 0m
        ? 0m
        : Math.Min(
            CarryOverDeficit,
            Math.Max(0m, CurrentPeriodNetContribution));
    public decimal RemainingCarryOverDeficit => EndingProjectedBalance < 0m
        ? Math.Abs(EndingProjectedBalance)
        : 0m;
    public bool HasCarryOverDeficit => CarryOverDeficit > 0m;
    public bool RecoveredCarryOverDeficit =>
        HasCarryOverDeficit && EndingProjectedBalance >= 0m;
}

public sealed record FinancialProjectionResult(
    IReadOnlyList<CashFlowPeriodProjection> Periods,
    CashFlowAllocationPlan allocationPlan,
    IReadOnlyList<CreditCardPaymentProjectionStatus> CardPaymentStatuses)
{
    public decimal TotalCreditCardInterest =>
        Periods.Sum(x => x.CardInterestGenerated);
    public decimal TotalDeficitFinancingInterest =>
        Periods.Sum(x => x.DeficitFinancingInterest);
    public decimal TotalInterestCost =>
        TotalCreditCardInterest + TotalDeficitFinancingInterest;
}

public sealed record ProjectionInterestSummary(
    decimal CreditCardInterest,
    decimal DeficitFinancingInterest)
{
    public decimal TotalInterestCost =>
        CreditCardInterest + DeficitFinancingInterest;

    public static ProjectionInterestSummary From(
        IEnumerable<CashFlowPeriodProjection> periods)
    {
        var rows = periods.ToArray();
        return new ProjectionInterestSummary(
            rows.Sum(x => x.CardInterestGenerated),
            rows.Sum(x => x.DeficitFinancingInterest));
    }
}
