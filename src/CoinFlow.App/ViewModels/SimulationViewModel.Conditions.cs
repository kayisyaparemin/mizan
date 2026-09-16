using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoinFlow.App.Models;
using CoinFlow.App.Services;
using CoinFlow.Application.Models;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.App.ViewModels;

public partial class SimulationViewModel
{
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

    private void QueueLiveRecalculation()
    {
        if (!HasResults)
        {
            return;
        }

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

    public async Task InitializeAsync()
    {
        if (_preserveOnNextAppearance)
        {
            _preserveOnNextAppearance = false;
            return;
        }

        try
        {
            var plan = await service.GetFinancialPlanAsync();
            _projectionAnchorDate = plan.Settings.ProjectionAnchorDate == default
                ? null
                : plan.Settings.ProjectionAnchorDate;
            IsPlanAvailable =
                plan.Salaries.Count > 0 &&
                plan.PaymentAssignmentStrategies.Count > 0 &&
                plan.Settings.ProjectionAnchorDate != default;
            IsPlanUnavailable = !IsPlanAvailable;
            Form.LoadOptions(plan);
            await RefreshSavedDraftsAsync();
            NotifyDraftChanged();
        }
        catch (Exception exception)
        {
            IsPlanAvailable = false;
            IsPlanUnavailable = true;
            SetStatus(UserFacingMessages.FromException(exception));
        }
    }

    public void PreserveStateOnNextAppearance() =>
        _preserveOnNextAppearance = true;

    public Task LoadAsync() => InitializeAsync();

    public bool ConsumeDetailReturn()
    {
        if (!_preserveOnNextAppearance)
        {
            return false;
        }

        _preserveOnNextAppearance = false;
        return true;
    }
}
