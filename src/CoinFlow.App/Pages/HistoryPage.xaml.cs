using CoinFlow.App.Models;
using CoinFlow.App.Services;
using CoinFlow.App.ViewModels;

namespace CoinFlow.App.Pages;

public partial class HistoryPage : ContentPage
{
    private readonly HistoryViewModel _viewModel;
    private readonly INavigationService _navigation;

    public HistoryPage(
        HistoryViewModel viewModel,
        INavigationService navigation)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _navigation = navigation;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    private async void OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not
            HistoryCardItem selected)
        {
            return;
        }

        ((CollectionView)sender!).SelectedItem = null;
        await _navigation.OpenHistoryDetailModalAsync(selected.ActualId);
    }
}
