using Mizan.App.ViewModels;

namespace Mizan.App.Pages;

public partial class CashFlowPeriodDetailPage : ContentPage
{
    public CashFlowPeriodDetailPage(
        CashFlowPeriodDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
