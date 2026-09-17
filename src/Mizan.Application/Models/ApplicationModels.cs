using Mizan.Domain.Calculations;
using Mizan.Domain.Models;

namespace Mizan.Application.Models;

public sealed record DashboardSnapshot(
    CashFlowPeriodProjection CurrentPeriod,
    IReadOnlyList<ObligationItem> PreFirstPeriodObligations,
    IReadOnlyList<ObligationItem> UpcomingPayments,
    decimal TwelvePeriodEndingProjectedBalance,
    CashFlowPeriodProjection TightestPeriod,
    bool HasUndeterminedCardPayments,
    CashFlowAllocationStrategy CurrentStrategy,
    CashFlowAllocationStrategy? PendingStrategy,
    DateOnly ProjectionAnchorDate,
    decimal ProjectionOpeningBalance,
    decimal TwelvePeriodCreditCardInterest,
    decimal TwelvePeriodDeficitFinancingInterest,
    decimal TwelvePeriodTotalInterest);

public sealed record CashFlowAllocationStrategyOverview(
    CashFlowAllocationStrategy? Current,
    CashFlowAllocationStrategy? Pending,
    IReadOnlyList<CashFlowAllocationStrategy> History,
    IReadOnlyList<DateOnly> AvailableEffectivePeriodDates);

public sealed record InitialPaymentStrategySetup(
    DateOnly ProjectionAnchorDate,
    DateOnly EffectivePeriodDate,
    DateOnly ExampleSalaryDate,
    DateOnly PreviousExampleStart,
    DateOnly UpcomingExampleEnd);

public sealed record PaymentAllocationChangePreview(
    DateOnly EffectivePeriodDate,
    CashFlowAllocationMode CurrentMode,
    CashFlowAllocationMode NewMode,
    CashFlowPeriodProjection Baseline,
    CashFlowPeriodProjection Scenario)
{
    public decimal TotalTransitionBurden => Scenario.MandatoryOutflow;
    public decimal FinancingGap => Math.Min(
        0m,
        Scenario.EstimatedSurplus);
}

public enum SimulationApplyDestination
{
    Payments,
    CreditCard,
    Income,
    SalaryHistory,
    Settings
}

public sealed record SimulationApplyResult(
    Guid ScenarioId,
    Guid EntityId,
    SimulationApplyDestination Destination,
    bool AlreadyApplied,
    string Message);

public sealed record SimulationPersistenceBatch(
    IReadOnlyList<PlannedLargeExpense> PlannedLargeExpenses,
    IReadOnlyList<TemporaryPaymentPlan> PaymentPlans,
    IReadOnlyList<CreditCard> CreditCards,
    IReadOnlyList<OneTimeIncome> OtherIncomes,
    IReadOnlyList<SalaryScheduleEntry> Salaries,
    IReadOnlyList<CashFlowAllocationStrategy> PaymentAssignmentStrategies,
    IReadOnlyList<LoanPrepayment> LoanPrepayments)
{
    public static readonly SimulationPersistenceBatch Empty = new(
        [],
        [],
        [],
        [],
        [],
        [],
        []);
}
