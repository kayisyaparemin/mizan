using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mizan.App.Models;
using Mizan.App.Services;
using Mizan.Application.Abstractions;
using Mizan.Application.Models;
using Mizan.Application.Services;
using Mizan.Domain.Models;

namespace Mizan.App.ViewModels;

public partial class OnboardingViewModel : ViewModelBase
{
    private const int LastStep = 8;
    private readonly MizanService _service;
    private readonly IClock _clock;
    private readonly IUserFeedbackService _feedback;
    private readonly CreditCardStatementImportWorkflow _statementImportWorkflow;
    private readonly List<SalaryScheduleEntry> _salaries = [];
    private readonly List<Loan> _loans = [];
    private readonly List<CreditCard> _cards = [];
    private readonly List<TemporaryPaymentPlan> _paymentPlans = [];
    private readonly List<PlannedLargeExpense> _payments = [];
    private DateOnly _draftAnchorDate;
    private DateOnly? _cardExactNextStatementDate;
    private DateOnly? _cardExactNextDueDate;
    private CancellationTokenSource? _statementImportCancellation;
    private string? _cardStatementFingerprint;
    private CreditCardStatementSource _cardStatementSource = CreditCardStatementSource.Manual;

    public OnboardingViewModel(
        MizanService service,
        IClock clock,
        IUserFeedbackService feedback,
        CreditCardStatementImportWorkflow statementImportWorkflow)
    {
        _service = service;
        _clock = clock;
        _feedback = feedback;
        _statementImportWorkflow = statementImportWorkflow;
        _draftAnchorDate = clock.Today;
        IncomeEffectiveDate = clock.Today.ToDateTime(TimeOnly.MinValue);
        LoanNextPaymentDate = clock.Today.AddMonths(1).ToDateTime(TimeOnly.MinValue);
        PaymentDate = clock.Today.AddMonths(1).ToDateTime(TimeOnly.MinValue);
        CardBalanceDate = clock.Today.ToDateTime(TimeOnly.MinValue);
        CardStatementDate = clock.Today.ToDateTime(TimeOnly.MinValue);
        CardStatementDueDate = clock.Today.AddDays(10).ToDateTime(TimeOnly.MinValue);
        SelectedAssignmentMode = AssignmentModes[0];
        SelectedCardPaymentStrategy = CardPaymentStrategies[0];
        SelectedCardFallbackStrategy = CardFallbackStrategies[0];
        SelectedCurrentStatementPaymentMode = CurrentStatementPaymentModes[0];
        SelectedPaymentType = PaymentTypes[0];
        RefreshStepState();
        RefreshDraftLines();
    }

    public event Action<bool>? Completed;

    public ObservableCollection<SelectionOption<CashFlowAllocationMode>> AssignmentModes { get; } =
    [
        new("Gelecek dönemi karşılarım", CashFlowAllocationMode.UpcomingPeriod),
        new("Geçmiş dönemi kapatırım", CashFlowAllocationMode.PreviousPeriod)
    ];

    public ObservableCollection<SelectionOption<CreditCardPaymentStrategy>> CardPaymentStrategies { get; } =
    [
        new("Her ekstrede sor", CreditCardPaymentStrategy.AskEachStatement),
        new("Asgari", CreditCardPaymentStrategy.Minimum),
        new("Tamamı", CreditCardPaymentStrategy.FullStatement)
    ];

    public ObservableCollection<SelectionOption<ProjectionFallbackStrategy>> CardFallbackStrategies { get; } =
    [
        new("Asgari", ProjectionFallbackStrategy.Minimum),
        new("Tamamı", ProjectionFallbackStrategy.FullStatement)
    ];

    public ObservableCollection<SelectionOption<CurrentStatementPaymentMode>> CurrentStatementPaymentModes { get; } =
    [
        new("Asgari", CurrentStatementPaymentMode.Minimum),
        new("Tamamı", CurrentStatementPaymentMode.Full),
        new("Başka tutar", CurrentStatementPaymentMode.Custom)
    ];

    public ObservableCollection<SelectionOption<string>> PaymentTypes { get; } =
    [
        new("Tek seferlik ödeme", "one-time"),
        new("Düzenli ödeme", "recurring"),
        new("Geçici ödeme planı", "temporary")
    ];

    public ObservableCollection<FinancialRecordLine> DraftIncomes { get; } = [];
    public ObservableCollection<FinancialRecordLine> DraftCards { get; } = [];
    public ObservableCollection<FinancialRecordLine> DraftLoans { get; } = [];
    public ObservableCollection<FinancialRecordLine> DraftPayments { get; } = [];

    public bool IsDevelopment => BuildInfo.IsDevelopment;

