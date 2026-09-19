using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mizan.App.Services;
using Mizan.Application.Models;
using Mizan.Application.Services;
using Mizan.Domain.Models;

namespace Mizan.App.ViewModels;

public partial class CashFlowPeriodDetailViewModel(
    CashFlowPeriodDetailPresenter presenter,
    MizanService service,
    PaymentReminderCardViewModel reminders,
    INavigationService navigation) :
    ViewModelBase,
    IQueryAttributable
{
    public const string DetailQueryKey = "periodDetail";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasNoDetail))]
    private bool hasDetail;
    public bool HasNoDetail => !HasDetail;

    [ObservableProperty] private SalaryPeriodDetailData? detail;
    [ObservableProperty] private bool showReminders;

    [RelayCommand]
    private Task NavigateBackAsync() => navigation.NavigateBackAsync();

    /// <summary>Ana Sayfa'daki hatırlatıcı kartının aynısı.</summary>
    public PaymentReminderCardViewModel Reminders { get; } = reminders;
    public bool IsDevelopment => BuildInfo.IsDevelopment;

    private DateOnly _periodStart;

    public void ApplyQueryAttributes(
        IDictionary<string, object> query)
    {
        try
        {
            if (!query.TryGetValue(DetailQueryKey, out var value) ||
                value is not SalaryPeriodDetailRequest request)
            {
                throw new InvalidOperationException(
                    "Dönem detayı bulunamadı.");
            }

            _periodStart = request.Scenario.PeriodStart;
            Detail = presenter.Build(
                request.Scenario,
                request.Baseline,
                request.IsSimulationScenario);
            HasDetail = true;
            SetStatus(string.Empty);
            ShowReminders = request.IsCurrentPeriod;
            if (ShowReminders)
            {
                _ = Reminders.LoadAsync();
            }
        }
        catch (Exception exception)
        {
            Detail = null;
            HasDetail = false;
            SetStatus(UserFacingMessages.FromException(exception));
        }
    }

    [RelayCommand]
    private Task PayMinimumAsync(DetailPaymentRow? row) =>
        ChangeCardPaymentModeAsync(row, CreditCardPaymentType.Minimum);

    [RelayCommand]
    private Task PayFullStatementAsync(DetailPaymentRow? row) =>
        ChangeCardPaymentModeAsync(row, CreditCardPaymentType.FullStatement);

    /// <summary>
    /// Kararı doğrudan plana yazar, sonra aynı dönemi yeniden hesaplayıp
    /// ekranı tazeler. Servis kararı kesilmiş ekstre ile ileride kesilecek
    /// ekstre arasında kendisi ayırır.
    /// </summary>
    private async Task ChangeCardPaymentModeAsync(
        DetailPaymentRow? row,
        CreditCardPaymentType paymentType)
    {
        if (row is not { CanChangeCardPaymentMode: true } ||
            row.CreditCardId is not { } cardId ||
            row.CardPaymentType == paymentType ||
            IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            SetStatus(string.Empty);
            await service.SetStatementPaymentModeAsync(
                cardId,
                row.Date,
                paymentType);
            var periods = await service.GetFuturePeriodsAsync();
            var refreshed = periods.FirstOrDefault(x =>
                x.PeriodStart == _periodStart);
            if (refreshed is null)
            {
                throw new InvalidOperationException(
                    "Dönem yeniden hesaplanamadı.");
            }

            Detail = presenter.Build(refreshed);
            HasDetail = true;
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(exception));
        }
        finally
        {
            IsBusy = false;
        }
    }
}
