using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CoinFlow.App.Models;
using CoinFlow.Application.Models;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.App.ViewModels;

public sealed partial class ScenarioConditionForm : ViewModelBase
{
    private readonly bool _directEntryOnly;
    public bool ShowsTypePicker => !_directEntryOnly;
    private readonly Dictionary<Guid, Loan> _loans = [];

    public ScenarioConditionForm(bool directEntryOnly)
    {
        _directEntryOnly = directEntryOnly;
        foreach (var (group, label) in SimulationScenarioCatalog.Groups)
        {
            if (SimulationScenarioCatalog.OptionsIn(group, directEntryOnly).Count > 0)
            {
                Groups.Add(new ScenarioGroupView(group, label));
            }
        }

        SelectOption(SimulationScenarioCatalog.CashPayment);
    }

    public ObservableCollection<ScenarioGroupView> Groups { get; } = [];
    public ObservableCollection<ScenarioOptionView> VisibleOptions { get; } = [];
    public ObservableCollection<SelectionOption<Guid>> CreditCards { get; } = [];
    public ObservableCollection<SelectionOption<Guid>> Loans { get; } = [];
    public ObservableCollection<SelectionOption<DateOnly>> StrategySalaryDates { get; } = [];

    public IReadOnlyList<SelectionOption<PaymentAssignmentMode>> StrategyModes { get; } =
    [
        new("Geçmiş dönemi kapatırım", PaymentAssignmentMode.PreviousPeriod),
        new("Gelecek dönemi karşılarım", PaymentAssignmentMode.UpcomingPeriod)
    ];

    public IReadOnlyList<SelectionOption<CreditCardPaymentType>> CardPaymentModes { get; } =
    [
        new("Tamamını öde", CreditCardPaymentType.FullStatement),
        new("Asgari öde", CreditCardPaymentType.Minimum)
    ];

    public IReadOnlyList<SelectionOption<bool>> CardPaymentScopes { get; } =
    [
        new("Yalnızca bu ekstre", false),
        new("Bundan sonraki tüm ekstreler", true)
    ];

    public IReadOnlyList<SelectionOption<LoanPrepaymentMode>> PrepaymentModes { get; } =
    [
        new("Tamamen kapat", LoanPrepaymentMode.FullClosure),
        new("Ara ödeme · vadeyi kısalt (taksit aynı kalır)", LoanPrepaymentMode.ReduceTerm),
        new("Ara ödeme · taksiti azalt (vade aynı kalır)", LoanPrepaymentMode.ReduceInstallment)
    ];

    [ObservableProperty] private ScenarioOption selectedOption = SimulationScenarioCatalog.CashPayment;
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private string amount = string.Empty;
    [ObservableProperty] private SelectionOption<Guid>? selectedCreditCard;
    [ObservableProperty] private DateTime startDate = DateTime.Today;
    [ObservableProperty] private string paymentCount = string.Empty;
    [ObservableProperty] private DateTime firstPaymentDate = DateTime.Today.AddMonths(1);
    [ObservableProperty] private string totalRepaymentAmount = string.Empty;
    [ObservableProperty] private bool isCard;
    [ObservableProperty] private bool needsPaymentCount;
    [ObservableProperty] private string paymentCountLabel = "Ödeme / taksit sayısı";
    [ObservableProperty] private string paymentCountPlaceholder = "Örn. 12";
    [ObservableProperty] private string nameSuggestion = string.Empty;
    [ObservableProperty] private bool needsFirstPayment;
    [ObservableProperty] private bool isFinancing;
    [ObservableProperty] private bool isStrategyChange;
    [ObservableProperty] private SelectionOption<DateOnly>? selectedStrategySalaryDate;
    [ObservableProperty] private SelectionOption<PaymentAssignmentMode>? selectedStrategyMode;
    [ObservableProperty] private bool isCardPayoff;
    [ObservableProperty] private SelectionOption<CreditCardPaymentType>? selectedCardPaymentMode;
    [ObservableProperty] private SelectionOption<bool>? selectedCardPaymentScope;
    [ObservableProperty] private bool isLoanPrepayment;
    [ObservableProperty] private SelectionOption<Guid>? selectedLoan;
    [ObservableProperty] private SelectionOption<LoanPrepaymentMode>? selectedPrepaymentMode;
    [ObservableProperty] private bool isPartialPrepayment;
    [ObservableProperty] private bool needsAmount;
    [ObservableProperty] private bool isRegularScenario = true;
    [ObservableProperty] private string startDateLabel = "Başlangıç / işlem tarihi";
    [ObservableProperty] private string amountLabel = "Tutar";
    [ObservableProperty] private string scenarioDescription = string.Empty;

    public SimulationScenarioType? EditingType { get; private set; }

