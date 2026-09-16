using System.Collections.ObjectModel;
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
    public ScenarioConditionForm Form { get; } = new(directEntryOnly: false);

    public ObservableCollection<LoanImpactLine> LoanImpacts { get; } = [];
    public ObservableCollection<SimulationDraftConditionView> DraftConditions { get; } = [];
    public ObservableCollection<SimulatorPeriodView> Results { get; } = [];
    public ObservableCollection<string> NarrativeInsights { get; } = [];
    public ObservableCollection<SimulatorSummaryMetric> SummaryMetrics { get; } = [];
    public ObservableCollection<SimulatorInterestRow> InterestComparison { get; } = [];
    public ObservableCollection<SavedSimulationDraftView> SavedDrafts { get; } = [];

    private IReadOnlyList<SimulationRequest> _lastRequests = [];
    private IReadOnlyList<SalaryPeriodProjection> _lastScenarioProjection = [];
    private IReadOnlyList<SalaryPeriodProjection> _lastBaselineProjection = [];
    private Guid? _editingConditionId;
    private readonly SemaphoreSlim _applyLock = new(1, 1);
    private readonly SemaphoreSlim _calculationLock = new(1, 1);
    private CancellationTokenSource? _liveRecalculation;
    private static readonly TimeSpan LiveRecalculationDelay = TimeSpan.FromMilliseconds(200);

    private bool _preserveOnNextAppearance;
    private DateOnly? _projectionAnchorDate;

    [ObservableProperty] private bool hasLoanImpacts;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanApplyPlan), nameof(HasCurrentResults), nameof(HasScenarioResults), nameof(HasStaleResult))]
    private bool hasResults;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanApplyPlan), nameof(HasCurrentResults), nameof(HasScenarioResults), nameof(HasStaleResult), nameof(RunSimulationButtonText))]
    private bool isResultStale;
    [ObservableProperty] private string assignmentModeText = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanRunSimulation), nameof(HasNoDraftConditions))]
    private bool isPlanAvailable;
    [ObservableProperty] private bool isPlanUnavailable = true;
    [ObservableProperty] private string emptyStateMessage =
        "Simülasyon yapabilmek için önce temel finans planını oluştur.";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanApplyPlan))]
    private bool isApplyingPlan;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanApplyPlan))]
    private bool isPlanApplied;
    [ObservableProperty] private string applyButtonText = "Planı Uygula";
    [ObservableProperty] private string applyConfirmationText =
        "Bu plan gerçek finans planına eklenecek.";
    [ObservableProperty] private string targetAmount = string.Empty;
    [ObservableProperty] private string targetResult = string.Empty;
    [ObservableProperty] private bool hasTargetResult;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasScenarioResults), nameof(CanApplyPlan))]
    private bool isBaselineOnly;
    [ObservableProperty] private string draftName = string.Empty;
    [ObservableProperty] private bool hasSavedDrafts;
    [ObservableProperty] private SimulationApplyResult? lastApplyResult;

    public bool HasDraftConditions => DraftConditions.Count > 0;
    public bool HasNoDraftConditions => IsPlanAvailable && !HasDraftConditions;
    public bool HasMultipleDraftConditions => DraftConditions.Count > 1;
    public bool CanRunSimulation => IsPlanAvailable && HasDraftConditions;
    public bool IsEditingCondition => _editingConditionId is not null;
    public string AddConditionButtonText => IsEditingCondition ? "Değişikliği Kaydet" : "Koşulu Ekle";
    public string RunSimulationButtonText => IsResultStale ? "Simülasyonu Güncelle" : "Simülasyonu Yap";
    public bool HasCurrentResults => HasResults && !IsResultStale;
    public bool HasStaleResult => HasResults && IsResultStale;
    public bool HasScenarioResults => HasResults && !IsBaselineOnly;
    public bool CanApplyPlan =>
        HasResults &&
        !IsResultStale &&
        !IsPlanApplied &&
        !IsApplyingPlan &&
        !IsBaselineOnly &&
        EnabledRequests().Count > 0;

    public string DraftConditionCountText => DraftConditions.Count == 0
        ? string.Empty
        : EnabledConditions().Count == DraftConditions.Count
            ? $"{DraftConditions.Count} koşul"
            : $"{EnabledConditions().Count}/{DraftConditions.Count} koşul aktif";

    private IReadOnlyList<SimulationRequest> EnabledRequests() =>
        DraftConditions.Where(x => x.IsEnabled)
            .Select(x => x.Request)
            .ToArray();

    private IReadOnlyList<SimulationDraftConditionView> EnabledConditions() =>
        DraftConditions.Where(x => x.IsEnabled).ToArray();

    [RelayCommand]
    private async Task CalculateAsync()
    {
        if (IsBusy) return;
        try { IsBusy = true; await RunCalculationAsync(showErrorDialog: true); }
        finally { IsBusy = false; }
    }

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
        }
        catch (Exception exception)
        {
            if (!HasResults) HasResults = false;
            var message = UserFacingMessages.FromException(
                exception,
                "Simülasyon hesaplanırken bir sorun oluştu. Tekrar deneyebilirsin.");
            SetStatus(message);
            if (showErrorDialog)
            {
                await feedback.ShowErrorAsync(message, title: "Hesaplanamadı");
            }
        }
        finally
        {
            _calculationLock.Release();
        }
    }

    private async Task PopulateBaselineOnlyAsync(
        CancellationToken cancellationToken)
    {
        var baseline = await service.GetFuturePeriodsAsync(
            cancellationToken: cancellationToken);
        if (baseline.Count == 0)
        {
            SetStatus("Temel projeksiyon hesaplanamadı.");
            return;
        }

        _lastScenarioProjection = [];
        _lastBaselineProjection = baseline;
        _lastRequests = [];
        IsBaselineOnly = true;
        IsPlanApplied = false;
        ApplyButtonText = "Planı Uygula";
        LastApplyResult = null;
        ApplyConfirmationText = string.Empty;
        PopulateBaseline(baseline);
        HasResults = true;
        IsResultStale = false;
        RefreshTargetResultAfterSimulation();
    }

    [RelayCommand]
    public async Task<SimulationApplyResult?> ApplyLastPlanAsync()
    {
        if (!CanApplyPlan) return null;
        if (!await _applyLock.WaitAsync(0)) return null;

        try
        {
            IsApplyingPlan = true;
            var requests = _lastRequests;
            if (requests.Count == 0)
            {
                SetStatus("Uygulanacak geçerli bir simülasyon planı bulunamadı.");
                return null;
            }

            var plan = await service.ApplySimulationAsync(
                _lastRequests,
                confirmed: true);

            IsPlanApplied = true;
            ApplyButtonText = "Plan Uygulandı ✓";
            LastApplyResult = plan;

            foreach (var applied in EnabledConditions())
            {
                DraftConditions.Remove(applied);
            }

            ClearResults();
            NotifyDraftChanged();
            SetStatus(plan.Message);
            return plan;
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

    [RelayCommand]
    private async Task SaveDraftAsync()
    {
        if (DraftConditions.Count == 0)
        {
            SetStatus("Kaydedilecek en az bir koşul gerekiyor.");
            return;
        }

        try
        {
            SetStatus(string.Empty);
            var conditions = DraftConditions
                .Select(
                    x => new SimulationDraftCondition(
                        x.Request,
                        x.IsEnabled))
                .ToArray();
            var draft = await service.SaveSimulationDraftAsync(
                DraftName,
                conditions);
            DraftName = draft.Name;
            await RefreshSavedDraftsAsync();
            SetStatus($"\"{draft.Name}\" geçici planı kaydedildi.");
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(exception));
        }
    }

    [RelayCommand]
    private async Task LoadDraftAsync(SavedSimulationDraftView? draft)
    {
        if (draft is null) return;

        try
        {
            if (DraftConditions.Count > 0 &&
                !await feedback.ConfirmAsync(
                    "Plan değiştirilsin mi?",
                    $"Ekrandaki {DraftConditions.Count} koşul kaldırılıp \"{draft.Name}\" yüklenecek.",
                    "Yükle",
                    "Vazgeç"))
            {
                return;
            }

            DraftConditions.Clear();
            foreach (var condition in draft.Conditions)
            {
                DraftConditions.Add(CreateConditionView(
                    condition.Request,
                    condition.IsEnabled));
            }

            _editingConditionId = null;
            Form.EndEditing();
            DraftName = draft.Name;
            ClearResults();
            NotifyDraftChanged();
            OnPropertyChanged(nameof(IsEditingCondition));
            OnPropertyChanged(nameof(AddConditionButtonText));
            SetStatus($"\"{draft.Name}\" yüklendi. Sonucu görmek için Simülasyonu Yap.");
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(exception));
        }
    }

    private static string SavedDraftSummary(SimulationDraft draft)
    {
        var closed = draft.Conditions.Count - draft.EnabledConditionCount;
        var closedText = closed > 0 ? $" · {closed} kapalı" : string.Empty;
        var updated = draft.UpdatedAt
            .ToLocalTime()
            .ToString("dd MMMM yyyy", TurkishCulture);
        return $"{draft.Conditions.Count} koşul{closedText} · {updated}";
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
            SimulationScenarioCatalog.TypeText(request.Type),
            ConditionSummaryText(request))
        {
            IsEnabled = isEnabled
        };
        condition.PropertyChanged += OnDraftConditionPropertyChanged;
        return condition;
    }
}
