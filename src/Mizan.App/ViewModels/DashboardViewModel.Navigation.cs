using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Mizan.App.Services;
using Mizan.Domain.Calculations;

namespace Mizan.App.ViewModels;

public partial class DashboardViewModel
{
    [RelayCommand]
    private void ToggleCalculationDetails() =>
        ShowCalculationDetails = !ShowCalculationDetails;

    private static decimal ParseMoney(string value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0 ||
            !decimal.TryParse(
                text,
                NumberStyles.Number,
                TurkishCulture,
                out var amount))
        {
            throw new InvalidOperationException(
                "Mevcut tutar geçerli bir sayı olmalıdır.");
        }

        return amount;
    }

    private void ResetToEmptyState(string message, string action)
    {
        HasFinancialPlan = false;
        IsEmptyState = true;
        _currentPeriod = null;
        HasUndeterminedCardPayment = false;
        HasPendingStrategy = false;
        HasObservation = false;
        HasRemainingLines = false;
        IsPeriodClosable = false;
        RemainingLines.Clear();
        Alerts.Clear();
        HasAlerts = false;
        EmptyStateMessage = message;
        EmptyStateAction = action;
    }

    [RelayCommand]
    private Task OpenSimulationAsync() =>
        navigation.NavigateToAsync(NavigationRoutes.Simulation);

    [RelayCommand]
    private Task OpenSettingsAsync() =>
        navigation.NavigateToAsync(NavigationRoutes.Settings);

    [RelayCommand]
    private Task OpenCommitmentsAsync() =>
        navigation.NavigateToAsync(NavigationRoutes.Commitments);

    [RelayCommand]
    private Task OpenEmptyStateAsync() =>
        ShouldShowOnboarding
            ? OpenOnboardingAsync()
            : OpenCommitmentsAsync();

    [RelayCommand]
    private Task OpenFutureMonthsAsync() =>
        navigation.NavigateToAsync(NavigationRoutes.FutureMonths);

    [RelayCommand]
    private Task OpenHistoryAsync() =>
        navigation.NavigateToAsync(NavigationRoutes.History);

    [RelayCommand]
    private async Task OpenOnboardingAsync()
    {
        if (await navigation.OpenOnboardingModalAsync())
        {
            await LoadAsync();
        }
    }

    [RelayCommand]
    public Task OpenPeriodReviewAsync() =>
        navigation.OpenPeriodReviewModalAsync();

    private static string BuildDetails(CashFlowPeriodProjection row)
    {
        var incomeLines = row.IncomeItems.Select(x =>
            $"{x.SourceDate:dd.MM} {x.Name}: {Money(x.Amount, 2)}");
        var paymentLines = row.MandatoryItems.Select(x =>
            $"{x.DueDate:dd.MM} {x.Name}: {Money(x.Amount, 2)}" +
            (x.IsEstimate ? " (tahmini)" : string.Empty) +
            (x.PaymentBeforePeriodStart
                ? $" • ⚠ {x.AssignedPeriodDate:dd.MM} dönemi; gerçek vade önce"
                : string.Empty));
        var calculation = new[]
        {
            $"OpeningProjectedBalance: {Money(row.OpeningProjectedBalance, 2)}",
            $"CarryOverDeficit: {Money(row.CarryOverDeficit, 2)}",
            $"Income: {Money(row.TotalIncome, 2)}",
            $"MandatoryOutflow: {Money(row.MandatoryOutflow, 2)}",
            $"AvailableAfterMandatory: {Money(row.AvailableAfterMandatory, 2)}",
            $"AvailableAfterCarryOverDeficit: {Money(row.AvailableAfterCarryOverDeficit, 2)}",
            $"VariableExpenseAllowance: {Money(row.VariableExpenseAllowance, 2)}",
            $"LargeExpenses: {Money(row.PlannedLargeCashExpenses, 2)}",
            $"CurrentPeriodNetContribution: {Money(row.CurrentPeriodNetContribution, 2)}",
            $"EndingBeforeDeficitInterest: {Money(row.EndingProjectedBalanceBeforeDeficitInterest, 2)}",
            $"DeficitPrincipal: {Money(row.DeficitPrincipal, 2)}",
            $"DeficitInterestRate: %{(row.AppliedDeficitInterestRate * 100m).ToString("N2", TurkishCulture)}",
            $"DeficitInterest: {Money(row.DeficitFinancingInterest, 2)}",
            $"CardInterestGenerated: {Money(row.CardInterestGenerated, 2)}",
            $"TotalInterestGenerated: {Money(row.TotalInterestGenerated, 2)}",
            $"FinalEndingProjectedBalance: {Money(row.EndingProjectedBalance, 2)}",
            string.Empty
        };
        var cardInterestLines = row.CardPaymentStatuses.Select(x =>
            $"{x.CardName}: Statement={Money(x.StatementBalance ?? 0m, 2)} • " +
            $"Payment={Money(x.Payment ?? 0m, 2)} • " +
            $"RemainingPrincipal={Money(x.CarriedPrincipalAfterPayment ?? 0m, 2)} • " +
            $"Rate=%{(x.AppliedInterestRate * 100m).ToString("N2", TurkishCulture)} • " +
            $"CarryInterest={Money(x.CarryInterest, 2)} • " +
            $"NextCarry={Money(x.NextCarriedBalance ?? 0m, 2)}");
        return string.Join(
            Environment.NewLine,
            calculation.Concat(incomeLines)
                .Concat(paymentLines)
                .Concat(cardInterestLines));
    }

    private static string ModeText(
        Mizan.Domain.Models.CashFlowAllocationMode mode) =>
        mode == Mizan.Domain.Models.CashFlowAllocationMode.PreviousPeriod
            ? "Geçmiş dönemi kapatırım"
            : "Gelecek dönemi karşılarım";
}