    public string NamePlaceholder => NameSuggestion.Length > 0
        ? NameSuggestion
        : "Örn. Beyaz eşya, tatil, okul taksidi";

    public void SelectOption(ScenarioOption option)
    {
        if (_directEntryOnly && option.EntryHome != ScenarioEntryHome.SharedForm)
        {
            throw new InvalidOperationException($"{option.Title} bu ekrandan girilemez.");
        }

        if (VisibleOptions.Count == 0 || VisibleOptions[0].Option.Group != option.Group)
        {
            VisibleOptions.Clear();
            foreach (var item in SimulationScenarioCatalog.OptionsIn(option.Group, _directEntryOnly))
            {
                VisibleOptions.Add(new ScenarioOptionView(item));
            }
        }

        foreach (var group in Groups) group.IsSelected = group.Group == option.Group;
        foreach (var item in VisibleOptions) item.IsSelected = item.Option.Key == option.Key;

        if (SelectedOption.Key == option.Key)
        {
            RefreshFields(optionChanged: false);
            return;
        }

        SelectedOption = option;
    }

    partial void OnSelectedOptionChanged(ScenarioOption value) => RefreshFields(optionChanged: true);

    partial void OnSelectedLoanChanged(SelectionOption<Guid>? value)
    {
        if (IsLoanPrepayment)
        {
            RefreshNameSuggestion();
            MoveStartDateToNextInstallment();
        }
    }

    partial void OnSelectedPrepaymentModeChanged(SelectionOption<LoanPrepaymentMode>? value)
    {
        if (!IsLoanPrepayment) return;
        RefreshFields(optionChanged: false);
        MoveStartDateToNextInstallment();
    }

    private void RefreshFields(bool optionChanged)
    {
        var option = SelectedOption;
        var isCardSpending = option.Key == SimulationScenarioCatalog.CardSpending.Key;
        IsCard = isCardSpending || option.Key == SimulationScenarioCatalog.CardPaymentMode.Key;
        NeedsPaymentCount = isCardSpending ||
                            option.Key == SimulationScenarioCatalog.Financing.Key ||
                            option.Key == SimulationScenarioCatalog.CashDebt.Key ||
                            option.Key == SimulationScenarioCatalog.RecurringPayment.Key;
        PaymentCountLabel = isCardSpending ? "Taksit sayısı (1 = tek çekim)" : "Ödeme / taksit sayısı";
        PaymentCountPlaceholder = isCardSpending ? "Boş bırakırsan tek çekim" : "Örn. 12";
        NeedsFirstPayment = option.Key == SimulationScenarioCatalog.Financing.Key ||
                            option.Key == SimulationScenarioCatalog.CashDebt.Key ||
                            option.Key == SimulationScenarioCatalog.RecurringPayment.Key;
        IsFinancing = option.Key == SimulationScenarioCatalog.Financing.Key;
        IsStrategyChange = option.Key == SimulationScenarioCatalog.PaymentStrategy.Key;
        IsCardPayoff = option.Key == SimulationScenarioCatalog.CardPaymentMode.Key;
        IsLoanPrepayment = option.Key == SimulationScenarioCatalog.LoanPrepayment.Key;

        if (IsLoanPrepayment)
        {
            SelectedLoan ??= Loans.FirstOrDefault();
            SelectedPrepaymentMode ??= PrepaymentModes[0];
        }

        if (IsCardPayoff)
        {
            SelectedCardPaymentMode ??= CardPaymentModes[0];
            SelectedCardPaymentScope ??= CardPaymentScopes[0];
        }

        IsPartialPrepayment = IsLoanPrepayment &&
                              SelectedPrepaymentMode?.Value is LoanPrepaymentMode.ReduceTerm or LoanPrepaymentMode.ReduceInstallment;
        NeedsAmount = !IsStrategyChange && !IsCardPayoff && (!IsLoanPrepayment || IsPartialPrepayment);
        AmountLabel = IsPartialPrepayment ? "Anaparadan düşecek tutar" : "Tutar";
        StartDateLabel = IsCardPayoff
            ? "Hangi ekstreden itibaren (son ödeme tarihi)"
            : IsLoanPrepayment ? "Ödeme tarihi" : "Başlangıç / işlem tarihi";
        IsRegularScenario = !IsStrategyChange;
        ScenarioDescription = option.Description;
        RefreshNameSuggestion();

        if (optionChanged && IsLoanPrepayment)
        {
            MoveStartDateToNextInstallment();
        }
    }

