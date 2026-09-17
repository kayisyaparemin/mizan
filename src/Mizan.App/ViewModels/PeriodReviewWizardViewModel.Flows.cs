using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Mizan.App.Models;
using Mizan.App.Services;
using Mizan.Application.Models;
using Mizan.Domain.Models;

namespace Mizan.App.ViewModels;

public partial class PeriodReviewWizardViewModel
{
    [RelayCommand]
    private void AddUnplannedPayment()
    {
        AddFlow(
            ActualFlowType.UnplannedPayment,
            NewPaymentName,
            NewPaymentCategory,
            NewPaymentDate,
            NewPaymentAmount);
        NewPaymentName = string.Empty;
        NewPaymentAmount = string.Empty;
    }

    [RelayCommand]
    private void AddUnplannedIncome()
    {
        AddFlow(
            ActualFlowType.UnplannedIncome,
            NewIncomeName,
            NewIncomeCategory,
            NewIncomeDate,
            NewIncomeAmount);
        NewIncomeName = string.Empty;
        NewIncomeAmount = string.Empty;
    }

    [RelayCommand]
    private void RemoveFlow(ActualFlowInputItem item)
    {
        Flows.Remove(item);
        OnPropertyChanged(nameof(HasFlows));
    }

    private LivingBreakdownDraft Breakdown(string category, string amount) =>
        new(
            category,
            string.IsNullOrWhiteSpace(amount)
                ? 0m
                : ParseMoney(amount, category));

    private void AddFlow(
        ActualFlowType type,
        string name,
        string category,
        DateTime date,
        string amountText)
    {
        try
        {
            var amount = ParseMoney(
                amountText,
                type == ActualFlowType.UnplannedIncome
                    ? "Gelir tutarı"
                    : "Ödeme tutarı");
            if (string.IsNullOrWhiteSpace(name) || amount <= 0m)
            {
                throw new InvalidOperationException(
                    "Ad ve sıfırdan büyük tutar gereklidir.");
            }

            Flows.Add(new ActualFlowInputItem(
                Guid.NewGuid(),
                type,
                name.Trim(),
                string.IsNullOrWhiteSpace(category)
                    ? type == ActualFlowType.UnplannedIncome ? "Ek gelir" : "Diğer"
                    : category.Trim(),
                DateOnly.FromDateTime(date),
                amount));
            OnPropertyChanged(nameof(HasFlows));
            SetStatus(string.Empty);
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(exception));
        }
    }

    partial void OnCurrentStepChanged(int value) => NotifyStepProperties();
    partial void OnIsSuccessChanged(bool value) => NotifyStepProperties();

    private void NotifyStepProperties()
    {
        OnPropertyChanged(nameof(IsStep1));
        OnPropertyChanged(nameof(IsStep2));
        OnPropertyChanged(nameof(IsStep3));
        OnPropertyChanged(nameof(IsReviewVisible));
        OnPropertyChanged(nameof(PlanIndicator));
        OnPropertyChanged(nameof(ActualIndicator));
        OnPropertyChanged(nameof(ResultIndicator));
    }

    private static string SignedMoney(decimal value) =>
        $"{(value > 0m ? "+" : string.Empty)}{value.ToString("N2", CultureInfo.GetCultureInfo("tr-TR"))} TL";

    private static IReadOnlyList<PeriodPlanPaymentLine> FinalPaymentLines(
        PeriodPlanSnapshot plan,
        PeriodPlanRevision? revision) =>
        revision?.PaymentLines.Count > 0
            ? revision.PaymentLines
            : plan.PaymentLines;

    private sealed record FinalPlanValues(
        decimal PlannedIncome,
        decimal PlannedLoanPayments,
        decimal PlannedCardPayments,
        decimal PlannedTemporaryPayments,
        decimal PlannedInstallmentPayments,
        decimal PlannedOtherScheduledPayments,
        decimal PlannedLargeExpenses,
        decimal PlannedVariableExpenseAllowance,
        decimal PlannedInterest,
        decimal PlannedDeficitInterest,
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
                plan.PlannedLargeExpenses,
                plan.PlannedVariableExpenseAllowance,
                plan.PlannedCardInterest + plan.PlannedDeficitInterest,
                plan.PlannedDeficitInterest,
                plan.PlannedEndingBalance)
            : new FinalPlanValues(
                revision.PlannedIncome,
                revision.PlannedLoanPayments,
                revision.PlannedCardPayments,
                revision.PlannedTemporaryPayments,
                revision.PlannedInstallmentPayments,
                revision.PlannedOtherScheduledPayments,
                revision.PlannedLargeExpenses,
                revision.PlannedVariableExpenseAllowance,
                revision.PlannedCardInterest +
                revision.PlannedDeficitInterest,
                revision.PlannedDeficitInterest,
                revision.PlannedEndingBalance);
    }
}
