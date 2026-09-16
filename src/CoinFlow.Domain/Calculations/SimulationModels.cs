using CoinFlow.Domain.Models;

namespace CoinFlow.Domain.Calculations;

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
    PaymentAssignmentMode? NewPaymentAssignmentMode = null,
    DateOnly? EffectiveSalaryDate = null,
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
    SalaryPeriodProjection Baseline,
    SalaryPeriodProjection Scenario)
{
    public decimal MandatoryOutflowDifference =>
        Scenario.MandatoryOutflow - Baseline.MandatoryOutflow;
    public decimal SavingsCapacityDifference =>
        Scenario.EstimatedSavingsCapacity - Baseline.EstimatedSavingsCapacity;
    public decimal ProjectedSavingsDifference =>
        Scenario.EndingProjectedSavings - Baseline.EndingProjectedSavings;
    public decimal InterestDifference =>
        Scenario.TotalInterestGenerated - Baseline.TotalInterestGenerated;
}

public sealed record SimulationRiskSummary(
    decimal LowestAvailableAfterMandatory,
    decimal LowestSavingsCapacity,
    decimal LowestProjectedSavings,
    SalaryPeriod LowestPeriod,
    SalaryPeriod? FirstNegativeSavingsCapacityPeriod,
    SalaryPeriod? FirstNegativeProjectedSavingsPeriod,
    decimal MaximumCarryOverDeficit,
    SalaryPeriod? RecoveryPeriod,
    decimal EndingProjectedSavings,
    decimal TotalScenarioCost,
    decimal? FinancingCost)
{
    public SalaryPeriod? FirstDeficitPeriod =>
        FirstNegativeProjectedSavingsPeriod;
}

public sealed record SimulationResult(
    IReadOnlyList<SalaryPeriodProjection> Baseline,
    IReadOnlyList<SalaryPeriodProjection> Scenario,
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
