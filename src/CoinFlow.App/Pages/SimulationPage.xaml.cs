using System.ComponentModel;
using CoinFlow.App.Drawables;
using CoinFlow.App.Services;
using CoinFlow.App.ViewModels;
using CoinFlow.Application.Models;

namespace CoinFlow.App.Pages;

public partial class SimulationPage : ContentPage
{
    private readonly SimulationViewModel _viewModel;
    private readonly IUserFeedbackService _feedback;

    public SimulationPage(
        SimulationViewModel viewModel,
        IUserFeedbackService feedback)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _feedback = feedback;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        SyncCashChart();
    }

    /// <summary>
    /// GraphicsView veriyi binding ile almaz; seriyi drawable'a elle verip
    /// yeniden çizim istememiz gerekiyor.
    /// </summary>
    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(SimulationViewModel.CashChart)
            or nameof(SimulationViewModel.SelectedChartIndex))
        {
            SyncCashChart();
        }
    }

    private void SyncCashChart()
    {
        if (Resources["CashChartDrawable"] is
            CashProjectionChartDrawable drawable)
        {
            drawable.Series = _viewModel.CashChart;
            drawable.SelectedIndex = _viewModel.SelectedChartIndex;
            CashChartView.Invalidate();
        }
    }

    private PointF? _chartTouchStart;

    private void OnCashChartTouchStart(
        object? sender,
        TouchEventArgs eventArgs)
    {
        _chartTouchStart = eventArgs.Touches.Length > 0
            ? eventArgs.Touches[0]
            : null;
    }

    /// <summary>
    /// Grafik bir ScrollView içinde; parmağını grafiğin üstünden sürükleyerek
    /// sayfayı kaydırmak seçimi ele geçiriyordu. Yalnızca yerinde kalan bir
    /// dokunuş seçim sayılır, sürükleme kaydırmaya bırakılır. Telefonda 12
    /// noktadan birine tam basmak zor olduğu için dokunuş en yakın döneme
    /// kancalanır.
    /// </summary>
    private void OnCashChartTouchEnd(
        object? sender,
        TouchEventArgs eventArgs)
    {
        var start = _chartTouchStart;
        _chartTouchStart = null;
        if (start is not { } origin ||
            eventArgs.Touches.Length == 0 ||
            CashChartView.Width <= 0)
        {
            return;
        }

        var end = eventArgs.Touches[0];
        const float tapSlop = 20f;
        if (Math.Abs(end.X - origin.X) > tapSlop ||
            Math.Abs(end.Y - origin.Y) > tapSlop)
        {
            return;
        }

        if (Resources["CashChartDrawable"] is not
            CashProjectionChartDrawable drawable)
        {
            return;
        }

        var index = drawable.IndexAt(end.X, (float)CashChartView.Width);
        if (index >= 0 && index != _viewModel.SelectedChartIndex)
        {
            _viewModel.SelectChartIndexCommand.Execute(index);
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_viewModel.ConsumeDetailReturn())
        {
            await _viewModel.LoadAsync();
        }
    }

    private async void OnApplyPlanClicked(object? sender, EventArgs eventArgs)
    {
        var confirmed = await _feedback.ConfirmAsync(
            "Planı Uygula",
            _viewModel.ApplyConfirmationText,
            "Planı Uygula",
            "Vazgeç");
        if (!confirmed)
        {
            return;
        }

        var result = await _viewModel.ApplyLastPlanAsync();
        if (result is not null)
        {
            var showRecord = await _feedback.ConfirmAsync(
                "Plan uygulandı",
                result.Message,
                result.Destination == SimulationApplyDestination.Settings
                    ? "Ayarlarda Gör"
                    : "Finansal Yapıda Gör",
                "Tamam");
            if (showRecord)
            {
                await NavigateToAppliedRecordAsync(result);
            }
        }
    }

    private static Task NavigateToAppliedRecordAsync(
        SimulationApplyResult result) => result.Destination switch
        {
            SimulationApplyDestination.CreditCard =>
                Shell.Current.GoToAsync(
                    AppShell.CardControlRoute,
                    new ShellNavigationQueryParameters
                    {
                        [CardControlViewModel.CardIdQueryKey] =
                            result.EntityId.ToString("D")
                    }),
            SimulationApplyDestination.Payments =>
                Shell.Current.GoToAsync(
                    "//commitments/commitments-content?section=payment"),
            SimulationApplyDestination.Income or
                SimulationApplyDestination.SalaryHistory =>
                Shell.Current.GoToAsync(
                    "//commitments/commitments-content?section=income"),
            SimulationApplyDestination.Settings =>
                Shell.Current.GoToAsync("//settings/settings-content"),
            _ => throw new ArgumentOutOfRangeException()
        };
}
