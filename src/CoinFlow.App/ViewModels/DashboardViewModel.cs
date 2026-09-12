using System.Collections.ObjectModel;
using System.Globalization;
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
    /// <summary>
    /// Açık dönemin donmuş planında kalan ödemeler. Gelecek projeksiyonundan
    /// değil, dönemin kendi planından gelir (I16).
    /// </summary>
    public ObservableCollection<RemainingPaymentLine>
        RemainingLines
    { get; } = [];

    // --- Dönem kimliği ve ilerlemesi ---
    [ObservableProperty] private string currentPeriodText = "—";
    [ObservableProperty] private string periodElapsedText = string.Empty;
    [ObservableProperty] private double periodElapsedRatio;

    // --- Mevcut tutar (gözlem) ---
    [ObservableProperty] private string lastObservationText =
        "Henüz gözlem girmedin.";
    [ObservableProperty] private bool hasObservation;
    [ObservableProperty] private string observationDateText = string.Empty;

    // --- PLAN bloğu ---
    [ObservableProperty] private string planFrozenText = "—";
    [ObservableProperty] private string plannedEndingText = "—";

    // --- GİDİŞAT bloğu ---
    /// <summary>
    /// Yaşam gideri bir havuz: planlanan / harcanan / kalan. Günlere
    /// bölünmez — 20.000 planlanıp 15.000 harcandıysa 5.000 kalmıştır.
    /// </summary>
    [ObservableProperty] private string plannedLivingText = "—";
    [ObservableProperty] private string spentLivingText = "—";
    [ObservableProperty] private string remainingLivingText = "—";
    [ObservableProperty] private bool hasLivingOverspend;
    [ObservableProperty] private string livingOverspendText = string.Empty;

    /// <summary>Kartlar: planlanan ödeme ile kartın şu anki hâli.</summary>
    public ObservableCollection<CardProgressLine> CardLines { get; } = [];
    [ObservableProperty] private bool hasCardLines;

    [ObservableProperty] private bool hasDeficitFinancing;
    [ObservableProperty] private string plannedDeficitInterestText = "—";
    [ObservableProperty] private string projectedDeficitInterestText = "—";

    [ObservableProperty] private string plannedEndingCompareText = "—";
    [ObservableProperty] private string projectedEndingText = "—";
    [ObservableProperty] private bool isProjectedEndingNegative;
    [ObservableProperty] private string observedBalanceText = "—";

    // --- KALAN bloğu ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoRemainingLines))]
    private bool hasRemainingLines;
    [ObservableProperty] private string remainingWindowText = string.Empty;
    [ObservableProperty] private string remainingTotalText = "—";
    public bool HasNoRemainingLines => !HasRemainingLines;

    [ObservableProperty] private bool isPeriodClosable;

    // --- Durum / boş ekran ---
    [ObservableProperty] private bool hasUndeterminedCardPayment;
    [ObservableProperty] private string calculationDetails = string.Empty;
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
    [ObservableProperty] private bool showCalculationDetails;

    /// <summary>Dönem detayına açılabilmek için tutulan mevcut dönem.</summary>
    private SalaryPeriodProjection? _currentPeriod;

    /// <summary>
    /// §11 — yalnız gerçekten anlamlı olan uyarılar, önceliğe göre.
    /// </summary>
    public ObservableCollection<DashboardAlert> Alerts { get; } = [];
    [ObservableProperty] private bool hasAlerts;

    /// <summary>
    /// Dönem içi gözlem: "bugün şu kadar param var". Uygulamaya gelen kişi
    /// işe bunu girerek başlar.
    /// Kaydetmek donmuş planı, review checkpointini ve geçmiş logunu
    /// değiştirmez (I14) — yalnız açık dönemin gözlem defterine yazar.
    /// </summary>
    [ObservableProperty] private string currentBalanceInput = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveCurrentBalance))]
    private bool isSavingCurrentBalance;

    public bool CanSaveCurrentBalance => !IsSavingCurrentBalance;

    [RelayCommand]
    private async Task SaveCurrentBalanceAsync()
    {
        if (IsSavingCurrentBalance)
        {
            return;
        }

        try
        {
            IsSavingCurrentBalance = true;
            SetStatus(string.Empty);
            var amount = ParseMoney(CurrentBalanceInput);
            // Bilinçli olarak RefreshCurrentFinancialStateAsync DEĞİL: o bir
            // checkpoint işlemidir ve dönem içinde çağrılırsa açık planın
            // penceresini kısaltır, orijinal planı yetim bırakır (I14).
            await service.ObserveCurrentBalanceAsync(amount);
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(
                exception,
                "Gözlem kaydedilemedi."));
            return;
        }
        finally
        {
            IsSavingCurrentBalance = false;
        }

        await LoadAsync();
    }

    /// <summary>
    /// "Bu dönem nasıl oluşuyor" tablosu ana sayfadan kalktı; aynı kırılım
    /// Dönem Detayı'nda zaten var ve orada daha ayrıntılı.
    /// </summary>
    [RelayCommand]
    private Task OpenCurrentPeriodDetailAsync() =>
        _currentPeriod is null
            ? Task.CompletedTask
            : Shell.Current.GoToAsync(
                AppShell.PeriodDetailRoute,
                new ShellNavigationQueryParameters
                {
                    [SalaryPeriodDetailViewModel.DetailQueryKey] =
                        new SalaryPeriodDetailRequest(_currentPeriod)
                });

    private static decimal ParseMoney(string value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0 ||
            !decimal.TryParse(
                text,
                NumberStyles.Number,
                TurkishCulture,
                out var amount))
        {
            throw new InvalidOperationException(
                "Mevcut tutar geçerli bir sayı olmalıdır.");
        }

        return amount;
    }

    [RelayCommand]
    private void ToggleCalculationDetails() =>
        ShowCalculationDetails = !ShowCalculationDetails;

    public bool IsDevelopment => BuildInfo.IsDevelopment;

    /// <summary>
    /// Ana Sayfa mevcut dönemin ekranıdır (I16). Rakamları
    /// <c>GetPeriodProgressAsync</c> üzerinden donmuş plandan ve gözlem
    /// defterinden okur — gelecek projeksiyonundan değil.
    /// </summary>
    /// <remarks>
    /// <c>GetDashboardAsync</c> yalnız uyarı üretmek ve dönem detayına
    /// açılabilmek için çağrılır; ekrandaki hiçbir rakam ondan gelmez.
    /// </remarks>
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
                ResetToEmptyState(
                    "İlk kurulumla gelirini, dönemini ve mevcut tutarını birlikte kaydedelim.",
                    "Kuruluma Başla");
                HasPendingReview = false;
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

            // Uyarılar ve dönem detayı bağlantısı için; ekrandaki rakamlar
            // buradan gelmiyor.
            var dashboard = await service.GetDashboardAsync();
            _currentPeriod = dashboard?.CurrentPeriod;
            HasUndeterminedCardPayment =
                dashboard?.HasUndeterminedCardPayments ?? false;
            HasPendingStrategy = dashboard?.PendingStrategy is not null;
            CalculationDetails = _currentPeriod is null
                ? string.Empty
                : BuildDetails(_currentPeriod);
            BuildAlerts(dashboard, review.IsDue);
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
        CurrentPeriodText =
            $"{progress.PeriodStart.ToString("dd MMMM yyyy", TurkishCulture)} Dönemi";
        PeriodElapsedText = progress.TotalDays > 0
            ? $"{progress.ElapsedDays}/{progress.TotalDays} gün · " +
              $"{progress.PeriodEnd.ToString("dd MMMM", TurkishCulture)} tarihinde kapanıyor"
            : string.Empty;
        PeriodElapsedRatio = progress.ElapsedRatio;

        // PLAN
        PlanFrozenText = progress.WasRevised
            ? $"{progress.PlanFrozenOn.ToString("dd MMMM", TurkishCulture)} tarihinde donduruldu · {progress.RevisionCount} revizyon"
            : $"{progress.PlanFrozenOn.ToString("dd MMMM", TurkishCulture)} tarihinde donduruldu";
        PlannedEndingText = Money(progress.PlannedEndingSavings);

        // GİDİŞAT — gözlem yoksa blok hiç görünmez, rakam uydurulmaz.
        HasObservation = progress.HasObservation;
        if (progress.Observation is { } observation)
        {
            LastObservationText =
                $"Son gözlem: {observation.ObservedOn.ToString("dd MMMM yyyy", TurkishCulture)}";
            ObservationDateText =
                $"{observation.ObservedOn.ToString("dd MMMM", TurkishCulture)} gözlemi";
            CurrentBalanceInput = observation.ObservedBalance
                ?.ToString("0.##", TurkishCulture) ?? string.Empty;
        }
        else
        {
            LastObservationText = "Henüz gözlem girmedin.";
            ObservationDateText = string.Empty;
            CurrentBalanceInput = string.Empty;
        }

        // Parametre parametre: her bölüm planlanan ile şu anki hâli yan yana
        // koyar. Tek bir "fark" rakamı hangi kalemin değiştiğini gizliyordu.
        ObservedBalanceText = progress.ObservedBalance is { } balance
            ? Money(balance)
            : "—";

        // YAŞAM GİDERİ — havuz
        PlannedLivingText = Money(progress.PlannedLivingBudget);
        SpentLivingText = progress.ObservedLivingSpend is { } spent
            ? Money(spent)
            : "—";
        RemainingLivingText = progress.RemainingLivingBudget is { } left
            ? Money(left)
            : "—";
        HasLivingOverspend = progress.LivingOverspend is not null;
        LivingOverspendText = progress.LivingOverspend is { } over
            ? $"Havuz {Money(over)} aşıldı; fazlası dönem sonuna yansıyor."
            : string.Empty;

        // KREDİ KARTLARI
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

        // KMH — yalnız gerçekten açık varsa
        HasDeficitFinancing = progress.HasDeficitFinancing;
        PlannedDeficitInterestText = Money(progress.PlannedDeficitInterest);
        ProjectedDeficitInterestText =
            progress.ProjectedDeficitInterest is { } projectedInterest
                ? Money(projectedInterest)
                : "—";

        // DÖNEM SONU
        PlannedEndingCompareText = Money(progress.PlannedEndingSavings);
        ProjectedEndingText = progress.ProjectedEndingSavings is { } projected
            ? Money(projected)
            : "—";
        IsProjectedEndingNegative = progress.ProjectedEndingSavings < 0m;

        // KALAN — "bugünden dönem sonuna" değil, "ödenmiş işaretlemediklerin".
        // Vadesi geçmiş ama işaretlenmemiş satırlar da burada durur; başlığı
        // bugünle sınırlamak ekranın kendini yalanlaması olurdu (07 Eyl tarihli
        // satır "09 Eyl → 10 Eyl" penceresinin altında görünüyordu).
        RemainingWindowText =
            "Ödenmiş işaretlemediklerin · dönem " +
            $"{progress.PeriodEnd.ToString("dd MMMM", TurkishCulture)} tarihinde kapanıyor";
        RemainingLines.Clear();
        foreach (var line in progress.RemainingLines)
        {
            var detail = line.IsEstimate
                ? $"{line.Detail} • Tahmini"
                : line.Detail;
            if (line.PlannedDate < progress.Today)
            {
                detail = string.IsNullOrWhiteSpace(detail)
                    ? "Vadesi geçti"
                    : $"{detail} • Vadesi geçti";
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

    private void ResetToEmptyState(string message, string action)
    {
        HasFinancialPlan = false;
        IsEmptyState = true;
        _currentPeriod = null;
        HasUndeterminedCardPayment = false;
        HasPendingStrategy = false;
        HasObservation = false;
        HasRemainingLines = false;
        IsPeriodClosable = false;
        RemainingLines.Clear();
        Alerts.Clear();
        HasAlerts = false;
        EmptyStateMessage = message;
        EmptyStateAction = action;
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
        DashboardSnapshot? dashboard,
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

        if (dashboard?.HasUndeterminedCardPayments == true)
        {
            Alerts.Add(new DashboardAlert(
                DashboardAlertLevel.Action,
                "Kart ödeme tercihin eksik",
                "Bir kredi kartı için bu ekstreyi nasıl ödeyeceğini seçmedin. Seçmeden dönem sonu tahminin eksik kalır.",
                "Kartlara git",
                OpenCommitmentsCommand));
        }

        // Dönem açığı uyarısı burada değil: aynı cümleyi ana sayfadaki durum
        // özeti zaten söylüyordu ve rakam üç yerde birden duruyordu. Uyarı
        // listesi yalnız başka yerde görünmeyen, aksiyon bekleyen şeyler için.

        if (dashboard?.PendingStrategy is { } pending)
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
