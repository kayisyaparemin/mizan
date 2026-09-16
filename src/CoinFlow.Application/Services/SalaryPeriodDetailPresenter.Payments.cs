using System.Globalization;
using CoinFlow.Application.Models;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Services;

public sealed partial class SalaryPeriodDetailPresenter
{
    private static IReadOnlyList<DetailPaymentRow> BuildPayments(
        SalaryPeriodProjection row,
        bool allowCardPaymentModeChange)
    {
        var result = row.MandatoryItems
            .Select(item => ToPaymentRow(
                item,
                row.CardPaymentStatuses,
                allowCardPaymentModeChange))
            .ToList();
        result.AddRange(row.LargeExpenseItems.Select(expense =>
            new DetailPaymentRow(
                expense.ExactDate,
                expense.Name,
                "Planlı Büyük Ödeme",
                expense.Amount,
                DetailSemanticType.Expense,
                row.PeriodStart,
                IsEstimated: false,
                IsBeforeFundingSalary: expense.ExactDate < row.PeriodStart,
                IsUndetermined: false,
                expense.Note)));
        result.AddRange(row.CardPaymentStatuses
            .Where(x => x.Payment is null)
            .Select(card => new DetailPaymentRow(
                card.PaymentDueDate,
                card.CardName,
                "Kredi Kartı",
                null,
                DetailSemanticType.Mandatory,
                card.AssignedSalaryDate,
                IsEstimated: false,
                card.PaymentBeforeSalary,
                IsUndetermined: true,
                "Gerçek ödeme planı henüz belirlenmedi.")));
        return result
            .OrderBy(x => x.Date)
            .ThenBy(x => x.Name)
            .ToArray();
    }

    private static DetailPaymentRow ToPaymentRow(
        ObligationItem item,
        IReadOnlyList<CreditCardPaymentProjectionStatus> cardStatuses,
        bool allowCardPaymentModeChange)
    {
        var row = new DetailPaymentRow(
            item.DueDate,
            item.Name,
            Category(item.Type),
            item.Amount,
            DetailSemanticType.Mandatory,
            item.AssignedSalaryDate,
            item.IsEstimate,
            item.PaymentBeforeSalary,
            IsUndetermined: false,
            item.Detail);
        if (item.Type != ObligationType.CreditCard ||
            item.PaymentId == Guid.Empty)
        {
            return row;
        }

        var cardId = item.PaymentId;

        // Kart yükümlülüğü PaymentId'de kart kimliğini taşır; uygulanan ödeme
        // şeklini aynı vadedeki ekstre durumundan okuyoruz.
        var status = cardStatuses.FirstOrDefault(x =>
            x.CardId == cardId && x.PaymentDueDate == item.DueDate);
        return row with
        {
            CreditCardId = cardId,
            CardPaymentType = status?.PaymentType,
            CanChangeCardPaymentMode = allowCardPaymentModeChange &&
                                       status?.PaymentType is
                                           CreditCardPaymentType.Minimum or
                                           CreditCardPaymentType.FullStatement
        };
    }

    private static string Category(ObligationType type) => type switch
    {
        ObligationType.Loan => "Kredi",
        ObligationType.CreditCard => "Kredi Kartı",
        ObligationType.TemporaryPayment => "Geçici Plan",
        ObligationType.InstallmentPayment => "Taksit / Finansman",
        ObligationType.OtherScheduledPayment => "Planlı Ödeme",
        ObligationType.PlannedLargeExpense => "Planlı Büyük Ödeme",
        _ => "Ödeme"
    };

    private static IReadOnlyList<DetailComparisonRow> BuildComparisonRows(
        SalaryPeriodProjection baseline,
        SalaryPeriodProjection scenario)
    {
        var impact = new SimulationImpactRow(baseline, scenario);
        return
        [
            Compare(
                "Zorunlu",
                baseline.MandatoryOutflow,
                scenario.MandatoryOutflow,
                impact.MandatoryOutflowDifference,
                higherIsBetter: false),
            Compare(
                "Dönem neti",
                baseline.EstimatedSavingsCapacity,
                scenario.EstimatedSavingsCapacity,
                impact.SavingsCapacityDifference,
                higherIsBetter: true),
            Compare(
                "Faiz yükü",
                baseline.TotalInterestGenerated,
                scenario.TotalInterestGenerated,
                impact.InterestDifference,
                higherIsBetter: false),
            Compare(
                "Dönem sonu durumu",
                baseline.EndingProjectedSavings,
                scenario.EndingProjectedSavings,
                impact.ProjectedSavingsDifference,
                higherIsBetter: true)
        ];
    }

    private static DetailComparisonRow Compare(
        string label,
        decimal baseline,
        decimal scenario,
        decimal difference,
        bool higherIsBetter) =>
        new(
            label,
            baseline,
            scenario,
            difference,
            higherIsBetter);

    private static IReadOnlyList<DetailMetric> BuildDebugRows(
        SalaryPeriodProjection row)
    {
        var result = new List<DetailMetric>
        {
            new(
                "OpeningSavings",
                row.OpeningProjectedSavings,
                DetailSemanticType.Neutral),
            new(
                "CurrentContribution",
                row.CurrentPeriodNetContribution,
                DetailSemanticType.Neutral),
            new(
                "EndingBeforeDeficitInterest",
                row.EndingProjectedSavingsBeforeDeficitInterest,
                DetailSemanticType.Neutral),
            new(
                "DeficitPrincipal",
                row.DeficitPrincipal,
                DetailSemanticType.Neutral),
            new(
                "DeficitInterestRate",
                row.AppliedDeficitInterestRate,
                DetailSemanticType.Neutral,
                DisplayText:
                    $"%{(row.AppliedDeficitInterestRate * 100m).ToString("N2", TurkishCulture)}"),
            new(
                "DeficitInterest",
                row.DeficitFinancingInterest,
                DetailSemanticType.Interest),
            new(
                "FinalEndingSavings",
                row.EndingProjectedSavings,
                DetailSemanticType.Neutral)
        };
        result.AddRange(row.CardPaymentStatuses.SelectMany(card =>
            new[]
            {
                new DetailMetric(
                    $"{card.CardName} • OpeningCarry",
                    card.OpeningCarriedBalance ?? 0m,
                    DetailSemanticType.Neutral),
                new DetailMetric(
                    $"{card.CardName} • NewCharges",
                    card.NewCharges,
                    DetailSemanticType.Neutral),
                new DetailMetric(
                    $"{card.CardName} • Statement",
                    card.StatementBalance ?? 0m,
                    DetailSemanticType.Neutral),
                new DetailMetric(
                    $"{card.CardName} • Payment",
                    card.Payment ?? 0m,
                    DetailSemanticType.Neutral),
                new DetailMetric(
                    $"{card.CardName} • RemainingPrincipal",
                    card.CarriedPrincipalAfterPayment ?? 0m,
                    DetailSemanticType.Neutral),
                new DetailMetric(
                    $"{card.CardName} • InterestRate",
                    card.AppliedInterestRate,
                    DetailSemanticType.Neutral,
                    DisplayText:
                        $"%{(card.AppliedInterestRate * 100m).ToString("N2", TurkishCulture)}"),
                new DetailMetric(
                    $"{card.CardName} • CarryInterest",
                    card.CarryInterest,
                    DetailSemanticType.Interest),
                new DetailMetric(
                    $"{card.CardName} • NextCarry",
                    card.NextCarriedBalance ?? 0m,
                    DetailSemanticType.Neutral)
            }));
        return result;
    }
}
