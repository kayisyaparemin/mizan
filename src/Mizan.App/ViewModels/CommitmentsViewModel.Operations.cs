using CommunityToolkit.Mvvm.Input;
using Mizan.App.Models;
using Mizan.App.Services;
using Mizan.Application.Models;
using Mizan.Application.Services;
using Mizan.Domain.Calculations;
using Mizan.Domain.Models;

namespace Mizan.App.ViewModels;

public partial class CommitmentsViewModel
{
    private void PopulateAllItems(FinancialPlan plan)
    {
        _allItems.Clear();

        foreach (var salary in plan.Salaries.OrderByDescending(x => x.EffectiveDate))
        {
            _allItems.Add(new FinancialRecordLine(
                salary.Id,
                FinancialRecordKind.Salary,
                salary.Description.Length == 0 ? "Gelir" : salary.Description,
                $"Geçerli: {salary.EffectiveDate:dd.MM.yyyy}",
                Money(salary.Amount),
                salary.EffectiveDate > DateOnly.FromDateTime(DateTime.Today)
                    ? "Planlanan gelir"
                    : "Gelir"));
        }

        foreach (var income in plan.OtherIncomes.OrderBy(x => x.ExactDate))
        {
            _allItems.Add(new FinancialRecordLine(
                income.Id,
                FinancialRecordKind.OtherIncome,
                income.Description.Length == 0 ? "Diğer gelir" : income.Description,
                income.ExactDate.ToString("dd.MM.yyyy"),
                Money(income.Amount),
                "Tek seferlik gelir"));
        }

        foreach (var overview in loanPayoffService.Describe(plan.Loans))
        {
            var loan = overview.Loan;
            _allItems.Add(new FinancialRecordLine(
                loan.Id,
                FinancialRecordKind.Loan,
                $"{loan.Bank} {loan.Name}".Trim(),
                $"Sonraki: {loan.NextPaymentDate:dd.MM.yyyy} • {loan.RemainingInstallmentCount} ödeme",
                Money(loan.MonthlyPayment),
                "Kredi",
                LoanInsight(overview),
                overview.IssueMessage is not null));
        }

        foreach (var planned in loanPayoffService.DescribePrepayments(plan))
        {
            var loanName = $"{planned.Loan.Bank} {planned.Loan.Name}".Trim();
            var mode = planned.Prepayment.Mode switch
            {
                LoanPrepaymentMode.FullClosure => "erken kapama",
                LoanPrepaymentMode.ReduceTerm => "ara ödeme · vade kısalır",
                _ => "ara ödeme · taksit azalır"
            };
            _allItems.Add(new FinancialRecordLine(
                planned.Prepayment.Id,
                FinancialRecordKind.LoanPrepayment,
                $"{loanName} · {mode}",
                $"Planlı: {planned.Prepayment.Date:dd.MM.yyyy}",
                planned.Amount is decimal amount ? Money(amount) : "—",
                "Erken ödeme",
                planned.IsUnquotable
                    ? "Kredinin anaparası hesaplanamadığı için bu ödeme projeksiyona girmiyor."
                    : "Simülatörden uygulandı. Silersen kredi eski ödeme planına döner.",
                planned.IsUnquotable));
        }

        foreach (var card in plan.CreditCards.OrderBy(x => x.Bank).ThenBy(x => x.Name))
        {
            var statement = card.CurrentStatement;
            var statementLine = statement is null
                ? "Kesilmiş ekstre girilmedi"
                : $"Ekstre: {Money(statement.StatementAmount)} • Son ödeme {statement.DueDate:dd.MM.yyyy}";
            var detailLine =
                $"{statementLine}\n" +
                $"Asgari {Money(statement is null ? 0m : statement.MinimumPaymentAmount)} ({(card.MinimumPaymentRate * 100m).ToString("N0", TurkishCulture)}%) • " +
                $"Kesim her ayın {card.StatementClosingDay}. günü • Son ödeme {card.PaymentDueDay}. gün\n" +
                $"Ödeme kuralı: {StrategyLabel(card.PaymentStrategy)}";
            _allItems.Add(new FinancialRecordLine(
                card.Id,
                FinancialRecordKind.CreditCard,
                $"{card.Bank} {card.Name}".Trim(),
                $"Limit: {Money(card.Limit)} • Güncel borç: {Money(card.KnownTotalDebt)}",
                statement is null ? "—" : Money(statement.StatementAmount),
                "Kredi kartı",
                detailLine));
        }

        foreach (var planItem in plan.PaymentPlans.OrderBy(x => x.Installments.Min(i => i.DueDate)))
        {
            var first = planItem.Installments.OrderBy(x => x.DueDate).First();
            var total = planItem.Installments.Sum(x => x.Amount);
            _allItems.Add(new FinancialRecordLine(
                planItem.Id,
                FinancialRecordKind.TemporaryPlan,
                planItem.Name,
                $"İlk ödeme: {first.DueDate:dd.MM.yyyy} • {planItem.Installments.Count} ödeme",
                Money(total),
                "Ödeme planı",
                $"{planItem.Installments.Count} taksit toplamı {Money(total)}"));
        }

        foreach (var expense in plan.PlannedLargeExpenses.OrderBy(x => x.ExactDate))
        {
            _allItems.Add(new FinancialRecordLine(
                expense.Id,
                FinancialRecordKind.LargeExpense,
                expense.Name,
                expense.ExactDate.ToString("dd.MM.yyyy"),
                Money(expense.Amount),
                "Planlı ödeme",
                expense.Status == PlannedExpenseStatus.Completed
                    ? "Gerçekleşti olarak işaretlendi"
                    : "Planlanan büyük ödeme"));
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (IsScenarioEntry)
        {
            await SaveScenarioEntryAsync();
            return;
        }

        Func<Task> persist;
        string successMessage;
        try
        {
            persist = BuildPersistOperation(out successMessage);
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(exception));
            return;
        }

        try
        {
            IsBusy = true;
            SetStatus(string.Empty);
            await persist();
            SetStatus(string.Empty);
            await feedback.ShowSuccessAsync(successMessage);
            ResetForm();
            await LoadAsync();
        }
        catch (Exception exception)
        {
            var message = UserFacingMessages.FromException(exception);
            SetStatus(message);
            await feedback.ShowErrorAsync(message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveScenarioEntryAsync()
    {
        SimulationRequest request;
        try
        {
            request = EntryForm.BuildRequest(_pendingEntryId);
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(exception));
            return;
        }

        try
        {
            IsBusy = true;
            SetStatus(string.Empty);
            var result = await service.AddRecordFromScenarioAsync(request);
            await feedback.ShowSuccessAsync(result.AlreadyApplied
                ? "Bu kayıt zaten eklenmişti."
                : $"{SimulationScenarioCatalog.TypeText(request.Type)} kaydedildi.");
            ResetForm();
            _pendingEntryId = Guid.NewGuid();
            await LoadAsync();
        }
        catch (Exception exception)
        {
            var message = UserFacingMessages.FromException(exception);
            SetStatus(message);
            await feedback.ShowErrorAsync(message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DeleteAsync(FinancialRecordLine item)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            switch (item.Kind)
            {
                case FinancialRecordKind.Salary:
                    await service.DeleteSalaryAsync(item.Id);
                    break;
                case FinancialRecordKind.OtherIncome:
                    await service.DeleteOtherIncomeAsync(item.Id);
                    break;
                case FinancialRecordKind.Loan:
                    await service.DeleteLoanAsync(item.Id);
                    break;
                case FinancialRecordKind.LoanPrepayment:
                    await service.DeleteLoanPrepaymentAsync(item.Id);
                    break;
                case FinancialRecordKind.CreditCard:
                    await service.DeleteCreditCardAsync(item.Id);
                    break;
                case FinancialRecordKind.TemporaryPlan:
                case FinancialRecordKind.InstallmentPlan:
                    await service.DeletePaymentPlanAsync(item.Id);
                    break;
                case FinancialRecordKind.LargeExpense:
                    await service.DeletePlannedLargeExpenseAsync(item.Id);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(item.Kind));
            }

            SetStatus(string.Empty);
            await LoadAsync();
        }
        catch (Exception exception)
        {
            var message = UserFacingMessages.FromException(exception);
            SetStatus(message);
            await feedback.ShowErrorAsync(message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task ShowManualFallbackAsync(bool timedOut = false) =>
        feedback.ShowErrorAsync(
        timedOut
            ? "Ekstreyi otomatik okumak uzun sürdü. Bilgileri elle girebilirsin."
            : "Bilgileri elle girebilirsin.",
        "Ekstre Otomatik Okunamadı",
        "Elle Gir");

    private string RequireName() =>
        string.IsNullOrWhiteSpace(Name)
            ? throw new InvalidOperationException("Kayıt adı gereklidir.")
            : Name.Trim();

    private static decimal RequirePositive(decimal value, string field) =>
        value > 0m
            ? value
            : throw new InvalidOperationException($"{field} sıfırdan büyük olmalıdır.");

    private static decimal? ParseOptionalMoney(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : ParseMoney(value, "Tutar");

    private static string StrategyLabel(CreditCardPaymentStrategy strategy) =>
        strategy switch
        {
            CreditCardPaymentStrategy.AskEachStatement => "Her ekstrede sor",
            CreditCardPaymentStrategy.Minimum => "Her ekstrede asgari öde",
            CreditCardPaymentStrategy.FullStatement => "Ekstrenin tamamını öde",
            CreditCardPaymentStrategy.FixedAmount => "Sabit tutar",
            _ => "—"
        };

    private static string FallbackLabel(ProjectionFallbackStrategy strategy) =>
        strategy switch
        {
            ProjectionFallbackStrategy.None => "Hesaba katma",
            ProjectionFallbackStrategy.Minimum => "Asgari ödeme üzerinden hesapla",
            ProjectionFallbackStrategy.FullStatement => "Ekstrenin tamamı üzerinden hesapla",
            ProjectionFallbackStrategy.FixedAmount => "Sabit tutar üzerinden hesapla",
            _ => "—"
        };

    private static string CurrentStatementPlanLabel(
        CurrentStatementPaymentPlan? plan) => plan?.Mode switch
    {
        CurrentStatementPaymentMode.Full => "Tamamı",
        CurrentStatementPaymentMode.Custom =>
            $"Başka tutar {Money(plan.CustomAmount.GetValueOrDefault())}",
        CurrentStatementPaymentMode.Minimum => "Asgari",
        _ => "Henüz seçilmedi"
    };
}
