using Mizan.Domain.Models;

namespace Mizan.Domain.Calculations;

public enum SimulationScenarioType
{
    CashPurchase,
    CreditCardSinglePayment,
    CreditCardInstallmentPurchase,
    FinancingLoan,
    CashDebt,
    FutureOneTimePayment,
    RecurringPayment,
    FutureIncome,
    SalaryChange,
    PaymentStrategyChange,
    CreditCardPaymentMode,
    LoanEarlyClosure,
    LoanPartialPrepayment
}

public sealed record SimulationRequest(
    SimulationScenarioType Type,
    string Name,
    decimal Amount,
    DateOnly StartDate,
    int PaymentCount = 1,
    DateOnly? FirstPaymentDate = null,
    Guid? CreditCardId = null,
    decimal? TotalRepaymentAmount = null,
    CashFlowAllocationMode? NewCashFlowAllocationMode = null,
    DateOnly? EffectivePeriodDate = null,
    Guid ScenarioId = default,
    // CreditCardPaymentMode senaryosu için: hangi ödeme şekli ve kapsamı.
    // Kapsam false ise yalnızca StartDate'teki ekstre, true ise kartın genel
    // ödeme şekli değişir.
    CreditCardPaymentType? CardPaymentType = null,
    bool AppliesToAllStatements = false,
    // Kredi erken kapama ve ara ödeme için: hangi kredi ve ara ödemede vade mi
    // taksit mi azalacak. Ara ödemede Amount anaparadan düşecek tutardır.
    Guid? LoanId = null,
    LoanPrepaymentMode? PrepaymentMode = null);

/// <summary>
/// Senaryodaki erken ödemelerin bir krediye etkisi.
/// </summary>
/// <param name="PrepaidAmount">Senaryonun eklediği erken ödemelerin toplamı.</param>
/// <param name="InterestSaving">
/// Kredinin ömrü boyunca ödenecek toplamdaki düşüş — 12 dönemle sınırlı
/// değildir. Ödenmeyecek taksitler eksi erken ödeme.
/// </param>
public sealed record LoanPrepaymentImpact(
    Guid LoanId,
    string LoanName,
    decimal PrepaidAmount,
    decimal InterestSaving,
    DateOnly? BaselineEndDate,
    DateOnly? ScenarioEndDate,
    decimal BaselineMonthlyPayment,
    decimal? ScenarioMonthlyPayment);

public sealed record SimulationImpactRow(
    CashFlowPeriodProjection Baseline,
    CashFlowPeriodProjection Scenario)
{
    public decimal MandatoryOutflowDifference =>
        Scenario.MandatoryOutflow - Baseline.MandatoryOutflow;
    public decimal SurplusDifference =>
        Scenario.EstimatedSurplus - Baseline.EstimatedSurplus;
    public decimal ProjectedBalanceDifference =>
        Scenario.EndingProjectedBalance - Baseline.EndingProjectedBalance;
    public decimal InterestDifference =>
        Scenario.TotalInterestGenerated - Baseline.TotalInterestGenerated;
}

public sealed record SimulationRiskSummary(
    decimal LowestAvailableAfterMandatory,
    decimal LowestSurplus,
    decimal LowestProjectedBalance,
    CashFlowPeriod LowestPeriod,
    CashFlowPeriod? FirstNegativeSurplusPeriod,
    CashFlowPeriod? FirstNegativeProjectedBalancePeriod,
    decimal MaximumCarryOverDeficit,
    CashFlowPeriod? RecoveryPeriod,
    decimal EndingProjectedBalance,
    decimal TotalScenarioCost,
    decimal? FinancingCost)
{
    public CashFlowPeriod? FirstDeficitPeriod =>
        FirstNegativeProjectedBalancePeriod;
}

public sealed record SimulationResult(
    IReadOnlyList<CashFlowPeriodProjection> Baseline,
    IReadOnlyList<CashFlowPeriodProjection> Scenario,
    IReadOnlyList<SimulationImpactRow> Rows,
    SimulationRiskSummary Risk,
    string FriendlySummary)
{
    public ProjectionInterestSummary BaselineInterest =>
        ProjectionInterestSummary.From(Baseline);
    public ProjectionInterestSummary ScenarioInterest =>
        ProjectionInterestSummary.From(Scenario);
    public decimal AdditionalInterestCost =>
        ScenarioInterest.TotalInterestCost -
        BaselineInterest.TotalInterestCost;
    public decimal InterestSaving =>
        Math.Max(0m, -AdditionalInterestCost);

    public IReadOnlyList<LoanPrepaymentImpact> LoanImpacts { get; init; } = [];
}
