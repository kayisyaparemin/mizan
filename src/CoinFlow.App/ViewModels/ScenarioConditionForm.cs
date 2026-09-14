using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoinFlow.App.Models;
using CoinFlow.Application.Models;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.App.ViewModels;

/// <summary>
/// Simülatör ile Finansal Yapı'nın paylaştığı koşul formu. Tür seçimi,
/// alanlar ve <see cref="SimulationRequest"/> üretimi tek yerde durur; böylece
/// simüle edilen bir koşul ile doğrudan girilen kayıt aynı isteği üretir.
/// </summary>
public sealed partial class ScenarioConditionForm : ViewModelBase
{
    private readonly bool _directEntryOnly;
    private readonly Dictionary<Guid, Loan> _loans = [];

    /// <param name="directEntryOnly">
    /// Finansal Yapı'da true: yalnız o ekrandan doğrudan kaydedilebilen türler
    /// görünür. Kart ödeme şekli, gelir değişikliği ve düzen değişikliğinin
    /// kendi ekranları var.
    /// </param>
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

        if (directEntryOnly)
        {
            // Finansal Yapı'da örnek değerler kayıt olarak kaydedilebilirdi.
            Name = string.Empty;
            Amount = string.Empty;
            PaymentCount = "1";
            TotalRepaymentAmount = string.Empty;
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

    public IReadOnlyList<SelectionOption<CreditCardPaymentType>>
        CardPaymentModes { get; } =
    [
        new("Tamamını öde", CreditCardPaymentType.FullStatement),
        new("Asgari öde", CreditCardPaymentType.Minimum)
    ];

    public IReadOnlyList<SelectionOption<bool>> CardPaymentScopes { get; } =
    [
        new("Yalnızca bu ekstre", false),
        new("Bundan sonraki tüm ekstreler", true)
    ];

    /// <summary>
    /// Erken kapama ile ara ödeme aynı türün iki hâli; tür listesinde ayrı
    /// durmaları yerine burada mod olarak seçilir.
    /// </summary>
    public IReadOnlyList<SelectionOption<LoanPrepaymentMode>>
        PrepaymentModes { get; } =
    [
        new("Tamamen kapat", LoanPrepaymentMode.FullClosure),
        new("Ara ödeme · vadeyi kısalt (taksit aynı kalır)", LoanPrepaymentMode.ReduceTerm),
        new("Ara ödeme · taksiti azalt (vade aynı kalır)", LoanPrepaymentMode.ReduceInstallment)
    ];

    [ObservableProperty] private ScenarioOption selectedOption =
        SimulationScenarioCatalog.CashPayment;
    [ObservableProperty] private string name = "Beyaz eşya";
    [ObservableProperty] private string amount = "120000";
    [ObservableProperty] private SelectionOption<Guid>? selectedCreditCard;
    [ObservableProperty] private DateTime startDate = DateTime.Today;
    [ObservableProperty] private string paymentCount = "9";
    [ObservableProperty] private DateTime firstPaymentDate = DateTime.Today.AddMonths(1);
    [ObservableProperty] private string totalRepaymentAmount = "145000";
    [ObservableProperty] private bool isCard;
    [ObservableProperty] private bool needsPaymentCount;
    [ObservableProperty] private string paymentCountLabel = "Ödeme / taksit sayısı";
    [ObservableProperty] private bool needsFirstPayment;
    [ObservableProperty] private bool isFinancing;
    [ObservableProperty] private bool isStrategyChange;
    [ObservableProperty] private bool isCardPayoff;
    [ObservableProperty] private bool needsAmount = true;
    [ObservableProperty] private string startDateLabel = "Başlangıç / işlem tarihi";
    [ObservableProperty] private bool isRegularScenario = true;
    [ObservableProperty] private SelectionOption<PaymentAssignmentMode>? selectedStrategyMode;
    [ObservableProperty] private SelectionOption<DateOnly>? selectedStrategySalaryDate;
    [ObservableProperty] private SelectionOption<CreditCardPaymentType>? selectedCardPaymentMode;
    [ObservableProperty] private SelectionOption<bool>? selectedCardPaymentScope;
    [ObservableProperty] private SelectionOption<Guid>? selectedLoan;
    [ObservableProperty] private SelectionOption<LoanPrepaymentMode>? selectedPrepaymentMode;
    [ObservableProperty] private bool isLoanPrepayment;
    [ObservableProperty] private bool isPartialPrepayment;
    [ObservableProperty] private string amountLabel = "Tutar";
    [ObservableProperty] private string scenarioDescription = string.Empty;

    /// <summary>
    /// Düzenlenen koşulun kayıtlı türü; yeni koşulda null. Tür çözümü ve
    /// "tarihi taksit gününe taşı" gibi yeni koşul kolaylıkları buna bakar.
    /// </summary>
    public SimulationScenarioType? EditingType { get; private set; }

    public void SetLookups(FinancialPlan plan)
    {
        CreditCards.Clear();
        foreach (var card in plan.CreditCards)
        {
            CreditCards.Add(new SelectionOption<Guid>(
                $"{card.Bank} {card.Name}".Trim(),
                card.Id));
        }

        Loans.Clear();
        _loans.Clear();
        foreach (var loan in plan.Loans.Where(x => x.IsActive))
        {
            Loans.Add(new SelectionOption<Guid>(
                $"{loan.Bank} {loan.Name}".Trim(),
                loan.Id));
            _loans[loan.Id] = loan;
        }

        SelectedCreditCard ??= CreditCards.FirstOrDefault();
        SelectedLoan ??= Loans.FirstOrDefault();
    }

    public void SetStrategyLookups(
        IEnumerable<DateOnly> availableSalaryDates,
        PaymentAssignmentMode currentMode)
    {
        StrategySalaryDates.Clear();
        foreach (var date in availableSalaryDates)
        {
            StrategySalaryDates.Add(new SelectionOption<DateOnly>(
                $"{date.ToString("dd MMMM yyyy", TurkishCulture)} dönemi",
                date));
        }

        SelectedStrategySalaryDate ??= StrategySalaryDates.FirstOrDefault();
        SelectedStrategyMode ??= StrategyModes.First(x => x.Value != currentMode);
    }

    public void ClearLookups()
    {
        CreditCards.Clear();
        Loans.Clear();
        _loans.Clear();
        StrategySalaryDates.Clear();
    }

    [RelayCommand]
    private void SelectGroup(ScenarioGroupView? group)
    {
        if (group is null || group.IsSelected)
        {
            return;
        }

        SelectGroup(group.Group);
    }

    /// <summary>Grubu açar ve grubun ilk türünü seçer.</summary>
    public void SelectGroup(ScenarioGroup group) =>
        SelectOption(SimulationScenarioCatalog
            .OptionsIn(group, _directEntryOnly)
            .First());

    [RelayCommand]
    private void SelectOption(ScenarioOptionView? option)
    {
        if (option is not null)
        {
            SelectOption(option.Option);
        }
    }

    public void SelectOption(ScenarioOption option)
    {
        if (_directEntryOnly && option.EntryHome != ScenarioEntryHome.SharedForm)
        {
            throw new InvalidOperationException(
                $"{option.Title} bu ekrandan girilemez.");
        }

        if (VisibleOptions.Count == 0 ||
            VisibleOptions[0].Option.Group != option.Group)
        {
            VisibleOptions.Clear();
            foreach (var item in SimulationScenarioCatalog
                         .OptionsIn(option.Group, _directEntryOnly))
            {
                VisibleOptions.Add(new ScenarioOptionView(item));
            }
        }

        foreach (var group in Groups)
        {
            group.IsSelected = group.Group == option.Group;
        }

        foreach (var item in VisibleOptions)
        {
            item.IsSelected = item.Option.Key == option.Key;
        }

        if (SelectedOption.Key == option.Key)
        {
            RefreshFields(optionChanged: false);
            return;
        }

        SelectedOption = option;
    }

    partial void OnSelectedOptionChanged(ScenarioOption value) =>
        RefreshFields(optionChanged: true);

    partial void OnSelectedLoanChanged(SelectionOption<Guid>? value)
    {
        if (IsLoanPrepayment)
        {
            MoveStartDateToNextInstallment();
        }
    }

    partial void OnSelectedPrepaymentModeChanged(
        SelectionOption<LoanPrepaymentMode>? value)
    {
        if (!IsLoanPrepayment)
        {
            return;
        }

        RefreshFields(optionChanged: false);
        MoveStartDateToNextInstallment();
    }

    private void RefreshFields(bool optionChanged)
    {
        var option = SelectedOption;
        var isCardSpending = option.Key == SimulationScenarioCatalog.CardSpending.Key;
        IsCard = isCardSpending ||
                 option.Key == SimulationScenarioCatalog.CardPaymentMode.Key;
        NeedsPaymentCount = isCardSpending ||
                            option.Key == SimulationScenarioCatalog.Financing.Key ||
                            option.Key == SimulationScenarioCatalog.CashDebt.Key ||
                            option.Key == SimulationScenarioCatalog.RecurringPayment.Key;
        PaymentCountLabel = isCardSpending
            ? "Taksit sayısı (1 = tek çekim)"
            : "Ödeme / taksit sayısı";
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
                              SelectedPrepaymentMode?.Value is
                                  LoanPrepaymentMode.ReduceTerm or
                                  LoanPrepaymentMode.ReduceInstallment;
        NeedsAmount = !IsStrategyChange && !IsCardPayoff &&
                      (!IsLoanPrepayment || IsPartialPrepayment);
        AmountLabel = IsPartialPrepayment
            ? "Anaparadan düşecek tutar"
            : "Tutar";
        StartDateLabel = IsCardPayoff
            ? "Hangi ekstreden itibaren (son ödeme tarihi)"
            : IsLoanPrepayment
                ? "Ödeme tarihi"
                : "Başlangıç / işlem tarihi";
        IsRegularScenario = !IsStrategyChange;
        ScenarioDescription = option.Description;

        if (optionChanged && isCardSpending && string.IsNullOrWhiteSpace(PaymentCount))
        {
            PaymentCount = "1";
        }

        if (optionChanged && IsLoanPrepayment)
        {
            MoveStartDateToNextInstallment();
        }
    }

    /// <summary>
    /// Erken ödemenin en ucuz günü taksit günüdür: işleyen faiz sıfırdır.
    /// Tarihi bugünden sonraki ilk taksit gününe taşır ve plan adını
    /// krediden türetir; düzenlenen koşulda kullanıcının seçimine dokunmaz.
    /// </summary>
    private void MoveStartDateToNextInstallment()
    {
        if (EditingType is not null ||
            SelectedLoan is not { } option ||
            !_loans.TryGetValue(option.Value, out var loan))
        {
            return;
        }

        Name = IsPartialPrepayment
            ? $"{option.Label} ara ödeme"
            : $"{option.Label} erken kapama";
        var from = DateOnly.FromDateTime(DateTime.Today);
        var next = Enumerable.Range(0, loan.RemainingInstallmentCount)
            .Select(index => CalendarRules.AddMonthsKeepingDay(
                loan.NextPaymentDate,
                index,
                loan.PaymentDay))
            .FirstOrDefault(date => date >= from);
        if (next != default)
        {
            StartDate = next.ToDateTime(TimeOnly.MinValue);
        }
    }

    public SimulationRequest BuildRequest(Guid scenarioId)
    {
        var count = NeedsPaymentCount
            ? int.TryParse(PaymentCount, out var parsed)
                ? parsed
                : throw new InvalidOperationException("Ödeme sayısı geçerli olmalıdır.")
            : 1;
        var type = SimulationScenarioCatalog.Resolve(
            SelectedOption,
            count,
            IsLoanPrepayment ? SelectedPrepaymentMode?.Value : null,
            EditingType);
        decimal? repayment = IsFinancing
            ? ParseMoney(TotalRepaymentAmount, "Toplam geri ödeme")
            : null;
        return new SimulationRequest(
            type,
            Name,
            NeedsAmount
                ? ParseMoney(Amount, AmountLabel)
                : 0m,
            IsStrategyChange
                ? SelectedStrategySalaryDate?.Value ??
                  throw new InvalidOperationException(
                      "Planın başlayacağı dönemi seçmelisin.")
                : DateOnly.FromDateTime(StartDate),
            count,
            NeedsFirstPayment
                ? DateOnly.FromDateTime(FirstPaymentDate)
                : null,
            IsCard
                ? SelectedCreditCard?.Value ??
                  throw new InvalidOperationException("Bir kredi kartı seçmelisin.")
                : null,
            repayment,
            IsStrategyChange ? SelectedStrategyMode?.Value : null,
            IsStrategyChange ? SelectedStrategySalaryDate?.Value : null,
            scenarioId,
            IsCardPayoff
                ? SelectedCardPaymentMode?.Value ??
                  throw new InvalidOperationException(
                      "Kart ödeme şeklini seçmelisin.")
                : null,
            IsCardPayoff && SelectedCardPaymentScope?.Value == true,
            IsLoanPrepayment
                ? SelectedLoan?.Value ??
                  throw new InvalidOperationException("Bir kredi seçmelisin.")
                : null,
            IsPartialPrepayment ? SelectedPrepaymentMode?.Value : null);
    }

    /// <summary>Kayıtlı koşulu düzenlemek için forma yükler.</summary>
    public void Load(SimulationRequest request)
    {
        // Tür önce işaretlenir: seçenek değişince tarih taksit gününe
        // taşınmasın, kayıtlı tarih kullanıcının seçimidir.
        EditingType = request.Type;
        SelectedPrepaymentMode = request.Type == SimulationScenarioType.LoanEarlyClosure
            ? PrepaymentModes[0]
            : request.PrepaymentMode is { } prepaymentMode
                ? PrepaymentModes.FirstOrDefault(x => x.Value == prepaymentMode)
                : SelectedPrepaymentMode;
        SelectOption(SimulationScenarioCatalog.For(request.Type));
        Name = request.Name;
        Amount = request.Amount > 0m
            ? request.Amount.ToString("0.##", TurkishCulture)
            : string.Empty;
        StartDate = request.StartDate.ToDateTime(TimeOnly.MinValue);
        PaymentCount = request.PaymentCount.ToString(TurkishCulture);
        FirstPaymentDate = (request.FirstPaymentDate ?? request.StartDate)
            .ToDateTime(TimeOnly.MinValue);
        TotalRepaymentAmount = request.TotalRepaymentAmount is decimal repayment
            ? repayment.ToString("0.##", TurkishCulture)
            : string.Empty;
        SelectedCreditCard = CreditCards.FirstOrDefault(x =>
            x.Value == request.CreditCardId);
        SelectedStrategyMode = request.NewPaymentAssignmentMode is { } mode
            ? StrategyModes.First(x => x.Value == mode)
            : SelectedStrategyMode;
        SelectedStrategySalaryDate = request.EffectiveSalaryDate is { } date
            ? StrategySalaryDates.FirstOrDefault(x => x.Value == date)
            : SelectedStrategySalaryDate;
        SelectedCardPaymentMode = request.CardPaymentType is { } cardPaymentType
            ? CardPaymentModes.First(x => x.Value == cardPaymentType)
            : SelectedCardPaymentMode;
        SelectedCardPaymentScope = CardPaymentScopes.First(x =>
            x.Value == request.AppliesToAllStatements);
        SelectedLoan = request.LoanId is { } loanId
            ? Loans.FirstOrDefault(x => x.Value == loanId)
            : SelectedLoan;
        StartDate = request.StartDate.ToDateTime(TimeOnly.MinValue);
    }

    /// <summary>
    /// Koşul listeden silinince ya da plan değişince düzenleme biter; form
    /// alanları olduğu gibi kalır.
    /// </summary>
    public void EndEditing() => EditingType = null;

    public void Reset()
    {
        // Seçenek önce değişir: kredi seçenekteyken aşağıdaki kredi/mod
        // atamaları adı ve tarihi krediden yeniden türetirdi.
        EditingType = null;
        SelectOption(SimulationScenarioCatalog.CashPayment);
        Name = _directEntryOnly ? string.Empty : "Yeni koşul";
        Amount = string.Empty;
        PaymentCount = "1";
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

    public string CardLabel(Guid? creditCardId) =>
        CreditCards.FirstOrDefault(x => x.Value == creditCardId)?.Label ??
        "Kart";

    public string LoanLabel(Guid? loanId) =>
        Loans.FirstOrDefault(x => x.Value == loanId)?.Label ?? "Kredi";
}
