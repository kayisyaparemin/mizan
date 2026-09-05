using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoinFlow.App.Services;
using CoinFlow.App.Models;
using CoinFlow.App.Pages;
using CoinFlow.Application.Models;
using CoinFlow.Application.Services;
using CoinFlow.Domain.Calculations;

namespace CoinFlow.App.ViewModels;

public partial class DashboardViewModel(
    CoinFlowService service,
    IServiceProvider services) : ViewModelBase
{
    public ObservableCollection<UpcomingPaymentLine>
        UpcomingPayments
    { get; } = [];
    public ObservableCollection<UpcomingPaymentLine>
        PreFirstSalaryPayments
    { get; } = [];

    [ObservableProperty] private string currentPeriodText = "—";
    [ObservableProperty] private string currentSnapshotDate = "—";
    [ObservableProperty] private string planningStartingState = "—";
    [ObservableProperty] private string assignmentModeText = "—";
    [ObservableProperty] private string paymentWindowText = "—";
    [ObservableProperty] private string income = "—";
    [ObservableProperty] private string mandatory = "—";
    [ObservableProperty] private string available = "—";
    [ObservableProperty] private string carryOverDeficit = "—";
    [ObservableProperty] private string availableAfterCarryOverDeficit = "—";
    [ObservableProperty] private string carryOverMessage = string.Empty;
    [ObservableProperty] private bool hasCarryOverDeficit;
    [ObservableProperty] private string living = "—";
    [ObservableProperty] private string estimatedSavings = "—";
    [ObservableProperty] private string endingSavings = "—";
    [ObservableProperty] private string twelveMonthSavings = "—";
    [ObservableProperty] private string twelveMonthInterest = "—";
    [ObservableProperty] private string twelveMonthCardInterest = "—";
    [ObservableProperty] private string twelveMonthDeficitInterest = "—";
    [ObservableProperty] private bool hasTwelveMonthInterest;
    [ObservableProperty] private string tightestPeriod = "—";
    [ObservableProperty] private string tightestValue = "—";
    [ObservableProperty] private string deficitMessage = string.Empty;
    [ObservableProperty] private bool hasDeficit;
    [ObservableProperty] private bool hasUpcomingPayments;
    [ObservableProperty] private bool hasNoUpcomingPayments = true;
    [ObservableProperty] private bool hasPreFirstSalaryPayments;
    [ObservableProperty] private bool hasUndeterminedCardPayment;
    [ObservableProperty] private string calculationDetails = string.Empty;
    [ObservableProperty] private string strategyStatusText = "—";
    [ObservableProperty] private string pendingStrategyText = string.Empty;
    [ObservableProperty] private bool hasPendingStrategy;
    [ObservableProperty] private bool hasFinancialPlan;
    [ObservableProperty] private bool isEmptyState = true;
    [ObservableProperty]
    private string emptyStateMessage =
        "Başlamak için gelirini ekle.";
    [ObservableProperty] private string emptyStateAction = "Gelir Ekle";
    [ObservableProperty] private bool hasPendingReview;
    [ObservableProperty] private string pendingReviewTitle = string.Empty;
    [ObservableProperty] private string pendingReviewMessage = string.Empty;
    [ObservableProperty] private bool shouldShowOnboarding;

    // PRIMARY: ekranı açar açmaz görülmesi gereken tek rakam ve tek cümle.
    [ObservableProperty] private string headlineAmount = "—";
    [ObservableProperty] private string headlineCaption = string.Empty;
    [ObservableProperty] private string periodVerdict = string.Empty;
    [ObservableProperty] private bool isPeriodVerdictNegative;
    // SECONDARY: bölüm özetleri.
    [ObservableProperty] private string twelveMonthCaption = "—";
    [ObservableProperty] private string structureSummary = "—";
    [ObservableProperty] private string historySummary = "—";
    [ObservableProperty] private bool showCalculationDetails;

    /// <summary>
    /// §11 — yalnız gerçekten anlamlı olan uyarılar, önceliğe göre.
    /// </summary>
    public ObservableCollection<DashboardAlert> Alerts { get; } = [];
    [ObservableProperty] private bool hasAlerts;

    [RelayCommand]
    private void ToggleCalculationDetails() =>
        ShowCalculationDetails = !ShowCalculationDetails;

    public bool IsDevelopment => BuildInfo.IsDevelopment;

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            SetStatus(string.Empty);
            ShouldShowOnboarding =
                await service.IsOnboardingRequiredAsync();
            if (ShouldShowOnboarding)
            {
                HasFinancialPlan = false;
                IsEmptyState = true;
                HasPendingReview = false;
                HasPreFirstSalaryPayments = false;
                HasUpcomingPayments = false;
                HasNoUpcomingPayments = true;
                HasUndeterminedCardPayment = false;
                HasPendingStrategy = false;
                Alerts.Clear();
                HasAlerts = false;
                HasTwelveMonthInterest = false;
                PreFirstSalaryPayments.Clear();
                UpcomingPayments.Clear();
                EmptyStateMessage =
                    "İlk kurulumla gelirini, dönemini ve mevcut tutarını birlikte kaydedelim.";
                EmptyStateAction = "Kuruluma Başla";
                return;
            }

            var review = await service.GetPeriodReviewAvailabilityAsync();
            HasPendingReview = review.IsDue;
            PendingReviewTitle = review.IsDue
                ? "Geçen dönemi güncelle"
                : string.Empty;
            PendingReviewMessage = review.IsDue
                ? "Planınla gerçekte olanı karşılaştır ve yeni planını güncel durumundan başlat."
                : string.Empty;
            var dashboard = await service.GetDashboardAsync();
            if (dashboard is null)
            {
                var plan = await service.GetFinancialPlanAsync();
                HasFinancialPlan = false;
                IsEmptyState = true;
                HasPreFirstSalaryPayments = false;
                HasUpcomingPayments = false;
                HasNoUpcomingPayments = true;
                HasUndeterminedCardPayment = false;
                HasPendingStrategy = false;
                Alerts.Clear();
                HasAlerts = false;
                HasTwelveMonthInterest = false;
                PreFirstSalaryPayments.Clear();
                UpcomingPayments.Clear();
                EmptyStateMessage = plan.Salaries.Count == 0
                    ? "Henüz finansal plan oluşturulmadı. Başlamak için gelirini ekle."
                    : "Gelir kullanım düzenini seçerek 12 dönemlik planı tamamla.";
                EmptyStateAction = plan.Salaries.Count == 0
                    ? "Gelir Ekle"
                    : "Düzeni Seç";
                return;
            }

            var currentPlan = await service.GetFinancialPlanAsync();
            var history = await service.GetHistorySummaryAsync(1);

            HasFinancialPlan = true;
            IsEmptyState = false;
            var current = dashboard.CurrentPeriod;

            CurrentSnapshotDate = dashboard.ProjectionAnchorDate == default
                ? "Henüz güncellenmedi"
                : dashboard.ProjectionAnchorDate.ToString(
                    "dd MMMM yyyy",
                    TurkishCulture);
            PlanningStartingState = Money(
                dashboard.ProjectionStartingSavings);
            CurrentPeriodText =
                $"{current.PeriodStart.ToString("dd MMMM yyyy", TurkishCulture)} Dönemi";
            AssignmentModeText = current.PaymentAssignmentMode ==
                                 CoinFlow.Domain.Models.PaymentAssignmentMode.PreviousPeriod
                ? "Geçmiş dönemi kapatırım"
                : "Gelecek dönemi karşılarım";
            PaymentWindowText =
                $"{current.PaymentWindowStart.ToString("dd MMM", TurkishCulture)}–" +
                $"{current.PaymentWindowEnd.ToString("dd MMM", TurkishCulture)} ödemeleri";
            Income = Money(current.TotalIncome);
            Mandatory = Money(current.MandatoryOutflow);
            Available = Money(current.AvailableAfterMandatory);
            CarryOverDeficit = Money(-current.CarryOverDeficit);
            AvailableAfterCarryOverDeficit = Money(
                current.AvailableAfterCarryOverDeficit);
            HasCarryOverDeficit = current.HasCarryOverDeficit;
            CarryOverMessage = current.HasCarryOverDeficit
                ? current.RemainingCarryOverDeficit > 0m
                    ? $"Bu dönem {Money(current.DeficitCoveredThisPeriod)} karşılanıyor; sonraki döneme {Money(current.RemainingCarryOverDeficit)} açık devrediyor."
                    : $"Devreden {Money(current.CarryOverDeficit)} açık bu dönem tamamen kapanıyor."
                : string.Empty;
            Living = Money(current.LivingBudget);
            EstimatedSavings = Money(current.EstimatedSavingsCapacity);
            EndingSavings = Money(current.EndingProjectedSavings);
            TwelveMonthSavings = Money(
                dashboard.TwelvePeriodEndingProjectedSavings);
            TwelveMonthInterest = Money(
                dashboard.TwelvePeriodTotalInterest);
            TwelveMonthCardInterest = Money(
                dashboard.TwelvePeriodCreditCardInterest);
            TwelveMonthDeficitInterest = Money(
                dashboard.TwelvePeriodDeficitFinancingInterest);
            HasTwelveMonthInterest =
                dashboard.TwelvePeriodTotalInterest > 0m;
            TightestPeriod = PeriodText(dashboard.TightestPeriod.Period);
            TightestValue = Money(
                dashboard.TightestPeriod.EndingProjectedSavings);
            HasDeficit = current.EndingProjectedSavings < 0m;
            DeficitMessage = HasDeficit
                ? $"Dönem sonu durumunda {Money(Math.Abs(current.EndingProjectedSavings))} finansman açığı oluşuyor."
                : string.Empty;
            HasUndeterminedCardPayment =
                dashboard.HasUndeterminedCardPayments;
            StrategyStatusText = AssignmentModeText;
            HasPendingStrategy = dashboard.PendingStrategy is not null;
            PendingStrategyText = dashboard.PendingStrategy is null
                ? string.Empty
                : $"{dashboard.PendingStrategy.EffectiveFromSalaryDate.ToString("dd MMMM yyyy", TurkishCulture)} döneminden itibaren " +
                  ModeText(dashboard.PendingStrategy.Mode);

            PreFirstSalaryPayments.Clear();
            foreach (var payment in dashboard.PreFirstSalaryObligations)
            {
                PreFirstSalaryPayments.Add(ToLine(
                    payment,
                    "Son ödeme tarihi dönem gelirinden önce"));
            }
            HasPreFirstSalaryPayments = PreFirstSalaryPayments.Count > 0;

            UpcomingPayments.Clear();
            foreach (var payment in dashboard.UpcomingPayments.Where(x =>
                         !x.IsPreFirstSalaryObligation))
            {
                var category = payment.IsEstimate
                    ? "Kart ödemesi • Tahmini"
                    : payment.Type switch
                    {
                        ObligationType.Loan => "Kredi",
                        ObligationType.CreditCard => "Kredi kartı",
                        ObligationType.TemporaryPayment =>
                            "Geçici ödeme planı",
                        ObligationType.InstallmentPayment =>
                            "Taksit / finansman",
                        _ => "Planlı ödeme"
                    };
                var assignmentWarning = payment.PaymentBeforeSalary
                    ? $" • ⚠ Karşılayan dönem: {payment.AssignedSalaryDate.ToString("dd MMM", TurkishCulture)}; son ödeme: {payment.DueDate.ToString("dd MMM", TurkishCulture)}"
                    : string.Empty;
                UpcomingPayments.Add(new UpcomingPaymentLine(
                    payment.DueDate.ToString("dd MMM", TurkishCulture),
                    payment.Name,
                    Money(payment.Amount),
                    category + assignmentWarning));
            }

            HasUpcomingPayments = UpcomingPayments.Count > 0;
            HasNoUpcomingPayments = !HasUpcomingPayments;
            CalculationDetails = BuildDetails(current);

            // PRIMARY — tek rakam: bu dönem nasıl bitiyor.
            HeadlineAmount = EndingSavings;
            HeadlineCaption =
                $"{current.PeriodEnd.ToString("d MMMM", TurkishCulture)} tarihindeki tahmini durumun";
            IsPeriodVerdictNegative = current.EndingProjectedSavings < 0m;
            PeriodVerdict = IsPeriodVerdictNegative
                ? $"Bu dönemi {Money(Math.Abs(current.EndingProjectedSavings))} açıkla kapatıyorsun."
                : $"Zorunlu ödemeler ve yaşam giderinden sonra {Money(current.EndingProjectedSavings)} kalıyor.";

            TwelveMonthCaption =
                $"12 dönem sonunda {TwelveMonthSavings} • en düşük {TightestValue}";
            StructureSummary =
                $"{currentPlan.Salaries.Count} gelir • {currentPlan.CreditCards.Count} kart • " +
                $"{currentPlan.Loans.Count} kredi • {currentPlan.PaymentPlans.Count + currentPlan.PlannedLargeExpenses.Count} ödeme";
            HistorySummary = history is null
                ? "Henüz kapanan dönem yok"
                : $"Son dönem: plan {Money(history.Planned)} • gerçekleşen {Money(history.Actual)}";
            BuildAlerts(dashboard, current, review.IsDue);
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

    [RelayCommand]
    private Task OpenSimulationAsync() =>
        Shell.Current.GoToAsync("//simulation/simulation-content");

    [RelayCommand]
    private Task OpenSettingsAsync() =>
        Shell.Current.GoToAsync("//settings/settings-content");

    [RelayCommand]
    private Task OpenCommitmentsAsync() =>
        Shell.Current.GoToAsync("//commitments/commitments-content");

    [RelayCommand]
    private Task OpenEmptyStateAsync() =>
        ShouldShowOnboarding
            ? OpenOnboardingAsync()
            : OpenCommitmentsAsync();

    [RelayCommand]
    private Task OpenFutureMonthsAsync() =>
        Shell.Current.GoToAsync("//projection/future-months-content");

    [RelayCommand]
    private Task OpenHistoryAsync() =>
        Shell.Current.GoToAsync("//history/history-content");

    [RelayCommand]
    private async Task OpenOnboardingAsync()
    {
        var page = services.GetRequiredService<OnboardingPage>();
        await Shell.Current.Navigation.PushModalAsync(
            new NavigationPage(page));
        if (await page.Completion)
        {
            await LoadAsync();
        }
    }

    /// <summary>
    /// §11 — uyarılar yalnız aksiyon veya gerçek dikkat gerektirdiğinde üretilir.
    /// Her uyarı "ne oldu / neden önemli / ne yapabilirim" sorularını yanıtlar.
    /// </summary>
    private void BuildAlerts(
        DashboardSnapshot dashboard,
        SalaryPeriodProjection current,
        bool reviewIsDue)
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

        if (dashboard.HasUndeterminedCardPayments)
        {
            Alerts.Add(new DashboardAlert(
                DashboardAlertLevel.Action,
                "Kart ödeme tercihin eksik",
                "Bir kredi kartı için bu ekstreyi nasıl ödeyeceğini seçmedin. Seçmeden dönem sonu tahminin eksik kalır.",
                "Kartlara git",
                OpenCommitmentsCommand));
        }

        if (current.EndingProjectedSavings < 0m)
        {
            Alerts.Add(new DashboardAlert(
                DashboardAlertLevel.Attention,
                "Bu dönem açık veriyor",
                $"Dönem sonunda {Money(Math.Abs(current.EndingProjectedSavings))} açık oluşuyor. Simülatörde bir ödemeyi ertelemeyi veya kart ödemeni değiştirmeyi deneyebilirsin.",
                "Simülatörü aç",
                OpenSimulationCommand));
        }

        if (dashboard.PendingStrategy is { } pending)
        {
            Alerts.Add(new DashboardAlert(
                DashboardAlertLevel.Information,
                "Planlanan düzen değişikliği var",
                $"{pending.EffectiveFromSalaryDate.ToString("d MMMM yyyy", TurkishCulture)} döneminden itibaren {ModeText(pending.Mode).ToLower(TurkishCulture)}."));
        }

        HasAlerts = Alerts.Count > 0;
    }

    [RelayCommand]
    public Task OpenPeriodReviewAsync()
    {
        var page = services.GetRequiredService<PeriodReviewPage>();
        return Shell.Current.Navigation.PushModalAsync(
            new NavigationPage(page));
    }

    private static string BuildDetails(SalaryPeriodProjection row)
    {
        var incomeLines = row.IncomeItems.Select(x =>
            $"{x.SourceDate:dd.MM} {x.Name}: {Money(x.Amount, 2)}");
        var paymentLines = row.MandatoryItems.Select(x =>
            $"{x.DueDate:dd.MM} {x.Name}: {Money(x.Amount, 2)}" +
            (x.IsEstimate ? " (tahmini)" : string.Empty) +
            (x.PaymentBeforeSalary
                ? $" • ⚠ {x.AssignedSalaryDate:dd.MM} dönemi; gerçek vade önce"
                : string.Empty));
        var calculation = new[]
        {
            $"OpeningProjectedSavings: {Money(row.OpeningProjectedSavings, 2)}",
            $"CarryOverDeficit: {Money(row.CarryOverDeficit, 2)}",
            $"Income: {Money(row.TotalIncome, 2)}",
            $"MandatoryOutflow: {Money(row.MandatoryOutflow, 2)}",
            $"AvailableAfterMandatory: {Money(row.AvailableAfterMandatory, 2)}",
            $"AvailableAfterCarryOverDeficit: {Money(row.AvailableAfterCarryOverDeficit, 2)}",
            $"LivingBudget: {Money(row.LivingBudget, 2)}",
            $"LargeExpenses: {Money(row.PlannedLargeCashExpenses, 2)}",
            $"CurrentPeriodNetContribution: {Money(row.CurrentPeriodNetContribution, 2)}",
            $"EndingBeforeDeficitInterest: {Money(row.EndingProjectedSavingsBeforeDeficitInterest, 2)}",
            $"DeficitPrincipal: {Money(row.DeficitPrincipal, 2)}",
            $"DeficitInterestRate: %{row.AppliedDeficitInterestRate * 100m:N2}",
            $"DeficitInterest: {Money(row.DeficitFinancingInterest, 2)}",
            $"CardInterestGenerated: {Money(row.CardInterestGenerated, 2)}",
            $"TotalInterestGenerated: {Money(row.TotalInterestGenerated, 2)}",
            $"FinalEndingProjectedSavings: {Money(row.EndingProjectedSavings, 2)}",
            string.Empty
        };
        var cardInterestLines = row.CardPaymentStatuses.Select(x =>
            $"{x.CardName}: Statement={Money(x.StatementBalance ?? 0m, 2)} • " +
            $"Payment={Money(x.Payment ?? 0m, 2)} • " +
            $"RemainingPrincipal={Money(x.CarriedPrincipalAfterPayment ?? 0m, 2)} • " +
            $"Rate=%{x.AppliedInterestRate * 100m:N2} • " +
            $"CarryInterest={Money(x.CarryInterest, 2)} • " +
            $"NextCarry={Money(x.NextCarriedBalance ?? 0m, 2)}");
        return string.Join(
            Environment.NewLine,
            calculation.Concat(incomeLines)
                .Concat(paymentLines)
                .Concat(cardInterestLines));
    }

    private static UpcomingPaymentLine ToLine(
        ObligationItem payment,
        string detail) => new(
        payment.DueDate.ToString("dd MMM", TurkishCulture),
        payment.Name,
        Money(payment.Amount),
        detail);

    private static string ModeText(
        CoinFlow.Domain.Models.PaymentAssignmentMode mode) =>
        mode == CoinFlow.Domain.Models.PaymentAssignmentMode.PreviousPeriod
            ? "Geçmiş dönemi kapatırım"
            : "Gelecek dönemi karşılarım";

    private static string PeriodText(SalaryPeriod period) =>
        $"{period.Start.ToString("dd MMM", TurkishCulture)} → {period.End.ToString("dd MMM yyyy", TurkishCulture)}";

}