    public SimulationRequest BuildRequest(Guid scenarioId)
    {
        var isCardSpending = SelectedOption.Key == SimulationScenarioCatalog.CardSpending.Key;
        var count = !NeedsPaymentCount || (isCardSpending && string.IsNullOrWhiteSpace(PaymentCount))
            ? 1
            : int.TryParse(PaymentCount, out var parsed)
                ? parsed
                : throw new InvalidOperationException("Ödeme sayısı geçerli olmalıdır.");
        var type = SimulationScenarioCatalog.Resolve(
            SelectedOption,
            count,
            IsLoanPrepayment ? SelectedPrepaymentMode?.Value : null,
            EditingType);
        decimal? repayment = IsFinancing ? ParseMoney(TotalRepaymentAmount, "Toplam geri ödeme") : null;
        return new SimulationRequest(
            type,
            string.IsNullOrWhiteSpace(Name) ? NameSuggestion : Name.Trim(),
            NeedsAmount ? ParseMoney(Amount, AmountLabel) : 0m,
            IsStrategyChange
                ? SelectedStrategySalaryDate?.Value ?? throw new InvalidOperationException("Planın başlayacağı dönemi seçmelisin.")
                : DateOnly.FromDateTime(StartDate),
            count,
            NeedsFirstPayment ? DateOnly.FromDateTime(FirstPaymentDate) : null,
            IsCard ? SelectedCreditCard?.Value ?? throw new InvalidOperationException("Bir kredi kartı seçmelisin.") : null,
            repayment,
            IsStrategyChange ? SelectedStrategyMode?.Value : null,
            IsStrategyChange ? SelectedStrategySalaryDate?.Value : null,
            scenarioId,
            IsCardPayoff ? SelectedCardPaymentMode?.Value ?? throw new InvalidOperationException("Kart ödeme şeklini seçmelisin.") : null,
            IsCardPayoff && SelectedCardPaymentScope?.Value == true,
            IsLoanPrepayment ? SelectedLoan?.Value ?? throw new InvalidOperationException("Bir kredi seçmelisin.") : null,
            IsPartialPrepayment ? SelectedPrepaymentMode?.Value : null);
    }

    public void Load(SimulationRequest request)
    {
        EditingType = request.Type;
        SelectedPrepaymentMode = request.Type == SimulationScenarioType.LoanEarlyClosure
            ? PrepaymentModes[0]
            : request.PrepaymentMode is { } prepaymentMode
                ? PrepaymentModes.FirstOrDefault(x => x.Value == prepaymentMode)
                : SelectedPrepaymentMode;
        SelectOption(SimulationScenarioCatalog.For(request.Type));
        Name = request.Name;
        Amount = request.Amount > 0m ? request.Amount.ToString("0.##", TurkishCulture) : string.Empty;
        StartDate = request.StartDate.ToDateTime(TimeOnly.MinValue);
        PaymentCount = request.PaymentCount.ToString(TurkishCulture);
        FirstPaymentDate = (request.FirstPaymentDate ?? request.StartDate).ToDateTime(TimeOnly.MinValue);
        TotalRepaymentAmount = request.TotalRepaymentAmount is decimal repayment ? repayment.ToString("0.##", TurkishCulture) : string.Empty;
        SelectedCreditCard = CreditCards.FirstOrDefault(x => x.Value == request.CreditCardId);
        SelectedStrategyMode = request.NewPaymentAssignmentMode is { } mode ? StrategyModes.First(x => x.Value == mode) : SelectedStrategyMode;
        SelectedStrategySalaryDate = request.EffectiveSalaryDate is { } date ? StrategySalaryDates.FirstOrDefault(x => x.Value == date) : SelectedStrategySalaryDate;
        SelectedCardPaymentMode = request.CardPaymentType is { } cardPaymentType ? CardPaymentModes.First(x => x.Value == cardPaymentType) : SelectedCardPaymentMode;
        SelectedCardPaymentScope = CardPaymentScopes.First(x => x.Value == request.AppliesToAllStatements);
        SelectedLoan = request.LoanId is { } loanId ? Loans.FirstOrDefault(x => x.Value == loanId) : SelectedLoan;
        StartDate = request.StartDate.ToDateTime(TimeOnly.MinValue);
    }

    public void Reset()
    {
        EditingType = null;
        SelectOption(SimulationScenarioCatalog.CashPayment);
        Name = string.Empty;
        Amount = string.Empty;
        PaymentCount = string.Empty;
        StartDate = DateTime.Today;
        FirstPaymentDate = DateTime.Today.AddMonths(1);
        TotalRepaymentAmount = string.Empty;
        SelectedCreditCard = CreditCards.FirstOrDefault();
        SelectedStrategySalaryDate = StrategySalaryDates.FirstOrDefault();
        SelectedCardPaymentMode = CardPaymentModes[0];
        SelectedCardPaymentScope = CardPaymentScopes[0];
        SelectedLoan = Loans.FirstOrDefault();
        SelectedPrepaymentMode = PrepaymentModes[0];
    }
}
