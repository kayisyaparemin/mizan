using CommunityToolkit.Mvvm.Input;
using CoinFlow.App.Models;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.App.ViewModels;

public sealed partial class ScenarioConditionForm
{
    public void SetLookups(FinancialPlan plan)
    {
        CreditCards.Clear();
        foreach (var card in plan.CreditCards)
        {
            CreditCards.Add(new SelectionOption<Guid>(
                $"{card.Bank} {card.Name}".Trim(),
                card.Id));
        }

        Loans.Clear();
        _loans.Clear();
        foreach (var loan in plan.Loans.Where(x => x.IsActive))
        {
            Loans.Add(new SelectionOption<Guid>(
                $"{loan.Bank} {loan.Name}".Trim(),
                loan.Id));
            _loans[loan.Id] = loan;
        }

        SelectedCreditCard ??= CreditCards.FirstOrDefault();
        SelectedLoan ??= Loans.FirstOrDefault();
    }

    public void SetStrategyLookups(
        IEnumerable<DateOnly> availableSalaryDates,
        PaymentAssignmentMode currentMode)
    {
        StrategySalaryDates.Clear();
        foreach (var date in availableSalaryDates)
        {
            StrategySalaryDates.Add(new SelectionOption<DateOnly>(
                $"{date.ToString("dd MMMM yyyy", TurkishCulture)} dönemi",
                date));
        }

        SelectedStrategySalaryDate ??= StrategySalaryDates.FirstOrDefault();
        SelectedStrategyMode ??= StrategyModes.First(x => x.Value != currentMode);
    }

    public void LoadOptions(FinancialPlan plan)
    {
        SetLookups(plan);
        var futureSalaryDates = plan.Salaries
            .Select(s => s.EffectiveDate)
            .Where(d => plan.Settings.ProjectionAnchorDate == default || d >= plan.Settings.ProjectionAnchorDate)
            .Distinct()
            .OrderBy(d => d);
        SetStrategyLookups(futureSalaryDates, plan.InitialPaymentAssignmentMode);
    }

    public void ClearLookups()
    {
        CreditCards.Clear();
        Loans.Clear();
        _loans.Clear();
        StrategySalaryDates.Clear();
    }

    [RelayCommand]
    private void SelectGroup(ScenarioGroupView? group)
    {
        if (group is null || group.IsSelected)
        {
            return;
        }

        SelectGroup(group.Group);
    }

    public void SelectGroup(ScenarioGroup group) =>
        SelectOption(SimulationScenarioCatalog
            .OptionsIn(group, _directEntryOnly)
            .First());

    [RelayCommand]
    private void SelectOption(ScenarioOptionView? option)
    {
        if (option is not null)
        {
            SelectOption(option.Option);
        }
    }

    private void RefreshNameSuggestion() =>
        NameSuggestion = IsLoanPrepayment && SelectedLoan is { } loan
            ? IsPartialPrepayment
                ? $"{loan.Label} ara ödeme"
                : $"{loan.Label} erken kapama"
            : string.Empty;

    private void MoveStartDateToNextInstallment()
    {
        if (EditingType is not null ||
            SelectedLoan is not { } option ||
            !_loans.TryGetValue(option.Value, out var loan))
        {
            return;
        }

        var from = DateOnly.FromDateTime(DateTime.Today);
        var next = Enumerable.Range(0, loan.RemainingInstallmentCount)
            .Select(index => CalendarRules.AddMonthsKeepingDay(
                loan.NextPaymentDate,
                index,
                loan.PaymentDay))
            .FirstOrDefault(date => date >= from);
        if (next != default)
        {
            StartDate = next.ToDateTime(TimeOnly.MinValue);
        }
    }

    public void EndEditing() => EditingType = null;

    public string CardLabel(Guid? creditCardId) =>
        CreditCards.FirstOrDefault(x => x.Value == creditCardId)?.Label ??
        "Kart";

    public string LoanLabel(Guid? loanId) =>
        Loans.FirstOrDefault(x => x.Value == loanId)?.Label ?? "Kredi";
}
