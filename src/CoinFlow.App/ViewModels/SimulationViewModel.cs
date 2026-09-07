using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoinFlow.App.Models;
using CoinFlow.App.Services;
using CoinFlow.Application.Models;
using CoinFlow.Application.Services;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.App.ViewModels;

public partial class SimulationViewModel(
    CoinFlowService service,
    SimulatorInsightService simulatorInsightService,
    IUserFeedbackService feedback) : ViewModelBase
{
    public ObservableCollection<SelectionOption<SimulationScenarioType>>
        ScenarioTypes { get; } =
    [
        new("Nakit satın alma", SimulationScenarioType.CashPurchase),
        new("Karttan tek çekim", SimulationScenarioType.CreditCardSinglePayment),
        new("Kredi kartı taksitli", SimulationScenarioType.CreditCardInstallmentPurchase),
        new("Finansman / kredi", SimulationScenarioType.FinancingLoan),
        new("Nakit borç", SimulationScenarioType.CashDebt),
        new("Tek seferlik ödeme", SimulationScenarioType.FutureOneTimePayment),
        new("Düzenli ödeme", SimulationScenarioType.RecurringPayment),
        new("Tek seferlik gelir", SimulationScenarioType.FutureIncome),
        new("Gelir değişikliği", SimulationScenarioType.SalaryChange),
        new("Gelir kullanım düzeni değişikliği", SimulationScenarioType.PaymentStrategyChange),
        new("Kart ödeme şeklini değiştir", SimulationScenarioType.CreditCardPaymentMode)
    ];

    public ObservableCollection<SelectionOption<Guid>> CreditCards { get; } = [];
    public ObservableCollection<SimulationDraftConditionView>
        DraftConditions { get; } = [];
    public ObservableCollection<SimulatorPeriodView> Results { get; } = [];
    public ObservableCollection<string> NarrativeInsights { get; } = [];
    public ObservableCollection<SimulatorSummaryMetric> SummaryMetrics { get; } = [];
    public ObservableCollection<SimulatorInterestRow> InterestComparison
    { get; } = [];
    public ObservableCollection<SelectionOption<DateOnly>> StrategySalaryDates { get; } = [];
    public IReadOnlyList<SelectionOption<PaymentAssignmentMode>> StrategyModes { get; } =
    [
        new("Geçmiş dönemi kapatırım", PaymentAssignmentMode.PreviousPeriod),
        new("Gelecek dönemi karşılarım", PaymentAssignmentMode.UpcomingPeriod)
    ];

    public IReadOnlyList<SelectionOption<CreditCardPaymentType>>
        CardPaymentModes { get; } =
    [
        new("Tamamını öde", CreditCardPaymentType.FullStatement),
        new("Asgari öde", CreditCardPaymentType.Minimum)
    ];

    public IReadOnlyList<SelectionOption<bool>> CardPaymentScopes { get; } =
    [
        new("Yalnızca bu ekstre", false),
        new("Bundan sonraki tüm ekstreler", true)
    ];

    private IReadOnlyList<SimulationRequest> _lastRequests = [];
    private IReadOnlyList<SalaryPeriodProjection> _lastScenarioProjection = [];
    private IReadOnlyList<SalaryPeriodProjection> _lastBaselineProjection = [];
    private Guid? _editingConditionId;
    private readonly SemaphoreSlim _applyLock = new(1, 1);
    private readonly SemaphoreSlim _calculationLock = new(1, 1);
    private CancellationTokenSource? _liveRecalculation;
    private static readonly TimeSpan LiveRecalculationDelay =
        TimeSpan.FromMilliseconds(200);

    private bool _preserveOnNextAppearance;
    private DateOnly? _projectionAnchorDate;

    [ObservableProperty] private string name = "Beyaz eşya";
    [ObservableProperty] private string amount = "120000";
    [ObservableProperty] private SelectionOption<SimulationScenarioType>? selectedScenarioType;
    [ObservableProperty] private SelectionOption<Guid>? selectedCreditCard;
    [ObservableProperty] private DateTime startDate = DateTime.Today;
    [ObservableProperty] private string paymentCount = "9";
    [ObservableProperty] private DateTime firstPaymentDate = DateTime.Today.AddMonths(1);
    [ObservableProperty] private string totalRepaymentAmount = "145000";
    [ObservableProperty] private bool isCard;
    [ObservableProperty] private bool needsPaymentCount;
    [ObservableProperty] private bool needsFirstPayment;
    [ObservableProperty] private bool isFinancing;
    [ObservableProperty] private bool isStrategyChange;
    [ObservableProperty] private bool isCardPayoff;
    [ObservableProperty] private bool needsAmount = true;
    [ObservableProperty] private string startDateLabel =
        "Başlangıç / işlem tarihi";
    [ObservableProperty] private bool isRegularScenario = true;
    [ObservableProperty] private SelectionOption<PaymentAssignmentMode>? selectedStrategyMode;
    [ObservableProperty] private SelectionOption<DateOnly>? selectedStrategySalaryDate;
    [ObservableProperty] private SelectionOption<CreditCardPaymentType>? selectedCardPaymentMode;
    [ObservableProperty] private SelectionOption<bool>? selectedCardPaymentScope;
    [ObservableProperty] private string scenarioDescription = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyPlan))]
    [NotifyPropertyChangedFor(nameof(HasCurrentResults))]
    [NotifyPropertyChangedFor(nameof(HasScenarioResults))]
    [NotifyPropertyChangedFor(nameof(HasStaleResult))]
    private bool hasResults;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyPlan))]
    [NotifyPropertyChangedFor(nameof(HasCurrentResults))]
    [NotifyPropertyChangedFor(nameof(HasScenarioResults))]
    [NotifyPropertyChangedFor(nameof(HasStaleResult))]
    [NotifyPropertyChangedFor(nameof(RunSimulationButtonText))]
    private bool isResultStale;
    [ObservableProperty] private string baselineEnding = "—";
    [ObservableProperty] private string scenarioEnding = "—";
    [ObservableProperty] private string endingDifference = "—";
    [ObservableProperty] private string tightestPeriod = "—";
    [ObservableProperty] private string lowestAvailable = "—";
    [ObservableProperty] private string lowestSavingsCapacity = "—";
    [ObservableProperty] private string lowestProjectedSavings = "—";
    [ObservableProperty] private string firstNegativePeriod = "Yok";
    [ObservableProperty] private string maximumCarryOverDeficit = "—";
    [ObservableProperty] private string recoveryPeriod = "—";
    [ObservableProperty] private string totalScenarioCost = "—";
    [ObservableProperty] private string monthlyBurden = string.Empty;
    [ObservableProperty] private bool hasMonthlyBurden;
    [ObservableProperty] private string financingCost = string.Empty;
    [ObservableProperty] private bool hasFinancingCost;
    [ObservableProperty] private string baselineInterest = "—";
    [ObservableProperty] private string scenarioInterest = "—";
    [ObservableProperty] private string interestDifference = "—";
    [ObservableProperty] private string interestDifferenceTitle =
        "Ek Faiz Yükü";
    [ObservableProperty] private string friendlySummary = string.Empty;
    [ObservableProperty] private string assignmentModeText = string.Empty;
    [ObservableProperty] private bool hasStrategyTransitionSummary;
    [ObservableProperty] private string strategyTransitionSummary = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRunSimulation))]
    [NotifyPropertyChangedFor(nameof(HasNoDraftConditions))]
    private bool isPlanAvailable;
    [ObservableProperty] private bool isPlanUnavailable = true;
    [ObservableProperty] private string emptyStateMessage =
        "Simülasyon yapabilmek için önce temel finans planını oluştur.";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyPlan))]
    private bool isApplyingPlan;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyPlan))]
    private bool isPlanApplied;
    [ObservableProperty] private string applyButtonText = "Planı Uygula";
    [ObservableProperty] private string applyConfirmationText =
        "Bu plan gerçek finans planına eklenecek.";
    [ObservableProperty] private string targetAmount = string.Empty;
    [ObservableProperty] private string targetResult = string.Empty;
    [ObservableProperty] private bool hasTargetResult;

    /// <summary>
    /// Hiçbir koşul açık değilken sonuçlar tek bir baz çizgiden ibarettir:
    /// "hiçbirini uygulamazsam" hâli. Karşılaştırmaya dayanan bölümler
    /// (faiz tablosu, dönem sonu farkı, senaryo maliyeti, Planı Uygula)
    /// senaryo yokken anlamsız olduğu için gizlenir.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasScenarioResults))]
    [NotifyPropertyChangedFor(nameof(CanApplyPlan))]
    private bool isBaselineOnly;

    public bool HasDraftConditions => DraftConditions.Count > 0;
    public bool HasNoDraftConditions => IsPlanAvailable && !HasDraftConditions;
    public bool HasMultipleDraftConditions => DraftConditions.Count > 1;
    public bool CanRunSimulation => IsPlanAvailable && HasDraftConditions;
    public bool IsEditingCondition => _editingConditionId is not null;

    /// <summary>
    /// Kapalı koşul da listede durduğu için yalnızca toplamı yazmak yanıltıcı;
    /// kaçının hesaba girdiği de görünmeli.
    /// </summary>
    public string DraftConditionCountText
    {
        get
        {
            var enabled = EnabledConditions().Count;
            return enabled == DraftConditions.Count
                ? $"{DraftConditions.Count} koşul"
                : $"{DraftConditions.Count} koşul · {enabled} açık";
        }
    }

    public string AddConditionButtonText =>
        IsEditingCondition ? "Düzenlemeyi Kaydet" : "Koşul Ekle";
    public string RunSimulationButtonText =>
        IsResultStale ? "Simülasyonu Güncelle" : "Simülasyonu Yap";
    public bool HasCurrentResults => HasResults && !IsResultStale;
    public bool HasScenarioResults => HasCurrentResults && !IsBaselineOnly;
    public bool HasStaleResult => HasResults && IsResultStale;
    public bool CanApplyPlan =>
        HasCurrentResults && !IsBaselineOnly && !IsApplyingPlan && !IsPlanApplied;

    /// <summary>
    /// Hesaba ve "Planı Uygula"ya yalnızca açık koşullar girer; bu iki yolun
    /// aynı listeyi görmesi şart, yoksa uygulanan plan ekranda gösterilenden
    /// farklı olur.
    /// </summary>
    private IReadOnlyList<SimulationDraftConditionView> EnabledConditions() =>
        DraftConditions.Where(x => x.IsEnabled).ToArray();

    private IReadOnlyList<SimulationRequest> EnabledRequests() =>
        EnabledConditions().Select(x => x.Request).ToArray();

    public SimulationApplyResult? LastApplyResult { get; private set; }

    public async Task LoadAsync()
    {
        try
        {
            SetStatus(string.Empty);
            var plan = await service.GetFinancialPlanAsync();
            _projectionAnchorDate =
                plan.Settings.ProjectionAnchorDate == default
                    ? null
                    : plan.Settings.ProjectionAnchorDate;
            IsPlanAvailable = plan.Salaries.Count > 0 &&
                              plan.PaymentAssignmentStrategies.Count > 0 &&
                              plan.Settings.ProjectionAnchorDate != default;
            IsPlanUnavailable = !IsPlanAvailable;
            if (HasResults)
            {
                MarkResultsStale();
            }
            else
            {
                ResetApplyState(clearRequest: true);
            }
            if (!IsPlanAvailable)
            {
                EmptyStateMessage = plan.Salaries.Count == 0
                    ? "Simülasyon yapabilmek için önce temel finans planını oluştur."
                    : "Simülasyon için gelir kullanım düzenini seçerek finans planını tamamla.";
                AssignmentModeText = string.Empty;
                CreditCards.Clear();
                StrategySalaryDates.Clear();
                DraftConditions.Clear();
                NotifyDraftChanged();
                Results.Clear();
                ClearTargetResult();
                IsBaselineOnly = false;
                _lastScenarioProjection = [];
                return;
            }

            var overview = await service.GetPaymentAssignmentStrategyOverviewAsync();
            CreditCards.Clear();
            foreach (var card in plan.CreditCards)
            {
                CreditCards.Add(new SelectionOption<Guid>(
                    $"{card.Bank} {card.Name}".Trim(),
                    card.Id));
            }

            var currentMode = overview.Current?.Mode ??
                              throw new InvalidOperationException(
                                  "Gelir kullanım düzeni bulunamadı.");
            AssignmentModeText = AssignmentModeLabel(currentMode);
            StrategySalaryDates.Clear();
            foreach (var date in overview.AvailableEffectiveSalaryDates)
            {
                StrategySalaryDates.Add(new SelectionOption<DateOnly>(
                    $"{date.ToString("dd MMMM yyyy", TurkishCulture)} dönemi",
                    date));
            }
            SelectedStrategySalaryDate ??= StrategySalaryDates.FirstOrDefault();
            SelectedStrategyMode ??= StrategyModes.First(x =>
                x.Value != currentMode);

            SelectedScenarioType ??= ScenarioTypes[0];
            SelectedCreditCard ??= CreditCards.FirstOrDefault();
        }
        catch (Exception exception)
        {
            IsPlanAvailable = false;
            IsPlanUnavailable = true;
            HasResults = false;
            IsResultStale = false;
            _lastScenarioProjection = [];
            ClearTargetResult();
            SetStatus(UserFacingMessages.FromException(exception));
        }
    }

    [RelayCommand]
    private Task OpenCommitmentsAsync() =>
        Shell.Current.GoToAsync("//commitments/commitments-content");

    [RelayCommand]
    private async Task OpenPeriodDetailAsync(SimulatorPeriodView? line)
    {
        if (line is null)
        {
            return;
        }

        await Shell.Current.GoToAsync(
            AppShell.PeriodDetailRoute,
            new ShellNavigationQueryParameters
            {
                [SalaryPeriodDetailViewModel.DetailQueryKey] =
                    new SalaryPeriodDetailRequest(
                        line.Projection,
                        IsSimulationScenario: true)
            });
        _preserveOnNextAppearance = true;
    }

    public bool ConsumeDetailReturn()
    {
        if (!_preserveOnNextAppearance)
        {
            return false;
        }

        _preserveOnNextAppearance = false;
        return true;
    }

    partial void OnSelectedScenarioTypeChanged(
        SelectionOption<SimulationScenarioType>? value)
    {
        var type = value?.Value ?? SimulationScenarioType.CashPurchase;
        IsCard = type is
            SimulationScenarioType.CreditCardSinglePayment or
            SimulationScenarioType.CreditCardInstallmentPurchase or
            SimulationScenarioType.CreditCardPaymentMode;
        NeedsPaymentCount = type is
            SimulationScenarioType.CreditCardInstallmentPurchase or
            SimulationScenarioType.FinancingLoan or
            SimulationScenarioType.CashDebt or
            SimulationScenarioType.RecurringPayment;
        NeedsFirstPayment = type is
            SimulationScenarioType.FinancingLoan or
            SimulationScenarioType.CashDebt or
            SimulationScenarioType.RecurringPayment;
        IsFinancing = type == SimulationScenarioType.FinancingLoan;
        IsStrategyChange = type == SimulationScenarioType.PaymentStrategyChange;
        IsCardPayoff = type == SimulationScenarioType.CreditCardPaymentMode;
        NeedsAmount = !IsStrategyChange && !IsCardPayoff;
        StartDateLabel = IsCardPayoff
            ? "Hangi ekstreden itibaren (son ödeme tarihi)"
            : "Başlangıç / işlem tarihi";
        if (IsCardPayoff)
        {
            SelectedCardPaymentMode ??= CardPaymentModes[0];
            SelectedCardPaymentScope ??= CardPaymentScopes[0];
        }
        IsRegularScenario = !IsStrategyChange;
        ScenarioDescription = type switch
        {
            SimulationScenarioType.CashPurchase =>
                "Tutar, seçtiğin tarihte finansal durumundan düşer.",
            SimulationScenarioType.CreditCardSinglePayment =>
                "Harcama, kartının ekstre kesim ve son ödeme tarihlerine göre hesaplanır.",
            SimulationScenarioType.CreditCardInstallmentPurchase =>
                "Taksitler ilgili kart ekstrelerine yansıtılır.",
            SimulationScenarioType.FinancingLoan =>
                "Kredi tutarı işlem tarihinde gelir olarak eklenir; toplam geri ödeme, ilk ödeme tarihinden başlayarak taksitlere bölünür.",
            SimulationScenarioType.CashDebt =>
                "Borç tutarı, seçtiğin ödeme sayısına kuruş farkı bırakmadan bölünür.",
            SimulationScenarioType.FutureOneTimePayment =>
                "Ödeme, seçtiğin tarihte zorunlu ödemelere eklenir.",
            SimulationScenarioType.RecurringPayment =>
                "Girilen tutar, belirtilen dönem sayısı boyunca aylık tekrarlanır.",
            SimulationScenarioType.FutureIncome =>
                "Gelir, seçtiğin tarihin dahil olduğu döneme eklenir.",
            SimulationScenarioType.SalaryChange =>
                "Yeni gelir, seçtiğin tarihten itibaren kullanılır.",
            SimulationScenarioType.PaymentStrategyChange =>
                "Yeni düzen yalnızca seçtiğin dönemden itibaren hesaplanır; Simülasyon Yap finans kayıtlarını değiştirmez.",
            SimulationScenarioType.CreditCardPaymentMode =>
                "Kartın ödeme şeklini değiştirir. Kart faizi ile finansman açığı faizi ters yönde hareket edebilir; Faiz Karşılaştırması ikisini ayrı gösterir.",
            _ => string.Empty
        };
    }

    partial void OnNameChanged(string value) { }
    partial void OnAmountChanged(string value) { }
    partial void OnSelectedCreditCardChanged(SelectionOption<Guid>? value) =>
        _ = value;
    partial void OnStartDateChanged(DateTime value) { }
    partial void OnPaymentCountChanged(string value) { }
    partial void OnFirstPaymentDateChanged(DateTime value) =>
        _ = value;
    partial void OnTotalRepaymentAmountChanged(string value) =>
        _ = value;
    partial void OnSelectedStrategyModeChanged(
        SelectionOption<PaymentAssignmentMode>? value) =>
        _ = value;
    partial void OnSelectedStrategySalaryDateChanged(
        SelectionOption<DateOnly>? value) =>
        _ = value;

    [RelayCommand]
    private void AddCondition()
    {
        try
        {
            SetStatus(string.Empty);
            var request = BuildRequest();
            SimulationCalculator.Validate(request, _projectionAnchorDate);
            if (_editingConditionId is Guid editingId)
            {
                var index = DraftConditions
                    .ToList()
                    .FindIndex(x => x.Id == editingId);
                if (index < 0 || index >= DraftConditions.Count)
                {
                    throw new InvalidOperationException(
                        "Düzenlenecek koşul bulunamadı.");
                }

                // Düzenleme koşulun içeriğini değiştirir, hesaba girip
                // girmediğini değil; kapalı bir koşul düzenlenince kapalı kalır.
                DraftConditions[index] = CreateConditionView(
                    request,
                    DraftConditions[index].IsEnabled);
                _editingConditionId = null;
            }
            else
            {
                DraftConditions.Add(CreateConditionView(request));
            }

            ResetConditionForm();
            MarkResultsStale();
            NotifyDraftChanged();
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(exception));
        }
    }

    [RelayCommand]
    private void EditCondition(SimulationDraftConditionView? condition)
    {
        if (condition is null)
        {
            return;
        }

        LoadConditionIntoForm(condition.Request);
        _editingConditionId = condition.Id;
        OnPropertyChanged(nameof(IsEditingCondition));
        OnPropertyChanged(nameof(AddConditionButtonText));
    }

    [RelayCommand]
    private void RemoveCondition(SimulationDraftConditionView? condition)
    {
        if (condition is null)
        {
            return;
        }

        DraftConditions.Remove(condition);
        if (_editingConditionId == condition.Id)
        {
            _editingConditionId = null;
            OnPropertyChanged(nameof(IsEditingCondition));
            OnPropertyChanged(nameof(AddConditionButtonText));
        }

        // Kapalı koşul zaten hesaba girmiyordu; silinmesi sonucu bayatlatmaz.
        // "Plan değişti" demek burada ekranın kendini yalanlaması olurdu.
        if (condition.IsEnabled)
        {
            MarkResultsStale();
        }

        NotifyDraftChanged();
    }

    [RelayCommand]
    private void ClearDraft()
    {
        DraftConditions.Clear();
        _editingConditionId = null;
        Results.Clear();
        NarrativeInsights.Clear();
        SummaryMetrics.Clear();
        InterestComparison.Clear();
        _lastBaselineProjection = [];
        HasResults = false;
        IsResultStale = false;
        IsBaselineOnly = false;
        ResetApplyState(clearRequest: true);
        _lastScenarioProjection = [];
        ClearTargetResult();
        NotifyDraftChanged();
        OnPropertyChanged(nameof(IsEditingCondition));
        OnPropertyChanged(nameof(AddConditionButtonText));
        SetStatus("Simülasyon planı temizlendi.");
    }

    [RelayCommand]
    private async Task CalculateAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await RunCalculationAsync(showErrorDialog: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Hesabın tek gövdesi. Switch'ler canlı yeniden hesap tetiklediği için
    /// aynı anda iki hesap çalışmamalı; semafor sırayı korur, iptal edilen
    /// bekleyen istek sıraya girmeden düşer.
    /// </summary>
    private async Task RunCalculationAsync(
        bool showErrorDialog,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _calculationLock.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            SetStatus(string.Empty);
            if (DraftConditions.Count == 0)
            {
                SetStatus("Önce denemek istediğin en az bir koşul ekle.");
                return;
            }

            var requests = EnabledRequests();
            if (requests.Count == 0)
            {
                await PopulateBaselineOnlyAsync(cancellationToken);
                return;
            }

            var result = await service.SimulateAsync(
                requests,
                cancellationToken: cancellationToken);
            _lastRequests = requests;
            IsBaselineOnly = false;
            IsPlanApplied = false;
            ApplyButtonText = "Planı Uygula";
            LastApplyResult = null;
            ApplyConfirmationText =
                BuildApplyConfirmation(requests);
            _lastScenarioProjection = result.Scenario;
            _lastBaselineProjection = result.Baseline;
            Populate(result);
            HasResults = true;
            IsResultStale = false;
            RefreshTargetResultAfterSimulation();
        }
        catch (OperationCanceledException)
        {
            // Sonraki toggle bu hesabı geçersiz kıldı; yenisi zaten geliyor.
        }
        catch (Exception exception)
        {
            if (!HasResults)
            {
                HasResults = false;
            }
            var message = UserFacingMessages.FromException(
                exception,
                "Simülasyon hesaplanırken bir sorun oluştu. Tekrar deneyebilirsin.");
            SetStatus(message);
            if (showErrorDialog)
            {
                await feedback.ShowErrorAsync(
                    message,
                    title: "Hesaplanamadı");
            }
        }
        finally
        {
            _calculationLock.Release();
        }
    }

    /// <summary>
    /// Tüm koşullar kapalıyken gösterilen hâl: senaryo yok, yalnızca bugünkü
    /// planın 12 dönemi. <c>SimulateAsync</c> bu yoldan çağrılamaz —
    /// <c>SimulationCalculator.Validate</c> boş koşul listesini reddediyor —
    /// bu yüzden baz projeksiyon doğrudan alınır.
    /// </summary>
    private async Task PopulateBaselineOnlyAsync(
        CancellationToken cancellationToken)
    {
        var baseline = await service.GetFuturePeriodsAsync(
            cancellationToken: cancellationToken);
        if (baseline.Count == 0)
        {
            SetStatus("Baz projeksiyon hesaplanamadı.");
            return;
        }

        _lastRequests = [];
        _lastBaselineProjection = baseline;
        _lastScenarioProjection = [];
        IsBaselineOnly = true;
        ResetApplyState(clearRequest: false);
        PopulateBaseline(baseline);
        HasResults = true;
        IsResultStale = false;
        RefreshTargetResultAfterSimulation();
    }

    [RelayCommand]
    private void FindTarget()
    {
        try
        {
            var target = ParsePositiveMoney(TargetAmount, "Hedef tutar");
            UpdateTargetResult(target);
            SetStatus(string.Empty);
        }
        catch (Exception exception)
        {
            ClearTargetResult();
            SetStatus(UserFacingMessages.FromException(exception));
        }
    }

    public async Task<SimulationApplyResult?> ApplyLastPlanAsync()
    {
        if (_lastRequests.Count == 0 || !HasResults)
        {
            SetStatus("Önce Simülasyon Yap ile sonucu hesaplamalısın.");
            return null;
        }

        if (IsResultStale)
        {
            SetStatus("Plan değişti. Uygulamadan önce simülasyonu güncelle.");
            return null;
        }

        if (IsPlanApplied)
        {
            SetStatus("Plan zaten uygulandı.");
            return LastApplyResult;
        }

        if (!await _applyLock.WaitAsync(0))
        {
            return null;
        }

        try
        {
            IsApplyingPlan = true;
            var result = await service.ApplySimulationAsync(
                _lastRequests,
                confirmed: true);
            LastApplyResult = result;
            IsPlanApplied = true;
            ApplyButtonText = "Plan Uygulandı";
            // Yalnızca kaydedilen koşullar listeden düşer. Kapalı koşulu
            // kullanıcı bilerek dışarıda bıraktı; sessizce silmek veri kaybı
            // gibi hissettirir, kenarda tutup üzerine yeni plan kurabilmeli.
            foreach (var applied in EnabledConditions())
            {
                DraftConditions.Remove(applied);
            }

            Results.Clear();
            NarrativeInsights.Clear();
            SummaryMetrics.Clear();
            InterestComparison.Clear();
            _lastBaselineProjection = [];
            HasResults = false;
            IsResultStale = false;
            IsBaselineOnly = false;
            _lastRequests = [];
            _lastScenarioProjection = [];
            ClearTargetResult();
            NotifyDraftChanged();
            SetStatus(result.Message);
            return result;
        }
        catch (Exception exception)
        {
            var message = UserFacingMessages.FromException(exception);
            SetStatus(message);
            await feedback.ShowErrorAsync(message);
            return null;
        }
        finally
        {
            IsApplyingPlan = false;
            _applyLock.Release();
        }
    }

    private SimulationRequest BuildRequest()
    {
        var type = SelectedScenarioType?.Value
            ?? throw new InvalidOperationException("Plan türü seçmelisin.");
        var count = NeedsPaymentCount
            ? int.TryParse(PaymentCount, out var parsed)
                ? parsed
                : throw new InvalidOperationException("Ödeme sayısı geçerli olmalıdır.")
            : 1;
        decimal? repayment = IsFinancing
            ? ParseMoney(TotalRepaymentAmount, "Toplam geri ödeme")
            : null;
        return new SimulationRequest(
            type,
            Name,
            IsStrategyChange || IsCardPayoff
                ? 0m
                : ParseMoney(Amount, "Tutar"),
            IsStrategyChange
                ? SelectedStrategySalaryDate?.Value ??
                  throw new InvalidOperationException(
                      "Planın başlayacağı dönemi seçmelisin.")
                : DateOnly.FromDateTime(StartDate),
            count,
            NeedsFirstPayment
                ? DateOnly.FromDateTime(FirstPaymentDate)
                : null,
            IsCard
                ? SelectedCreditCard?.Value ??
                  throw new InvalidOperationException("Bir kredi kartı seçmelisin.")
                : null,
            repayment,
            IsStrategyChange ? SelectedStrategyMode?.Value : null,
            IsStrategyChange ? SelectedStrategySalaryDate?.Value : null,
            _editingConditionId ?? Guid.NewGuid(),
            IsCardPayoff
                ? SelectedCardPaymentMode?.Value ??
                  throw new InvalidOperationException(
                      "Kart ödeme şeklini seçmelisin.")
                : null,
            IsCardPayoff && SelectedCardPaymentScope?.Value == true);
    }

    private string BuildApplyConfirmation(IReadOnlyList<SimulationRequest> requests)
    {
        if (requests.Count == 1)
        {
            return BuildApplyConfirmation(requests[0]);
        }

        var preview = string.Join(
            Environment.NewLine,
            EnabledConditions()
                .Take(6)
                .Select(x => $"• {x.DateText} — {x.SummaryText}"));
        return
            $"Bu simülasyon planındaki {requests.Count} koşul gerçek finans planına birlikte eklenecek.\n\n{preview}\n\nHer şey tek seferde kaydedilir; bir koşul kaydedilemezse hiçbir değişiklik yapılmaz.";
    }

    private string BuildApplyConfirmation(SimulationRequest request)
    {
        var summary = request.Type is
            SimulationScenarioType.PaymentStrategyChange or
            SimulationScenarioType.CreditCardPaymentMode
                ? request.Name.Trim()
                : $"{Money(request.Amount)} {request.Name.Trim()}";
        var detail = request.Type switch
        {
            SimulationScenarioType.CreditCardInstallmentPurchase =>
                $"Kart: {CardLabel(request.CreditCardId)}\n{request.PaymentCount} taksit\nİşlem: {request.StartDate:dd MMMM yyyy}",
            SimulationScenarioType.CreditCardSinglePayment =>
                $"Kart: {CardLabel(request.CreditCardId)}\nİşlem: {request.StartDate:dd MMMM yyyy}",
            SimulationScenarioType.CreditCardPaymentMode =>
                $"Kart: {CardLabel(request.CreditCardId)}\n{CardModeLabel(request.CardPaymentType)} · {CardScopeLabel(request.AppliesToAllStatements)}\n{request.StartDate:dd MMMM yyyy}",
            SimulationScenarioType.FinancingLoan =>
                $"{request.PaymentCount} taksit • toplam {Money(request.TotalRepaymentAmount.GetValueOrDefault())}\nİlk ödeme: {request.FirstPaymentDate:dd MMMM yyyy}",
            SimulationScenarioType.CashDebt or
                SimulationScenarioType.RecurringPayment =>
                $"{request.PaymentCount} ödeme\nİlk ödeme: {request.FirstPaymentDate:dd MMMM yyyy}",
            SimulationScenarioType.PaymentStrategyChange =>
                $"Başlangıç dönemi: {request.EffectiveSalaryDate:dd MMMM yyyy}",
            _ => $"Tarih: {request.StartDate:dd MMMM yyyy}"
        };
        return $"Bu plan gerçek finans planına eklenecek.\n\n{summary}\n{detail}";
    }

    private void RefreshTargetResultAfterSimulation()
    {
        if (string.IsNullOrWhiteSpace(TargetAmount))
        {
            ClearTargetResult();
            return;
        }

        try
        {
            var target = ParsePositiveMoney(TargetAmount, "Hedef tutar");
            UpdateTargetResult(target);
            SetStatus(string.Empty);
        }
        catch (Exception exception)
        {
            ClearTargetResult();
            SetStatus(UserFacingMessages.FromException(exception));
        }
    }

    private void UpdateTargetResult(decimal target)
    {
        // Hiçbir koşul açık değilken senaryo yoktur; hedef sorusunun cevabı
        // bu durumda bugünkü planın kendi seyrinden okunur.
        var projection = _lastScenarioProjection.Count > 0
            ? _lastScenarioProjection
            : _lastBaselineProjection;
        if (!HasCurrentResults || projection.Count == 0)
        {
            throw new InvalidOperationException(
                "Önce simülasyonu hesaplamalısın.");
        }

        var result = service.FindTargetReachability(
            projection,
            target);
        TargetResult = result switch
        {
            { IsAlreadyReached: true } =>
                "Bu seviyenin zaten üzerindesin.",
            { FirstReachedPeriod: { } reached } =>
                $"Bu planla {Money(target)} seviyesine ilk kez {TargetPeriodText(reached.Period)} döneminde ulaşıyorsun.",
            _ =>
                $"Bu planla {Money(target)} seviyesine 12 dönemlik görünüm içinde ulaşılamıyor."
        };
        HasTargetResult = true;
    }

    private void ClearTargetResult()
    {
        TargetResult = string.Empty;
        HasTargetResult = false;
    }

    private SimulationDraftConditionView CreateConditionView(
        SimulationRequest request,
        bool isEnabled = true)
    {
        var date = request.Type == SimulationScenarioType.PaymentStrategyChange
            ? request.EffectiveSalaryDate ?? request.StartDate
            : request.StartDate;
        var condition = new SimulationDraftConditionView(
            request.ScenarioId,
            request,
            date.ToString("MMMM yyyy", TurkishCulture),
            ConditionTypeText(request.Type),
            ConditionSummaryText(request))
        {
            IsEnabled = isEnabled
        };
        // Abonelik burada kurulur: koşulu üreten tek yer burası. Listeden
        // düşen koşulun aboneliği kendisiyle birlikte gider — bağ koşuldan
        // ViewModel'e doğrudur, ters yönde referans tutmaz.
        condition.PropertyChanged += OnDraftConditionPropertyChanged;
        return condition;
    }

    /// <summary>
    /// Switch değişince kullanıcı "Simülasyonu Güncelle"ye basmamalı; sonuç
    /// bayat değil taze olmalı. Hızlı arka arkaya toggle'da bekleyen istek
    /// iptal edilir, yalnızca sonuncusu hesaplanır.
    /// </summary>
    private void OnDraftConditionPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is not
            nameof(SimulationDraftConditionView.IsEnabled))
        {
            return;
        }

        OnPropertyChanged(nameof(DraftConditionCountText));
        QueueLiveRecalculation();
    }

    /// <summary>
    /// Canlı hesap hattının tek girişi: koşul switch'leri ve yaşam gideri
    /// slider'ı buradan geçer. Bekleyen istek iptal edilir, yalnızca sonuncusu
    /// hesaplanır.
    /// </summary>
    private void QueueLiveRecalculation()
    {
        if (!HasResults)
        {
            // Kullanıcı henüz hiç hesaplamadı; kontroller tek başına sonuç
            // üretmez, "Simülasyonu Yap" ilk adımdır.
            return;
        }

        // Dispose çağrılmıyor: iptal edilen token hâlâ süren bir hesabın
        // içinde (SimulateAsync, semafor beklemesi) yaşıyor olabilir; kaynağı
        // altından çekmek ObjectDisposedException'a açık kapı bırakır.
        _liveRecalculation?.Cancel();
        var source = new CancellationTokenSource();
        _liveRecalculation = source;
        _ = RecalculateLiveAsync(source.Token);
    }

    private async Task RecalculateLiveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(LiveRecalculationDelay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await RunCalculationAsync(
            showErrorDialog: false,
            cancellationToken: cancellationToken);
    }

    private string ConditionSummaryText(SimulationRequest request)
    {
        var amount = Money(request.Amount);
        return request.Type switch
        {
            SimulationScenarioType.CashPurchase =>
                $"{amount} • Nakit satın alma",
            SimulationScenarioType.CreditCardSinglePayment =>
                $"{CardLabel(request.CreditCardId)} • {amount} • Tek çekim",
            SimulationScenarioType.CreditCardInstallmentPurchase =>
                $"{CardLabel(request.CreditCardId)} • {amount} • {request.PaymentCount} taksit",
            SimulationScenarioType.FinancingLoan =>
                $"{amount} • {request.PaymentCount} taksit • toplam {Money(request.TotalRepaymentAmount.GetValueOrDefault())}",
            SimulationScenarioType.CashDebt =>
                $"{amount} • {request.PaymentCount} ödeme",
            SimulationScenarioType.FutureOneTimePayment =>
                $"{amount} • Tek seferlik ödeme",
            SimulationScenarioType.RecurringPayment =>
                $"{amount}/ay • {request.PaymentCount} ay",
            SimulationScenarioType.FutureIncome =>
                $"{amount} • Tek seferlik gelir",
            SimulationScenarioType.SalaryChange =>
                $"{amount} • Yeni gelir",
            SimulationScenarioType.PaymentStrategyChange =>
                $"{StrategyModeLabel(request.NewPaymentAssignmentMode)} • {request.EffectiveSalaryDate:dd MMMM yyyy} dönemi",
            SimulationScenarioType.CreditCardPaymentMode =>
                $"{CardLabel(request.CreditCardId)} • {CardModeLabel(request.CardPaymentType)}",
            _ => request.Name
        };
    }

    private static string ConditionTypeText(SimulationScenarioType type) =>
        type switch
        {
            SimulationScenarioType.CashPurchase => "Nakit satın alma",
            SimulationScenarioType.CreditCardSinglePayment => "Karttan tek çekim",
            SimulationScenarioType.CreditCardInstallmentPurchase => "Kart taksitli harcama",
            SimulationScenarioType.FinancingLoan => "Finansman / kredi",
            SimulationScenarioType.CashDebt => "Nakit borç",
            SimulationScenarioType.FutureOneTimePayment => "Tek seferlik ödeme",
            SimulationScenarioType.RecurringPayment => "Düzenli ödeme",
            SimulationScenarioType.FutureIncome => "Tek seferlik gelir",
            SimulationScenarioType.SalaryChange => "Gelir değişikliği",
            SimulationScenarioType.PaymentStrategyChange => "Gelir kullanım düzeni",
            SimulationScenarioType.CreditCardPaymentMode => "Kart ödeme şekli",
            _ => "Koşul"
        };

    private string CardLabel(Guid? creditCardId) =>
        CreditCards.FirstOrDefault(x => x.Value == creditCardId)?.Label ??
        "Kart";

    private static string StrategyModeLabel(PaymentAssignmentMode? mode) =>
        mode == PaymentAssignmentMode.PreviousPeriod
            ? "Geçmiş dönemi kapatırım"
            : "Gelecek dönemi karşılarım";

    private static string CardModeLabel(CreditCardPaymentType? paymentType) =>
        paymentType == CreditCardPaymentType.Minimum
            ? "Asgari öde"
            : "Tamamını öde";

    private static string CardScopeLabel(bool appliesToAllStatements) =>
        appliesToAllStatements
            ? "Bundan sonraki tüm ekstreler"
            : "Yalnızca bu ekstre";

    private void LoadConditionIntoForm(SimulationRequest request)
    {
        SelectedScenarioType = ScenarioTypes.First(x => x.Value == request.Type);
        Name = request.Name;
        Amount = request.Amount > 0m
            ? request.Amount.ToString("0.##", TurkishCulture)
            : string.Empty;
        StartDate = request.StartDate.ToDateTime(TimeOnly.MinValue);
        PaymentCount = request.PaymentCount.ToString(TurkishCulture);
        FirstPaymentDate = (request.FirstPaymentDate ?? request.StartDate)
            .ToDateTime(TimeOnly.MinValue);
        TotalRepaymentAmount = request.TotalRepaymentAmount is decimal repayment
            ? repayment.ToString("0.##", TurkishCulture)
            : string.Empty;
        SelectedCreditCard = CreditCards.FirstOrDefault(x =>
            x.Value == request.CreditCardId);
        SelectedStrategyMode = request.NewPaymentAssignmentMode is { } mode
            ? StrategyModes.First(x => x.Value == mode)
            : SelectedStrategyMode;
        SelectedStrategySalaryDate = request.EffectiveSalaryDate is { } date
            ? StrategySalaryDates.FirstOrDefault(x => x.Value == date)
            : SelectedStrategySalaryDate;
        SelectedCardPaymentMode = request.CardPaymentType is { } cardPaymentType
            ? CardPaymentModes.First(x => x.Value == cardPaymentType)
            : SelectedCardPaymentMode;
        SelectedCardPaymentScope = CardPaymentScopes.First(x =>
            x.Value == request.AppliesToAllStatements);
    }

    private void ResetConditionForm()
    {
        _editingConditionId = null;
        Name = "Yeni koşul";
        Amount = string.Empty;
        PaymentCount = "1";
        StartDate = DateTime.Today;
        FirstPaymentDate = DateTime.Today.AddMonths(1);
        TotalRepaymentAmount = string.Empty;
        SelectedScenarioType = ScenarioTypes[0];
        SelectedCreditCard = CreditCards.FirstOrDefault();
        SelectedStrategySalaryDate = StrategySalaryDates.FirstOrDefault();
        SelectedCardPaymentMode = CardPaymentModes[0];
        SelectedCardPaymentScope = CardPaymentScopes[0];
        OnPropertyChanged(nameof(IsEditingCondition));
        OnPropertyChanged(nameof(AddConditionButtonText));
    }

    private void ResetApplyState(bool clearRequest)
    {
        if (clearRequest)
        {
            _lastRequests = [];
            _lastScenarioProjection = [];
        }

        LastApplyResult = null;
        IsPlanApplied = false;
        IsApplyingPlan = false;
        ApplyButtonText = "Planı Uygula";
    }

    private void MarkResultsStale()
    {
        if (HasResults && !IsPlanApplied)
        {
            IsResultStale = true;
        }

        ClearTargetResult();
        ResetApplyState(clearRequest: true);
    }

    private void NotifyDraftChanged()
    {
        OnPropertyChanged(nameof(HasDraftConditions));
        OnPropertyChanged(nameof(HasNoDraftConditions));
        OnPropertyChanged(nameof(HasMultipleDraftConditions));
        OnPropertyChanged(nameof(CanRunSimulation));
        OnPropertyChanged(nameof(DraftConditionCountText));
        OnPropertyChanged(nameof(RunSimulationButtonText));
        OnPropertyChanged(nameof(CanApplyPlan));
    }

    private void Populate(SimulationResult result)
    {
        var projectionSummary = simulatorInsightService.Build(result.Scenario);
        var baselineEnding = result.Baseline[^1].EndingProjectedSavings;
        var scenarioEnding = result.Risk.EndingProjectedSavings;
        BaselineEnding = Money(baselineEnding);
        ScenarioEnding = Money(scenarioEnding);
        EndingDifference = Money(scenarioEnding - baselineEnding);
        AssignmentModeText = AssignmentModeLabel(
            result.Scenario[0].PaymentAssignmentMode);
        TightestPeriod = PeriodTitle(result.Risk.LowestPeriod.Start);
        LowestAvailable = Money(result.Risk.LowestAvailableAfterMandatory);
        LowestSavingsCapacity = Money(result.Risk.LowestSavingsCapacity);
        LowestProjectedSavings = Money(result.Risk.LowestProjectedSavings);
        FirstNegativePeriod =
            result.Risk.FirstDeficitPeriod is { } negative
                ? PeriodTitle(negative.Start)
                : "12 dönemlik görünümde finansman açığı oluşmuyor.";
        MaximumCarryOverDeficit = Money(
            result.Risk.MaximumCarryOverDeficit);
        RecoveryPeriod = result.Risk.RecoveryPeriod is { } recovery
            ? PeriodTitle(recovery.Start)
            : result.Risk.MaximumCarryOverDeficit > 0m
                ? "Gösterilen dönemde kapanmıyor"
                : "Gerekmedi";
        TotalScenarioCost = Money(result.Risk.TotalScenarioCost);
        var monthlyBurden = ResolveMonthlyBurden(_lastRequests, result);
        HasMonthlyBurden = monthlyBurden is not null;
        MonthlyBurden = monthlyBurden is decimal burden
            ? Money(burden)
            : string.Empty;
        HasFinancingCost = result.Risk.FinancingCost is not null;
        FinancingCost = result.Risk.FinancingCost is decimal cost
            ? Money(cost)
            : string.Empty;
        BaselineInterest = Money(
            result.BaselineInterest.TotalInterestCost);
        ScenarioInterest = Money(
            result.ScenarioInterest.TotalInterestCost);
        InterestDifferenceTitle = result.AdditionalInterestCost < 0m
            ? "Faiz Tasarrufu"
            : "Ek Faiz Yükü";
        InterestDifference = Money(
            result.AdditionalInterestCost < 0m
                ? result.InterestSaving
                : result.AdditionalInterestCost);
        InterestComparison.Clear();
        foreach (var row in SimulatorInsightService.BuildInterestComparison(
                     result.BaselineInterest,
                     result.ScenarioInterest,
                     result.Risk.FinancingCost))
        {
            InterestComparison.Add(row);
        }
        _lastBaselineProjection = result.Baseline;
        _lastScenarioProjection = result.Scenario;
        FriendlySummary = string.Join(Environment.NewLine,
            projectionSummary.NarrativeInsights);
        var transition = result.Scenario.FirstOrDefault(x =>
            x.IsStrategyTransition);
        HasStrategyTransitionSummary = transition is not null;
        StrategyTransitionSummary = transition is null
            ? string.Empty
            : string.Join(Environment.NewLine,
                $"Geçiş dönemi: {PeriodTitle(transition.PeriodStart)}",
                $"Normal zorunlu ödemeler: {Money(result.Baseline.Single(x => x.PeriodStart == transition.PeriodStart).MandatoryOutflow)}",
                $"Geçmiş düzenden kapanacak: {Money(transition.TransitionCatchUpAmount)}",
                $"İleri dönem için ayrılacak: {Money(transition.ForwardFundedAmount)}",
                $"Toplam geçiş yükü: {Money(transition.MandatoryOutflow)}",
                $"Dönem neti: {Money(transition.EstimatedSavingsCapacity)}",
                $"Dönem sonu durumu: {Money(transition.EndingProjectedSavings)}");

        NarrativeInsights.Clear();
        foreach (var insight in projectionSummary.NarrativeInsights)
        {
            NarrativeInsights.Add(insight);
        }

        SummaryMetrics.Clear();
        foreach (var metric in projectionSummary.KeyMetrics)
        {
            SummaryMetrics.Add(metric);
        }

        Results.Clear();
        foreach (var row in projectionSummary.Periods)
        {
            Results.Add(row);
        }

    }

    /// <summary>
    /// Senaryosuz sunum. Yalnızca grafiği, anlatıyı, öne çıkanları ve dönem
    /// listesini doldurur; karşılaştırmalı alanlar
    /// (<see cref="HasScenarioResults"/> ile gizlenenler) dokunulmadan bırakılır.
    /// </summary>
    private void PopulateBaseline(
        IReadOnlyList<SalaryPeriodProjection> baseline)
    {
        var projectionSummary = simulatorInsightService.Build(baseline);
        AssignmentModeText = AssignmentModeLabel(
            baseline[0].PaymentAssignmentMode);
        InterestComparison.Clear();
        HasMonthlyBurden = false;
        MonthlyBurden = string.Empty;
        HasFinancingCost = false;
        FinancingCost = string.Empty;
        HasStrategyTransitionSummary = false;
        StrategyTransitionSummary = string.Empty;
        FriendlySummary = string.Join(Environment.NewLine,
            projectionSummary.NarrativeInsights);

        NarrativeInsights.Clear();
        foreach (var insight in projectionSummary.NarrativeInsights)
        {
            NarrativeInsights.Add(insight);
        }

        SummaryMetrics.Clear();
        foreach (var metric in projectionSummary.KeyMetrics)
        {
            SummaryMetrics.Add(metric);
        }

        Results.Clear();
        foreach (var row in projectionSummary.Periods)
        {
            Results.Add(row);
        }

    }

    private static string PeriodTitle(DateOnly salaryDate) =>
        $"{salaryDate.ToString("dd MMMM yyyy", TurkishCulture)} Dönemi";

    private static string TargetPeriodText(SalaryPeriod period) =>
        period.Start.ToString("MMMM yyyy", TurkishCulture);

    private static string AssignmentModeLabel(PaymentAssignmentMode mode) =>
        mode == PaymentAssignmentMode.PreviousPeriod
            ? "Gelir kullanımı: Geçmiş dönemi kapatırım"
            : "Gelir kullanımı: Gelecek dönemi karşılarım";

    private static decimal? ResolveMonthlyBurden(
        IReadOnlyList<SimulationRequest> requests,
        SimulationResult result)
    {
        var request = requests.Count == 1 ? requests[0] : null;
        if (request is null || request.PaymentCount <= 1)
        {
            return null;
        }

        return request.Type switch
        {
            SimulationScenarioType.CreditCardInstallmentPurchase or
                SimulationScenarioType.FinancingLoan or
                SimulationScenarioType.CashDebt or
                SimulationScenarioType.RecurringPayment =>
                result.Risk.TotalScenarioCost / request.PaymentCount,
            _ => null
        };
    }

    private static string AssignmentText(SalaryPeriodProjection row)
    {
        var action = row.PaymentAssignmentMode ==
                     PaymentAssignmentMode.PreviousPeriod
            ? "ödemelerini kapatır"
            : "ödemelerini karşılar";
        return $"{row.PaymentWindowStart.ToString("dd MMM", TurkishCulture)}–" +
               $"{row.PaymentWindowEnd.ToString("dd MMM", TurkishCulture)} {action}";
    }

    private static string SignedMoney(decimal value)
    {
        var formatted = Money(value);
        return value > 0m ? $"+{formatted}" : formatted;
    }
}
