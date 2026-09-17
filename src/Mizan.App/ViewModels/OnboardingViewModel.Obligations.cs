using CommunityToolkit.Mvvm.Input;
using Mizan.App.Models;
using Mizan.App.Services;
using Mizan.Application.Models;
using Mizan.Domain.Calculations;
using Mizan.Domain.Models;

namespace Mizan.App.ViewModels;

public partial class OnboardingViewModel
{
    [RelayCommand]
    private void AddIncome()
    {
        try
        {
            _salaries.Add(new SalaryScheduleEntry
            {
                Amount = ParsePositiveMoney(IncomeAmount, "Gelir"),
                EffectiveDate = DateOnly.FromDateTime(IncomeEffectiveDate),
                Description = string.IsNullOrWhiteSpace(IncomeName)
                    ? "Gelir"
                    : IncomeName.Trim()
            });
            IncomeName = string.Empty;
            IncomeAmount = string.Empty;
            RefreshDraftLines();
            SetStatus(string.Empty);
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(exception));
        }
    }

    [RelayCommand]
    private void AddLoan()
    {
        try
        {
            if (!int.TryParse(LoanPaymentDay, out var paymentDay) ||
                !int.TryParse(LoanInstallmentCount, out var count))
            {
                throw new InvalidOperationException(
                    "Kredi günü ve taksit sayısı geçerli olmalıdır.");
            }

            _loans.Add(new Loan
            {
                Name = RequireText(LoanName, "Kredi adı"),
                Bank = LoanBank.Trim(),
                MonthlyPayment = ParsePositiveMoney(
                    LoanMonthlyPayment,
                    "Aylık ödeme"),
                PaymentDay = paymentDay,
                NextPaymentDate = DateOnly.FromDateTime(
                    LoanNextPaymentDate),
                RemainingInstallmentCount = count,
                RemainingDebt = ParseOptionalMoney(LoanRemainingDebt)
            });
            LoanName = string.Empty;
            LoanBank = string.Empty;
            LoanMonthlyPayment = string.Empty;
            LoanRemainingDebt = string.Empty;
            LoanPaymentDay = string.Empty;
            LoanInstallmentCount = string.Empty;
            RefreshDraftLines();
            SetStatus(string.Empty);
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(exception));
        }
    }

    [RelayCommand]
    private void AddPayment()
    {
        try
        {
            var type = SelectedPaymentType?.Value ?? "one-time";
            var amount = ParsePositiveMoney(PaymentAmount, "Ödeme tutarı");
            var date = DateOnly.FromDateTime(PaymentDate);
            var name = RequireText(PaymentName, "Ödeme adı");
            if (type == "one-time")
            {
                _payments.Add(new PlannedLargeExpense
                {
                    Name = name,
                    Amount = amount,
                    ExactDate = date
                });
            }
            else
            {
                if (!int.TryParse(PaymentCount, out var count) || count < 1)
                {
                    throw new InvalidOperationException(
                        "Ödeme adedi geçerli olmalıdır.");
                }

                var id = Guid.NewGuid();
                _paymentPlans.Add(new TemporaryPaymentPlan
                {
                    Id = id,
                    Name = name,
                    Kind = type == "recurring"
                        ? PaymentPlanKind.Recurring
                        : PaymentPlanKind.Temporary,
                    OriginalAmount = amount,
                    TotalRepaymentAmount = amount * count,
                    Installments = Enumerable.Range(0, count)
                        .Select(index => new TemporaryPaymentInstallment
                        {
                            Id = Guid.NewGuid(),
                            PlanId = id,
                            DueDate = CalendarRules.AddMonthsKeepingDay(
                                date,
                                index,
                                date.Day),
                            Amount = amount
                        })
                        .ToArray()
                });
            }

            PaymentName = string.Empty;
            PaymentAmount = string.Empty;
            RefreshDraftLines();
            SetStatus(string.Empty);
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(exception));
        }
    }

    public void RemoveDraftLine(FinancialRecordLine line)
    {
        switch (line.Kind)
        {
            case FinancialRecordKind.Salary:
                _salaries.RemoveAll(x => x.Id == line.Id);
                break;
            case FinancialRecordKind.CreditCard:
                _cards.RemoveAll(x => x.Id == line.Id);
                break;
            case FinancialRecordKind.Loan:
                _loans.RemoveAll(x => x.Id == line.Id);
                break;
            case FinancialRecordKind.LargeExpense:
                _payments.RemoveAll(x => x.Id == line.Id);
                break;
            case FinancialRecordKind.TemporaryPlan:
                _paymentPlans.RemoveAll(x => x.Id == line.Id);
                break;
        }

        RefreshDraftLines();
        SetStatus(string.Empty);
    }

    private void ApplyDraft(OnboardingDraft draft)
    {
        ClearDraft();
        _draftAnchorDate = draft.Settings.ProjectionAnchorDate == default
            ? _clock.Today
            : draft.Settings.ProjectionAnchorDate;
        PeriodDay = draft.Settings.IncomeDay.ToString(TurkishCulture);
        MonthlyVariableExpenseAllowance = draft.Settings.MonthlyVariableExpenseAllowance
            .ToString("N2", TurkishCulture);
        CurrentAmount = draft.Settings.ProjectionOpeningBalance
            .ToString("N2", TurkishCulture);
        SelectedAssignmentMode = AssignmentModes.First(x =>
            x.Value == draft.InitialCashFlowAllocationMode);
        _salaries.AddRange(draft.Salaries);
        _loans.AddRange(draft.Loans);
        _cards.AddRange(draft.CreditCards);
        _paymentPlans.AddRange(draft.PaymentPlans);
        _payments.AddRange(draft.PlannedLargeExpenses);
        RefreshDraftLines();
        SetStatus(string.Empty);
    }

    private void ClearDraft()
    {
        _draftAnchorDate = _clock.Today;
        _salaries.Clear();
        _loans.Clear();
        _cards.Clear();
        _paymentPlans.Clear();
        _payments.Clear();
        PeriodDay = string.Empty;
        MonthlyVariableExpenseAllowance = string.Empty;
        CurrentAmount = string.Empty;
        SelectedAssignmentMode = AssignmentModes[0];
        SelectedPaymentType = PaymentTypes[0];
        CardHasActualStatement = false;
        CardStatementAmount = string.Empty;
        CardStatementMinimum = string.Empty;
        CardNextStatementDate = string.Empty;
        CardNextDueDate = string.Empty;
        CardStatementImportWarnings = string.Empty;
        HasCardStatementImportWarnings = false;
        CurrentStatementCustomPayment = string.Empty;
        _cardStatementFingerprint = null;
        _cardStatementSource = CreditCardStatementSource.Manual;
        _cardExactNextStatementDate = null;
        _cardExactNextDueDate = null;
        RefreshDraftLines();
        SetStatus(string.Empty);
    }

    private void RefreshDraftLines()
    {
        DraftIncomes.Clear();
        foreach (var salary in _salaries.OrderBy(x => x.EffectiveDate))
        {
            DraftIncomes.Add(new FinancialRecordLine(
                salary.Id,
                FinancialRecordKind.Salary,
                salary.Description,
                $"Geçerli: {salary.EffectiveDate:dd.MM.yyyy}",
                Money(salary.Amount),
                "Gelir"));
        }

        DraftCards.Clear();
        foreach (var card in _cards.OrderBy(x => x.Bank).ThenBy(x => x.Name))
        {
            var subtitle = card.CurrentStatement is { } statement
                ? $"Ekstre: {statement.StatementDate:dd.MM.yyyy} • Son ödeme: {statement.DueDate:dd.MM.yyyy}"
                : $"Kesim {card.StatementClosingDay}. gün • Son ödeme {card.PaymentDueDay}. gün";
            DraftCards.Add(new FinancialRecordLine(
                card.Id,
                FinancialRecordKind.CreditCard,
                $"{card.Bank} {card.Name}".Trim(),
                subtitle,
                Money(card.KnownTotalDebt),
                card.CurrentStatement is null
                    ? "Kredi kartı"
                    : $"Plan: {CurrentStatementPlanLabel(card.CurrentStatementPaymentPlan)}"));
        }

        DraftLoans.Clear();
        foreach (var loan in _loans.OrderBy(x => x.NextPaymentDate))
        {
            DraftLoans.Add(new FinancialRecordLine(
                loan.Id,
                FinancialRecordKind.Loan,
                $"{loan.Bank} {loan.Name}".Trim(),
                $"Sonraki: {loan.NextPaymentDate:dd.MM.yyyy} • {loan.RemainingInstallmentCount} ödeme",
                Money(loan.MonthlyPayment),
                "Kredi"));
        }

        DraftPayments.Clear();
        foreach (var payment in _payments.OrderBy(x => x.ExactDate))
        {
            DraftPayments.Add(new FinancialRecordLine(
                payment.Id,
                FinancialRecordKind.LargeExpense,
                payment.Name,
                payment.ExactDate.ToString("dd.MM.yyyy"),
                Money(payment.Amount),
                "Tek seferlik ödeme"));
        }

        foreach (var plan in _paymentPlans
                     .OrderBy(x => x.Installments.Min(i => i.DueDate)))
        {
            DraftPayments.Add(new FinancialRecordLine(
                plan.Id,
                FinancialRecordKind.TemporaryPlan,
                plan.Name,
                $"{plan.Installments.Min(x => x.DueDate):dd.MM.yyyy} • {plan.Installments.Count} ödeme",
                Money(plan.Installments.Sum(x => x.Amount)),
                plan.Kind == PaymentPlanKind.Recurring
                    ? "Düzenli ödeme"
                    : "Geçici ödeme planı"));
        }

        HasDraftIncomes = DraftIncomes.Count > 0;
        HasDraftCards = DraftCards.Count > 0;
        HasDraftLoans = DraftLoans.Count > 0;
        HasDraftPayments = DraftPayments.Count > 0;
        RefreshReview();
    }

    private void RefreshReview()
    {
        ReviewIncomeText = HasDraftIncomes ? $"{DraftIncomes.Count} gelir" : "Gelir eklenmedi";
        ReviewCardText = HasDraftCards ? $"{DraftCards.Count} kart" : "Kart eklenmedi";
        ReviewLoanText = HasDraftLoans ? $"{DraftLoans.Count} kredi" : "Kredi eklenmedi";
        ReviewPaymentText = HasDraftPayments ? $"{DraftPayments.Count} ödeme" : "Yaklaşan ödeme eklenmedi";
        ReviewLivingText = TryMoneyText(MonthlyVariableExpenseAllowance);
        ReviewCurrentAmountText = TryMoneyTextSigned(CurrentAmount);
        ReviewPeriodText = string.IsNullOrWhiteSpace(PeriodDay)
            ? "Dönem günü seçilmedi"
            : $"Dönem günü {PeriodDay}";
    }

    private static string RequireText(string value, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{field} gereklidir.")
            : value.Trim();

    private static decimal ParseNonNegativeMoney(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0m;
        var amount = ParseMoney(value, field);
        if (amount < 0m) throw new InvalidOperationException($"{field} negatif olamaz.");
        return amount;
    }

    private static decimal? ParseOptionalMoney(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : ParseNonNegativeMoney(value, "Tutar");

    private static string TryMoneyText(string value)
    {
        try { return Money(ParseNonNegativeMoney(value, "Tutar")); }
        catch { return value; }
    }

    private static string TryMoneyTextSigned(string value)
    {
        try { return Money(string.IsNullOrWhiteSpace(value) ? 0m : ParseMoney(value, "Tutar")); }
        catch { return value; }
    }
}
