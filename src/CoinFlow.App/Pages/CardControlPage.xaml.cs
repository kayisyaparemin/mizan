using CoinFlow.App.Models;
using CoinFlow.App.ViewModels;

namespace CoinFlow.App.Pages;

public partial class CardControlPage : ContentPage, IQueryAttributable
{
    private readonly CardControlViewModel _viewModel;
    private Guid? _cardId;

    public CardControlPage(CardControlViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_cardId is Guid cardId)
        {
            await _viewModel.LoadAsync(cardId);
        }
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        _cardId = query.TryGetValue(
                      CardControlViewModel.CardIdQueryKey,
                      out var cardId) &&
                  Guid.TryParse(cardId?.ToString(), out var parsed)
            ? parsed
            : null;
    }

    private void OnRemoveChargeClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: DatedAmountLine line })
        {
            _viewModel.RemoveCharge(line);
        }
    }

    private async void OnUpcomingMinimumClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: UpcomingStatementLine line })
        {
            await _viewModel.SetUpcomingStatementMinimumAsync(line);
        }
    }

    private async void OnUpcomingFullClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: UpcomingStatementLine line })
        {
            await _viewModel.SetUpcomingStatementFullAsync(line);
        }
    }

    private async void OnUpcomingClearClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: UpcomingStatementLine line })
        {
            await _viewModel.ClearUpcomingStatementPlanAsync(line);
        }
    }
}
