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

public partial class CommitmentsViewModel(
    CoinFlowService service,
    CreditCardStatementCalculator cardCalculator,
    CreditCardStatementImportWorkflow statementImportWorkflow,
    LoanPayoffService loanPayoffService,
    IUserFeedbackService feedback) : ViewModelBase
{
    public event Action<InitialPaymentStrategySetup>?
        InitialStrategySetupRequested;
    public ObservableCollection<SelectionOption<string>> RecordTypes { get; } = [];

    /// <summary>
    /// Simülatörle aynı koşul formu. Simüle edilebilen bir harcama, borç ya da
    /// gelir buradan doğrudan girilir; eskiden yalnız Simülatör → Planı Uygula
    /// yoluyla girilebiliyordu.
    /// </summary>
    public ScenarioConditionForm EntryForm { get; } = new(directEntryOnly: true);

    /// <summary>
    /// Açık formun kimliği. Form açılınca üretilir, kayıt başarılı olunca
    /// yenilenir; hızlı çift dokunuş aynı kimlikle gider ve ikinci kayıt
    /// oluşturmaz.
    /// </summary>
    private Guid _pendingEntryId = Guid.NewGuid();

    public ObservableCollection<SelectionOption<CreditCardPaymentStrategy>>
        PaymentStrategies { get; } =
    [
        new("Her ekstrede bana sor", CreditCardPaymentStrategy.AskEachStatement),
        new("Her ekstrede asgari öde", CreditCardPaymentStrategy.Minimum),
        new("Ekstrenin tamamını öde", CreditCardPaymentStrategy.FullStatement),
        new("Sabit tutar öde", CreditCardPaymentStrategy.FixedAmount)
    ];

    public ObservableCollection<SelectionOption<ProjectionFallbackStrategy>>
        ProjectionFallbackStrategies { get; } =
    [
        new("Hesaba katma", ProjectionFallbackStrategy.None),
        new("Asgari ödeme üzerinden hesapla", ProjectionFallbackStrategy.Minimum),
        new("Ekstrenin tamamı üzerinden hesapla", ProjectionFallbackStrategy.FullStatement),
        new("Sabit tutar üzerinden hesapla", ProjectionFallbackStrategy.FixedAmount)
    ];

    public ObservableCollection<SelectionOption<LoanKind>> LoanKinds { get; } =
    [
        new("Tüketici kredisi (ihtiyaç, taşıt)", LoanKind.Consumer),
        new("Konut kredisi — sabit faiz", LoanKind.HousingFixed),
        new("Konut kredisi — değişken faiz", LoanKind.HousingVariable)
    ];

    public ObservableCollection<SelectionOption<CurrentStatementPaymentMode>>
        CurrentStatementPaymentModes { get; } =
    [
        new("Asgari", CurrentStatementPaymentMode.Minimum),
        new("Tamamı", CurrentStatementPaymentMode.Full),
        new("Başka tutar", CurrentStatementPaymentMode.Custom)
    ];

    public ObservableCollection<FinancialRecordLine> IncomeItems { get; } = [];
    public ObservableCollection<FinancialRecordLine> CreditCardItems { get; } = [];
    public ObservableCollection<FinancialRecordLine> LoanItems { get; } = [];
    public ObservableCollection<FinancialRecordLine> RegularPaymentItems { get; } = [];
    public ObservableCollection<FinancialRecordLine> OneTimePaymentItems { get; } = [];
    public ObservableCollection<DatedAmountLine> PlanInstallments { get; } = [];
    public ObservableCollection<DatedAmountLine> CardFutureCharges { get; } = [];
    // Kart ödeme kararları Kart Kontrol ekranında verilir; kart bilgileri
    // düzenlenirken olduğu gibi geri yazılır, yoksa kayıt onları silerdi.
    private IReadOnlyList<CreditCardPaymentPlan> _editingCardPaymentPlans = [];

    private readonly List<FinancialRecordLine> _allItems = [];
    private readonly Dictionary<Guid, string> _cardChargeDescriptions = [];
    private Guid? _editingCardId;
    private Guid? _editingLoanId;
    private (decimal Amount, DateOnly AsOf)? _editingQuote;
    private DateOnly? _editingCardBalanceDate;
    private CreditCardStatement? _editingCardStatement;
    private string? _cardStatementFingerprint;
    private CreditCardStatementSource _cardStatementSource =
        CreditCardStatementSource.Manual;
    private DateOnly? _cardExactNextStatementDate;
    private DateOnly? _cardExactNextDueDate;
    private CancellationTokenSource? _statementImportCancellation;

    [ObservableProperty] private bool isIncomeSection = true;
    [ObservableProperty] private SelectionOption<string>? selectedRecordType;
    [ObservableProperty] private bool isSalary;
    [ObservableProperty] private bool isLoan;
    [ObservableProperty] private bool isPlan;
    [ObservableProperty] private bool isCard;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRecordForm))]
    private bool isScenarioEntry;

    /// <summary>Kayda özel formlar (gelir, kredi, kart, ödeme planı).</summary>
    public bool IsRecordForm => !IsScenarioEntry;
    [ObservableProperty] private bool hasActiveForm;
    [ObservableProperty] private string formTitle = "Yeni kayıt";
    [ObservableProperty] private string formLead = string.Empty;
    [ObservableProperty] private string structureSummary = "—";

    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private string bank = string.Empty;
    [ObservableProperty] private string amount = string.Empty;
    [ObservableProperty] private DateTime effectiveDate = DateTime.Today;

    [ObservableProperty] private string paymentDay = "10";
    [ObservableProperty] private DateTime nextPaymentDate = DateTime.Today.AddMonths(1);
    [ObservableProperty] private string installmentCount = "12";
    [ObservableProperty] private string remainingDebt = string.Empty;
    [ObservableProperty] private string earlyClosureAmount = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEarlyClosureNote))]
    private string earlyClosureNote = string.Empty;
    public bool HasEarlyClosureNote => EarlyClosureNote.Length > 0;
    [ObservableProperty] private SelectionOption<LoanKind>? selectedLoanKind;

    [ObservableProperty] private DateTime planPaymentDate = DateTime.Today.AddMonths(1);
    [ObservableProperty] private string planPaymentAmount = string.Empty;

    [ObservableProperty] private string cardLimit = string.Empty;
    [ObservableProperty] private bool cardHasActualStatement;
    [ObservableProperty] private bool isLegacyCardSetup = true;
    [ObservableProperty] private string carriedBalance = string.Empty;
    [ObservableProperty] private string unbilledSpending = string.Empty;
    [ObservableProperty] private DateTime cardBalanceDate = DateTime.Today;
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
    // §14 progressive disclosure: varsayılan davranış kuralları gelişmiş
    // bölümde saklı; kullanıcı isterse açar.
    [ObservableProperty] private bool showAdvancedCardOptions;
    [ObservableProperty] private bool hasNoIncomeItems = true;
    [ObservableProperty] private bool hasNoCreditCardItems = true;
    [ObservableProperty] private bool hasNoLoanItems = true;
    [ObservableProperty] private bool hasNoRegularPaymentItems = true;
    [ObservableProperty] private bool hasNoOneTimePaymentItems = true;

    [RelayCommand]
    private void ToggleAdvancedCardOptions() =>
        ShowAdvancedCardOptions = !ShowAdvancedCardOptions;
    [ObservableProperty] private string closingDay = "25";
    [ObservableProperty] private string dueDay = "5";
    [ObservableProperty] private string minimumRate = "40";
    [ObservableProperty] private DateTime cardChargeDate = DateTime.Today.AddMonths(1);
    [ObservableProperty] private string cardChargeAmount = string.Empty;
    [ObservableProperty] private SelectionOption<CreditCardPaymentStrategy>? selectedPaymentStrategy;
    [ObservableProperty] private string fixedPaymentAmount = string.Empty;
    [ObservableProperty] private bool isFixedPaymentStrategy;
    [ObservableProperty] private SelectionOption<ProjectionFallbackStrategy>? selectedProjectionFallbackStrategy;
    [ObservableProperty] private string projectionFallbackFixedAmount = string.Empty;
    [ObservableProperty] private bool isFixedProjectionFallback;
    [ObservableProperty] private bool isEditingCard;
    [ObservableProperty] private string saveButtonText = "Kaydet";

    public async Task LoadAsync()
    {
        var plan = await service.GetFinancialPlanAsync();
        _allItems.Clear();

        foreach (var salary in plan.Salaries.OrderByDescending(x => x.EffectiveDate))
        {
            _allItems.Add(new FinancialRecordLine(
                salary.Id,
                FinancialRecordKind.Salary,
                salary.Description.Length == 0 ? "Gelir" : salary.Description,
                $"Geçerli: {salary.EffectiveDate:dd.MM.yyyy}",
                Money(salary.Amount),
                salary.EffectiveDate > DateOnly.FromDateTime(DateTime.Today)
                    ? "Planlanan gelir"
                    : "Gelir"));
        }

        var initialSetup = await service
            .GetInitialPaymentStrategySetupAsync();
        if (initialSetup is not null)
        {
            InitialStrategySetupRequested?.Invoke(initialSetup);
        }

        foreach (var income in plan.OtherIncomes.OrderBy(x => x.ExactDate))
        {
            _allItems.Add(new FinancialRecordLine(
                income.Id,
                FinancialRecordKind.OtherIncome,
                income.Description.Length == 0 ? "Diğer gelir" : income.Description,
                income.ExactDate.ToString("dd.MM.yyyy"),
                Money(income.Amount),
                "Tek seferlik gelir"));
        }

        foreach (var overview in loanPayoffService.Describe(plan.Loans))
        {
            var loan = overview.Loan;
            _allItems.Add(new FinancialRecordLine(
                loan.Id,
                FinancialRecordKind.Loan,
                $"{loan.Bank} {loan.Name}".Trim(),
                $"Sonraki: {loan.NextPaymentDate:dd.MM.yyyy} • {loan.RemainingInstallmentCount} ödeme",
                Money(loan.MonthlyPayment),
                "Kredi",
                LoanInsight(overview),
                overview.IssueMessage is not null));
        }

        foreach (var planned in loanPayoffService.DescribePrepayments(plan))
        {
            var loanName = $"{planned.Loan.Bank} {planned.Loan.Name}".Trim();
            var mode = planned.Prepayment.Mode switch
            {
                LoanPrepaymentMode.FullClosure => "erken kapama",
                LoanPrepaymentMode.ReduceTerm => "ara ödeme · vade kısalır",
                _ => "ara ödeme · taksit azalır"
            };
            _allItems.Add(new FinancialRecordLine(
                planned.Prepayment.Id,
                FinancialRecordKind.LoanPrepayment,
                $"{loanName} · {mode}",
                $"Planlı: {planned.Prepayment.Date:dd.MM.yyyy}",
                planned.Amount is decimal amount ? Money(amount) : "—",
                "Erken ödeme",
                planned.IsUnquotable
                    ? "Kredinin anaparası hesaplanamadığı için bu ödeme projeksiyona girmiyor."
                    : "Simülatörden uygulandı. Silersen kredi eski ödeme planına döner.",
                planned.IsUnquotable));
        }

        foreach (var paymentPlan in plan.PaymentPlans)
        {
            var paymentDetail = paymentPlan.Kind == PaymentPlanKind.Installment &&
                                paymentPlan.OriginalAmount is decimal original &&
                                paymentPlan.TotalRepaymentAmount is decimal repayment
                ? $"Ana tutar: {Money(original)} • Toplam geri ödeme: {Money(repayment)} • {paymentPlan.Installments.Count} ödeme"
                : $"{paymentPlan.Installments.Count(x => !x.IsPaid)} ödeme • tarihleri belli";
            _allItems.Add(new FinancialRecordLine(
                paymentPlan.Id,
                paymentPlan.Kind == PaymentPlanKind.Temporary
                    ? FinancialRecordKind.TemporaryPlan
                    : FinancialRecordKind.InstallmentPlan,
                paymentPlan.Name,
                paymentDetail,
                Money(paymentPlan.Installments.Where(x => !x.IsPaid).Sum(x => x.Amount)),
                paymentPlan.Kind switch
                {
                    PaymentPlanKind.Temporary => "Geçici ödeme planı",
                    PaymentPlanKind.Installment => "Taksit / finansman",
                    PaymentPlanKind.Recurring => "Düzenli ödeme",
                    _ => "Planlı ödeme"
                }));
        }

        foreach (var card in plan.CreditCards)
        {
            var upcoming = cardCalculator.Project(
                card,
                1,
                useProjectionFallback: true)[0];
            var paymentText = card.CurrentStatement is { } statement
                ? $"Ekstre: {Money(statement.StatementAmount)} • Son ödeme: {statement.DueDate.ToString("dd MMM", TurkishCulture)}"
                : upcoming.Payment is decimal payment
                    ? $"Yaklaşan tahmini ödeme: {Money(payment)} • Son ödeme: {upcoming.PaymentDueDate:dd.MM.yyyy}"
                    : "Yaklaşan ödeme henüz belirlenmedi";
            var badgeText = card.CurrentStatement is not null
                ? $"Plan: {CurrentStatementPlanLabel(card.CurrentStatementPaymentPlan)}"
                : $"Ödeme tercihi: {StrategyLabel(card.PaymentStrategy)} • Henüz karar vermediğim ekstrelerde: {FallbackLabel(card.ProjectionFallbackStrategy)}";
            _allItems.Add(new FinancialRecordLine(
                card.Id,
                FinancialRecordKind.CreditCard,
                $"{card.Bank} {card.Name}".Trim(),
                paymentText,
                Money(card.KnownTotalDebt),
                badgeText));
        }

        foreach (var expense in plan.PlannedLargeExpenses)
        {
            _allItems.Add(new FinancialRecordLine(
                expense.Id,
                FinancialRecordKind.LargeExpense,
                expense.Name,
                $"{expense.ExactDate:dd.MM.yyyy} • {expense.Note}",
                Money(expense.Amount),
                "Planlı büyük ödeme"));
        }

        RefreshGroupedItems();
        RefreshRecordTypes();
        EntryForm.SetLookups(plan);
        SelectedPaymentStrategy ??= PaymentStrategies[0];
        SelectedProjectionFallbackStrategy ??=
            ProjectionFallbackStrategies[0];
        SelectedCurrentStatementPaymentMode ??=
            CurrentStatementPaymentModes[0];
    }

    public async Task<bool> CompleteInitialStrategySetupAsync(
        PaymentAssignmentMode mode)
    {
        try
        {
            await service.CompleteInitialPaymentStrategySetupAsync(mode);
            SetStatus(string.Empty);
            await feedback.ShowSuccessAsync(
                "Gelir kullanım düzeni kaydedildi.");
            return true;
        }
        catch (Exception exception)
        {
            var message = UserFacingMessages.FromException(exception);
            SetStatus(message);
            await feedback.ShowErrorAsync(message);
            return false;
        }
    }

    public void SelectIncomeSection()
    {
        IsIncomeSection = true;
        CancelEditingCard();
        RefreshRecordTypes();
    }

    public void SelectPaymentSection()
    {
        IsIncomeSection = false;
        CancelEditingCard();
        RefreshRecordTypes();
    }

    public void StartAdd(string recordType)
    {
        ResetForm();
        IsIncomeSection = recordType == "salary";
        RefreshRecordTypes();
        SelectedRecordType = RecordTypes.SingleOrDefault(x =>
            x.Value == recordType) ?? RecordTypes.FirstOrDefault();
        HasActiveForm = true;
        FormTitle = recordType switch
        {
            "salary" => "Gelir Ekle",
            "loan" => "Kredi Ekle",
            "card" => "Kredi Kartı Ekle",
            "temporary" => "Ödeme Planı Ekle",
            _ => "Yeni Kayıt"
        };
        FormLead = recordType switch
        {
            "salary" => "Düzenli gelir veya gelir değişikliği.",
            "loan" => "Bankada zaten devam eden kredinin taksitleri.",
            "card" => "Kart limiti, borç ve ödeme tercihleri.",
            "temporary" => "Tutarı ya da tarihi aydan aya değişen ödemeler; her ödemeyi tarihiyle ekle.",
            _ => string.Empty
        };
        SaveButtonText = "Kaydet";
    }

    /// <summary>
    /// Simülatördeki formu açar. Kaydet, simülasyonu uygulamakla aynı yoldan
    /// yazar; burada girilen kayıt simülasyonda görülen sonucu üretir.
    /// </summary>
    public void StartScenarioEntry(ScenarioGroup group)
    {
        ResetForm();
        SelectedRecordType = null;
        EntryForm.Reset();
        EntryForm.SelectGroup(group);
        _pendingEntryId = Guid.NewGuid();
        IsScenarioEntry = true;
        IsIncomeSection = group == ScenarioGroup.Income;
        HasActiveForm = true;
        FormTitle = group switch
        {
            ScenarioGroup.Spending => "Harcama Ekle",
            ScenarioGroup.Debt => "Borç veya Kredi Ekle",
            ScenarioGroup.Income => "Tek Seferlik Gelir Ekle",
            _ => "Yeni Kayıt"
        };
        FormLead = "Simülatördeki formun aynısı. Kaydettiğinde doğrudan finans planına eklenir; önce denemek istersen Simülatör'ü kullan.";
        SaveButtonText = "Kaydet";
    }

    partial void OnSelectedRecordTypeChanged(
        SelectionOption<string>? value) =>
        RefreshRecordFormFlags();

    partial void OnIsScenarioEntryChanged(bool value) =>
        RefreshRecordFormFlags();

    /// <summary>
    /// Ortak form açıkken kayda özel alanlar görünmez; sayfa yeniden
    /// yüklenince kayıt türü listesi varsayılana dönse bile.
    /// </summary>
    private void RefreshRecordFormFlags()
    {
        var type = IsScenarioEntry ? null : SelectedRecordType?.Value;
        IsSalary = type == "salary";
        IsLoan = type == "loan";
        IsPlan = type == "temporary";
        IsCard = type == "card";
    }

    partial void OnSelectedPaymentStrategyChanged(
        SelectionOption<CreditCardPaymentStrategy>? value) =>
        IsFixedPaymentStrategy =
            value?.Value == CreditCardPaymentStrategy.FixedAmount;

    partial void OnSelectedProjectionFallbackStrategyChanged(
        SelectionOption<ProjectionFallbackStrategy>? value) =>
        IsFixedProjectionFallback =
            value?.Value == ProjectionFallbackStrategy.FixedAmount;

    partial void OnCardHasActualStatementChanged(bool value) =>
        IsLegacyCardSetup = !value;

    partial void OnSelectedCurrentStatementPaymentModeChanged(
        SelectionOption<CurrentStatementPaymentMode>? value) =>
        IsCurrentStatementCustomPayment =
            value?.Value == CurrentStatementPaymentMode.Custom;

    partial void OnCardStatementDateChanged(DateTime value) =>
        RefreshCardNextDates();

    partial void OnClosingDayChanged(string value) =>
        RefreshCardNextDates();

    partial void OnDueDayChanged(string value) =>
        RefreshCardNextDates();

    [RelayCommand]
    private void AddPlanPayment()
    {
        try
        {
            var parsed = RequirePositive(
                ParseMoney(PlanPaymentAmount, "Ödeme tutarı"),
                "Ödeme tutarı");
            PlanInstallments.Add(new DatedAmountLine(
                Guid.NewGuid(),
                DateOnly.FromDateTime(PlanPaymentDate),
                parsed));
            PlanPaymentAmount = string.Empty;
            SetStatus(string.Empty);
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(exception));
        }
    }

    [RelayCommand]
    private void AddCardCharge()
    {
        try
        {
            var parsed = RequirePositive(
                ParseMoney(CardChargeAmount, "Kart harcaması tutarı"),
                "Kart harcaması tutarı");
            var id = Guid.NewGuid();
            CardFutureCharges.Add(new DatedAmountLine(
                id,
                DateOnly.FromDateTime(CardChargeDate),
                parsed,
                "Gelecek taksit"));
            _cardChargeDescriptions[id] = CardFutureCharges[^1].Description;
            CardChargeAmount = string.Empty;
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
        CardStatementDate = DateTime.Today;
        CardStatementDueDate = DateTime.Today;
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
            var attempt = await statementImportWorkflow
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
            statementImportWorkflow.NotifyPreviewStarted();
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

    public void RemovePlanPayment(DatedAmountLine line) =>
        PlanInstallments.Remove(line);

    public void RemoveCardCharge(DatedAmountLine line)
    {
        CardFutureCharges.Remove(line);
        _cardChargeDescriptions.Remove(line.Id);
    }

    public async Task EditCardAsync(Guid cardId)
    {
        var card = (await service.GetFinancialPlanAsync()).CreditCards
            .Single(x => x.Id == cardId);
        _editingCardId = card.Id;
        _editingCardBalanceDate = card.BalanceAsOfDate;
        _editingCardStatement = card.CurrentStatement;
        _cardStatementFingerprint =
            card.CurrentStatement?.SourceDocumentFingerprint;
        _cardStatementSource =
            card.CurrentStatement?.Source ?? CreditCardStatementSource.Manual;
        _cardExactNextStatementDate =
            card.CurrentStatement?.NextStatementDate;
        _cardExactNextDueDate = card.CurrentStatement?.NextDueDate;
        IsIncomeSection = false;
        RefreshRecordTypes();
        SelectedRecordType = RecordTypes.Single(x => x.Value == "card");
        HasActiveForm = true;
        FormTitle = "Kart Bilgilerini Düzenle";
        FormLead = "Sık kararlar kart kontrol ekranında; burada kartın temel bilgileri var.";
        IsEditingCard = true;
        SaveButtonText = "Değişiklikleri Kaydet";
        Name = card.Name;
        Bank = card.Bank;
        CardLimit = card.Limit.ToString("N2", TurkishCulture);
        CardHasActualStatement = card.CurrentStatement is not null;
        CarriedBalance = card.CarriedBalance.ToString("N2", TurkishCulture);
        UnbilledSpending = card.UnbilledSpending.ToString("N2", TurkishCulture);
        CardBalanceDate = card.BalanceAsOfDate.ToDateTime(TimeOnly.MinValue);
        if (card.CurrentStatement is { } statement)
        {
            CardStatementAmount = statement.StatementAmount
                .ToString("N2", TurkishCulture);
            CardStatementMinimum = statement.MinimumPaymentAmount
                .ToString("N2", TurkishCulture);
            CardStatementDate =
                statement.StatementDate.ToDateTime(TimeOnly.MinValue);
            CardStatementDueDate =
                statement.DueDate.ToDateTime(TimeOnly.MinValue);
            RefreshCardNextDates();
            SelectedCurrentStatementPaymentMode =
                CurrentStatementPaymentModes.Single(x =>
                    x.Value == (card.CurrentStatementPaymentPlan?.Mode ??
                                CurrentStatementPaymentMode.Minimum));
            CurrentStatementCustomPayment =
                card.CurrentStatementPaymentPlan?.CustomAmount
                    ?.ToString("N2", TurkishCulture) ?? string.Empty;
        }
        else
        {
            CardStatementAmount = string.Empty;
            CardStatementMinimum = string.Empty;
            _cardExactNextStatementDate = null;
            _cardExactNextDueDate = null;
            CardNextStatementDate = string.Empty;
            CardNextDueDate = string.Empty;
            SelectedCurrentStatementPaymentMode =
                CurrentStatementPaymentModes[0];
            CurrentStatementCustomPayment = string.Empty;
        }
        ClosingDay = card.StatementClosingDay.ToString(TurkishCulture);
        DueDay = card.PaymentDueDay.ToString(TurkishCulture);
        MinimumRate = (card.MinimumPaymentRate * 100m).ToString("N2", TurkishCulture);
        SelectedPaymentStrategy = PaymentStrategies.Single(x =>
            x.Value == card.PaymentStrategy);
        FixedPaymentAmount = card.FixedPaymentAmount?.ToString("N2", TurkishCulture) ?? string.Empty;
        SelectedProjectionFallbackStrategy =
            ProjectionFallbackStrategies.Single(x =>
                x.Value == card.ProjectionFallbackStrategy);
        ProjectionFallbackFixedAmount =
            card.ProjectionFallbackFixedAmount?.ToString("N2", TurkishCulture) ?? string.Empty;

        CardFutureCharges.Clear();
        _cardChargeDescriptions.Clear();
        foreach (var charge in card.Charges)
        {
            CardFutureCharges.Add(new DatedAmountLine(
                charge.Id,
                charge.PostingDate,
                charge.Amount,
                charge.Description));
            _cardChargeDescriptions[charge.Id] = charge.Description;
        }

        _editingCardPaymentPlans = card.PaymentPlans;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (IsScenarioEntry)
        {
            await SaveScenarioEntryAsync();
            return;
        }

        Func<Task> persist;
        string successMessage;
        try
        {
            persist = BuildPersistOperation(out successMessage);
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(exception));
            return;
        }

        try
        {
            IsBusy = true;
            SetStatus(string.Empty);
            await persist();
            SetStatus(string.Empty);
            await feedback.ShowSuccessAsync(successMessage);
            ResetForm();
            await LoadAsync();
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

    private async Task SaveScenarioEntryAsync()
    {
        SimulationRequest request;
        try
        {
            request = EntryForm.BuildRequest(_pendingEntryId);
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(exception));
            return;
        }

        try
        {
            IsBusy = true;
            SetStatus(string.Empty);
            var result = await service.AddRecordFromScenarioAsync(request);
            await feedback.ShowSuccessAsync(result.AlreadyApplied
                ? "Bu kayıt zaten eklenmişti."
                : $"{SimulationScenarioCatalog.TypeText(request.Type)} kaydedildi.");
            ResetForm();
            _pendingEntryId = Guid.NewGuid();
            await LoadAsync();
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

    public async Task DeleteAsync(FinancialRecordLine item)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            switch (item.Kind)
            {
                case FinancialRecordKind.Salary:
                    await service.DeleteSalaryAsync(item.Id);
                    break;
                case FinancialRecordKind.OtherIncome:
                    await service.DeleteOtherIncomeAsync(item.Id);
                    break;
                case FinancialRecordKind.Loan:
                    await service.DeleteLoanAsync(item.Id);
                    break;
                case FinancialRecordKind.LoanPrepayment:
                    await service.DeleteLoanPrepaymentAsync(item.Id);
                    break;
                case FinancialRecordKind.CreditCard:
                    await service.DeleteCreditCardAsync(item.Id);
                    break;
                case FinancialRecordKind.TemporaryPlan:
                case FinancialRecordKind.InstallmentPlan:
                    await service.DeletePaymentPlanAsync(item.Id);
                    break;
                case FinancialRecordKind.LargeExpense:
                    await service.DeletePlannedLargeExpenseAsync(item.Id);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(item.Kind));
            }

            SetStatus(string.Empty);
            await LoadAsync();
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

    private Func<Task> BuildPersistOperation(out string successMessage)
    {
        switch (SelectedRecordType?.Value)
        {
            case "salary":
                var salary = new SalaryScheduleEntry
                {
                    Amount = RequirePositive(ParseMoney(Amount, "Gelir"), "Gelir"),
                    EffectiveDate = DateOnly.FromDateTime(EffectiveDate),
                    Description = string.IsNullOrWhiteSpace(Name) ? "Gelir" : Name.Trim()
                };
                successMessage = "Gelir kaydedildi.";
                return () => service.SaveSalaryAsync(salary);
            case "loan":
                var loan = BuildLoan();
                successMessage = "Kredi kaydedildi.";
                return () => service.SaveLoanAsync(loan);
            case "temporary":
                var plan = BuildPlan();
                successMessage = "Ödeme planı kaydedildi.";
                return () => service.SavePaymentPlanAsync(plan);
            case "card":
                var wasEditingCard = IsEditingCard;
                var card = BuildCard();
                successMessage = wasEditingCard
                    ? "Kredi kartı güncellendi."
                    : "Kredi kartı kaydedildi.";
                return () => service.SaveCreditCardAsync(card);
            default:
                throw new InvalidOperationException("Kayıt türü seçilmelidir.");
        }
    }

    [RelayCommand]
    private void CancelEditingCard()
    {
        _editingCardId = null;
        _editingLoanId = null;
        _editingCardBalanceDate = null;
        _editingCardStatement = null;
        _cardStatementFingerprint = null;
        _cardStatementSource = CreditCardStatementSource.Manual;
        _cardExactNextStatementDate = null;
        _cardExactNextDueDate = null;
        IsEditingCard = false;
        IsScenarioEntry = false;
        HasActiveForm = false;
        SaveButtonText = "Kaydet";
        CardFutureCharges.Clear();
        _editingCardPaymentPlans = [];
        _cardChargeDescriptions.Clear();
    }

    private Loan BuildLoan()
    {
        if (!int.TryParse(PaymentDay, out var day))
        {
            throw new InvalidOperationException("Ödeme günü geçerli olmalıdır.");
        }

        if (!int.TryParse(InstallmentCount, out var count))
        {
            throw new InvalidOperationException("Kalan taksit sayısı geçerli olmalıdır.");
        }

        var closureAmount = ParseOptionalMoney(EarlyClosureAmount);
        return new Loan
        {
            Id = _editingLoanId ?? Guid.NewGuid(),
            Name = RequireName(),
            Bank = Bank.Trim(),
            MonthlyPayment = RequirePositive(ParseMoney(Amount, "Aylık ödeme"), "Aylık ödeme"),
            PaymentDay = day,
            NextPaymentDate = DateOnly.FromDateTime(NextPaymentDate),
            RemainingInstallmentCount = count,
            RemainingDebt = ParseOptionalMoney(RemainingDebt),
            EarlyClosureAmount = closureAmount,
            // Banka tutarı yalnız görüldüğü gün geçerlidir. Kayıtlı tutar
            // değişmediyse kendi tarihi korunur; yeni tutarı servis bugünün
            // tarihiyle damgalar.
            EarlyClosureAmountAsOf =
                closureAmount is decimal entered &&
                _editingQuote is { } stored &&
                stored.Amount == entered
                    ? stored.AsOf
                    : null,
            Kind = SelectedLoanKind?.Value ?? LoanKind.Consumer
        };
    }

    public async Task EditLoanAsync(Guid loanId)
    {
        var loan = (await service.GetFinancialPlanAsync()).Loans
            .Single(x => x.Id == loanId);
        ResetForm();
        _editingLoanId = loan.Id;
        IsIncomeSection = false;
        RefreshRecordTypes();
        SelectedRecordType = RecordTypes.Single(x => x.Value == "loan");
        HasActiveForm = true;
        FormTitle = "Krediyi Düzenle";
        FormLead = "Kalan anaparayı ya da bankadan aldığın kapatma tutarını " +
                   "güncel tut; erken kapama hesabı bunlardan yapılır.";
        SaveButtonText = "Değişiklikleri Kaydet";
        Name = loan.Name;
        Bank = loan.Bank;
        Amount = loan.MonthlyPayment.ToString("N2", TurkishCulture);
        PaymentDay = loan.PaymentDay.ToString(TurkishCulture);
        InstallmentCount =
            loan.RemainingInstallmentCount.ToString(TurkishCulture);
        NextPaymentDate = loan.NextPaymentDate.ToDateTime(TimeOnly.MinValue);
        RemainingDebt = loan.RemainingDebt?.ToString("N2", TurkishCulture) ??
                        string.Empty;
        SelectedLoanKind = LoanKinds.Single(x => x.Value == loan.Kind);
        // Bayat banka tutarı forma geri gelmez: tarihi son ödenen taksitten
        // eskiyse artık hiçbir şeyi kalibre etmez ve kaydederken reddedilir.
        var quoteIsCurrent =
            loan.EarlyClosureAmount is not null &&
            loan.EarlyClosureAmountAsOf is DateOnly asOf &&
            asOf >= LoanAmortizationCalculator.PreviousDueDate(loan);
        EarlyClosureAmount = quoteIsCurrent
            ? loan.EarlyClosureAmount!.Value.ToString("N2", TurkishCulture)
            : string.Empty;
        _editingQuote = quoteIsCurrent
            ? (loan.EarlyClosureAmount!.Value, loan.EarlyClosureAmountAsOf!.Value)
            : null;
        EarlyClosureNote = quoteIsCurrent
            ? $"Kayıtlı tutar {loan.EarlyClosureAmountAsOf!.Value:dd.MM.yyyy} tarihli. " +
              "Değiştirirsen bugünün tarihiyle saklanır."
            : string.Empty;
    }

    private static string LoanInsight(LoanPayoffOverview overview)
    {
        if (overview.IssueMessage is { } issue)
        {
            return issue;
        }

        if (overview.Analysis.Amortization is not { } amortization ||
            overview.Today is not { } today)
        {
            return string.Empty;
        }

        var rate =
            $"aylık %{(amortization.MonthlyRate * 100m).ToString("N2", TurkishCulture)}";
        var source = amortization.Source == LoanRateSource.BankQuote
            ? " · banka tutarından"
            : string.Empty;
        var head =
            $"Kalan anapara {Money(amortization.Principal)} · {rate}{source}";
        if (!today.HasAnythingToClose)
        {
            return head;
        }

        var fee = today.Fee > 0m
            ? $" ({Money(today.Fee)} erken ödeme ücreti dahil)"
            : string.Empty;
        return $"{head}\nBugün kapatma ≈ {Money(today.Amount)}{fee} · " +
               $"{Money(today.InterestSaving)} faiz ödemezsin";
    }

    private TemporaryPaymentPlan BuildPlan()
    {
        if (PlanInstallments.Count == 0)
        {
            throw new InvalidOperationException("En az bir ödeme eklemelisin.");
        }

        var id = Guid.NewGuid();
        return new TemporaryPaymentPlan
        {
            Id = id,
            Name = RequireName(),
            // Eşit tutarlı taksit, düzenli ödeme ve finansman ortak formdan
            // girilir; buradaki elle takvim yalnız düzensiz planlar içindir.
            Kind = PaymentPlanKind.Temporary,
            Installments = PlanInstallments
                .OrderBy(x => x.Date)
                .Select(x => new TemporaryPaymentInstallment
                {
                    Id = x.Id,
                    PlanId = id,
                    DueDate = x.Date,
                    Amount = x.Amount
                })
                .ToArray()
        };
    }

    private CreditCard BuildCard()
    {
        if (!int.TryParse(ClosingDay, out var closeDay) ||
            !int.TryParse(DueDay, out var paymentDueDay))
        {
            throw new InvalidOperationException("Kart günleri geçerli olmalıdır.");
        }

        var minimumRatePercent = ParseMoney(MinimumRate, "Asgari oran");
        var strategy = SelectedPaymentStrategy?.Value
            ?? CreditCardPaymentStrategy.AskEachStatement;
        var fallback = SelectedProjectionFallbackStrategy?.Value
            ?? ProjectionFallbackStrategy.None;
        var cardId = _editingCardId ?? Guid.NewGuid();
        var actualStatement = CardHasActualStatement
            ? BuildCurrentStatement(cardId)
            : null;
        var currentStatementPaymentPlan = actualStatement is null
            ? null
            : BuildCurrentStatementPaymentPlan(
                actualStatement.StatementAmount);
        var card = new CreditCard
        {
            Id = cardId,
            Name = RequireName(),
            Bank = Bank.Trim(),
            Limit = RequirePositive(ParseMoney(CardLimit, "Kart limiti"), "Kart limiti"),
            CarriedBalance = actualStatement is null
                ? Math.Max(0m, ParseMoney(CarriedBalance, "Devreden bakiye"))
                : 0m,
            UnbilledSpending = actualStatement is null
                ? Math.Max(0m, ParseMoney(UnbilledSpending, "Ekstreleşmemiş harcama"))
                : 0m,
            BalanceAsOfDate = actualStatement?.StatementDate ??
                (_editingCardBalanceDate ??
                 DateOnly.FromDateTime(CardBalanceDate)),
            StatementClosingDay = closeDay,
            PaymentDueDay = paymentDueDay,
            MinimumPaymentRate = minimumRatePercent / 100m,
            PaymentStrategy = strategy,
            FixedPaymentAmount = strategy == CreditCardPaymentStrategy.FixedAmount
                ? RequirePositive(ParseMoney(FixedPaymentAmount, "Sabit ödeme"), "Sabit ödeme")
                : null,
            ProjectionFallbackStrategy = fallback,
            ProjectionFallbackFixedAmount =
                fallback == ProjectionFallbackStrategy.FixedAmount
                    ? RequirePositive(ParseMoney(
                        ProjectionFallbackFixedAmount,
                        "Gelecek hesaplamada kullanılacak sabit tutar"),
                        "Gelecek hesaplamada kullanılacak sabit tutar")
                    : null,
            CurrentStatement = actualStatement,
            CurrentStatementPaymentPlan = currentStatementPaymentPlan,
            Charges = CardFutureCharges
                .OrderBy(x => x.Date)
                .Select(x => new CardCharge
                {
                    Id = x.Id,
                    CreditCardId = cardId,
                    Description = _cardChargeDescriptions.GetValueOrDefault(x.Id, "Gelecek taksit"),
                    PostingDate = x.Date,
                    Amount = x.Amount
                })
                .ToArray(),
            PaymentPlans = _editingCardPaymentPlans
                .OrderBy(x => x.DueDate)
                .Select(x => x with { CreditCardId = cardId })
                .ToArray()
        };
        return card;
    }

    private CreditCardStatement BuildCurrentStatement(Guid cardId)
    {
        var amount = RequirePositive(
            ParseMoney(CardStatementAmount, "Ekstre tutarı"),
            "Ekstre tutarı");
        var minimum = ParseMoney(CardStatementMinimum, "Asgari ödeme");
        if (minimum < 0m || minimum > amount)
        {
            throw new InvalidOperationException(
                "Asgari ödeme 0 ile ekstre tutarı arasında olmalıdır.");
        }

        return new CreditCardStatement
        {
            Id = _editingCardStatement?.Id ?? Guid.NewGuid(),
            CreditCardId = cardId,
            StatementDate = DateOnly.FromDateTime(CardStatementDate),
            DueDate = DateOnly.FromDateTime(CardStatementDueDate),
            StatementAmount = amount,
            MinimumPaymentAmount = minimum,
            NextStatementDate = ResolveCardNextStatementDate(),
            NextDueDate = ResolveCardNextDueDate(),
            Source = _cardStatementSource,
            SourceDocumentFingerprint = _cardStatementFingerprint,
            ImportedAt = _cardStatementSource == CreditCardStatementSource.PdfImport
                ? _editingCardStatement?.ImportedAt ?? DateTimeOffset.UtcNow
                : null,
            CreatedAt = _editingCardStatement?.CreatedAt ??
                        DateTimeOffset.UtcNow,
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

    private void RefreshRecordTypes()
    {
        var selected = SelectedRecordType?.Value;
        RecordTypes.Clear();
        if (IsIncomeSection)
        {
            RecordTypes.Add(new SelectionOption<string>("Gelir / Gelir değişikliği", "salary"));
        }
        else
        {
            RecordTypes.Add(new SelectionOption<string>("Kredi", "loan"));
            RecordTypes.Add(new SelectionOption<string>("Kredi kartı", "card"));
            RecordTypes.Add(new SelectionOption<string>("Ödeme planı", "temporary"));
        }

        SelectedRecordType =
            RecordTypes.FirstOrDefault(x => x.Value == selected) ??
            RecordTypes.FirstOrDefault();
    }

    private void ResetForm()
    {
        Name = string.Empty;
        Bank = string.Empty;
        Amount = string.Empty;
        RemainingDebt = string.Empty;
        EarlyClosureAmount = string.Empty;
        EarlyClosureNote = string.Empty;
        _editingQuote = null;
        SelectedLoanKind = LoanKinds[0];
        PlanInstallments.Clear();
        CardFutureCharges.Clear();
        _editingCardPaymentPlans = [];
        _cardChargeDescriptions.Clear();
        CardHasActualStatement = false;
        CardStatementAmount = string.Empty;
        CardStatementMinimum = string.Empty;
        CardStatementDate = DateTime.Today;
        CardStatementDueDate = DateTime.Today;
        CardNextStatementDate = string.Empty;
        CardNextDueDate = string.Empty;
        _cardExactNextStatementDate = null;
        _cardExactNextDueDate = null;
        CardStatementImportWarnings = string.Empty;
        HasCardStatementImportWarnings = false;
        CurrentStatementCustomPayment = string.Empty;
        SelectedCurrentStatementPaymentMode =
            CurrentStatementPaymentModes[0];
        CancelEditingCard();
    }

    private void RefreshGroupedItems()
    {
        IncomeItems.Clear();
        CreditCardItems.Clear();
        LoanItems.Clear();
        RegularPaymentItems.Clear();
        OneTimePaymentItems.Clear();

        foreach (var item in _allItems)
        {
            switch (item.Kind)
            {
                case FinancialRecordKind.Salary:
                case FinancialRecordKind.OtherIncome:
                    IncomeItems.Add(item);
                    break;
                case FinancialRecordKind.CreditCard:
                    CreditCardItems.Add(item);
                    break;
                case FinancialRecordKind.Loan:
                case FinancialRecordKind.LoanPrepayment:
                    LoanItems.Add(item);
                    break;
                case FinancialRecordKind.InstallmentPlan:
                    RegularPaymentItems.Add(item);
                    break;
                case FinancialRecordKind.TemporaryPlan:
                case FinancialRecordKind.LargeExpense:
                    OneTimePaymentItems.Add(item);
                    break;
                default:
                    break;
            }
        }

        // §12 — her bölümün kendi boş durumu olsun.
        HasNoIncomeItems = IncomeItems.Count == 0;
        HasNoCreditCardItems = CreditCardItems.Count == 0;
        HasNoLoanItems = LoanItems.Count == 0;
        HasNoRegularPaymentItems = RegularPaymentItems.Count == 0;
        HasNoOneTimePaymentItems = OneTimePaymentItems.Count == 0;
        StructureSummary =
            $"{IncomeItems.Count} gelir • {CreditCardItems.Count} kart • " +
            $"{LoanItems.Count(x => x.Kind == FinancialRecordKind.Loan)} kredi • {RegularPaymentItems.Count + OneTimePaymentItems.Count} ödeme";
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
            string.IsNullOrWhiteSpace(Bank))
        {
            Bank = result.DetectedBank;
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            Name = result.DetectedBank.Contains(
                "Bonus",
                StringComparison.OrdinalIgnoreCase)
                ? "Bonus"
                : "Axess";
        }

        CardStatementDate = (result.StatementDate ??
                             DateOnly.FromDateTime(DateTime.Today))
            .ToDateTime(TimeOnly.MinValue);
        CardStatementDueDate = (result.DueDate ??
                                DateOnly.FromDateTime(DateTime.Today))
            .ToDateTime(TimeOnly.MinValue);
        CardStatementAmount =
            result.StatementAmount?.ToString("N2", TurkishCulture) ??
            CardStatementAmount;
        CardStatementMinimum =
            result.MinimumPaymentAmount?.ToString("N2", TurkishCulture) ??
            CardStatementMinimum;
        RefreshCardNextDates();
        SelectedCurrentStatementPaymentMode ??=
            CurrentStatementPaymentModes[0];
        CardStatementImportWarnings =
            string.Join(Environment.NewLine, result.Warnings);
        HasCardStatementImportWarnings = result.Warnings.Count > 0;
    }

    private void RefreshCardNextDates()
    {
        if (!int.TryParse(ClosingDay, out var closingDay) ||
            closingDay is < 1 or > 31 ||
            !int.TryParse(DueDay, out var dueDay) ||
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
        if (!int.TryParse(ClosingDay, out var closingDay))
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
        if (!int.TryParse(DueDay, out var dueDay))
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
        feedback.ShowErrorAsync(
        timedOut
            ? "Ekstreyi otomatik okumak uzun sürdü. Bilgileri elle girebilirsin."
            : "Bilgileri elle girebilirsin.",
        "Ekstre Otomatik Okunamadı",
        "Elle Gir");

    private string RequireName() =>
        string.IsNullOrWhiteSpace(Name)
            ? throw new InvalidOperationException("Kayıt adı gereklidir.")
            : Name.Trim();

    private static decimal RequirePositive(decimal value, string field) =>
        value > 0m
            ? value
            : throw new InvalidOperationException($"{field} sıfırdan büyük olmalıdır.");

    private static decimal? ParseOptionalMoney(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : ParseMoney(value, "Tutar");

    private static string StrategyLabel(CreditCardPaymentStrategy strategy) =>
        strategy switch
        {
            CreditCardPaymentStrategy.AskEachStatement => "Her ekstrede sor",
            CreditCardPaymentStrategy.Minimum => "Her ekstrede asgari öde",
            CreditCardPaymentStrategy.FullStatement => "Ekstrenin tamamını öde",
            CreditCardPaymentStrategy.FixedAmount => "Sabit tutar",
            _ => "—"
        };

    private static string FallbackLabel(ProjectionFallbackStrategy strategy) =>
        strategy switch
        {
            ProjectionFallbackStrategy.None => "Hesaba katma",
            ProjectionFallbackStrategy.Minimum => "Asgari ödeme üzerinden hesapla",
            ProjectionFallbackStrategy.FullStatement => "Ekstrenin tamamı üzerinden hesapla",
            ProjectionFallbackStrategy.FixedAmount => "Sabit tutar üzerinden hesapla",
            _ => "—"
        };

    private static string CurrentStatementPlanLabel(
        CurrentStatementPaymentPlan? plan) => plan?.Mode switch
    {
        CurrentStatementPaymentMode.Full => "Tamamı",
        CurrentStatementPaymentMode.Custom =>
            $"Başka tutar {Money(plan.CustomAmount.GetValueOrDefault())}",
        CurrentStatementPaymentMode.Minimum => "Asgari",
        _ => "Henüz seçilmedi"
    };

}
