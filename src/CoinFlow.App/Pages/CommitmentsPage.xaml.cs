using CoinFlow.App.Models;
using CoinFlow.App.Services;
using CoinFlow.App.ViewModels;

namespace CoinFlow.App.Pages;

public partial class CommitmentsPage : ContentPage, IQueryAttributable
{
    private readonly CommitmentsViewModel _viewModel;
    private bool _isShowingInitialStrategySetup;
    private string? _requestedSection;
    private Guid? _requestedCardId;
    private bool _requestedCardDetailsEdit;
    private readonly IUserFeedbackService _feedback;

    public CommitmentsPage(
        CommitmentsViewModel viewModel,
        IUserFeedbackService feedback)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _feedback = feedback;
        _viewModel.InitialStrategySetupRequested +=
            OnInitialStrategySetupRequested;
    }

    private async void OnInitialStrategySetupRequested(
        CoinFlow.Application.Models.InitialPaymentStrategySetup setup)
    {
        if (_isShowingInitialStrategySetup)
        {
            return;
        }

        _isShowingInitialStrategySetup = true;
        try
        {
            var page = new InitialStrategyPage(setup, _viewModel);
            await Navigation.PushModalAsync(page);
            await page.Completion;
        }
        finally
        {
            _isShowingInitialStrategySetup = false;
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
        if (string.Equals(
                _requestedSection,
                "payment",
                StringComparison.OrdinalIgnoreCase))
        {
            _viewModel.SelectPaymentSection();
        }
        else if (string.Equals(
                     _requestedSection,
                     "income",
                     StringComparison.OrdinalIgnoreCase))
        {
            _viewModel.SelectIncomeSection();
        }

        if (_requestedCardId is Guid cardId)
        {
            if (_requestedCardDetailsEdit)
            {
                await _viewModel.EditCardAsync(cardId);
                await PageScroll.ScrollToAsync(0, 0, false);
            }
            else
            {
                await OpenCardControlAsync(cardId);
            }
        }

        _requestedSection = null;
        _requestedCardId = null;
        _requestedCardDetailsEdit = false;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        _requestedSection = query.TryGetValue("section", out var section)
            ? section?.ToString()
            : null;
        _requestedCardId = query.TryGetValue("cardId", out var cardId) &&
                           Guid.TryParse(cardId?.ToString(), out var parsed)
            ? parsed
            : null;
        _requestedCardDetailsEdit =
            query.TryGetValue("editCard", out var editCard) &&
            bool.TryParse(editCard?.ToString(), out var parsedEdit) &&
            parsedEdit;
    }

    private async void OnAddClicked(object? sender, EventArgs eventArgs)
    {
        var options = new List<string>
        {
            "Gelir",
            "Kredi Kartı",
            "Kredi",
            "Tek Seferlik Ödeme",
            "Düzenli Ödeme"
        };
        if (_viewModel.FirstCardId is not null)
        {
            options.Add("Kart Harcaması");
        }

        var choice = await DisplayActionSheet(
            "Ne eklemek istiyorsun?",
            "Vazgeç",
            null,
            options.ToArray());
        switch (choice)
        {
            case "Gelir":
                _viewModel.StartAdd("salary");
                break;
            case "Kredi Kartı":
                _viewModel.StartAdd("card");
                break;
            case "Kredi":
                _viewModel.StartAdd("loan");
                break;
            case "Tek Seferlik Ödeme":
                _viewModel.StartAdd("large");
                break;
            case "Düzenli Ödeme":
                _viewModel.StartAdd("recurring");
                break;
            case "Kart Harcaması":
                var cards = _viewModel.CreditCardItems.ToArray();
                if (cards.Length == 1)
                {
                    await OpenCardControlAsync(cards[0].Id);
                    return;
                }

                if (cards.Length > 1)
                {
                    var cardChoice = await DisplayActionSheet(
                        "Kart seç",
                        "Vazgeç",
                        null,
                        cards.Select(x => x.Title).ToArray());
                    var selectedCard = cards.FirstOrDefault(x =>
                        x.Title == cardChoice);
                    if (selectedCard is not null)
                    {
                        await OpenCardControlAsync(selectedCard.Id);
                    }

                    return;
                }

                break;
        }

        if (!string.IsNullOrWhiteSpace(choice) && choice != "Vazgeç")
        {
            await PageScroll.ScrollToAsync(0, 0, true);
        }
    }

    private void OnRemovePlanPaymentClicked(
        object? sender,
        EventArgs eventArgs)
    {
        if (sender is Button { CommandParameter: DatedAmountLine line })
        {
            _viewModel.RemovePlanPayment(line);
        }
    }

    private void OnRemoveCardChargeClicked(
        object? sender,
        EventArgs eventArgs)
    {
        if (sender is Button { CommandParameter: DatedAmountLine line })
        {
            _viewModel.RemoveCardCharge(line);
        }
    }

    private void OnRemoveCardPaymentPlanClicked(
        object? sender,
        EventArgs eventArgs)
    {
        if (sender is Button { CommandParameter: CardPaymentPlanLine line })
        {
            _viewModel.RemoveCardPaymentPlan(line);
        }
    }

    private async void OnEditCardClicked(
        object? sender,
        EventArgs eventArgs)
    {
        if (sender is not Button
            {
                CommandParameter: FinancialRecordLine item
            } ||
            !item.CanEditCard)
        {
            return;
        }

        await OpenCardControlAsync(item.Id);
    }

    private Task OpenCardControlAsync(Guid cardId) =>
        Shell.Current.GoToAsync(
            AppShell.CardControlRoute,
            new ShellNavigationQueryParameters
            {
                [CardControlViewModel.CardIdQueryKey] = cardId.ToString("D")
            });

    private async void OnDeleteClicked(
        object? sender,
        EventArgs eventArgs)
    {
        if (sender is not Button
            {
                CommandParameter: FinancialRecordLine item
            })
        {
            return;
        }

        var confirmed = await _feedback.ConfirmAsync(
            "Kaydı sil",
            $"{item.Title} kalıcı olarak silinsin mi?",
            "Sil",
            "Vazgeç");
        if (confirmed)
        {
            await _viewModel.DeleteAsync(item);
        }
    }
}
