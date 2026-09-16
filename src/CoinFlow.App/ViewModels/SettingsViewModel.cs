using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoinFlow.App.Models;
using CoinFlow.App.Services;
using CoinFlow.Application.Models;
using CoinFlow.Application.Services;
using CoinFlow.Domain.Models;

namespace CoinFlow.App.ViewModels;

public partial class SettingsViewModel(
    CoinFlowService service,
    BackupService backup,
    IUserFeedbackService feedback,
    INavigationService navigation) : ViewModelBase
{
    private DateOnly _projectionAnchorDate;
    private decimal _projectionStartingSavings;
    private PaymentAssignmentStrategy? _pendingStrategy;
    private bool _settingsLoaded;
    private bool _isUpdatingSettingsForm;
    private SettingsFormSnapshot _savedSettingsSnapshot = SettingsFormSnapshot.Empty;

    public ObservableCollection<StrategyHistoryLine> StrategyHistory { get; } = [];
    public IReadOnlyList<SelectionOption<PaymentAssignmentMode>> StrategyModes { get; } =
    [
        new("Geçmiş dönemi kapatırım", PaymentAssignmentMode.PreviousPeriod),
        new("Gelecek dönemi karşılarım", PaymentAssignmentMode.UpcomingPeriod)
    ];
    public ObservableCollection<SelectionOption<DateOnly>> EffectiveSalaryDates { get; } = [];

    [ObservableProperty] private string salaryDay = "10";
    [ObservableProperty] private string monthlyLivingBudget = "0";
    [ObservableProperty] private string creditCardCarryInterestRate = "5";
    [ObservableProperty] private string deficitFinancingInterestRate = "5";
    [ObservableProperty] private string currentStrategyText = "Henüz seçilmedi";
    [ObservableProperty] private string currentStrategySinceText = string.Empty;
    [ObservableProperty] private string pendingStrategyText = string.Empty;
    [ObservableProperty] private bool hasPendingStrategy;
    [ObservableProperty] private bool canManageStrategy;
    [ObservableProperty] private bool hasNoStrategy = true;
    [ObservableProperty] private SelectionOption<PaymentAssignmentMode>? selectedStrategyMode;
    [ObservableProperty] private SelectionOption<DateOnly>? selectedEffectiveSalary;
    [ObservableProperty] private string strategyNote = string.Empty;
    [ObservableProperty] private string previewText = string.Empty;
    [ObservableProperty] private bool hasPreview;
    [ObservableProperty] private bool isSettingsDirty;
    [ObservableProperty] private string lastBackupText = string.Empty;
    [ObservableProperty] private bool hasBackupAccess;

    public string BackupDescription =>
        $"Her gece bütün profiller {backup.LocationDescription} klasörüne yedeklenir; " +
        "o gün hiçbir şey değişmediyse yeni dosya yazılmaz. Son 7 yedek saklanır. " +
        "Uygulamayı kaldırıp yeniden kurarsan ilk açılışta yedekten geri yükleyebilirsin.";

    public bool NeedsBackupAccess => !HasBackupAccess;

    partial void OnHasBackupAccessChanged(bool value) => OnPropertyChanged(nameof(NeedsBackupAccess));
    partial void OnSalaryDayChanged(string value) => RefreshSettingsDirtyState();
    partial void OnMonthlyLivingBudgetChanged(string value) => RefreshSettingsDirtyState();
    partial void OnCreditCardCarryInterestRateChanged(string value) => RefreshSettingsDirtyState();
    partial void OnDeficitFinancingInterestRateChanged(string value) => RefreshSettingsDirtyState();
    partial void OnIsSettingsDirtyChanged(bool value) => SaveCommand.NotifyCanExecuteChanged();

    public bool IsDevelopment => BuildInfo.IsDevelopment;
    public string BuildChannel => BuildInfo.Channel;
    public string VersionText => $"Sürüm {BuildInfo.Version}";
    public string CommitText => $"Commit {BuildInfo.Commit}";
    public string BuildText => $"Build #{BuildInfo.BuildNumber}";
    public bool CanSaveSettings => IsSettingsDirty && !IsBusy;

    public async Task LoadAsync()
    {
        await LoadBackupStateAsync();
        var plan = await service.GetFinancialPlanAsync();
        var settings = plan.Settings;
        var overview = await service.GetPaymentAssignmentStrategyOverviewAsync();
        ApplySettingsToForm(settings);

        CanManageStrategy = overview.Current is not null;
        HasNoStrategy = !CanManageStrategy;
        CurrentStrategyText = overview.Current is null ? "Henüz seçilmedi" : ModeText(overview.Current.Mode);
        CurrentStrategySinceText = overview.Current is null
            ? plan.Salaries.Count == 0
                ? "İlk gelirini eklediğinde kullanım düzenini seçersin."
                : "Gelir kullanım düzenini seçerek 12 dönemlik planı tamamla."
            : overview.Current.EffectiveFromSalaryDate > DateOnly.FromDateTime(DateTime.Today)
                ? $"{overview.Current.EffectiveFromSalaryDate.ToString("dd MMMM yyyy", TurkishCulture)} döneminden itibaren"
                : $"{overview.Current.EffectiveFromSalaryDate.ToString("dd MMMM yyyy", TurkishCulture)} döneminden beri";
        _pendingStrategy = overview.Pending;
        HasPendingStrategy = overview.Pending is not null;
        PendingStrategyText = overview.Pending is null
            ? string.Empty
            : $"{overview.Pending.EffectiveFromSalaryDate.ToString("dd MMMM yyyy", TurkishCulture)} döneminden itibaren {ModeText(overview.Pending.Mode)}";

        StrategyHistory.Clear();
        foreach (var strategy in overview.History.OrderByDescending(x => x.EffectiveFromSalaryDate))
        {
            StrategyHistory.Add(new StrategyHistoryLine(
                strategy.Id,
                strategy.EffectiveFromSalaryDate.ToString("dd MMMM yyyy", TurkishCulture),
                ModeText(strategy.Mode),
                strategy.Note,
                strategy.EffectiveFromSalaryDate > DateOnly.FromDateTime(DateTime.Today)));
        }

        EffectiveSalaryDates.Clear();
        foreach (var date in overview.AvailableEffectiveSalaryDates)
        {
            EffectiveSalaryDates.Add(new SelectionOption<DateOnly>(
                $"{date.ToString("dd MMMM yyyy", TurkishCulture)} dönemi",
                date));
        }

        var defaultMode = overview.Pending?.Mode ??
                          (overview.Current is null
                              ? PaymentAssignmentMode.UpcomingPeriod
                              : Opposite(overview.Current.Mode));
        SelectedStrategyMode = StrategyModes.First(x => x.Value == defaultMode);
        SelectedEffectiveSalary = overview.Pending is null
            ? EffectiveSalaryDates.FirstOrDefault()
            : EffectiveSalaryDates.FirstOrDefault(x => x.Value == overview.Pending.EffectiveFromSalaryDate) ??
              EffectiveSalaryDates.FirstOrDefault();
        StrategyNote = overview.Pending?.Note ?? "Planlanan düzen değişikliği";
        HasPreview = false;
    }

    [RelayCommand(CanExecute = nameof(CanSaveSettings))]
    private async Task SaveAsync()
    {
        UserSettings settings;
        try
        {
            settings = BuildSettingsFromForm();
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(exception));
            return;
        }

        try
        {
            IsBusy = true;
            SaveCommand.NotifyCanExecuteChanged();
            await service.SaveSettingsAsync(settings);
            ApplySettingsToForm(settings);
            SetStatus(string.Empty);
            await feedback.ShowSuccessAsync("Ayarların kaydedildi.");
        }
        catch (Exception exception)
        {
            var message = UserFacingMessages.FromException(exception);
            SetStatus(message);
            await feedback.ShowErrorAsync(message);
        }
        finally
        {
            IsBusy = false;
            SaveCommand.NotifyCanExecuteChanged();
        }
    }

    private UserSettings BuildSettingsFromForm()
    {
        if (!int.TryParse(SalaryDay, out var day) || day is < 1 or > 31)
        {
            throw new InvalidOperationException("Dönem günü 1 ile 31 arasında olmalıdır.");
        }

        return new UserSettings
        {
            SalaryDay = day,
            MonthlyLivingBudget = ParseMoney(MonthlyLivingBudget, "Aylık tahmini yaşam bütçesi"),
            ProjectionStartingSavings = _projectionStartingSavings,
            ProjectionAnchorDate = _projectionAnchorDate,
            CreditCardCarryInterestRate = ParseRate(CreditCardCarryInterestRate, "Kredi kartı devreden borç faizi"),
            DeficitFinancingInterestRate = ParseRate(DeficitFinancingInterestRate, "Finansman açığı faizi")
        };
    }

    private void ApplySettingsToForm(UserSettings settings)
    {
        _isUpdatingSettingsForm = true;
        _projectionAnchorDate = settings.ProjectionAnchorDate;
        SalaryDay = settings.SalaryDay.ToString(TurkishCulture);
        MonthlyLivingBudget = settings.MonthlyLivingBudget.ToString("N2", TurkishCulture);
        _projectionStartingSavings = settings.ProjectionStartingSavings;
        CreditCardCarryInterestRate = (settings.CreditCardCarryInterestRate * 100m).ToString("N2", TurkishCulture);
        DeficitFinancingInterestRate = (settings.DeficitFinancingInterestRate * 100m).ToString("N2", TurkishCulture);
        _isUpdatingSettingsForm = false;

        _savedSettingsSnapshot = CaptureSettingsSnapshot();
        _settingsLoaded = true;
        IsSettingsDirty = false;
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void RefreshSettingsDirtyState()
    {
        if (_isUpdatingSettingsForm || !_settingsLoaded) return;
        IsSettingsDirty = CaptureSettingsSnapshot() != _savedSettingsSnapshot;
        SaveCommand.NotifyCanExecuteChanged();
    }

    private static decimal ParseRate(string value, string field)
    {
        var percentage = ParseMoney(value, field);
        if (percentage is < 0m or > 100m)
        {
            throw new InvalidOperationException($"{field} %0 ile %100 arasında olmalıdır.");
        }

        return percentage / 100m;
    }

    private sealed record SettingsFormSnapshot(
        string SalaryDay,
        string MonthlyLivingBudget,
        string CreditCardCarryInterestRate,
        string DeficitFinancingInterestRate)
    {
        public static SettingsFormSnapshot Empty { get; } =
            new(string.Empty, string.Empty, string.Empty, string.Empty);
    }

    private SettingsFormSnapshot CaptureSettingsSnapshot() =>
        new(
            SalaryDay.Trim(),
            MonthlyLivingBudget.Trim(),
            CreditCardCarryInterestRate.Trim(),
            DeficitFinancingInterestRate.Trim());
}
