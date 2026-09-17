using System.Globalization;
using Mizan.Application.Models;
using Mizan.Domain.Calculations;
using Mizan.Domain.Models;

namespace Mizan.Application.Services;

public sealed partial class CashFlowPeriodDetailPresenter
{
    private static IReadOnlyList<DetailPaymentRow> BuildPayments(
        CashFlowPeriodProjection row,
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
                card.AssignedPeriodDate,
                IsEstimated: false,
                card.PaymentBeforePeriodStart,
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
            item.AssignedPeriodDate,
            item.IsEstimate,
            item.PaymentBeforePeriodStart,
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
        CashFlowPeriodProjection baseline,
        CashFlowPeriodProjection scenario)
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
                baseline.EstimatedSurplus,
                scenario.EstimatedSurplus,
                impact.SurplusDifference,
                higherIsBetter: true),
            Compare(
                "Faiz yükü",
                baseline.TotalInterestGenerated,
                scenario.TotalInterestGenerated,
                impact.InterestDifference,
                higherIsBetter: false),
            Compare(
                "Dönem sonu durumu",
                baseline.EndingProjectedBalance,
                scenario.EndingProjectedBalance,
                impact.ProjectedBalanceDifference,
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
        CashFlowPeriodProjection row)
    {
        var result = new List<DetailMetric>
        {
            new(
                "OpeningBalance",
                row.OpeningProjectedBalance,
                DetailSemanticType.Neutral),
            new(
                "CurrentContribution",
                row.CurrentPeriodNetContribution,
                DetailSemanticType.Neutral),
            new(
                "EndingBeforeDeficitInterest",
                row.EndingProjectedBalanceBeforeDeficitInterest,
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
                row.EndingProjectedBalance,
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
