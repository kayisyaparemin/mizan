using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoinFlow.App.Models;
using CoinFlow.App.Services;
using CoinFlow.Application.Abstractions;
using CoinFlow.Application.Models;
using CoinFlow.Application.Services;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.App.ViewModels;

public partial class OnboardingViewModel : ViewModelBase
{
    private const int LastStep = 8;
    private readonly CoinFlowService _service;
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

    public OnboardingViewModel(
        CoinFlowService service,
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
        LoanNextPaymentDate = clock.Today.AddMonths(1)
            .ToDateTime(TimeOnly.MinValue);
        PaymentDate = clock.Today.AddMonths(1).ToDateTime(TimeOnly.MinValue);
        CardBalanceDate = clock.Today.ToDateTime(TimeOnly.MinValue);
        CardStatementDate = clock.Today.ToDateTime(TimeOnly.MinValue);
        CardStatementDueDate = clock.Today.AddDays(10)
            .ToDateTime(TimeOnly.MinValue);
        SelectedAssignmentMode = AssignmentModes[0];
        SelectedCardPaymentStrategy = CardPaymentStrategies[0];
        SelectedCardFallbackStrategy = CardFallbackStrategies[0];
        SelectedCurrentStatementPaymentMode = CurrentStatementPaymentModes[0];
        SelectedPaymentType = PaymentTypes[0];
        RefreshStepState();
        RefreshDraftLines();
    }

    public event Action<bool>? Completed;

    public ObservableCollection<SelectionOption<PaymentAssignmentMode>>
        AssignmentModes { get; } =
    [
        new("Gelecek dönemi karşılarım", PaymentAssignmentMode.UpcomingPeriod),
        new("Geçmiş dönemi kapatırım", PaymentAssignmentMode.PreviousPeriod)
    ];

    public ObservableCollection<SelectionOption<CreditCardPaymentStrategy>>
        CardPaymentStrategies { get; } =
    [
        new("Her ekstrede sor", CreditCardPaymentStrategy.AskEachStatement),
        new("Asgari", CreditCardPaymentStrategy.Minimum),
        new("Tamamı", CreditCardPaymentStrategy.FullStatement)
    ];

    public ObservableCollection<SelectionOption<ProjectionFallbackStrategy>>
        CardFallbackStrategies { get; } =
    [
        new("Asgari", ProjectionFallbackStrategy.Minimum),
        new("Tamamı", ProjectionFallbackStrategy.FullStatement)
    ];

    public ObservableCollection<SelectionOption<CurrentStatementPaymentMode>>
        CurrentStatementPaymentModes { get; } =
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

    [ObservableProperty] private string periodDay = "10";
    [ObservableProperty] private SelectionOption<PaymentAssignmentMode>? selectedAssignmentMode;

    [ObservableProperty] private string incomeName = "Maaş";
    [ObservableProperty] private string incomeAmount = string.Empty;
    [ObservableProperty] private DateTime incomeEffectiveDate;

    [ObservableProperty] private string cardName = string.Empty;
    [ObservableProperty] private string cardBank = string.Empty;
    [ObservableProperty] private string cardLimit = string.Empty;
    [ObservableProperty] private bool cardHasActualStatement;
    [ObservableProperty] private bool isLegacyCardSetup = true;
    [ObservableProperty] private string cardCarriedBalance = "0";
    [ObservableProperty] private string cardUnbilledSpending = "0";
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
    private string? _cardStatementFingerprint;
    private CreditCardStatementSource _cardStatementSource =
        CreditCardStatementSource.Manual;
    [ObservableProperty] private string cardClosingDay = "25";
    [ObservableProperty] private string cardDueDay = "5";
    [ObservableProperty] private string cardMinimumRate = "40";
    [ObservableProperty] private SelectionOption<CreditCardPaymentStrategy>? selectedCardPaymentStrategy;
    [ObservableProperty] private SelectionOption<ProjectionFallbackStrategy>? selectedCardFallbackStrategy;

