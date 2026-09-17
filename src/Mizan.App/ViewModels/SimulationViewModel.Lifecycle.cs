using Mizan.App.Services;

namespace Mizan.App.ViewModels;

public partial class SimulationViewModel
{
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

            // Tarih seçenekleri ve güncel düzen plandan tahmin edilmez:
            // maaş kayıtlarının geçerlilik tarihleri dönem başlangıcı değildir.
            var overview = await service.GetCashFlowAllocationStrategyOverviewAsync();
            Form.SetLookups(plan);
            var currentMode = overview.Current?.Mode ??
                              throw new InvalidOperationException(
                                  "Gelir kullanım düzeni bulunamadı.");
            AssignmentModeText = AssignmentModeLabel(currentMode);
            Form.SetStrategyLookups(
                overview.AvailableEffectivePeriodDates,
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

    public void PreserveStateOnNextAppearance() =>
        _preserveOnNextAppearance = true;

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