    [ObservableProperty] private int stepIndex;
    [ObservableProperty] private double progress;
    [ObservableProperty] private string stepCounter = "1/8";
    [ObservableProperty] private string stepTitle = "Mizan'i hazirlayalim";
    [ObservableProperty] private string stepLead = string.Empty;
    [ObservableProperty] private bool isIntroStep = true;
    [ObservableProperty] private bool isPeriodStep;
    [ObservableProperty] private bool isIncomeStep;
    [ObservableProperty] private bool isCardStep;
    [ObservableProperty] private bool isLoanStep;
    [ObservableProperty] private bool isPaymentStep;
    [ObservableProperty] private bool isLivingStep;
    [ObservableProperty] private bool isCurrentAmountStep;
    [ObservableProperty] private bool isReviewStep;
    [ObservableProperty] private bool canGoBack;
    [ObservableProperty] private bool canGoNext;

    [ObservableProperty] private string periodDay = string.Empty;
    [ObservableProperty] private SelectionOption<CashFlowAllocationMode>? selectedAssignmentMode;

    [ObservableProperty] private string incomeName = string.Empty;
    [ObservableProperty] private string incomeAmount = string.Empty;
    [ObservableProperty] private DateTime incomeEffectiveDate;

    [ObservableProperty] private string cardName = string.Empty;
    [ObservableProperty] private string cardBank = string.Empty;
    [ObservableProperty] private string cardLimit = string.Empty;
    [ObservableProperty] private bool cardHasActualStatement;
    [ObservableProperty] private bool isLegacyCardSetup = true;
    [ObservableProperty] private string cardCarriedBalance = string.Empty;
    [ObservableProperty] private string cardUnbilledSpending = string.Empty;
    [ObservableProperty] private DateTime cardBalanceDate;
    [ObservableProperty] private string cardStatementAmount = string.Empty;
    [ObservableProperty] private string cardStatementMinimum = string.Empty;
    [ObservableProperty] private DateTime cardStatementDate = DateTime.Today;
    [ObservableProperty] private DateTime cardStatementDueDate = DateTime.Today;
    [ObservableProperty] private string cardNextStatementDate = string.Empty;
    [ObservableProperty] private string cardNextDueDate = string.Empty;
    [ObservableProperty] private string cardStatementImportWarnings = string.Empty;
    [ObservableProperty] private bool hasCardStatementImportWarnings;
    [ObservableProperty] private SelectionOption<CurrentStatementPaymentMode>? selectedCurrentStatementPaymentMode;
    [ObservableProperty] private string currentStatementCustomPayment = string.Empty;
    [ObservableProperty] private bool isCurrentStatementCustomPayment;
    [ObservableProperty] private bool showAdvancedCardOptions;
    [ObservableProperty] private string cardClosingDay = string.Empty;
    [ObservableProperty] private string cardDueDay = string.Empty;
    [ObservableProperty] private string cardMinimumRate = string.Empty;
    [ObservableProperty] private SelectionOption<CreditCardPaymentStrategy>? selectedCardPaymentStrategy;
    [ObservableProperty] private SelectionOption<ProjectionFallbackStrategy>? selectedCardFallbackStrategy;

    [ObservableProperty] private string loanName = string.Empty;
    [ObservableProperty] private string loanBank = string.Empty;
    [ObservableProperty] private string loanMonthlyPayment = string.Empty;
    [ObservableProperty] private string loanPaymentDay = string.Empty;
    [ObservableProperty] private DateTime loanNextPaymentDate;
    [ObservableProperty] private string loanInstallmentCount = string.Empty;
    [ObservableProperty] private string loanRemainingDebt = string.Empty;

    [ObservableProperty] private string paymentName = string.Empty;
    [ObservableProperty] private string paymentAmount = string.Empty;
    [ObservableProperty] private DateTime paymentDate;
    [ObservableProperty] private string paymentCount = string.Empty;
    [ObservableProperty] private SelectionOption<string>? selectedPaymentType;
    [ObservableProperty] private bool isPaymentPlanType;
    [ObservableProperty] private string monthlyVariableExpenseAllowance = string.Empty;
    [ObservableProperty] private string currentAmount = string.Empty;
    [ObservableProperty] private bool hasDraftIncomes;
    [ObservableProperty] private bool hasDraftCards;
    [ObservableProperty] private bool hasDraftLoans;
    [ObservableProperty] private bool hasDraftPayments;
    [ObservableProperty] private string reviewIncomeText = "Gelir eklenmedi";
    [ObservableProperty] private string reviewCardText = "Kart eklenmedi";
    [ObservableProperty] private string reviewLoanText = "Kredi eklenmedi";
    [ObservableProperty] private string reviewPaymentText = "Yaklaşan ödeme eklenmedi";
    [ObservableProperty] private string reviewLivingText = "0 TL";
    [ObservableProperty] private string reviewCurrentAmountText = "0 TL";
    [ObservableProperty] private string reviewPeriodText = "Dönem günü seçilmedi";