    [ObservableProperty] private string loanName = string.Empty;
    [ObservableProperty] private string loanBank = string.Empty;
    [ObservableProperty] private string loanMonthlyPayment = string.Empty;
    [ObservableProperty] private string loanPaymentDay = "10";
    [ObservableProperty] private DateTime loanNextPaymentDate;
    [ObservableProperty] private string loanInstallmentCount = "12";
    [ObservableProperty] private string loanRemainingDebt = string.Empty;

    [ObservableProperty] private string paymentName = string.Empty;
    [ObservableProperty] private string paymentAmount = string.Empty;
    [ObservableProperty] private DateTime paymentDate;
    [ObservableProperty] private string paymentCount = "12";
    [ObservableProperty] private SelectionOption<string>? selectedPaymentType;
    [ObservableProperty] private bool isPaymentPlanType;
    [ObservableProperty] private string monthlyLivingBudget = "0";
    [ObservableProperty] private string currentAmount = "0";
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
    [ObservableProperty] private string reviewPeriodText = "Dönem günü 10";

    partial void OnStepIndexChanged(int value)
    {
        RefreshStepState();
        RefreshReview();
    }

    partial void OnPeriodDayChanged(string value) => RefreshReview();

    partial void OnMonthlyLivingBudgetChanged(string value) => RefreshReview();

    partial void OnCurrentAmountChanged(string value) => RefreshReview();

    partial void OnSelectedPaymentTypeChanged(SelectionOption<string>? value)
    {
        IsPaymentPlanType = value?.Value is "recurring" or "temporary";
        if (value?.Value == "temporary" && PaymentCount == "12")
        {
            PaymentCount = "3";
        }
        else if (value?.Value == "recurring" && PaymentCount == "3")
        {
            PaymentCount = "12";
        }
    }

    partial void OnCardHasActualStatementChanged(bool value) =>
        IsLegacyCardSetup = !value;

    partial void OnSelectedCurrentStatementPaymentModeChanged(
        SelectionOption<CurrentStatementPaymentMode>? value) =>
        IsCurrentStatementCustomPayment =
            value?.Value == CurrentStatementPaymentMode.Custom;

    partial void OnCardStatementDateChanged(DateTime value) =>
        RefreshCardNextDates();

    partial void OnCardClosingDayChanged(string value) =>
        RefreshCardNextDates();

    partial void OnCardDueDayChanged(string value) =>
        RefreshCardNextDates();

    [RelayCommand]
    private void Begin()
    {
        StepIndex = 1;
    }

    [RelayCommand]
    private void EmptySetup()
    {
        ClearDraft();
        StepIndex = 1;
    }

    [RelayCommand]
    private void FillSample()
    {
        ApplyDraft(CanonicalDevelopmentOnboardingFixture.Create());
        StepIndex = LastStep;
    }

    [RelayCommand]
    private void Back()
    {
        if (StepIndex > 1)
        {
            StepIndex--;
        }
        else
        {
            StepIndex = 0;
        }
    }

    [RelayCommand]
    private void Next()
    {
        if (StepIndex < LastStep)
        {
            StepIndex++;
        }
    }

