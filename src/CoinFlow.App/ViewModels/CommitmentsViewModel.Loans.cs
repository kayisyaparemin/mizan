using System.Collections.ObjectModel;
using CoinFlow.App.Models;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.App.ViewModels;

public partial class CommitmentsViewModel
{
    public ObservableCollection<SelectionOption<LoanKind>> LoanKinds { get; } =
    [
        new("Tüketici kredisi (ihtiyaç, taşıt)", LoanKind.Consumer),
        new("Konut kredisi — sabit faiz", LoanKind.HousingFixed),
        new("Konut kredisi — değişken faiz", LoanKind.HousingVariable)
    ];

    private Loan BuildLoan()
    {
        if (!int.TryParse(PaymentDay, out var day))
        {
            throw new InvalidOperationException("Ödeme günü geçerli olmalıdır.");
        }

        if (!int.TryParse(InstallmentCount, out var count))
        {
            throw new InvalidOperationException("Kalan taksit sayısı geçerli olmalıdır.");
        }

        var closureAmount = ParseOptionalMoney(EarlyClosureAmount);
        return new Loan
        {
            Id = _editingLoanId ?? Guid.NewGuid(),
            Name = RequireName(),
            Bank = Bank.Trim(),
            MonthlyPayment = RequirePositive(ParseMoney(Amount, "Aylık ödeme"), "Aylık ödeme"),
            PaymentDay = day,
            NextPaymentDate = DateOnly.FromDateTime(NextPaymentDate),
            RemainingInstallmentCount = count,
            RemainingDebt = ParseOptionalMoney(RemainingDebt),
            EarlyClosureAmount = closureAmount,
            EarlyClosureAmountAsOf =
                closureAmount is decimal entered &&
                _editingQuote is { } stored &&
                stored.Amount == entered
                    ? stored.AsOf
                    : null,
            Kind = SelectedLoanKind?.Value ?? LoanKind.Consumer
        };
    }

    public async Task EditLoanAsync(Guid loanId)
    {
        var loan = (await service.GetFinancialPlanAsync()).Loans
            .Single(x => x.Id == loanId);
        ResetForm();
        _editingLoanId = loan.Id;
        OpenRecordFormForEdit("loan");
        FormTitle = "Krediyi Düzenle";
        FormLead = "Kalan anaparayı ya da bankadan aldığın kapatma tutarını " +
                   "güncel tut; erken kapama hesabı bunlardan yapılır.";
        SaveButtonText = "Değişiklikleri Kaydet";
        Name = loan.Name;
        Bank = loan.Bank;
        Amount = loan.MonthlyPayment.ToString("N2", TurkishCulture);
        PaymentDay = loan.PaymentDay.ToString(TurkishCulture);
        InstallmentCount =
            loan.RemainingInstallmentCount.ToString(TurkishCulture);
        NextPaymentDate = loan.NextPaymentDate.ToDateTime(TimeOnly.MinValue);
        RemainingDebt = loan.RemainingDebt?.ToString("N2", TurkishCulture) ??
                        string.Empty;
        SelectedLoanKind = LoanKinds.Single(x => x.Value == loan.Kind);
        var quoteIsCurrent =
            loan.EarlyClosureAmount is not null &&
            loan.EarlyClosureAmountAsOf is DateOnly asOf &&
            asOf >= LoanAmortizationCalculator.PreviousDueDate(loan);
        EarlyClosureAmount = quoteIsCurrent
            ? loan.EarlyClosureAmount!.Value.ToString("N2", TurkishCulture)
            : string.Empty;
        _editingQuote = quoteIsCurrent
            ? (loan.EarlyClosureAmount!.Value, loan.EarlyClosureAmountAsOf!.Value)
            : null;
        EarlyClosureNote = quoteIsCurrent
            ? $"Kayıtlı tutar {loan.EarlyClosureAmountAsOf!.Value:dd.MM.yyyy} tarihli. " +
              "Değiştirirsen bugünün tarihiyle saklanır."
            : string.Empty;
    }

    private static string LoanInsight(LoanPayoffOverview overview)
    {
        if (overview.IssueMessage is { } issue)
        {
            return issue;
        }

        if (overview.Analysis.Amortization is not { } amortization ||
            overview.Today is not { } today)
        {
            return string.Empty;
        }

        var rate =
            $"aylık %{(amortization.MonthlyRate * 100m).ToString("N2", TurkishCulture)}";
        var source = amortization.Source == LoanRateSource.BankQuote
            ? " · banka tutarından"
            : string.Empty;
        var head =
            $"Kalan anapara {Money(amortization.Principal)} · {rate}{source}";
        if (!today.HasAnythingToClose)
        {
            return head;
        }

        var fee = today.Fee > 0m
            ? $" ({Money(today.Fee)} erken ödeme ücreti dahil)"
            : string.Empty;
        return $"{head}\nBugün kapatma ≈ {Money(today.Amount)}{fee} · " +
               $"{Money(today.InterestSaving)} faiz ödemezsin";
    }
}
