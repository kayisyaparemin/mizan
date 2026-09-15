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
    /// <summary>
    /// Koşul formu Finansal Yapı ile ortaktır; tür seçimi ve istek üretimi
    /// orada durur. Bu ekran koşulları listeler, hesaplar ve uygular.
    /// </summary>
    public ScenarioConditionForm Form { get; } = new(directEntryOnly: false);

    public ObservableCollection<LoanImpactLine> LoanImpacts { get; } = [];

    public ObservableCollection<SimulationDraftConditionView>
        DraftConditions { get; } = [];
    public ObservableCollection<SimulatorPeriodView> Results { get; } = [];
    public ObservableCollection<string> NarrativeInsights { get; } = [];
    public ObservableCollection<SimulatorSummaryMetric> SummaryMetrics { get; } = [];
    public ObservableCollection<SimulatorInterestRow> InterestComparison
    { get; } = [];

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

    [ObservableProperty] private bool hasLoanImpacts;
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
    [ObservableProperty] private string assignmentModeText = string.Empty;
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
            await RefreshSavedDraftsAsync();
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
                Form.ClearLookups();
                DraftConditions.Clear();
                NotifyDraftChanged();
                Results.Clear();
                ClearTargetResult();
                IsBaselineOnly = false;
                _lastScenarioProjection = [];
                return;
            }

            var overview = await service.GetPaymentAssignmentStrategyOverviewAsync();
            Form.SetLookups(plan);
            var currentMode = overview.Current?.Mode ??
                              throw new InvalidOperationException(
                                  "Gelir kullanım düzeni bulunamadı.");
            AssignmentModeText = AssignmentModeLabel(currentMode);
            Form.SetStrategyLookups(
                overview.AvailableEffectiveSalaryDates,
                currentMode);
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

    /// <summary>
    /// Kaydedilmiş geçici planlar. Uygulama kapanınca kaybolan tek şey
    /// ekrandaki taslaktı; kullanıcı bir denemeyi adlandırıp saklayabiliyor
    /// ve istediğinde simülatöre geri yükleyebiliyor.
    /// </summary>
    public ObservableCollection<SavedSimulationDraftView> SavedDrafts
    { get; } = [];

    [ObservableProperty] private string draftName = string.Empty;
    [ObservableProperty] private bool hasSavedDrafts;

    [RelayCommand]
    private async Task SaveDraftAsync()
    {
        try
        {
            SetStatus(string.Empty);
            if (DraftConditions.Count == 0)
            {
                SetStatus("Kaydedilecek koşul yok. Önce bir koşul ekle.");
                return;
            }

            // Ad boşsa kullanıcıyı boş bir alana bakakalmakla bırakmayalım:
            // ilk koşulun özeti zaten planın ne olduğunu söylüyor.
            var name = string.IsNullOrWhiteSpace(DraftName)
                ? DraftConditions[0].SummaryText
                : DraftName.Trim();
            var existing = SavedDrafts.FirstOrDefault(x =>
                string.Equals(
                    x.Name,
                    name,
                    StringComparison.CurrentCultureIgnoreCase));
            if (existing is not null &&
                !await feedback.ConfirmAsync(
                    "Üzerine yazılsın mı?",
                    $"\"{existing.Name}\" adında kayıtlı bir geçici plan var. Üzerine yazılsın mı?",
                    "Üzerine Yaz",
                    "Vazgeç"))
            {
                return;
            }

            // Açık/kapalı durumu da kaydedilir: bilerek kapatılmış bir koşul
            // geri yüklendiğinde kapalı gelmeli.
            await service.SaveSimulationDraftAsync(
                name,
                DraftConditions
                    .Select(x => new SimulationDraftCondition(
                        x.Request,
                        x.IsEnabled))
                    .ToArray(),
                existing?.Id);
            DraftName = string.Empty;
            await RefreshSavedDraftsAsync();
            SetStatus($"\"{name}\" geçici planı kaydedildi.");
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(
                exception,
                "Geçici plan kaydedilemedi."));
        }
    }

    /// <summary>
    /// Kaydedilmiş plan ekrandakinin yerine geçer; ikisini birleştirmek
    /// "hangi koşullar bu planındı" sorusunu cevapsız bırakırdı. Ekranda
    /// koşul varsa önce onay istenir.
    /// </summary>
    [RelayCommand]
    private async Task LoadDraftAsync(SavedSimulationDraftView? draft)
    {
        if (draft is null)
        {
            return;
        }

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
            SetStatus(
                $"\"{draft.Name}\" yüklendi. Sonucu görmek için Simülasyonu Yap.");
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(exception));
        }
    }

    [RelayCommand]
    private async Task DeleteDraftAsync(SavedSimulationDraftView? draft)
    {
        if (draft is null)
        {
            return;
        }

        try
        {
            if (!await feedback.ConfirmAsync(
                    "Geçici planı sil",
                    $"\"{draft.Name}\" silinecek. Finansal kayıtların etkilenmez.",
                    "Sil",
                    "Vazgeç"))
            {
                return;
            }

            await service.DeleteSimulationDraftAsync(draft.Id);
            await RefreshSavedDraftsAsync();
            SetStatus($"\"{draft.Name}\" silindi.");
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(exception));
        }
    }

    private async Task RefreshSavedDraftsAsync()
    {
        var drafts = await service.GetSimulationDraftsAsync();
        SavedDrafts.Clear();
        foreach (var draft in drafts)
        {
            SavedDrafts.Add(new SavedSimulationDraftView(
                draft.Id,
                draft.Name,
                SavedDraftSummary(draft),
                draft.Conditions));
        }

        HasSavedDrafts = SavedDrafts.Count > 0;
    }

    private static string SavedDraftSummary(SimulationDraft draft)
    {
        var closed = draft.Conditions.Count - draft.EnabledConditionCount;
        var closedText = closed > 0 ? $" · {closed} kapalı" : string.Empty;
        // Kültür açıkça verilir: varsayılan kültürde ay adı İngilizce çıkıyor.
        var updated = draft.UpdatedAt
            .ToLocalTime()
            .ToString("dd MMMM yyyy", TurkishCulture);
        return $"{draft.Conditions.Count} koşul{closedText} · {updated}";
    }

    [RelayCommand]
    private void AddCondition()
    {
        try
        {
            SetStatus(string.Empty);
            var request = Form.BuildRequest(
                _editingConditionId ?? Guid.NewGuid());
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

    /// <summary>
    /// 12 Dönem'in önerisini simülatöre koşul olarak ekler ve hesaplar. Aynı
    /// kredi için taslakta bir kapama koşulu varsa onun yerine geçer; diğer
    /// koşullara dokunmaz.
    /// </summary>
    public async Task TryLoanClosureAsync(Guid loanId, DateOnly date)
    {
        if (!IsPlanAvailable ||
            Form.Loans.FirstOrDefault(x => x.Value == loanId) is not { } loan)
        {
            return;
        }

        try
        {
            ResetConditionForm();
            Form.SelectOption(SimulationScenarioCatalog.LoanPrepayment);
            Form.SelectedPrepaymentMode = Form.PrepaymentModes.First(x =>
                x.Value == LoanPrepaymentMode.FullClosure);
            Form.SelectedLoan = loan;
            Form.StartDate = date.ToDateTime(TimeOnly.MinValue);
            Form.Name = $"{loan.Label} erken kapama";
            var request = Form.BuildRequest(Guid.NewGuid());
            foreach (var existing in DraftConditions
                         .Where(x => x.Request.Type ==
                                     SimulationScenarioType.LoanEarlyClosure &&
                                     x.Request.LoanId == loanId)
                         .ToArray())
            {
                DraftConditions.Remove(existing);
            }

            DraftConditions.Add(CreateConditionView(request));
            ResetConditionForm();
            MarkResultsStale();
            NotifyDraftChanged();
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(exception));
            return;
        }

        await CalculateAsync();
    }

    [RelayCommand]
    private void EditCondition(SimulationDraftConditionView? condition)
    {
        if (condition is null)
        {
            return;
        }

        _editingConditionId = condition.Id;
        Form.Load(condition.Request);
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
            Form.EndEditing();
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
        Form.EndEditing();
        ClearResults();
        NotifyDraftChanged();
        OnPropertyChanged(nameof(IsEditingCondition));
        OnPropertyChanged(nameof(AddConditionButtonText));
        SetStatus("Simülasyon planı temizlendi.");
    }

    /// <summary>
    /// Ekrandaki sonucu ve ona bağlı her şeyi sıfırlar. Koşul listesine
    /// dokunmaz: plan başka bir planla değiştirildiğinde koşullar yeniden
    /// dolduruluyor, sonuç ise eski hesabın kalıntısı olurdu.
    /// </summary>
    private void ClearResults()
    {
        Results.Clear();
        LoanImpacts.Clear();
        HasLoanImpacts = false;
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
            LoanImpacts.Clear();
            HasLoanImpacts = false;
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
            SimulationScenarioType.CreditCardPaymentMode or
            SimulationScenarioType.LoanEarlyClosure
                ? request.Name.Trim()
                : $"{Money(request.Amount)} {request.Name.Trim()}";
        var detail = request.Type switch
        {
            SimulationScenarioType.CreditCardInstallmentPurchase =>
                $"Kart: {Form.CardLabel(request.CreditCardId)}\n{request.PaymentCount} taksit\nİşlem: {LongDate(request.StartDate)}",
            SimulationScenarioType.CreditCardSinglePayment =>
                $"Kart: {Form.CardLabel(request.CreditCardId)}\nİşlem: {LongDate(request.StartDate)}",
            SimulationScenarioType.CreditCardPaymentMode =>
                $"Kart: {Form.CardLabel(request.CreditCardId)}\n{CardModeLabel(request.CardPaymentType)} · {CardScopeLabel(request.AppliesToAllStatements)}\n{LongDate(request.StartDate)}",
            SimulationScenarioType.FinancingLoan =>
                $"{request.PaymentCount} taksit • toplam {Money(request.TotalRepaymentAmount.GetValueOrDefault())}\nİlk ödeme: {LongDate(request.FirstPaymentDate)}",
            SimulationScenarioType.CashDebt or
                SimulationScenarioType.RecurringPayment =>
                $"{request.PaymentCount} ödeme\nİlk ödeme: {LongDate(request.FirstPaymentDate)}",
            SimulationScenarioType.PaymentStrategyChange =>
                $"Başlangıç dönemi: {LongDate(request.EffectiveSalaryDate)}",
            SimulationScenarioType.LoanEarlyClosure =>
                $"Kredi: {Form.LoanLabel(request.LoanId)}\nKapatma: {LongDate(request.StartDate)}\nTutar o günkü kalan anapara ve işleyen faizden hesaplanır.",
            SimulationScenarioType.LoanPartialPrepayment =>
                $"Kredi: {Form.LoanLabel(request.LoanId)}\nAnaparadan düşecek: {Money(request.Amount)} · {PrepaymentModeLabel(request.PrepaymentMode)}\nTarih: {LongDate(request.StartDate)}",
            _ => $"Tarih: {LongDate(request.StartDate)}"
        };
        return $"Bu plan gerçek finans planına eklenecek.\n\n{summary}\n{detail}";
    }

    // Kültür açıkça verilir: varsayılan kültürde ay adı İngilizce çıkıyor.
    // Tarih yoksa eski interpolasyon gibi boş metin döner.
    private static string LongDate(DateOnly? date) =>
        date?.ToString("dd MMMM yyyy", TurkishCulture) ?? string.Empty;

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
            SimulationScenarioCatalog.TypeText(request.Type),
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
                $"{amount} • Nakit ödeme",
            SimulationScenarioType.CreditCardSinglePayment =>
                $"{Form.CardLabel(request.CreditCardId)} • {amount} • Tek çekim",
            SimulationScenarioType.CreditCardInstallmentPurchase =>
                $"{Form.CardLabel(request.CreditCardId)} • {amount} • {request.PaymentCount} taksit",
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
                $"{StrategyModeLabel(request.NewPaymentAssignmentMode)} • {LongDate(request.EffectiveSalaryDate)} dönemi",
            SimulationScenarioType.CreditCardPaymentMode =>
                $"{Form.CardLabel(request.CreditCardId)} • {CardModeLabel(request.CardPaymentType)}",
            SimulationScenarioType.LoanEarlyClosure =>
                $"{Form.LoanLabel(request.LoanId)} • Erken kapama",
            SimulationScenarioType.LoanPartialPrepayment =>
                $"{Form.LoanLabel(request.LoanId)} • {amount} ara ödeme • {PrepaymentModeLabel(request.PrepaymentMode)}",
            _ => request.Name
        };
    }

    private static string PrepaymentModeLabel(LoanPrepaymentMode? mode) =>
        mode == LoanPrepaymentMode.ReduceInstallment
            ? "taksit azalır"
            : "vade kısalır";

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

    private void ResetConditionForm()
    {
        _editingConditionId = null;
        Form.Reset();
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
        AssignmentModeText = AssignmentModeLabel(
            result.Scenario[0].PaymentAssignmentMode);
        InterestComparison.Clear();
        foreach (var row in SimulatorInsightService.BuildInterestComparison(
                     result.BaselineInterest,
                     result.ScenarioInterest,
                     result.Risk.FinancingCost))
        {
            InterestComparison.Add(row);
        }
        PopulateLoanImpacts(result.LoanImpacts);
        _lastBaselineProjection = result.Baseline;
        _lastScenarioProjection = result.Scenario;

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
        LoanImpacts.Clear();
        HasLoanImpacts = false;
        InterestComparison.Clear();

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
    /// K8 — kredi faizi tasarrufu 12 dönemlik faiz tablosuna katılmaz; o
    /// tablo kart ve açık faizidir. Tasarruf kredinin ömrü boyuncadır, ayrı
    /// satırda gösterilir.
    /// </summary>
    private void PopulateLoanImpacts(
        IReadOnlyList<LoanPrepaymentImpact> impacts)
    {
        LoanImpacts.Clear();
        foreach (var impact in impacts)
        {
            var end = impact.ScenarioEndDate is { } scenarioEnd
                ? scenarioEnd.ToString("MMMM yyyy", TurkishCulture)
                : "—";
            var baselineEnd = impact.BaselineEndDate is { } baseline
                ? baseline.ToString("MMMM yyyy", TurkishCulture)
                : "—";
            var installment = impact.ScenarioMonthlyPayment is decimal payment &&
                              payment != impact.BaselineMonthlyPayment
                ? $" · taksit {Money(impact.BaselineMonthlyPayment)} → {Money(payment)}"
                : string.Empty;
            LoanImpacts.Add(new LoanImpactLine(
                impact.LoanName,
                $"Ödenecek {Money(impact.PrepaidAmount)} · son ödeme {baselineEnd} → {end}{installment}",
                Money(impact.InterestSaving)));
        }

        HasLoanImpacts = LoanImpacts.Count > 0;
    }

    private static string TargetPeriodText(SalaryPeriod period) =>
        period.Start.ToString("MMMM yyyy", TurkishCulture);

    private static string AssignmentModeLabel(PaymentAssignmentMode mode) =>
        mode == PaymentAssignmentMode.PreviousPeriod
            ? "Gelir kullanımı: Geçmiş dönemi kapatırım"
            : "Gelir kullanımı: Gelecek dönemi karşılarım";

}
