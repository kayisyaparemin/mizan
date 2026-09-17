using System.Globalization;
using Mizan.Application.Models;
using Mizan.Domain.Models;

namespace Mizan.Application.Services;

public sealed class PlanActualComparisonCalculator
{
    // Özet ekranda gösterilip PeriodActual ile kaydediliyor; cihaz kültürüne
    // bırakılırsa İngilizce cihazda "1,234.56 TL" olarak kalıcılaşır.
    private static readonly CultureInfo TurkishCulture =
        CultureInfo.GetCultureInfo("tr-TR");

    public PlanActualComparison Calculate(
        PeriodPlanSnapshot plan,
        PeriodPlanRevision? revision,
        PeriodActual actual)
    {
        var planned = FinalPlanValues.From(plan, revision);
        var lines = new[]
        {
            Line("Gelir", planned.PlannedIncome, actual.ActualIncome),
            Line("Krediler", planned.PlannedLoanPayments,
                actual.ActualLoanPayments),
            Line("Kredi kartları", planned.PlannedCardPayments,
                actual.ActualCardPayments),
            Line("Geçici ödemeler", planned.PlannedTemporaryPayments,
                actual.ActualTemporaryPayments),
            Line("Taksitli ödemeler", planned.PlannedInstallmentPayments,
                actual.ActualInstallmentPayments),
            Line("Diğer planlı ödemeler",
                planned.PlannedOtherScheduledPayments,
                actual.ActualOtherScheduledPayments),
            Line("Zorunlu ödemeler", planned.PlannedMandatoryPayments,
                actual.ActualMandatoryPayments),
            Line("Büyük ödemeler", planned.PlannedLargeExpenses,
                actual.ActualLargeExpenses),
            Line("Yaşam giderleri", planned.PlannedVariableExpenseAllowance,
                actual.ActualLivingSpend),
            Line("Faiz", planned.PlannedInterest, actual.ActualInterest),
            Line("Plan dışı ödemeler", 0m,
                actual.UnplannedPayments),
            Line("Dönem düzeltmesi", 0m,
                actual.ReconciliationAdjustment)
        };
        var difference = actual.ConfirmedEndingBalance -
                         planned.PlannedEndingBalance;
        return new PlanActualComparison(
            planned.PlannedEndingBalance,
            actual.ConfirmedEndingBalance,
            difference,
            BuildSummary(difference, lines),
            lines);
    }

    private static PlanActualComparisonLine Line(
        string category,
        decimal planned,
        decimal actual) =>
        new(category, planned, actual, actual - planned);

    private static string BuildSummary(
        decimal endingDifference,
        IReadOnlyList<PlanActualComparisonLine> lines)
    {
        if (endingDifference == 0m)
        {
            return "Dönem sonu finansal durumun planla aynı seviyede gerçekleşti.";
        }

        var direction = endingDifference > 0m ? "üzerinde" : "altında";
        var lead =
            $"Dönem sonu finansal durumun planın {Math.Abs(endingDifference).ToString("N2", TurkishCulture)} TL {direction} gerçekleşti.";
        var cause = lines
            .Where(x =>
                x.Category is not "Dönem düzeltmesi" and
                not "Zorunlu ödemeler" &&
                x.Difference != 0m)
            .OrderByDescending(x => Math.Abs(x.Difference))
            .FirstOrDefault();
        return cause is null
            ? lead
            : $"{lead} En belirgin fark {cause.Category} kaleminde {Math.Abs(cause.Difference).ToString("N2", TurkishCulture)} TL oldu.";
    }

    private sealed record FinalPlanValues(
        decimal PlannedIncome,
        decimal PlannedLoanPayments,
        decimal PlannedCardPayments,
        decimal PlannedTemporaryPayments,
        decimal PlannedInstallmentPayments,
        decimal PlannedOtherScheduledPayments,
        decimal PlannedMandatoryPayments,
        decimal PlannedVariableExpenseAllowance,
        decimal PlannedLargeExpenses,
        decimal PlannedInterest,
        decimal PlannedEndingBalance)
    {
        public static FinalPlanValues From(
            PeriodPlanSnapshot plan,
            PeriodPlanRevision? revision) => revision is null
            ? new FinalPlanValues(
                plan.PlannedIncome,
                plan.PlannedLoanPayments,
                plan.PlannedCardPayments,
                plan.PlannedTemporaryPayments,
                plan.PlannedInstallmentPayments,
                plan.PlannedOtherScheduledPayments,
                plan.PlannedMandatoryPayments,
                plan.PlannedVariableExpenseAllowance,
                plan.PlannedLargeExpenses,
                plan.PlannedCardInterest + plan.PlannedDeficitInterest,
                plan.PlannedEndingBalance)
            : new FinalPlanValues(
                revision.PlannedIncome,
                revision.PlannedLoanPayments,
                revision.PlannedCardPayments,
                revision.PlannedTemporaryPayments,
                revision.PlannedInstallmentPayments,
                revision.PlannedOtherScheduledPayments,
                revision.PlannedMandatoryPayments,
                revision.PlannedVariableExpenseAllowance,
                revision.PlannedLargeExpenses,
                revision.PlannedCardInterest +
                revision.PlannedDeficitInterest,
                revision.PlannedEndingBalance);
    }
}
