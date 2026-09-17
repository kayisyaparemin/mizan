using Mizan.App.Models;
using Mizan.App.Services;
using Mizan.Application.Models;
using Mizan.Domain.Models;

namespace Mizan.App.ViewModels;

public partial class CommitmentsViewModel
{
    public void CloseForm() => CancelEditingCard();

    private void OpenRecordFormForEdit(string recordType)
    {
        _recordType = recordType;
        IsScenarioEntry = false;
        ShowEntryPicker = false;
        RefreshRecordFormFlags();
        NamePlaceholder = NamePlaceholderFor(recordType);
        HasActiveForm = true;
    }

    public void StartEntry(RecordEntryOption? option = null)
    {
        ResetForm();
        EntryForm.Reset();
        _pendingEntryId = Guid.NewGuid();
        HasActiveForm = true;
        ShowEntryPicker = true;
        FormTitle = "Yeni Kayıt";
        SaveButtonText = "Kaydet";
        EntryPicker.Select(option ?? FinancialRecordEntryCatalog.Default);
    }

    public async Task<bool> CompleteInitialStrategySetupAsync(
        CashFlowAllocationMode mode)
    {
        try
        {
            await service.CompleteInitialPaymentStrategySetupAsync(mode);
            SetStatus(string.Empty);
            await feedback.ShowSuccessAsync(
                "Gelir kullanım düzeni kaydedildi.");
            return true;
        }
        catch (Exception exception)
        {
            var message = UserFacingMessages.FromException(exception);
            SetStatus(message);
            await feedback.ShowErrorAsync(message);
            return false;
        }
    }

    public async Task EditCardAsync(Guid cardId)
    {
        var card = (await service.GetFinancialPlanAsync()).CreditCards
            .Single(x => x.Id == cardId);
        _editingCardId = card.Id;
        _editingCardBalanceDate = card.BalanceAsOfDate;
        _editingCardStatement = card.CurrentStatement;
        _cardStatementFingerprint =
            card.CurrentStatement?.SourceDocumentFingerprint;
        _cardStatementSource =
            card.CurrentStatement?.Source ?? CreditCardStatementSource.Manual;
        _cardExactNextStatementDate =
            card.CurrentStatement?.NextStatementDate;
        _cardExactNextDueDate = card.CurrentStatement?.NextDueDate;
        OpenRecordFormForEdit("card");
        FormTitle = "Kart Bilgilerini Düzenle";
        FormLead = "Sık kararlar kart kontrol ekranında; burada kartın temel bilgileri var.";
        IsEditingCard = true;
        SaveButtonText = "Değişiklikleri Kaydet";
        Name = card.Name;
        Bank = card.Bank;
        CardLimit = card.Limit.ToString("N2", TurkishCulture);
        CardHasActualStatement = card.CurrentStatement is not null;
        CarriedBalance = card.CarriedBalance.ToString("N2", TurkishCulture);
        UnbilledSpending = card.UnbilledSpending.ToString("N2", TurkishCulture);
        CardBalanceDate = card.BalanceAsOfDate.ToDateTime(TimeOnly.MinValue);
        if (card.CurrentStatement is { } statement)
        {
            CardStatementAmount = statement.StatementAmount
                .ToString("N2", TurkishCulture);
            CardStatementMinimum = statement.MinimumPaymentAmount
                .ToString("N2", TurkishCulture);
            CardStatementDate =
                statement.StatementDate.ToDateTime(TimeOnly.MinValue);
            CardStatementDueDate =
                statement.DueDate.ToDateTime(TimeOnly.MinValue);
            RefreshCardNextDates();
            SelectedCurrentStatementPaymentMode =
                CurrentStatementPaymentModes.Single(x =>
                    x.Value == (card.CurrentStatementPaymentPlan?.Mode ??
                                CurrentStatementPaymentMode.Minimum));
            CurrentStatementCustomPayment =
                card.CurrentStatementPaymentPlan?.CustomAmount
                    ?.ToString("N2", TurkishCulture) ?? string.Empty;
        }
        else
        {
            CardStatementAmount = string.Empty;
            CardStatementMinimum = string.Empty;
            _cardExactNextStatementDate = null;
            _cardExactNextDueDate = null;
            CardNextStatementDate = string.Empty;
            CardNextDueDate = string.Empty;
            SelectedCurrentStatementPaymentMode =
                CurrentStatementPaymentModes[0];
            CurrentStatementCustomPayment = string.Empty;
        }
        ClosingDay = card.StatementClosingDay.ToString(TurkishCulture);
        DueDay = card.PaymentDueDay.ToString(TurkishCulture);
        MinimumRate = (card.MinimumPaymentRate * 100m).ToString("N2", TurkishCulture);
        SelectedPaymentStrategy = PaymentStrategies.Single(x =>
            x.Value == card.PaymentStrategy);
        FixedPaymentAmount = card.FixedPaymentAmount?.ToString("N2", TurkishCulture) ?? string.Empty;
        SelectedProjectionFallbackStrategy =
            ProjectionFallbackStrategies.Single(x =>
                x.Value == card.ProjectionFallbackStrategy);
        ProjectionFallbackFixedAmount =
            card.ProjectionFallbackFixedAmount?.ToString("N2", TurkishCulture) ?? string.Empty;

        CardFutureCharges.Clear();
        _cardChargeDescriptions.Clear();
        foreach (var charge in card.Charges)
        {
            CardFutureCharges.Add(new DatedAmountLine(
                charge.Id,
                charge.PostingDate,
                charge.Amount,
                charge.Description));
            _cardChargeDescriptions[charge.Id] = charge.Description;
        }

        _editingCardPaymentPlans = card.PaymentPlans;
    }
}
