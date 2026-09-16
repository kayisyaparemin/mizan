using CommunityToolkit.Mvvm.Input;
using CoinFlow.App.Models;
using CoinFlow.App.Services;
using CoinFlow.Application.Models;
using CoinFlow.Domain.Models;

namespace CoinFlow.App.ViewModels;

public partial class CardControlViewModel
{
    [RelayCommand]
    private Task SetCurrentStatementMinimumAsync() =>
        SaveCurrentStatementPlanAsync(CurrentStatementPaymentMode.Minimum);

    [RelayCommand]
    private Task SetCurrentStatementFullAsync() =>
        SaveCurrentStatementPlanAsync(CurrentStatementPaymentMode.Full);

    [RelayCommand]
    private void ShowCurrentStatementCustom() =>
        IsCurrentStatementCustom = true;

    [RelayCommand]
    private async Task SaveCurrentStatementCustomAsync()
    {
        var amount = ParseMoney(
            CurrentStatementCustomAmount,
            "Bu ekstre için özel ödeme");
        await SaveCurrentStatementPlanAsync(
            CurrentStatementPaymentMode.Custom,
            amount);
    }

    private async Task SaveCurrentStatementPlanAsync(
        CurrentStatementPaymentMode mode,
        decimal? customAmount = null)
    {
        try
        {
            var card = RequiredCard();
            var statement = card.CurrentStatement ??
                            throw new InvalidOperationException(
                                "Önce kesilmiş ekstre bilgisini gir.");
            await service.SaveCreditCardStatementAsync(
                card.Id,
                statement,
                new CurrentStatementPaymentPlan
                {
                    Mode = mode,
                    CustomAmount = mode == CurrentStatementPaymentMode.Custom
                        ? customAmount
                        : null
                });
            IsCurrentStatementCustom = false;
            await LoadAsync(card.Id);
            await feedback.ShowSuccessAsync(
                "Bu ekstre için ödeme planı kaydedildi.");
        }
        catch (Exception exception)
        {
            var message = UserFacingMessages.FromException(exception);
            SetStatus(message);
            await feedback.ShowErrorAsync(message);
        }
    }

    [RelayCommand]
    private void AddCharge()
    {
        try
        {
            var amount = ParsePositiveMoney(
                ChargeAmount,
                "Kart harcaması");
            var id = Guid.NewGuid();
            var description = string.IsNullOrWhiteSpace(ChargeDescription)
                ? "Gelecek taksit"
                : ChargeDescription.Trim();
            FutureCharges.Add(new DatedAmountLine(
                id,
                DateOnly.FromDateTime(ChargeDate),
                amount,
                description));
            _chargeDescriptions[id] = description;
            ChargeAmount = string.Empty;
            ChargeDescription = string.Empty;
            HasFutureCharges = FutureCharges.Count > 0;
            SetStatus(string.Empty);
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(exception));
        }
    }

    public void RemoveCharge(DatedAmountLine line)
    {
        FutureCharges.Remove(line);
        _chargeDescriptions.Remove(line.Id);
        HasFutureCharges = FutureCharges.Count > 0;
    }

    [RelayCommand]
    private async Task SaveChargesAsync()
    {
        try
        {
            var card = RequiredCard();
            await service.SaveCreditCardAsync(card with
            {
                Charges = FutureCharges
                    .OrderBy(x => x.Date)
                    .Select(x => new CardCharge
                    {
                        Id = x.Id,
                        CreditCardId = card.Id,
                        PostingDate = x.Date,
                        Amount = x.Amount,
                        Description = _chargeDescriptions
                            .GetValueOrDefault(x.Id, x.Description)
                    })
                    .ToArray()
            });
            await LoadAsync(card.Id);
            await feedback.ShowSuccessAsync(
                "Gelecek kart harcamaları kaydedildi.");
        }
        catch (Exception exception)
        {
            var message = UserFacingMessages.FromException(exception);
            SetStatus(message);
            await feedback.ShowErrorAsync(message);
        }
    }

    [RelayCommand]
    private Task OpenCardDetailsAsync()
    {
        var card = RequiredCard();
        return navigation.NavigateToAsync(
            NavigationRoutes.Commitments,
            new Dictionary<string, object>
            {
                ["cardId"] = card.Id.ToString("D"),
                ["editCard"] = "true"
            });
    }

    public Task SetUpcomingStatementMinimumAsync(UpcomingStatementLine line) =>
        SaveUpcomingStatementPlanAsync(
            line.DueDate,
            CreditCardPaymentType.Minimum,
            "Bu ekstre için asgari ödeme seçildi.");

    public Task SetUpcomingStatementFullAsync(UpcomingStatementLine line) =>
        SaveUpcomingStatementPlanAsync(
            line.DueDate,
            CreditCardPaymentType.FullStatement,
            "Bu ekstre için tamamı seçildi.");

    public async Task ClearUpcomingStatementPlanAsync(
        UpcomingStatementLine line)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var card = RequiredCard();
            await service.RemoveCreditCardPaymentPlanAsync(
                card.Id,
                line.DueDate);
            await LoadAsync(card.Id);
            await feedback.ShowSuccessAsync(
                "Bu ekstre yeniden genel plana bırakıldı.");
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

    private async Task SaveUpcomingStatementPlanAsync(
        DateOnly dueDate,
        CreditCardPaymentType paymentType,
        string successMessage)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var card = RequiredCard();
            await service.SaveCreditCardPaymentPlanAsync(
                card.Id,
                dueDate,
                paymentType);
            await LoadAsync(card.Id);
            await feedback.ShowSuccessAsync(successMessage);
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
}
