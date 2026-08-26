using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoinFlow.App.Models;
using CoinFlow.App.Services;
using CoinFlow.Application.Services;
using CoinFlow.Domain.Models;

namespace CoinFlow.App.ViewModels;

public partial class CardControlViewModel(
    CoinFlowService service,
    IUserFeedbackService feedback) : ViewModelBase
{
    public const string CardIdQueryKey = "cardId";
    private readonly Dictionary<Guid, string> _chargeDescriptions = [];
    private CreditCard? _card;

    public ObservableCollection<DatedAmountLine> FutureCharges { get; } = [];

    [ObservableProperty] private string title = "Kart";
    [ObservableProperty] private string subtitle = string.Empty;
    [ObservableProperty] private string knownDebt = "—";
    [ObservableProperty] private string limitText = "—";
    [ObservableProperty] private string statementText = "—";
    [ObservableProperty] private string paymentPreferenceText = "—";
    [ObservableProperty] private string fallbackText = "—";
    [ObservableProperty] private bool hasFutureCharges;
    [ObservableProperty] private DateTime chargeDate = DateTime.Today.AddMonths(1);
    [ObservableProperty] private string chargeAmount = string.Empty;
    [ObservableProperty] private string chargeDescription = string.Empty;

    public async Task LoadAsync(Guid cardId)
    {
        var plan = await service.GetFinancialPlanAsync();
        _card = plan.CreditCards.Single(x => x.Id == cardId);
        RefreshCardState();
        SetStatus(string.Empty);
    }

    [RelayCommand]
    private Task PayMinimumAsync() =>
        SavePreferenceAsync(
            CreditCardPaymentStrategy.Minimum,
            "Her ekstrede asgari ödeme seçildi.");

    [RelayCommand]
    private Task PayFullAsync() =>
        SavePreferenceAsync(
            CreditCardPaymentStrategy.FullStatement,
            "Her ekstrede tamamı seçildi.");

    [RelayCommand]
    private Task AskEachStatementAsync() =>
        SavePreferenceAsync(
            CreditCardPaymentStrategy.AskEachStatement,
            "Her ekstrede sor seçildi.");

    [RelayCommand]
    private Task FallbackMinimumAsync() =>
        SaveFallbackAsync(
            ProjectionFallbackStrategy.Minimum,
            "Kararsız ekstrelerde asgari ödeme kullanılacak.");

    [RelayCommand]
    private Task FallbackFullAsync() =>
        SaveFallbackAsync(
            ProjectionFallbackStrategy.FullStatement,
            "Kararsız ekstrelerde tamamı kullanılacak.");

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
        return Shell.Current.GoToAsync(
            "//commitments/commitments-content",
            new ShellNavigationQueryParameters
            {
                ["cardId"] = card.Id.ToString("D"),
                ["editCard"] = "true"
            });
    }

    private async Task SavePreferenceAsync(
        CreditCardPaymentStrategy strategy,
        string message)
    {
        try
        {
            var card = RequiredCard();
            await service.SaveCreditCardAsync(card with
            {
                PaymentStrategy = strategy,
                FixedPaymentAmount = null
            });
            await LoadAsync(card.Id);
            await feedback.ShowSuccessAsync(message);
        }
        catch (Exception exception)
        {
            var error = UserFacingMessages.FromException(exception);
            SetStatus(error);
            await feedback.ShowErrorAsync(error);
        }
    }

    private async Task SaveFallbackAsync(
        ProjectionFallbackStrategy strategy,
        string message)
    {
        try
        {
            var card = RequiredCard();
            await service.SaveCreditCardAsync(card with
            {
                ProjectionFallbackStrategy = strategy,
                ProjectionFallbackFixedAmount = null
            });
            await LoadAsync(card.Id);
            await feedback.ShowSuccessAsync(message);
        }
        catch (Exception exception)
        {
            var error = UserFacingMessages.FromException(exception);
            SetStatus(error);
            await feedback.ShowErrorAsync(error);
        }
    }

    private CreditCard RequiredCard() =>
        _card ?? throw new InvalidOperationException("Kredi kartı bulunamadı.");

    private void RefreshCardState()
    {
        var card = RequiredCard();
        Title = card.Name.Length == 0 ? "Kart" : card.Name;
        Subtitle = $"{card.Bank} • Kesim {card.StatementClosingDay}. gün • Son ödeme {card.PaymentDueDay}. gün";
        KnownDebt = Money(card.KnownTotalDebt);
        LimitText = Money(card.Limit);
        StatementText =
            $"Devreden {Money(card.CarriedBalance)} • Ekstreleşmemiş {Money(card.UnbilledSpending)}";
        PaymentPreferenceText = card.PaymentStrategy switch
        {
            CreditCardPaymentStrategy.Minimum => "Her ekstrede asgari",
            CreditCardPaymentStrategy.FullStatement => "Her ekstrede tamamı",
            CreditCardPaymentStrategy.FixedAmount =>
                $"Sabit {Money(card.FixedPaymentAmount.GetValueOrDefault())}",
            _ => "Her ekstrede sor"
        };
        FallbackText = card.ProjectionFallbackStrategy switch
        {
            ProjectionFallbackStrategy.FullStatement => "Tamamı",
            ProjectionFallbackStrategy.FixedAmount =>
                $"Sabit {Money(card.ProjectionFallbackFixedAmount.GetValueOrDefault())}",
            ProjectionFallbackStrategy.None => "Hesaba katma",
            _ => "Asgari"
        };

        FutureCharges.Clear();
        _chargeDescriptions.Clear();
        foreach (var charge in card.Charges.OrderBy(x => x.PostingDate))
        {
            FutureCharges.Add(new DatedAmountLine(
                charge.Id,
                charge.PostingDate,
                charge.Amount,
                charge.Description));
            _chargeDescriptions[charge.Id] = charge.Description;
        }

        HasFutureCharges = FutureCharges.Count > 0;
    }
}
