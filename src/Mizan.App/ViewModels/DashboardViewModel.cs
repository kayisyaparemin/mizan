using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mizan.App.Services;
using Mizan.App.Models;
using Mizan.Application.Models;
using Mizan.Application.Services;
using Mizan.Domain.Calculations;

namespace Mizan.App.ViewModels;

public partial class DashboardViewModel(
    MizanService service,
    INavigationService navigation,
    PaymentReminderCardViewModel reminders) : ViewModelBase
{
    public PaymentReminderCardViewModel Reminders { get; } = reminders;

    public ObservableCollection<RemainingPaymentLine> RemainingLines { get; } = [];

    [ObservableProperty] private string currentPeriodText = "—";
    [ObservableProperty] private string periodElapsedText = string.Empty;
    [ObservableProperty] private double periodElapsedRatio;

    [ObservableProperty] private string lastObservationText = "Henüz gözlem girmedin.";
    [ObservableProperty] private bool hasObservation;
    [ObservableProperty] private string observationDateText = string.Empty;

    [ObservableProperty] private string planFrozenText = "—";
    [ObservableProperty] private string plannedEndingText = "—";

    [ObservableProperty] private string plannedLivingText = "—";
    [ObservableProperty] private string spentLivingText = "—";
    [ObservableProperty] private string remainingLivingText = "—";
    [ObservableProperty] private bool hasLivingOverspend;
    [ObservableProperty] private string livingOverspendText = string.Empty;

    public ObservableCollection<CardProgressLine> CardLines { get; } = [];
    [ObservableProperty] private bool hasCardLines;

    [ObservableProperty] private bool hasDeficitFinancing;
    [ObservableProperty] private string plannedDeficitInterestText = "—";
    [ObservableProperty] private string projectedDeficitInterestText = "—";

    [ObservableProperty] private string plannedEndingCompareText = "—";
    [ObservableProperty] private string projectedEndingText = "—";
    [ObservableProperty] private bool isProjectedEndingNegative;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasNoRemainingLines))]
    private bool hasRemainingLines;
    [ObservableProperty] private string remainingWindowText = string.Empty;
    [ObservableProperty] private string remainingTotalText = "—";
    public bool HasNoRemainingLines => !HasRemainingLines;

    [ObservableProperty] private bool isPeriodClosable;

    [ObservableProperty] private bool hasUndeterminedCardPayment;
    [ObservableProperty] private string calculationDetails = string.Empty;
    [ObservableProperty] private bool hasPendingStrategy;
    [ObservableProperty] private bool hasFinancialPlan;
    [ObservableProperty] private bool isEmptyState = true;
    [ObservableProperty] private string emptyStateMessage = "Başlamak için gelirini ekle.";
    [ObservableProperty] private string emptyStateAction = "Gelir Ekle";
    [ObservableProperty] private bool hasPendingReview;
    [ObservableProperty] private bool shouldShowOnboarding;
    [ObservableProperty] private bool showCalculationDetails;

    private CashFlowPeriodProjection? _currentPeriod;
    private bool _listensToReminderAnswers;

    public ObservableCollection<DashboardAlert> Alerts { get; } = [];
    [ObservableProperty] private bool hasAlerts;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanSaveCurrentBalance))]
    private bool isSavingCurrentBalance;
    [ObservableProperty] private string currentBalanceInput = string.Empty;

    public bool CanSaveCurrentBalance => !IsSavingCurrentBalance;
    public bool IsDevelopment => BuildInfo.IsDevelopment;

    [RelayCommand]
    private async Task SaveCurrentBalanceAsync()
    {
        if (IsSavingCurrentBalance) return;
        try
        {
            IsSavingCurrentBalance = true;
            SetStatus(string.Empty);
            var amount = ParseMoney(CurrentBalanceInput);
            await service.ObserveCurrentBalanceAsync(amount);
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(exception, "Gözlem kaydedilemedi."));
            return;
        }
        finally
        {
            IsSavingCurrentBalance = false;
        }

        await LoadAsync();
    }

    [RelayCommand]
    private Task OpenCurrentPeriodDetailAsync() =>
        _currentPeriod is null
            ? Task.CompletedTask
            : navigation.NavigateToAsync(
                NavigationRoutes.SalaryPeriodDetail,
                new Dictionary<string, object>
                {
                    [CashFlowPeriodDetailViewModel.DetailQueryKey] =
                        new SalaryPeriodDetailRequest(_currentPeriod, IsCurrentPeriod: true)
                });

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsBusy) return;

        if (!_listensToReminderAnswers)
        {
            _listensToReminderAnswers = true;
            Reminders.AnswersChanged += async (_, _) => await LoadAsync();
        }

        try
        {
            IsBusy = true;
            SetStatus(string.Empty);
            ShouldShowOnboarding = await service.IsOnboardingRequiredAsync();
            if (ShouldShowOnboarding)
            {
                ResetToEmptyState(
                    "İlk kurulumla gelirini, dönemini ve mevcut tutarını birlikte kaydedelim.",
                    "Kuruluma Başla");
                HasPendingReview = false;
                return;
            }

            var review = await service.GetPeriodReviewAvailabilityAsync();
            HasPendingReview = review.IsDue;

            await Reminders.ApplyPendingAnswersAsync();
            var progress = await service.GetPeriodProgressAsync();
            if (progress is null)
            {
                var plan = await service.GetFinancialPlanAsync();
                ResetToEmptyState(
                    plan.Salaries.Count == 0
                        ? "Henüz finansal plan oluşturulmadı. Başlamak için gelirini ekle."
                        : "Gelir kullanım düzenini seçerek 12 dönemlik planı tamamla.",
                    plan.Salaries.Count == 0 ? "Gelir Ekle" : "Düzeni Seç");
                return;
            }

            HasFinancialPlan = true;
            IsEmptyState = false;
            ApplyProgress(progress);

            var dashboard = await service.GetDashboardAsync();
            _currentPeriod = dashboard?.CurrentPeriod;
            HasUndeterminedCardPayment = dashboard?.HasUndeterminedCardPayments ?? false;
            HasPendingStrategy = dashboard?.PendingStrategy is not null;
            CalculationDetails = _currentPeriod is null ? string.Empty : BuildDetails(_currentPeriod);
            BuildAlerts(dashboard, review.IsDue);
            await Reminders.LoadAsync();
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

    private void ApplyProgress(PeriodProgress progress)
    {
        CurrentPeriodText = $"{progress.PeriodStart.ToString("dd MMMM yyyy", TurkishCulture)} Dönemi";
        PeriodElapsedText = progress.TotalDays > 0
            ? $"{progress.ElapsedDays}/{progress.TotalDays} gün · {progress.PeriodEnd.ToString("dd MMMM", TurkishCulture)} tarihinde kapanıyor"
            : string.Empty;
        PeriodElapsedRatio = progress.ElapsedRatio;

        PlanFrozenText = progress.WasRevised
            ? $"{progress.PlanFrozenOn.ToString("dd MMMM", TurkishCulture)} tarihinde donduruldu · {progress.RevisionCount} revizyon"
            : $"{progress.PlanFrozenOn.ToString("dd MMMM", TurkishCulture)} tarihinde donduruldu";
        PlannedEndingText = Money(progress.PlannedEndingBalance);

        HasObservation = progress.HasObservation;
        if (progress.Observation is { } observation)
        {
            LastObservationText = observation.ObservedBalance is { } observed
                ? $"Son gözlem: {observation.ObservedOn.ToString("dd MMMM yyyy", TurkishCulture)} · {Money(observed, 2)}"
                : $"Son gözlem: {observation.ObservedOn.ToString("dd MMMM yyyy", TurkishCulture)}";
            ObservationDateText = $"{observation.ObservedOn.ToString("dd MMMM", TurkishCulture)} gözlemi";
            CurrentBalanceInput = string.Empty;
        }
        else
        {
            LastObservationText = "Henüz gözlem girmedin.";
            ObservationDateText = string.Empty;
            CurrentBalanceInput = string.Empty;
        }

        PlannedLivingText = Money(progress.PlannedVariableExpenseAllowance);
        SpentLivingText = progress.ObservedLivingSpend is { } spent ? Money(spent) : "—";
        RemainingLivingText = progress.RemainingLivingBudget is { } left ? Money(left) : "—";
        HasLivingOverspend = progress.LivingOverspend is not null;
        LivingOverspendText = progress.LivingOverspend is { } over
            ? $"Havuz {Money(over)} aşıldı; fazlası dönem sonuna yansıyor."
            : string.Empty;

        CardLines.Clear();
        foreach (var card in progress.Cards)
        {
            CardLines.Add(new CardProgressLine(
                card.Name,
                card.DueDate.ToString("dd MMM", TurkishCulture),
                Money(card.Planned),
                card.Current is { } now ? Money(now) : "—"));
        }

        HasCardLines = CardLines.Count > 0;

        HasDeficitFinancing = progress.HasDeficitFinancing;
        PlannedDeficitInterestText = Money(progress.PlannedDeficitInterest);
        ProjectedDeficitInterestText = progress.ProjectedDeficitInterest is { } projectedInterest
            ? Money(projectedInterest)
            : "—";

        PlannedEndingCompareText = Money(progress.PlannedEndingBalance);
        ProjectedEndingText = progress.ProjectedEndingSavings is { } projected ? Money(projected) : "—";
        IsProjectedEndingNegative = progress.ProjectedEndingSavings < 0m;

        RemainingWindowText = $"Ödenmiş işaretlemediklerin · dönem {progress.PeriodEnd.ToString("dd MMMM", TurkishCulture)} tarihinde kapanıyor";
        RemainingLines.Clear();
        foreach (var line in progress.RemainingLines)
        {
            var detail = line.IsEstimate ? $"{line.Detail} • Tahmini" : line.Detail;
            if (line.PlannedDate < progress.Today)
            {
                detail = string.IsNullOrWhiteSpace(detail) ? "Vadesi geçti" : $"{detail} • Vadesi geçti";
            }

            if (progress.IsSnoozed(line.Id))
            {
                detail = string.IsNullOrWhiteSpace(detail) ? "Ertelendi" : $"{detail} • Ertelendi";
            }

            RemainingLines.Add(new RemainingPaymentLine(
                line.PlannedDate.ToString("dd MMM", TurkishCulture),
                line.Name,
                line.PlannedAmount is { } amount ? Money(amount) : "—",
                detail));
        }

        HasRemainingLines = RemainingLines.Count > 0;
        RemainingTotalText = Money(progress.RemainingPlannedTotal);
        IsPeriodClosable = progress.IsClosable;
    }

    private void BuildAlerts(DashboardSnapshot? dashboard, bool reviewIsDue)
    {
        Alerts.Clear();

        if (reviewIsDue)
        {
            Alerts.Add(new DashboardAlert(
                DashboardAlertLevel.Action,
                "Geçen dönem kapandı",
                "Ödemelerin ve dönem harcaman netleştiyse gerçekte ne olduğunu kaydet; planını güncel durumundan yeniden kurayım.",
                "Güncelle",
                OpenPeriodReviewCommand));
        }

        if (dashboard?.HasUndeterminedCardPayments == true)
        {
            Alerts.Add(new DashboardAlert(
                DashboardAlertLevel.Action,
                "Kart ödeme tercihin eksik",
                "Bir kredi kartı için bu ekstreyi nasıl ödeyeceğini seçmedin. Seçmeden dönem sonu tahminin eksik kalır.",
                "Kartlara git",
                OpenCommitmentsCommand));
        }

        if (dashboard?.PendingStrategy is { } pending)
        {
            Alerts.Add(new DashboardAlert(
                DashboardAlertLevel.Information,
                "Planlanan düzen değişikliği var",
                $"{pending.EffectiveFromPeriodDate.ToString("d MMMM yyyy", TurkishCulture)} döneminden itibaren {ModeText(pending.Mode).ToLower(TurkishCulture)}."));
        }

        HasAlerts = Alerts.Count > 0;
    }
}
