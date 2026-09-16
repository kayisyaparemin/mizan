using System.Globalization;
using CoinFlow.Domain.Models;

namespace CoinFlow.Domain.Calculations;

public sealed partial class SimulationCalculator(
    FinancialProjectionCalculator projectionCalculator,
    InstallmentScheduleCalculator installmentScheduleCalculator,
    LoanPaymentScheduleBuilder? loanScheduleBuilder = null)
{
    // Saf hesaplayıcı; verilmezse kendi örneği kurulur ki mevcut çağıranlar
    // değişmesin.
    private readonly LoanPaymentScheduleBuilder _loanScheduleBuilder =
        loanScheduleBuilder ?? DefaultLoanScheduleBuilder();

    private static LoanPaymentScheduleBuilder DefaultLoanScheduleBuilder()
    {
        var schedule = new LoanScheduleCalculator();
        return new LoanPaymentScheduleBuilder(
            schedule,
            new LoanAmortizationCalculator(schedule));
    }

    private static readonly CultureInfo TurkishCulture =
        CultureInfo.GetCultureInfo("tr-TR");

    public SimulationResult Calculate(
        FinancialPlan currentPlan,
        DateOnly asOf,
        SimulationRequest request,
        int periodCount = 12,
        DateOnly? firstSalaryDate = null) =>
        Calculate(
            currentPlan,
            asOf,
            [request],
            periodCount,
            firstSalaryDate);

    public SimulationResult Calculate(
        FinancialPlan currentPlan,
        DateOnly asOf,
        IReadOnlyList<SimulationRequest> requests,
        int periodCount = 12,
        DateOnly? firstSalaryDate = null)
    {
        Validate(requests);
        var baseline = projectionCalculator.Calculate(
            currentPlan,
            asOf,
            periodCount,
            firstSalaryDate);
        var scenarioPlan = BuildScenarioPlan(currentPlan, requests);
        var scenario = projectionCalculator.Calculate(
            scenarioPlan,
            asOf,
            periodCount,
            firstSalaryDate);
        var rows = baseline
            .Zip(scenario, (current, planned) =>
                new SimulationImpactRow(current, planned))
            .ToArray();
        var lowest = scenario
            .OrderBy(x => x.EndingProjectedSavings)
            .ThenBy(x => x.PeriodStart)
            .First();
        var firstNegativeCapacity = scenario
            .FirstOrDefault(x => x.EstimatedSavingsCapacity < 0m);
        var firstNegativeSavings = scenario
            .FirstOrDefault(x => x.EndingProjectedSavings < 0m);
        var maximumCarryOverDeficit = scenario
            .Select(x => x.CarryOverDeficit)
            .Append(scenario[^1].RemainingCarryOverDeficit)
            .Max();
        var recovery = scenario.FirstOrDefault(x =>
            x.HasCarryOverDeficit && x.EndingProjectedSavings >= 0m);
        var loanImpacts = BuildLoanImpacts(currentPlan, scenarioPlan);
        var totalCost = requests.Sum(ResolveTotalCost) +
                        loanImpacts.Sum(x => x.PrepaidAmount);
        var financingCosts = requests
            .Where(x => x.Type == SimulationScenarioType.FinancingLoan)
            .Select(x => (x.TotalRepaymentAmount ?? x.Amount) - x.Amount)
            .ToArray();
        decimal? financingCost = financingCosts.Length > 0
            ? financingCosts.Sum()
            : null;
        var risk = new SimulationRiskSummary(
            scenario.Min(x => x.AvailableAfterMandatory),
            scenario.Min(x => x.EstimatedSavingsCapacity),
            scenario.Min(x => x.EndingProjectedSavings),
            lowest.Period,
            firstNegativeCapacity?.Period,
            firstNegativeSavings?.Period,
            maximumCarryOverDeficit,
            recovery?.Period,
            scenario[^1].EndingProjectedSavings,
            totalCost,
            financingCost);

        var interestDifference =
            ProjectionInterestSummary.From(scenario).TotalInterestCost -
            ProjectionInterestSummary.From(baseline).TotalInterestCost;
        return new SimulationResult(
            baseline,
            scenario,
            rows,
            risk,
            BuildFriendlySummary(
                risk,
                interestDifference,
                scenario[^1].EndingProjectedSavings -
                baseline[^1].EndingProjectedSavings))
        {
            LoanImpacts = loanImpacts
        };
    }

    private static string BuildFriendlySummary(
        SimulationRiskSummary risk,
        decimal additionalInterestCost,
        decimal endingDifference)
    {
        var parts = new List<string>();
        parts.Add(endingDifference switch
        {
            > 0m =>
                $"Bu plan 12 ay sonundaki tahmini finansal durumunu {Money(Math.Abs(endingDifference))} artırıyor.",
            < 0m =>
                $"Bu plan 12 ay sonundaki tahmini finansal durumunu {Money(Math.Abs(endingDifference))} azaltıyor.",
            _ => "Bu plan 12 ay sonundaki tahmini finansal durumu değiştirmiyor."
        });

        if (risk.FirstNegativeProjectedSavingsPeriod is SalaryPeriod negative)
        {
            parts.Add(
                $"Bu plan {PeriodMonth(negative)} döneminde finansman açığı oluşturuyor.");
            parts.Add(risk.RecoveryPeriod is SalaryPeriod recovered
                ? $"Açık {PeriodMonth(recovered)} döneminde kapanıyor."
                : "Açık gösterilen dönemlerde kapanmıyor.");
        }
        else if (risk.MaximumCarryOverDeficit > 0m)
        {
            parts.Add(risk.RecoveryPeriod is SalaryPeriod openingRecovery
                ? $"Devreden açık {PeriodMonth(openingRecovery)} döneminde kapanıyor."
                : "Devreden açık gösterilen dönemlerde kapanmıyor.");
        }
        else
        {
            parts.Add("12 dönemlik görünümde finansman açığı oluşmuyor.");
        }

        parts.Add(additionalInterestCost switch
        {
            > 0m =>
                $"Bu planın tahmini ek faiz yükü {Money(additionalInterestCost)}.",
            < 0m =>
                $"Bu plan mevcut plana göre {Money(Math.Abs(additionalInterestCost))} daha düşük faiz yükü oluşturuyor.",
            _ => "Bu plan tahmini faiz yükünü değiştirmiyor."
        });

        return string.Join(" ", parts);
    }

    private static string Money(decimal value) =>
        $"{value.ToString("N2", TurkishCulture)} TL";

    private static string PeriodMonth(SalaryPeriod period) =>
        period.Start.ToString("MMMM yyyy", TurkishCulture);
}
