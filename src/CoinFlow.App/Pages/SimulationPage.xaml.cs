using CoinFlow.App.Services;
using CoinFlow.App.ViewModels;
using CoinFlow.Application.Models;

namespace CoinFlow.App.Pages;

public partial class SimulationPage : ContentPage, IQueryAttributable
{
    private readonly SimulationViewModel _viewModel;
    private readonly IUserFeedbackService _feedback;
    private readonly INavigationService _navigation;
    private Guid? _requestedClosureLoanId;
    private DateOnly? _requestedClosureDate;

    public SimulationPage(
        SimulationViewModel viewModel,
        IUserFeedbackService feedback,
        INavigationService navigation)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _feedback = feedback;
        _navigation = navigation;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_viewModel.ConsumeDetailReturn())
        {
            await _viewModel.LoadAsync();
        }

        // 12 Dönem'in kredi kapatma önerisinden gelindi: öneri koşul olarak
        // eklenip hemen hesaplanır.
        if (_requestedClosureLoanId is Guid loanId &&
            _requestedClosureDate is DateOnly date)
        {
            _requestedClosureLoanId = null;
            _requestedClosureDate = null;
            await _viewModel.TryLoanClosureAsync(loanId, date);
        }
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        _requestedClosureLoanId =
            query.TryGetValue("closeLoan", out var loan) &&
            Guid.TryParse(loan?.ToString(), out var loanId)
                ? loanId
                : null;
        _requestedClosureDate =
            query.TryGetValue("date", out var date) &&
            DateOnly.TryParseExact(
                date?.ToString(),
                "yyyy-MM-dd",
                out var parsed)
                ? parsed
                : null;
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

    private Task NavigateToAppliedRecordAsync(
        SimulationApplyResult result) => result.Destination switch
        {
            SimulationApplyDestination.CreditCard =>
                _navigation.NavigateToAsync(
                    NavigationRoutes.CardControl,
                    new Dictionary<string, object>
                    {
                        [CardControlViewModel.CardIdQueryKey] =
                            result.EntityId.ToString("D")
                    }),
            SimulationApplyDestination.Payments =>
                _navigation.NavigateToAsync(
                    $"{NavigationRoutes.Commitments}?section=payment"),
            SimulationApplyDestination.Income or
                SimulationApplyDestination.SalaryHistory =>
                _navigation.NavigateToAsync(
                    $"{NavigationRoutes.Commitments}?section=income"),
            SimulationApplyDestination.Settings =>
                _navigation.NavigateToAsync(NavigationRoutes.Settings),
            _ => throw new ArgumentOutOfRangeException()
        };
}
