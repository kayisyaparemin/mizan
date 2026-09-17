using Mizan.Application.Models;
using Mizan.Domain.Calculations;
using Mizan.Domain.Models;

namespace Mizan.Application.Services;

public static class SimulatorProjectionMath
{
    public static decimal PeriodNeed(CashFlowPeriodProjection row) =>
        row.MandatoryOutflow +
        row.VariableExpenseAllowance +
        row.PlannedLargeCashExpenses +
        row.DeficitFinancingInterest;

    public static IReadOnlyList<DetailMetric> BuildNeedBreakdown(
        CashFlowPeriodProjection row)
    {
        var rows = new List<DetailMetric>();
        AddIfNonZero(rows, "Krediler", row.LoanPayments);
        AddIfNonZero(rows, "Kredi kartları", row.CreditCardPayments);
        AddIfNonZero(rows, "Geçici ödemeler", row.TemporaryPayments);
        AddIfNonZero(rows, "Taksit / finansman", row.InstallmentPayments);
        AddIfNonZero(
            rows,
            "Planlı nakit ödemeler",
            row.PlannedLargeCashExpenses,
            DetailSemanticType.Expense);
        AddIfNonZero(rows, "Diğer planlı ödemeler", row.OtherScheduledPayments);
        AddIfNonZero(
            rows,
            "Tahmini yaşam gideri",
            row.VariableExpenseAllowance,
            DetailSemanticType.Expense);
        AddIfNonZero(
            rows,
            "Finansman açığı faizi",
            row.DeficitFinancingInterest,
            DetailSemanticType.Interest);
        rows.Add(new DetailMetric(
            "Toplam",
            PeriodNeed(row),
            DetailSemanticType.Mandatory,
            IsTotal: true));
        return rows;
    }

    private static void AddIfNonZero(
        ICollection<DetailMetric> rows,
        string label,
        decimal amount,
        DetailSemanticType semantic = DetailSemanticType.Mandatory)
    {
        if (amount != 0m)
        {
            rows.Add(new DetailMetric(label, amount, semantic));
        }
    }
}
