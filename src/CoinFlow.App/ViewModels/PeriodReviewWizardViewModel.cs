using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoinFlow.App.Models;
using CoinFlow.App.Services;
using CoinFlow.Application.Models;
using CoinFlow.Application.Services;
using CoinFlow.Domain.Models;

namespace CoinFlow.App.ViewModels;

public partial class PeriodReviewWizardViewModel(
    CoinFlowService service,
    IUserFeedbackService feedback) : ViewModelBase
{
    private PeriodReviewContext? _context;
    private decimal _lastSuggestedSavings;
    private bool _loaded;
    private decimal _plannedLiving;
    private decimal _plannedInterest;

    public ObservableCollection<ActualPaymentInputItem> Payments { get; } = [];
    public ObservableCollection<ActualFlowInputItem> Flows { get; } = [];
    public ObservableCollection<ComparisonUiLine> ComparisonLines { get; } = [];

    [ObservableProperty] private int currentStep = 1;
    [ObservableProperty] private bool isSuccess;
    [ObservableProperty] private string periodText = string.Empty;
    [ObservableProperty] private string plannedIncome = string.Empty;
    [ObservableProperty] private string plannedLoans = string.Empty;
    [ObservableProperty] private string plannedCards = string.Empty;
    [ObservableProperty] private string plannedTemporary = string.Empty;
    [ObservableProperty] private string plannedInstallments = string.Empty;
    [ObservableProperty] private string plannedOther = string.Empty;
    [ObservableProperty] private string plannedLarge = string.Empty;
    [ObservableProperty] private string plannedLiving = string.Empty;
    [ObservableProperty] private string plannedInterest = string.Empty;
    [ObservableProperty] private string plannedEnding = string.Empty;
    [ObservableProperty] private bool hasPlanRevision;
    [ObservableProperty] private string planRevisionNotice = string.Empty;
    [ObservableProperty] private string actualLivingSpend = string.Empty;
    [ObservableProperty] private string actualInterest = string.Empty;
    [ObservableProperty] private string livingPlaceholder = string.Empty;
    [ObservableProperty] private string interestPlaceholder = string.Empty;
    [ObservableProperty] private string currentStartingSavings = string.Empty;
    [ObservableProperty] private string suggestedStartingSavings = string.Empty;
    [ObservableProperty] private bool showLivingBreakdown;
    [ObservableProperty] private string groceryAmount = string.Empty;
    [ObservableProperty] private string fuelAmount = string.Empty;
    [ObservableProperty] private string diningAmount = string.Empty;
    [ObservableProperty] private string entertainmentAmount = string.Empty;
    [ObservableProperty] private string otherLivingAmount = string.Empty;
    [ObservableProperty] private string newPaymentName = string.Empty;
    [ObservableProperty] private string newPaymentCategory = string.Empty;
    [ObservableProperty] private string newPaymentAmount = string.Empty;
    [ObservableProperty] private DateTime newPaymentDate = DateTime.Today;
    [ObservableProperty] private string newIncomeName = string.Empty;
    [ObservableProperty] private string newIncomeCategory = string.Empty;
    [ObservableProperty] private string newIncomeAmount = string.Empty;
    [ObservableProperty] private DateTime newIncomeDate = DateTime.Today;
    [ObservableProperty] private string comparisonSummary = string.Empty;
    [ObservableProperty] private string comparisonPlannedEnding = string.Empty;
    [ObservableProperty] private string comparisonActualEnding = string.Empty;
    [ObservableProperty] private string comparisonDifference = string.Empty;
    [ObservableProperty] private string successText = string.Empty;

    public bool IsStep1 => CurrentStep == 1 && !IsSuccess;
    public bool IsStep2 => CurrentStep == 2 && !IsSuccess;
    public bool IsStep3 => CurrentStep == 3 && !IsSuccess;
    public bool IsReviewVisible => !IsSuccess;
    public bool HasPayments => Payments.Count > 0;
    public bool HasFlows => Flows.Count > 0;
    public string PlanIndicator => CurrentStep == 1 ? "●  Plan" : "✓  Plan";
    public string ActualIndicator => CurrentStep < 2 ? "○  Gerçek" : CurrentStep == 2 ? "●  Gerçek" : "✓  Gerçek";
    public string ResultIndicator => CurrentStep < 3 ? "○  Sonuç" : "●  Sonuç";

    public async Task LoadAsync()
    {
        if (_loaded) return;
        try
        {
            IsBusy = true;
            _context = await service.GetPeriodReviewContextAsync();
            if (_context.Actual is not null) throw new InvalidOperationException("Bu dönem daha önce kaydedildi.");

            var plan = _context.OriginalPlan;
            var finalPlan = FinalPlanValues.From(plan, _context.Revision);
            PeriodText = $"{plan.PeriodStart.ToString("dd MMMM yyyy", TurkishCulture)} → {plan.PeriodEnd.ToString("dd MMMM yyyy", TurkishCulture)}";
            PlannedIncome = Money(finalPlan.PlannedIncome, 2);
            PlannedLoans = Money(finalPlan.PlannedLoanPayments, 2);
            PlannedCards = Money(finalPlan.PlannedCardPayments, 2);
            PlannedTemporary = Money(finalPlan.PlannedTemporaryPayments, 2);
            PlannedInstallments = Money(finalPlan.PlannedInstallmentPayments, 2);
            PlannedOther = Money(finalPlan.PlannedOtherScheduledPayments, 2);
            PlannedLarge = Money(finalPlan.PlannedLargeExpenses, 2);
            PlannedLiving = Money(finalPlan.PlannedLivingBudget, 2);
            PlannedInterest = Money(finalPlan.PlannedInterest, 2);
            PlannedEnding = Money(finalPlan.PlannedEndingSavings, 2);
            HasPlanRevision = _context.RevisionCount > 0;
            PlanRevisionNotice = HasPlanRevision ? $"Bu plan dönem içinde {_context.RevisionCount} kez güncellendi." : string.Empty;

            _plannedLiving = finalPlan.PlannedLivingBudget;
            _plannedInterest = finalPlan.PlannedDeficitInterest;
            LivingPlaceholder = $"Boş bırakırsan planlanan: {Money(_plannedLiving, 2)}";
            InterestPlaceholder = $"Boş bırakırsan planlanan: {Money(_plannedInterest, 2)}";
            ActualLivingSpend = string.Empty;
            ActualInterest = string.Empty;
            _lastSuggestedSavings = _context.SuggestedStartingSavings;
            SuggestedStartingSavings = Money(_lastSuggestedSavings, 2);
            CurrentStartingSavings = string.Empty;

            Payments.Clear();
            var snoozedKeys = (await service.GetPaymentReminderResponsesAsync())
                .Where(x => x.Kind == PaymentReminderAnswerKind.Snoozed)
                .Select(x => x.DueKey)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var line in FinalPaymentLines(plan, _context.Revision))
            {
                var snoozed = snoozedKeys.Contains(PaymentReminderPlanner.DueKey(line.SourceEntityId, line.Name, line.PlannedDate));
                var item = new ActualPaymentInputItem
                {
                    PlanLineId = line.Id,
                    Name = line.Name,
                    PlannedDate = line.PlannedDate,
                    PlannedAmountValue = line.PlannedAmount,
                    ActualDate = line.PlannedDate.ToDateTime(TimeOnly.MinValue),
                    Note = snoozed ? "Hatırlatıcıda ertelendi" : string.Empty
                };
                item.SelectedStatus = item.StatusOptions.First(x =>
                    x.Value == (line.PlannedAmount is null || snoozed ? ActualPaymentStatus.Unpaid : ActualPaymentStatus.Paid));
                Payments.Add(item);
            }

            OnPropertyChanged(nameof(HasPayments));
            _loaded = true;
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
    private async Task EverythingAsPlannedAsync()
    {
        try
        {
            SetStatus(string.Empty);
            ResetToPlannedValues();
            await RefreshPreviewAsync();
            CurrentStep = 3;
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(exception));
        }
    }

    private void ResetToPlannedValues()
    {
        foreach (var payment in Payments)
        {
            payment.ActualAmount = string.Empty;
            payment.ActualDate = payment.PlannedDate.ToDateTime(TimeOnly.MinValue);
            payment.Note = string.Empty;
            payment.SelectedStatus = payment.StatusOptions.First(x =>
                x.Value == (payment.PlannedAmountValue is null ? ActualPaymentStatus.Unpaid : ActualPaymentStatus.Paid));
        }

        ActualLivingSpend = string.Empty;
        ActualInterest = string.Empty;
        Flows.Clear();
        ShowLivingBreakdown = false;
        GroceryAmount = string.Empty;
        FuelAmount = string.Empty;
        DiningAmount = string.Empty;
        EntertainmentAmount = string.Empty;
        OtherLivingAmount = string.Empty;
    }

    [RelayCommand]
    private async Task NextAsync()
    {
        try
        {
            SetStatus(string.Empty);
            if (CurrentStep == 1) { CurrentStep = 2; return; }
            if (CurrentStep == 2) { await RefreshPreviewAsync(); CurrentStep = 3; }
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(exception));
        }
    }

    [RelayCommand]
    private void Back()
    {
        if (CurrentStep > 1 && !IsBusy) CurrentStep--;
    }

    [RelayCommand]
    private async Task RecalculateSuggestedAsync()
    {
        try
        {
            await RefreshPreviewAsync();
            SetStatus("Önerilen yeni başlangıç durumu güncellendi.");
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(exception));
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy) return;

        PeriodReviewDraft draft;
        try
        {
            SetStatus(string.Empty);
            draft = BuildDraft(true);
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(exception));
            return;
        }

        try
        {
            IsBusy = true;
            var result = await service.FinalizePeriodReviewAsync(draft);
            ComparisonSummary = result.Comparison.Summary;
            SuccessText = $"{result.NewSnapshot.SnapshotDate.ToString("dd MMMM yyyy", TurkishCulture)} itibarıyla yeni 12 dönemlik planın güncellendi.";
            await feedback.ShowSuccessAsync("Dönem bilgileri kaydedildi.");
            IsSuccess = true;
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
        }
    }

    private async Task RefreshPreviewAsync()
    {
        var suggested = await service.PreviewPeriodReviewAsync(BuildDraft(false));
        _lastSuggestedSavings = suggested.SuggestedStartingSavings;
        SuggestedStartingSavings = Money(_lastSuggestedSavings, 2);
        var preview = await service.PreviewPeriodReviewAsync(BuildDraft(true));
        ComparisonSummary = preview.Comparison.Summary;
        ComparisonPlannedEnding = Money(preview.Comparison.PlannedEndingSavings, 2);
        ComparisonActualEnding = Money(preview.Comparison.ActualEndingSavings, 2);
        ComparisonDifference = SignedMoney(preview.Comparison.Difference);
        ComparisonLines.Clear();
        foreach (var line in preview.Comparison.Lines)
        {
            ComparisonLines.Add(new ComparisonUiLine(
                line.Category,
                Money(line.Planned, 2),
                Money(line.Actual, 2),
                SignedMoney(line.Difference)));
        }
    }

    private PeriodReviewDraft BuildDraft(bool includeConfirmedSavings)
    {
        var context = _context ?? throw new InvalidOperationException("Dönem bilgisi henüz yüklenmedi.");
        var paymentDrafts = Payments.Select(item =>
        {
            var status = item.SelectedStatus?.Value
                ?? throw new InvalidOperationException($"{item.Name} için ödeme durumu seçilmelidir.");
            var amount = status == ActualPaymentStatus.Unpaid
                ? 0m
                : string.IsNullOrWhiteSpace(item.ActualAmount)
                    ? item.PlannedAmountValue ??
                      throw new InvalidOperationException($"{item.Name} için ödediğin tutarı yazmalısın.")
                    : ParseMoney(item.ActualAmount, $"{item.Name} gerçek ödeme");
            return new ActualPaymentDraft(
                item.PlanLineId,
                status,
                amount,
                status == ActualPaymentStatus.Unpaid ? null : DateOnly.FromDateTime(item.ActualDate),
                item.Note);
        }).ToArray();
        var living = string.IsNullOrWhiteSpace(ActualLivingSpend)
            ? _plannedLiving
            : ParseMoney(ActualLivingSpend, "Toplam yaşam gideri");
        var interest = string.IsNullOrWhiteSpace(ActualInterest)
            ? _plannedInterest
            : ParseMoney(ActualInterest, "Gerçekleşen faiz");
        var breakdown = new[]
        {
            Breakdown("Market", GroceryAmount),
            Breakdown("Yakıt", FuelAmount),
            Breakdown("Yeme-İçme", DiningAmount),
            Breakdown("Eğlence", EntertainmentAmount),
            Breakdown("Diğer", OtherLivingAmount)
        }.Where(x => x.Amount > 0m).ToArray();
        decimal? confirmed = includeConfirmedSavings && !string.IsNullOrWhiteSpace(CurrentStartingSavings)
            ? ParseMoney(CurrentStartingSavings, "Yeni planlama başlangıç durumu")
            : null;
        return new PeriodReviewDraft(
            context.OriginalPlan.Id,
            paymentDrafts,
            living,
            interest,
            Flows.Select(x => new ActualFlowDraft(x.Type, x.Name, x.Category, x.Date, x.Amount)).ToArray(),
            breakdown,
            confirmed,
            string.Empty);
    }
}