    partial void OnStepIndexChanged(int value) { RefreshStepState(); RefreshReview(); }
    partial void OnPeriodDayChanged(string value) => RefreshReview();
    partial void OnMonthlyVariableExpenseAllowanceChanged(string value) => RefreshReview();
    partial void OnCurrentAmountChanged(string value) => RefreshReview();
    partial void OnSelectedPaymentTypeChanged(SelectionOption<string>? value) =>
        IsPaymentPlanType = value?.Value is "recurring" or "temporary";
    partial void OnCardHasActualStatementChanged(bool value) => IsLegacyCardSetup = !value;
    partial void OnSelectedCurrentStatementPaymentModeChanged(SelectionOption<CurrentStatementPaymentMode>? value) =>
        IsCurrentStatementCustomPayment = value?.Value == CurrentStatementPaymentMode.Custom;
    partial void OnCardStatementDateChanged(DateTime value) => RefreshCardNextDates();
    partial void OnCardClosingDayChanged(string value) => RefreshCardNextDates();
    partial void OnCardDueDayChanged(string value) => RefreshCardNextDates();

    [RelayCommand] private void Begin() => StepIndex = 1;
    [RelayCommand] private void EmptySetup() { ClearDraft(); StepIndex = 1; }
    [RelayCommand] private void FillSample() { ApplyDraft(CanonicalDevelopmentOnboardingFixture.Create()); StepIndex = LastStep; }
    [RelayCommand] private void Back() { if (StepIndex > 1) StepIndex--; else StepIndex = 0; }
    [RelayCommand] private void Next() { if (StepIndex < LastStep) StepIndex++; }

    [RelayCommand]
    private async Task StartMizanAsync()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            var draft = BuildDraft();
            await _service.InitializeFromOnboardingAsync(draft);
            SetStatus(string.Empty);
            await _feedback.ShowSuccessAsync("Güncel durumun ve finansal yapın kaydedildi.", "Mizan hazır");
            Completed?.Invoke(true);
        }
        catch (Exception exception)
        {
            var message = UserFacingMessages.FromException(exception);
            SetStatus(message);
            await _feedback.ShowErrorAsync(message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Dismiss() => Completed?.Invoke(false);

    private OnboardingDraft BuildDraft()
    {
        if (!int.TryParse(PeriodDay, out var IncomeDay))
        {
            throw new InvalidOperationException("Dönem günü geçerli olmalıdır.");
        }

        return new OnboardingDraft
        {
            Settings = new UserSettings
            {
                IncomeDay = IncomeDay,
                MonthlyVariableExpenseAllowance = ParseNonNegativeMoney(MonthlyVariableExpenseAllowance, "Yaşam gideri"),
                ProjectionOpeningBalance = string.IsNullOrWhiteSpace(CurrentAmount)
                    ? 0m
                    : ParseMoney(CurrentAmount, "Mevcut tutar"),
                ProjectionAnchorDate = _draftAnchorDate
            },
            Salaries = _salaries.ToArray(),
            Loans = _loans.ToArray(),
            CreditCards = _cards.ToArray(),
            PaymentPlans = _paymentPlans.ToArray(),
            PlannedLargeExpenses = _payments.ToArray(),
            InitialCashFlowAllocationMode = SelectedAssignmentMode?.Value ?? CashFlowAllocationMode.UpcomingPeriod
        };
    }

    private void RefreshStepState()
    {
        IsIntroStep = StepIndex == 0;
        IsPeriodStep = StepIndex == 1;
        IsIncomeStep = StepIndex == 2;
        IsCardStep = StepIndex == 3;
        IsLoanStep = StepIndex == 4;
        IsPaymentStep = StepIndex == 5;
        IsLivingStep = StepIndex == 6;
        IsCurrentAmountStep = StepIndex == 7;
        IsReviewStep = StepIndex == LastStep;
        CanGoBack = StepIndex > 0;
        CanGoNext = StepIndex is > 0 and < LastStep;
        Progress = StepIndex <= 0 ? 0d : (double)StepIndex / LastStep;
        StepCounter = StepIndex <= 0 ? string.Empty : $"{StepIndex}/{LastStep}";
        StepTitle = StepIndex switch
        {
            0 => "Mizan'ı sana göre hazırlayalım",
            1 => "Dönemini ayarlayalım",
            2 => "Gelirlerin",
            3 => "Kredi kartların",
            4 => "Kredilerin",
            5 => "Yaklaşan ödemelerin",
            6 => "Yaşam giderin",
            7 => "Şu an elinde ne kadar var?",
            _ => "Kontrol edelim"
        };
        StepLead = StepIndex switch
        {
            0 => "Birkaç temel bilgiyle güncel durumunu ve ilk dönem planını oluşturacağız.",
            1 => "Gelirinin hangi gün geldiğini ve hangi dönemi karşıladığını seç.",
            2 => "Düzenli gelirlerini ekle. En az bir gelir gerekiyor.",
            3 => "Varsa kartlarını ekle; yoksa devam edebilirsin.",
            4 => "Varsa aktif kredilerini ekle; yoksa devam edebilirsin.",
            5 => "Yakında ödenecek tek seferlik tutarları ekle.",
            6 => "Takip etmediğin günlük harcamalar için aylık bir tahmin gir.",
            7 => "Mizan gelecek planını bu mevcut tutardan başlatacak.",
            _ => "Kaydetmeden önce taslağı son kez gözden geçir."
        };
    }
}
