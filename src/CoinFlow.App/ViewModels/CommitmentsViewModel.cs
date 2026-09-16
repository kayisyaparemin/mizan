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

    public ScenarioConditionForm EntryForm { get; } = new(directEntryOnly: true);

    private Guid _pendingEntryId = Guid.NewGuid();

    public ObservableCollection<FinancialRecordLine> IncomeItems { get; } = [];
    public ObservableCollection<FinancialRecordLine> CreditCardItems { get; } = [];
    public ObservableCollection<FinancialRecordLine> LoanItems { get; } = [];
    public ObservableCollection<FinancialRecordLine> RegularPaymentItems { get; } = [];
    public ObservableCollection<FinancialRecordLine> OneTimePaymentItems { get; } = [];
    public ObservableCollection<DatedAmountLine> PlanInstallments { get; } = [];
    public ObservableCollection<DatedAmountLine> CardFutureCharges { get; } = [];

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

    private string? _recordType;
    private RecordEntryPicker? _entryPicker;

    public RecordEntryPicker EntryPicker => _entryPicker ??= CreateEntryPicker();
    [ObservableProperty] private bool showEntryPicker;
    [ObservableProperty] private bool isSalary;
    [ObservableProperty] private bool isLoan;
    [ObservableProperty] private bool isPlan;
    [ObservableProperty] private bool isCard;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRecordForm))]
    private bool isScenarioEntry;

    public bool IsRecordForm => !IsScenarioEntry;
    [ObservableProperty] private bool hasActiveForm;
    [ObservableProperty] private string formTitle = "Yeni kayıt";
    [ObservableProperty] private string formLead = string.Empty;
    [ObservableProperty] private string namePlaceholder = "Kayıt adı";
    [ObservableProperty] private string structureSummary = "—";

    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private string bank = string.Empty;
    [ObservableProperty] private string amount = string.Empty;
    [ObservableProperty] private DateTime effectiveDate = DateTime.Today;

    [ObservableProperty] private string paymentDay = string.Empty;
    [ObservableProperty] private DateTime nextPaymentDate = DateTime.Today.AddMonths(1);
    [ObservableProperty] private string installmentCount = string.Empty;
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
    [ObservableProperty] private bool showAdvancedCardOptions;
    [ObservableProperty] private bool hasNoIncomeItems = true;
    [ObservableProperty] private bool hasNoCreditCardItems = true;
    [ObservableProperty] private bool hasNoLoanItems = true;
    [ObservableProperty] private bool hasNoRegularPaymentItems = true;
    [ObservableProperty] private bool hasNoOneTimePaymentItems = true;

    [ObservableProperty] private string closingDay = string.Empty;
    [ObservableProperty] private string dueDay = string.Empty;
    [ObservableProperty] private string minimumRate = string.Empty;
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
        var initialSetup = await service
            .GetInitialPaymentStrategySetupAsync();
        if (initialSetup is not null)
        {
            InitialStrategySetupRequested?.Invoke(initialSetup);
        }

        PopulateAllItems(plan);
        RefreshGroupedItems();
    }

    private RecordEntryPicker CreateEntryPicker()
    {
        var picker = new RecordEntryPicker();
        picker.OptionSelected += ApplyEntryOption;
        return picker;
    }

    private void ApplyEntryOption(RecordEntryOption option)
    {
        FormLead = option.Scenario is null
            ? option.Description
            : "Kaydettiğinde doğrudan finans planına eklenir; önce denemek istersen Simülatör'ü kullan.";
        if (option.Scenario is { } scenario)
        {
            EntryForm.SelectOption(scenario);
            _recordType = null;
            IsScenarioEntry = true;
        }
        else
        {
            _recordType = RecordTypeFor(option.Form);
            NamePlaceholder = NamePlaceholderFor(_recordType);
            IsScenarioEntry = false;
        }

        RefreshRecordFormFlags();
        SetStatus(string.Empty);
    }

    private static string RecordTypeFor(RecordEntryForm form) => form switch
    {
        RecordEntryForm.Salary => "salary",
        RecordEntryForm.Loan => "loan",
        RecordEntryForm.CreditCard => "card",
        RecordEntryForm.PaymentPlan => "temporary",
        _ => throw new ArgumentOutOfRangeException(nameof(form))
    };

    private static string NamePlaceholderFor(string recordType) => recordType switch
    {
        "salary" => "Örn. Maaş, kira geliri",
        "loan" => "Örn. İhtiyaç kredisi, taşıt kredisi",
        "card" => "Kartın adı",
        "temporary" => "Örn. Okul taksidi, vergi borcu",
        _ => "Kayıt adı"
    };

    partial void OnIsScenarioEntryChanged(bool value) =>
        RefreshRecordFormFlags();

    private void RefreshRecordFormFlags()
    {
        var type = IsScenarioEntry ? null : _recordType;
        IsSalary = type == "salary";
        IsLoan = type == "loan";
        IsPlan = type == "temporary";
        IsCard = type == "card";
    }

    private Func<Task> BuildPersistOperation(out string successMessage)
    {
        switch (_recordType)
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
        _recordType = null;
        RefreshRecordFormFlags();
        ShowEntryPicker = false;
        HasActiveForm = false;
        SaveButtonText = "Kaydet";
        CardFutureCharges.Clear();
        _editingCardPaymentPlans = [];
        _cardChargeDescriptions.Clear();
    }

    private void OpenRecordFormForEdit(string recordType)
    {
        _recordType = recordType;
        IsScenarioEntry = false;
        ShowEntryPicker = false;
        RefreshRecordFormFlags();
        NamePlaceholder = NamePlaceholderFor(recordType);
        HasActiveForm = true;
    }

    private void ResetForm()
    {
        Name = string.Empty;
        Bank = string.Empty;
        Amount = string.Empty;
        PaymentDay = string.Empty;
        InstallmentCount = string.Empty;
        RemainingDebt = string.Empty;
        EarlyClosureAmount = string.Empty;
        PlanPaymentAmount = string.Empty;
        CardLimit = string.Empty;
        CarriedBalance = string.Empty;
        UnbilledSpending = string.Empty;
        ClosingDay = string.Empty;
        DueDay = string.Empty;
        MinimumRate = string.Empty;
        FixedPaymentAmount = string.Empty;
        ProjectionFallbackFixedAmount = string.Empty;
        CardChargeAmount = string.Empty;
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

        HasNoIncomeItems = IncomeItems.Count == 0;
        HasNoCreditCardItems = CreditCardItems.Count == 0;
        HasNoLoanItems = LoanItems.Count == 0;
        HasNoRegularPaymentItems = RegularPaymentItems.Count == 0;
        HasNoOneTimePaymentItems = OneTimePaymentItems.Count == 0;
        StructureSummary =
            $"{IncomeItems.Count} gelir • {CreditCardItems.Count} kart • " +
            $"{LoanItems.Count(x => x.Kind == FinancialRecordKind.Loan)} kredi • {RegularPaymentItems.Count + OneTimePaymentItems.Count} ödeme";
    }
}
