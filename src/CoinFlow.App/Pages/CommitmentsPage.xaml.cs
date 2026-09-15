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
        if (!string.IsNullOrWhiteSpace(_requestedSection))
        {
            // Simülatörden uygulanan planla gelindi: kayıt listede, açık
            // form kapanır.
            _viewModel.CloseForm();
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
        // Eskiden yedi seçenekli bir menüydü. Artık simülatördeki gibi grup
        // çipleri ve açıklamalı kartlar formun üstünde duruyor.
        _viewModel.StartEntry();
        await PageScroll.ScrollToAsync(0, 0, true);
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

    private async void OnEditLoanClicked(
        object? sender,
        EventArgs eventArgs)
    {
        if (sender is not Button
            {
                CommandParameter: FinancialRecordLine item
            } ||
            !item.CanEditLoan)
        {
            return;
        }

        await _viewModel.EditLoanAsync(item.Id);
        await PageScroll.ScrollToAsync(0, 0, true);
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