    [RelayCommand]
    private void AddIncome()
    {
        try
        {
            _salaries.Add(new SalaryScheduleEntry
            {
                Amount = ParsePositiveMoney(IncomeAmount, "Gelir"),
                EffectiveDate = DateOnly.FromDateTime(IncomeEffectiveDate),
                Description = string.IsNullOrWhiteSpace(IncomeName)
                    ? "Gelir"
                    : IncomeName.Trim()
            });
            IncomeName = "Maaş";
            IncomeAmount = string.Empty;
            RefreshDraftLines();
            SetStatus(string.Empty);
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(exception));
        }
    }

    [RelayCommand]
    private void UseActualStatementForCard()
    {
        CardHasActualStatement = true;
        _cardExactNextStatementDate = null;
        _cardExactNextDueDate = null;
        CardStatementDate = _clock.Today.ToDateTime(TimeOnly.MinValue);
        CardStatementDueDate = _clock.Today.AddDays(10)
            .ToDateTime(TimeOnly.MinValue);
        RefreshCardNextDates();
    }

    [RelayCommand]
    private void UseLegacyCardSetup()
    {
        CardHasActualStatement = false;
        _cardExactNextStatementDate = null;
        _cardExactNextDueDate = null;
        HasCardStatementImportWarnings = false;
        CardStatementImportWarnings = string.Empty;
    }

    [RelayCommand]
    private async Task ImportCardStatementPdfAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        IsStatementImporting = true;
        BusyMessage = "Ekstre okunuyor...";
        SetStatus(string.Empty);
        using var importCancellation = new CancellationTokenSource();
        _statementImportCancellation = importCancellation;
        try
        {
            var attempt = await _statementImportWorkflow
                .RunAsync(importCancellation.Token);
            if (attempt.Outcome is
                CreditCardStatementImportOutcome.Cancelled or
                CreditCardStatementImportOutcome.AlreadyRunning)
            {
                return;
            }

            if (!attempt.IsCompleted || attempt.Result is null)
            {
                CardHasActualStatement = true;
                await ShowManualFallbackAsync(
                    attempt.Outcome ==
                    CreditCardStatementImportOutcome.TimedOut);
                return;
            }

            var result = attempt.Result;
            _statementImportWorkflow.NotifyPreviewStarted();
            ApplyStatementImport(result);
            if (!result.HasRequiredFields)
            {
                await ShowManualFallbackAsync();
            }
        }
        catch (Exception)
        {
            CardHasActualStatement = true;
            SetStatus(string.Empty);
            await ShowManualFallbackAsync();
        }
        finally
        {
            _statementImportCancellation = null;
            IsStatementImporting = false;
            BusyMessage = string.Empty;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelStatementImport() =>
        _statementImportCancellation?.Cancel();

    [RelayCommand]
    private void AddCard()
    {
        try
        {
            if (!int.TryParse(CardClosingDay, out var closingDay) ||
                !int.TryParse(CardDueDay, out var dueDay))
            {
                throw new InvalidOperationException(
                    "Kart günleri geçerli olmalıdır.");
            }

            var cardId = Guid.NewGuid();
            var actualStatement = CardHasActualStatement
                ? BuildCurrentStatement(cardId)
                : null;
            _cards.Add(new CreditCard
            {
                Id = cardId,
                Name = RequireText(CardName, "Kart adı"),
                Bank = CardBank.Trim(),
                Limit = ParsePositiveMoney(CardLimit, "Kart limiti"),
                CarriedBalance = actualStatement is null
                    ? ParseNonNegativeMoney(
                        CardCarriedBalance,
                        "Devreden bakiye")
                    : 0m,
                UnbilledSpending = actualStatement is null
                    ? ParseNonNegativeMoney(
                        CardUnbilledSpending,
                        "Ekstreleşmemiş harcama")
                    : 0m,
                BalanceAsOfDate = actualStatement?.StatementDate ??
                                  DateOnly.FromDateTime(CardBalanceDate),
                StatementClosingDay = closingDay,
                PaymentDueDay = dueDay,
                MinimumPaymentRate =
                    ParsePositiveMoney(CardMinimumRate, "Asgari oran") / 100m,
                PaymentStrategy = SelectedCardPaymentStrategy?.Value ??
                                  CreditCardPaymentStrategy.AskEachStatement,
                ProjectionFallbackStrategy =
                    SelectedCardFallbackStrategy?.Value ??
                    ProjectionFallbackStrategy.Minimum,
                CurrentStatement = actualStatement,
                CurrentStatementPaymentPlan = actualStatement is null
                    ? null
                    : BuildCurrentStatementPaymentPlan(
                        actualStatement.StatementAmount)
            });
            CardName = string.Empty;
            CardBank = string.Empty;
            CardLimit = string.Empty;
            CardCarriedBalance = "0";
            CardUnbilledSpending = "0";
            CardHasActualStatement = false;
            CardStatementAmount = string.Empty;
            CardStatementMinimum = string.Empty;
            CardNextStatementDate = string.Empty;
            CardNextDueDate = string.Empty;
            CurrentStatementCustomPayment = string.Empty;
            _cardStatementFingerprint = null;
            _cardStatementSource = CreditCardStatementSource.Manual;
            _cardExactNextStatementDate = null;
            _cardExactNextDueDate = null;
            RefreshDraftLines();
            SetStatus(string.Empty);
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(exception));
        }
    }

    [RelayCommand]
    private void AddLoan()
    {
        try
        {
            if (!int.TryParse(LoanPaymentDay, out var paymentDay) ||
                !int.TryParse(LoanInstallmentCount, out var count))
            {
                throw new InvalidOperationException(
                    "Kredi günü ve taksit sayısı geçerli olmalıdır.");
            }

            _loans.Add(new Loan
            {
                Name = RequireText(LoanName, "Kredi adı"),
                Bank = LoanBank.Trim(),
                MonthlyPayment = ParsePositiveMoney(
                    LoanMonthlyPayment,
                    "Aylık ödeme"),
                PaymentDay = paymentDay,
                NextPaymentDate = DateOnly.FromDateTime(
                    LoanNextPaymentDate),
                RemainingInstallmentCount = count,
                RemainingDebt = ParseOptionalMoney(LoanRemainingDebt)
            });
            LoanName = string.Empty;
            LoanBank = string.Empty;
            LoanMonthlyPayment = string.Empty;
            LoanRemainingDebt = string.Empty;
            RefreshDraftLines();
            SetStatus(string.Empty);
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(exception));
        }
    }

    [RelayCommand]
    private void AddPayment()
    {
        try
        {
            var type = SelectedPaymentType?.Value ?? "one-time";
            var amount = ParsePositiveMoney(PaymentAmount, "Ödeme tutarı");
            var date = DateOnly.FromDateTime(PaymentDate);
            var name = RequireText(PaymentName, "Ödeme adı");
            if (type == "one-time")
            {
                _payments.Add(new PlannedLargeExpense
                {
                    Name = name,
                    Amount = amount,
                    ExactDate = date
                });
            }
            else
            {
                if (!int.TryParse(PaymentCount, out var count) ||
                    count < 1)
                {
                    throw new InvalidOperationException(
                        "Ödeme adedi geçerli olmalıdır.");
                }

                var id = Guid.NewGuid();
                _paymentPlans.Add(new TemporaryPaymentPlan
                {
                    Id = id,
                    Name = name,
                    Kind = type == "recurring"
                        ? PaymentPlanKind.Recurring
                        : PaymentPlanKind.Temporary,
                    OriginalAmount = amount,
                    TotalRepaymentAmount = amount * count,
                    Installments = Enumerable.Range(0, count)
                        .Select(index => new TemporaryPaymentInstallment
                        {
                            Id = Guid.NewGuid(),
                            PlanId = id,
                            DueDate = CalendarRules.AddMonthsKeepingDay(
                                date,
                                index,
                                date.Day),
                            Amount = amount
                        })
                        .ToArray()
                });
            }

            PaymentName = string.Empty;
            PaymentAmount = string.Empty;
            RefreshDraftLines();
            SetStatus(string.Empty);
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(exception));
        }
    }

    [RelayCommand]
    private async Task StartMizanAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var draft = BuildDraft();
            await _service.InitializeFromOnboardingAsync(draft);
            SetStatus(string.Empty);
            await _feedback.ShowSuccessAsync(
                "Güncel durumun ve finansal yapın kaydedildi.",
                "Mizan hazır");
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

    public void RemoveDraftLine(FinancialRecordLine line)
    {
        switch (line.Kind)
        {
            case FinancialRecordKind.Salary:
                _salaries.RemoveAll(x => x.Id == line.Id);
                break;
            case FinancialRecordKind.CreditCard:
                _cards.RemoveAll(x => x.Id == line.Id);
                break;
            case FinancialRecordKind.Loan:
                _loans.RemoveAll(x => x.Id == line.Id);
                break;
            case FinancialRecordKind.LargeExpense:
                _payments.RemoveAll(x => x.Id == line.Id);
                break;
            case FinancialRecordKind.TemporaryPlan:
                _paymentPlans.RemoveAll(x => x.Id == line.Id);
                break;
        }

        RefreshDraftLines();
        SetStatus(string.Empty);
    }

    private OnboardingDraft BuildDraft()
    {
        if (!int.TryParse(PeriodDay, out var salaryDay))
        {
            throw new InvalidOperationException(
                "Dönem günü geçerli olmalıdır.");
        }

        return new OnboardingDraft
        {
            Settings = new UserSettings
            {
                SalaryDay = salaryDay,
                MonthlyLivingBudget = ParseNonNegativeMoney(
                    MonthlyLivingBudget,
                    "Yaşam gideri"),
                ProjectionStartingSavings = ParseMoney(
                    CurrentAmount,
                    "Mevcut tutar"),
                ProjectionAnchorDate = _draftAnchorDate
            },
            Salaries = _salaries.ToArray(),
            Loans = _loans.ToArray(),
            CreditCards = _cards.ToArray(),
            PaymentPlans = _paymentPlans.ToArray(),
            PlannedLargeExpenses = _payments.ToArray(),
            InitialPaymentAssignmentMode =
                SelectedAssignmentMode?.Value ??
                PaymentAssignmentMode.UpcomingPeriod
        };
    }

    private void ApplyDraft(OnboardingDraft draft)
    {
        ClearDraft();
        _draftAnchorDate = draft.Settings.ProjectionAnchorDate == default
            ? _clock.Today
            : draft.Settings.ProjectionAnchorDate;
        PeriodDay = draft.Settings.SalaryDay.ToString(TurkishCulture);
        MonthlyLivingBudget = draft.Settings.MonthlyLivingBudget
            .ToString("N2", TurkishCulture);
        CurrentAmount = draft.Settings.ProjectionStartingSavings
            .ToString("N2", TurkishCulture);
        SelectedAssignmentMode = AssignmentModes.First(x =>
            x.Value == draft.InitialPaymentAssignmentMode);
        _salaries.AddRange(draft.Salaries);
        _loans.AddRange(draft.Loans);
        _cards.AddRange(draft.CreditCards);
        _paymentPlans.AddRange(draft.PaymentPlans);
        _payments.AddRange(draft.PlannedLargeExpenses);
        RefreshDraftLines();
        SetStatus(string.Empty);
    }

    private void ClearDraft()
    {
        _draftAnchorDate = _clock.Today;
        _salaries.Clear();
        _loans.Clear();
        _cards.Clear();
        _paymentPlans.Clear();
        _payments.Clear();
        PeriodDay = "10";
        MonthlyLivingBudget = "0";
        CurrentAmount = "0";
        SelectedAssignmentMode = AssignmentModes[0];
        SelectedPaymentType = PaymentTypes[0];
        CardHasActualStatement = false;
        CardStatementAmount = string.Empty;
        CardStatementMinimum = string.Empty;
        CardNextStatementDate = string.Empty;
        CardNextDueDate = string.Empty;
        CardStatementImportWarnings = string.Empty;
        HasCardStatementImportWarnings = false;
        CurrentStatementCustomPayment = string.Empty;
        _cardStatementFingerprint = null;
        _cardStatementSource = CreditCardStatementSource.Manual;
        _cardExactNextStatementDate = null;
        _cardExactNextDueDate = null;
        RefreshDraftLines();
        SetStatus(string.Empty);
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

    private void RefreshDraftLines()
    {
        DraftIncomes.Clear();
        foreach (var salary in _salaries.OrderBy(x => x.EffectiveDate))
        {
            DraftIncomes.Add(new FinancialRecordLine(
                salary.Id,
                ManagementSection.Income,
                FinancialRecordKind.Salary,
                salary.Description,
                $"Geçerli: {salary.EffectiveDate:dd.MM.yyyy}",
                Money(salary.Amount),
                "Gelir"));
        }

        DraftCards.Clear();
        foreach (var card in _cards.OrderBy(x => x.Bank).ThenBy(x => x.Name))
        {
            var subtitle = card.CurrentStatement is { } statement
                ? $"Ekstre: {statement.StatementDate:dd.MM.yyyy} • Son ödeme: {statement.DueDate:dd.MM.yyyy}"
                : $"Kesim {card.StatementClosingDay}. gün • Son ödeme {card.PaymentDueDay}. gün";
            DraftCards.Add(new FinancialRecordLine(
                card.Id,
                ManagementSection.Payment,
                FinancialRecordKind.CreditCard,
                $"{card.Bank} {card.Name}".Trim(),
                subtitle,
                Money(card.KnownTotalDebt),
                card.CurrentStatement is null
                    ? "Kredi kartı"
                    : $"Plan: {CurrentStatementPlanLabel(card.CurrentStatementPaymentPlan)}"));
        }

        DraftLoans.Clear();
        foreach (var loan in _loans.OrderBy(x => x.NextPaymentDate))
        {
            DraftLoans.Add(new FinancialRecordLine(
                loan.Id,
                ManagementSection.Payment,
                FinancialRecordKind.Loan,
                $"{loan.Bank} {loan.Name}".Trim(),
                $"Sonraki: {loan.NextPaymentDate:dd.MM.yyyy} • {loan.RemainingInstallmentCount} ödeme",
                Money(loan.MonthlyPayment),
                "Kredi"));
        }

        DraftPayments.Clear();
        foreach (var payment in _payments.OrderBy(x => x.ExactDate))
        {
            DraftPayments.Add(new FinancialRecordLine(
                payment.Id,
                ManagementSection.Payment,
                FinancialRecordKind.LargeExpense,
                payment.Name,
                payment.ExactDate.ToString("dd.MM.yyyy"),
                Money(payment.Amount),
                "Tek seferlik ödeme"));
        }

        foreach (var plan in _paymentPlans
                     .OrderBy(x => x.Installments.Min(i => i.DueDate)))
        {
            DraftPayments.Add(new FinancialRecordLine(
                plan.Id,
                ManagementSection.Payment,
                FinancialRecordKind.TemporaryPlan,
                plan.Name,
                $"{plan.Installments.Min(x => x.DueDate):dd.MM.yyyy} • {plan.Installments.Count} ödeme",
                Money(plan.Installments.Sum(x => x.Amount)),
                plan.Kind == PaymentPlanKind.Recurring
                    ? "Düzenli ödeme"
                    : "Geçici ödeme planı"));
        }

        HasDraftIncomes = DraftIncomes.Count > 0;
        HasDraftCards = DraftCards.Count > 0;
        HasDraftLoans = DraftLoans.Count > 0;
        HasDraftPayments = DraftPayments.Count > 0;
        RefreshReview();
    }

    private void RefreshReview()
    {
        ReviewIncomeText = HasDraftIncomes
            ? $"{DraftIncomes.Count} gelir"
            : "Gelir eklenmedi";
        ReviewCardText = HasDraftCards
            ? $"{DraftCards.Count} kart"
            : "Kart eklenmedi";
        ReviewLoanText = HasDraftLoans
            ? $"{DraftLoans.Count} kredi"
            : "Kredi eklenmedi";
        ReviewPaymentText = HasDraftPayments
            ? $"{DraftPayments.Count} ödeme"
            : "Yaklaşan ödeme eklenmedi";
        ReviewLivingText = TryMoneyText(MonthlyLivingBudget);
        ReviewCurrentAmountText = TryMoneyTextSigned(CurrentAmount);
        ReviewPeriodText = $"Dönem günü {PeriodDay}";
    }

    private CreditCardStatement BuildCurrentStatement(Guid cardId)
    {
        var amount = ParsePositiveMoney(
            CardStatementAmount,
            "Ekstre tutarı");
        var minimum = ParseNonNegativeMoney(
            CardStatementMinimum,
            "Asgari ödeme");
        if (minimum > amount)
        {
            throw new InvalidOperationException(
                "Asgari ödeme ekstre tutarından büyük olamaz.");
        }

        return new CreditCardStatement
        {
            CreditCardId = cardId,
            StatementDate = DateOnly.FromDateTime(CardStatementDate),
            DueDate = DateOnly.FromDateTime(CardStatementDueDate),
            StatementAmount = amount,
            MinimumPaymentAmount = minimum,
            NextStatementDate = ResolveCardNextStatementDate(),
            NextDueDate = ResolveCardNextDueDate(),
            Source = _cardStatementSource,
            SourceDocumentFingerprint = _cardStatementFingerprint,
            ImportedAt = _cardStatementSource ==
                         CreditCardStatementSource.PdfImport
                ? DateTimeOffset.UtcNow
                : null,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private CurrentStatementPaymentPlan BuildCurrentStatementPaymentPlan(
        decimal statementAmount)
    {
        var mode = SelectedCurrentStatementPaymentMode?.Value ??
                   CurrentStatementPaymentMode.Minimum;
        var custom = mode == CurrentStatementPaymentMode.Custom
            ? ParseMoney(
                CurrentStatementCustomPayment,
                "Bu ekstre için özel ödeme")
            : (decimal?)null;
        if (custom is decimal customAmount &&
            (customAmount < 0m || customAmount > statementAmount))
        {
            throw new InvalidOperationException(
                "Bu ekstre için ödeme tutarı 0 ile ekstre tutarı arasında olmalıdır.");
        }

        return new CurrentStatementPaymentPlan
        {
            Mode = mode,
            CustomAmount = custom
        };
    }

    private void ApplyStatementImport(
        CreditCardStatementImportResult result)
    {
        CardHasActualStatement = true;
        _cardStatementSource = CreditCardStatementSource.PdfImport;
        _cardStatementFingerprint = result.SourceDocumentFingerprint;
        _cardExactNextStatementDate = result.NextStatementDate;
        _cardExactNextDueDate = result.NextDueDate;
        if (!string.IsNullOrWhiteSpace(result.DetectedBank) &&
            string.IsNullOrWhiteSpace(CardBank))
        {
            CardBank = result.DetectedBank;
        }

        if (string.IsNullOrWhiteSpace(CardName))
        {
            CardName = result.DetectedBank.Contains(
                "Bonus",
                StringComparison.OrdinalIgnoreCase)
                ? "Bonus"
                : "Axess";
        }

        CardStatementDate = (result.StatementDate ?? _clock.Today)
            .ToDateTime(TimeOnly.MinValue);
        CardStatementDueDate = (result.DueDate ?? _clock.Today.AddDays(10))
            .ToDateTime(TimeOnly.MinValue);
        CardStatementAmount =
            result.StatementAmount?.ToString("N2", TurkishCulture) ??
            CardStatementAmount;
        CardStatementMinimum =
            result.MinimumPaymentAmount?.ToString("N2", TurkishCulture) ??
            CardStatementMinimum;
        RefreshCardNextDates();
        CardStatementImportWarnings =
            string.Join(Environment.NewLine, result.Warnings);
        HasCardStatementImportWarnings = result.Warnings.Count > 0;
        SelectedCurrentStatementPaymentMode ??=
            CurrentStatementPaymentModes[0];
    }

    private void RefreshCardNextDates()
    {
        if (!int.TryParse(CardClosingDay, out var closingDay) ||
            closingDay is < 1 or > 31 ||
            !int.TryParse(CardDueDay, out var dueDay) ||
            dueDay is < 1 or > 31)
        {
            CardNextStatementDate = "-";
            CardNextDueDate = "-";
            return;
        }

        var nextStatementDate =
            CreditCardStatementCalculator.ResolveNextStatementDate(
                DateOnly.FromDateTime(CardStatementDate),
                closingDay,
                _cardExactNextStatementDate);
        var nextDueDate = CreditCardStatementCalculator.ResolveNextDueDate(
            nextStatementDate,
            dueDay,
            _cardExactNextDueDate);
        CardNextStatementDate = nextStatementDate
            .ToString("dd.MM.yyyy", TurkishCulture);
        CardNextDueDate = nextDueDate
            .ToString("dd.MM.yyyy", TurkishCulture);
    }

    private DateOnly ResolveCardNextStatementDate()
    {
        if (!int.TryParse(CardClosingDay, out var closingDay))
        {
            throw new InvalidOperationException(
                "Kart kesim günü geçerli olmalıdır.");
        }

        return CreditCardStatementCalculator.ResolveNextStatementDate(
            DateOnly.FromDateTime(CardStatementDate),
            closingDay,
            _cardExactNextStatementDate);
    }

    private DateOnly ResolveCardNextDueDate()
    {
        if (!int.TryParse(CardDueDay, out var dueDay))
        {
            throw new InvalidOperationException(
                "Kart son ödeme günü geçerli olmalıdır.");
        }

        return CreditCardStatementCalculator.ResolveNextDueDate(
            ResolveCardNextStatementDate(),
            dueDay,
            _cardExactNextDueDate);
    }

    private Task ShowManualFallbackAsync(bool timedOut = false) =>
        _feedback.ShowErrorAsync(
        timedOut
            ? "Ekstreyi otomatik okumak uzun sürdü. Bilgileri elle girebilirsin."
            : "Bilgileri elle girebilirsin.",
        "Ekstre Otomatik Okunamadı",
        "Elle Gir");

    private static string CurrentStatementPlanLabel(
        CurrentStatementPaymentPlan? plan) => plan?.Mode switch
    {
        CurrentStatementPaymentMode.Full => "Tamamı",
        CurrentStatementPaymentMode.Custom =>
            $"Başka tutar {Money(plan.CustomAmount.GetValueOrDefault())}",
        CurrentStatementPaymentMode.Minimum => "Asgari",
        _ => "Henüz seçilmedi"
    };

    private static string RequireText(string value, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{field} gereklidir.")
            : value.Trim();

    private static decimal ParseNonNegativeMoney(
        string? value,
        string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0m;
        }

        var amount = ParseMoney(value, field);
        if (amount < 0m)
        {
            throw new InvalidOperationException($"{field} negatif olamaz.");
        }

        return amount;
    }

    private static decimal? ParseOptionalMoney(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : ParseNonNegativeMoney(value, "Tutar");

    private static string TryMoneyText(string value)
    {
        try
        {
            return Money(ParseNonNegativeMoney(value, "Tutar"));
        }
        catch
        {
            return value;
        }
    }

    // Mevcut tutar negatif olabilir; negatif değeri de biçimli göster.
    private static string TryMoneyTextSigned(string value)
    {
        try
        {
            return Money(ParseMoney(value, "Tutar"));
        }
        catch
        {
            return value;
        }
    }
}
