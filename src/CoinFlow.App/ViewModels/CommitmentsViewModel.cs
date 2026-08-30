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
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;

namespace CoinFlow.App.ViewModels;

public partial class CommitmentsViewModel(
    CoinFlowService service,
    CreditCardStatementCalculator cardCalculator,
    ICreditCardStatementImporter statementImporter,
    IUserFeedbackService feedback) : ViewModelBase
{
    public event Action<InitialPaymentStrategySetup>?
        InitialStrategySetupRequested;
    public ObservableCollection<SelectionOption<string>> RecordTypes { get; } = [];

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

    public ObservableCollection<SelectionOption<CreditCardPaymentType>>
        PaymentPlanTypes { get; } =
    [
        new("Asgari ödeme", CreditCardPaymentType.Minimum),
        new("Ekstrenin tamamı", CreditCardPaymentType.FullStatement),
        new("Özel tutar", CreditCardPaymentType.FixedAmount)
    ];

    public ObservableCollection<SelectionOption<CurrentStatementPaymentMode>>
        CurrentStatementPaymentModes { get; } =
    [
        new("Asgari", CurrentStatementPaymentMode.Minimum),
        new("Tamamı", CurrentStatementPaymentMode.Full),
        new("Başka tutar", CurrentStatementPaymentMode.Custom)
    ];

    public ObservableCollection<FinancialRecordLine> Items { get; } = [];
    public ObservableCollection<FinancialRecordLine> IncomeItems { get; } = [];
    public ObservableCollection<FinancialRecordLine> CreditCardItems { get; } = [];
    public ObservableCollection<FinancialRecordLine> LoanItems { get; } = [];
    public ObservableCollection<FinancialRecordLine> RegularPaymentItems { get; } = [];
    public ObservableCollection<FinancialRecordLine> OneTimePaymentItems { get; } = [];
    public ObservableCollection<DatedAmountLine> PlanInstallments { get; } = [];
    public ObservableCollection<DatedAmountLine> CardFutureCharges { get; } = [];
    public ObservableCollection<CardPaymentPlanLine> CardPaymentPlans { get; } = [];

    private readonly List<FinancialRecordLine> _allItems = [];
    private readonly Dictionary<Guid, string> _cardChargeDescriptions = [];
    private Guid? _editingCardId;
    private DateOnly? _editingCardBalanceDate;
    private CreditCardStatement? _editingCardStatement;
    private string? _cardStatementFingerprint;
    private CreditCardStatementSource _cardStatementSource =
        CreditCardStatementSource.Manual;

    [ObservableProperty] private bool isIncomeSection = true;
    [ObservableProperty] private bool isPaymentSection;
    [ObservableProperty] private SelectionOption<string>? selectedRecordType;
    [ObservableProperty] private bool isSalary;
    [ObservableProperty] private bool isOtherIncome;
    [ObservableProperty] private bool isLoan;
    [ObservableProperty] private bool isPlan;
    [ObservableProperty] private bool isCard;
    [ObservableProperty] private bool isLargeExpense;
    [ObservableProperty] private bool hasNoSalary;
    [ObservableProperty] private bool hasActiveForm;
    [ObservableProperty] private string formTitle = "Yeni kayıt";
    [ObservableProperty] private string formLead = string.Empty;
    [ObservableProperty] private string structureSummary = "—";
    [ObservableProperty] private bool hasIncomeItems;
    [ObservableProperty] private bool hasCreditCardItems;
    [ObservableProperty] private bool hasLoanItems;
    [ObservableProperty] private bool hasRegularPaymentItems;
    [ObservableProperty] private bool hasOneTimePaymentItems;
    [ObservableProperty] private Guid? firstCardId;

    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private string bank = string.Empty;
    [ObservableProperty] private string amount = string.Empty;
    [ObservableProperty] private DateTime effectiveDate = DateTime.Today;
    [ObservableProperty] private string note = string.Empty;

    [ObservableProperty] private string paymentDay = "10";
    [ObservableProperty] private DateTime nextPaymentDate = DateTime.Today.AddMonths(1);
    [ObservableProperty] private string installmentCount = "12";
    [ObservableProperty] private string remainingDebt = string.Empty;
    [ObservableProperty] private string earlyClosureAmount = string.Empty;

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
    [ObservableProperty] private DateTime cardPaymentPlanDate = DateTime.Today.AddMonths(1);
    [ObservableProperty] private SelectionOption<CreditCardPaymentType>? selectedPaymentPlanType;
    [ObservableProperty] private string cardPaymentPlanAmount = string.Empty;
    [ObservableProperty] private bool isFixedPaymentPlan;
    [ObservableProperty] private bool isEditingCard;
    [ObservableProperty] private string saveButtonText = "Kaydet";

    public async Task LoadAsync()
    {
        var plan = await service.GetFinancialPlanAsync();
        HasNoSalary = plan.Salaries.Count == 0;
        _allItems.Clear();

        foreach (var salary in plan.Salaries.OrderByDescending(x => x.EffectiveDate))
        {
            _allItems.Add(new FinancialRecordLine(
                salary.Id,
                ManagementSection.Income,
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
                ManagementSection.Income,
                FinancialRecordKind.OtherIncome,
                income.Description.Length == 0 ? "Diğer gelir" : income.Description,
                income.ExactDate.ToString("dd.MM.yyyy"),
                Money(income.Amount),
                "Tek seferlik gelir"));
        }

        foreach (var loan in plan.Loans)
        {
            _allItems.Add(new FinancialRecordLine(
                loan.Id,
                ManagementSection.Payment,
                FinancialRecordKind.Loan,
                $"{loan.Bank} {loan.Name}".Trim(),
                $"Sonraki: {loan.NextPaymentDate:dd.MM.yyyy} • {loan.RemainingInstallmentCount} ödeme",
                Money(loan.MonthlyPayment),
                loan.RemainingDebt is decimal debt
                    ? $"Kalan borç: {Money(debt)}"
                    : "Kredi"));
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
                ManagementSection.Payment,
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
                ? $"Ekstre: {Money(statement.StatementAmount)} • Son ödeme: {statement.DueDate:dd MMM}"
                : upcoming.Payment is decimal payment
                    ? $"Yaklaşan tahmini ödeme: {Money(payment)} • Son ödeme: {upcoming.PaymentDueDate:dd.MM.yyyy}"
                    : "Yaklaşan ödeme henüz belirlenmedi";
            var badgeText = card.CurrentStatement is not null
                ? $"Plan: {CurrentStatementPlanLabel(card.CurrentStatementPaymentPlan)}"
                : $"Ödeme tercihi: {StrategyLabel(card.PaymentStrategy)} • Henüz karar vermediğim ekstrelerde: {FallbackLabel(card.ProjectionFallbackStrategy)}";
            _allItems.Add(new FinancialRecordLine(
                card.Id,
                ManagementSection.Payment,
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
                ManagementSection.Payment,
                FinancialRecordKind.LargeExpense,
                expense.Name,
                $"{expense.ExactDate:dd.MM.yyyy} • {expense.Note}",
                Money(expense.Amount),
                "Planlı büyük ödeme"));
        }

        RefreshGroupedItems();
        RefreshRecordTypes();
        RefreshVisibleItems();
        SelectedPaymentStrategy ??= PaymentStrategies[0];
        SelectedProjectionFallbackStrategy ??=
            ProjectionFallbackStrategies[0];
        SelectedPaymentPlanType ??= PaymentPlanTypes[0];
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

    [RelayCommand]
    private void ShowIncome()
    {
        IsIncomeSection = true;
        IsPaymentSection = false;
        CancelEditingCard();
        RefreshRecordTypes();
        RefreshVisibleItems();
    }

    public void SelectIncomeSection() => ShowIncome();

    [RelayCommand]
    private void ShowPayments()
    {
        IsIncomeSection = false;
        IsPaymentSection = true;
        CancelEditingCard();
        RefreshRecordTypes();
        RefreshVisibleItems();
    }

    public void SelectPaymentSection() => ShowPayments();

    public void StartAdd(string recordType)
    {
        ResetForm();
        IsIncomeSection = recordType is "salary" or "income";
        IsPaymentSection = !IsIncomeSection;
        RefreshRecordTypes();
        SelectedRecordType = RecordTypes.SingleOrDefault(x =>
            x.Value == recordType) ?? RecordTypes.FirstOrDefault();
        HasActiveForm = true;
        FormTitle = recordType switch
        {
            "salary" => "Gelir Ekle",
            "income" => "Tek Seferlik Gelir Ekle",
            "loan" => "Kredi Ekle",
            "card" => "Kredi Kartı Ekle",
            "temporary" => "Geçici Ödeme Ekle",
            "recurring" => "Düzenli Ödeme Ekle",
            "installment" => "Taksit / Finansman Ekle",
            "large" => "Tek Seferlik Ödeme Ekle",
            _ => "Yeni Kayıt"
        };
        FormLead = recordType switch
        {
            "salary" => "Düzenli gelir veya gelir değişikliği.",
            "income" => "Belirli tarihte gelecek tek seferlik gelir.",
            "loan" => "Aylık kredi taksitleri.",
            "card" => "Kart limiti, borç ve ödeme tercihleri.",
            "temporary" => "Tarihleri belli geçici ödemeler.",
            "recurring" => "Tekrarlayan düzenli ödemeler.",
            "installment" => "Taksit veya finansman planı.",
            "large" => "Belirli tarihte ödenecek tek seferlik tutar.",
            _ => string.Empty
        };
        SaveButtonText = "Kaydet";
    }

    partial void OnSelectedRecordTypeChanged(
        SelectionOption<string>? value)
    {
        IsSalary = value?.Value == "salary";
        IsOtherIncome = value?.Value == "income";
        IsLoan = value?.Value == "loan";
        IsPlan = value?.Value is "temporary" or "installment" or "recurring";
        IsCard = value?.Value == "card";
        IsLargeExpense = value?.Value == "large";
    }

    partial void OnSelectedPaymentStrategyChanged(
        SelectionOption<CreditCardPaymentStrategy>? value) =>
        IsFixedPaymentStrategy =
            value?.Value == CreditCardPaymentStrategy.FixedAmount;

    partial void OnSelectedProjectionFallbackStrategyChanged(
        SelectionOption<ProjectionFallbackStrategy>? value) =>
        IsFixedProjectionFallback =
            value?.Value == ProjectionFallbackStrategy.FixedAmount;

    partial void OnSelectedPaymentPlanTypeChanged(
        SelectionOption<CreditCardPaymentType>? value) =>
        IsFixedPaymentPlan =
            value?.Value == CreditCardPaymentType.FixedAmount;

    partial void OnCardHasActualStatementChanged(bool value) =>
        IsLegacyCardSetup = !value;

    partial void OnSelectedCurrentStatementPaymentModeChanged(
        SelectionOption<CurrentStatementPaymentMode>? value) =>
        IsCurrentStatementCustomPayment =
            value?.Value == CurrentStatementPaymentMode.Custom;

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
                string.IsNullOrWhiteSpace(Note)
                ? "Gelecek taksit"
                : Note.Trim()));
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
        CardStatementDate = DateTime.Today;
        CardStatementDueDate = DateTime.Today;
    }

    [RelayCommand]
    private void UseLegacyCardSetup()
    {
        CardHasActualStatement = false;
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

        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Ekstre PDF seç",
                FileTypes = FilePickerFileType.Pdf
            });
            if (file is null)
            {
                return;
            }

            IsBusy = true;
            SetStatus(string.Empty);
            await using var stream = await file.OpenReadAsync();
            var result = await statementImporter.ImportPdfAsync(stream);
            ApplyStatementImport(result);
            if (!result.HasRequiredFields)
            {
                await feedback.ShowErrorAsync(
                    "Bilgileri elle girebilirsin.",
                    "Ekstre Otomatik Okunamadı");
            }
        }
        catch (Exception exception)
        {
            var message = UserFacingMessages.FromException(exception);
            SetStatus(message);
            await feedback.ShowErrorAsync(
                "Bilgileri elle girebilirsin.",
                "Ekstre Kaydedilemedi");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void AddCardPaymentPlan()
    {
        try
        {
            var type = SelectedPaymentPlanType?.Value
                ?? throw new InvalidOperationException("Ödeme tercihi seçmelisin.");
            var parsed = type == CreditCardPaymentType.FixedAmount
                ? RequirePositive(
                    ParseMoney(CardPaymentPlanAmount, "Özel ödeme tutarı"),
                    "Özel ödeme tutarı")
                : (decimal?)null;
            var date = DateOnly.FromDateTime(CardPaymentPlanDate);
            var existing = CardPaymentPlans.FirstOrDefault(x => x.DueDate == date);
            if (existing is not null)
            {
                CardPaymentPlans.Remove(existing);
            }

            CardPaymentPlans.Add(new CardPaymentPlanLine(
                existing?.Id ?? Guid.NewGuid(),
                date,
                type,
                parsed));
            CardPaymentPlanAmount = string.Empty;
            SetStatus(string.Empty);
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(exception));
        }
    }

    public void RemovePlanPayment(DatedAmountLine line) =>
        PlanInstallments.Remove(line);

    public void RemoveCardCharge(DatedAmountLine line)
    {
        CardFutureCharges.Remove(line);
        _cardChargeDescriptions.Remove(line.Id);
    }

    public void RemoveCardPaymentPlan(CardPaymentPlanLine line) =>
        CardPaymentPlans.Remove(line);

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
        IsIncomeSection = false;
        IsPaymentSection = true;
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
            CardNextStatementDate =
                statement.NextStatementDate?.ToString("dd.MM.yyyy") ??
                string.Empty;
            CardNextDueDate =
                statement.NextDueDate?.ToString("dd.MM.yyyy") ??
                string.Empty;
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

        CardPaymentPlans.Clear();
        foreach (var payment in card.PaymentPlans)
        {
            CardPaymentPlans.Add(new CardPaymentPlanLine(
                payment.Id,
                payment.DueDate,
                payment.PaymentType,
                payment.Amount));
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy)
        {
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
            case "income":
                var income = new OneTimeIncome
                {
                    Amount = RequirePositive(ParseMoney(Amount, "Gelir"), "Gelir"),
                    ExactDate = DateOnly.FromDateTime(EffectiveDate),
                    Description = string.IsNullOrWhiteSpace(Name)
                        ? "Diğer gelir"
                        : Name.Trim()
                };
                successMessage = "Gelir kaydedildi.";
                return () => service.SaveOtherIncomeAsync(income);
            case "loan":
                var loan = BuildLoan();
                successMessage = "Kredi kaydedildi.";
                return () => service.SaveLoanAsync(loan);
            case "temporary":
            case "installment":
            case "recurring":
                var plan = BuildPlan();
                successMessage = plan.Kind switch
                {
                    PaymentPlanKind.Installment => "Taksit planı kaydedildi.",
                    PaymentPlanKind.Recurring => "Düzenli ödeme kaydedildi.",
                    _ => "Geçici ödeme planı kaydedildi."
                };
                return () => service.SavePaymentPlanAsync(plan);
            case "card":
                var wasEditingCard = IsEditingCard;
                var card = BuildCard();
                successMessage = wasEditingCard
                    ? "Kredi kartı güncellendi."
                    : "Kredi kartı kaydedildi.";
                return () => service.SaveCreditCardAsync(card);
            case "large":
                var expense = new PlannedLargeExpense
                {
                    Name = RequireName(),
                    Amount = RequirePositive(ParseMoney(Amount, "Tutar"), "Tutar"),
                    ExactDate = DateOnly.FromDateTime(EffectiveDate),
                    Note = Note.Trim()
                };
                successMessage = "Planlı ödeme kaydedildi.";
                return () => service.SavePlannedLargeExpenseAsync(expense);
            default:
                throw new InvalidOperationException("Kayıt türü seçilmelidir.");
        }
    }

    [RelayCommand]
    private void CancelEditingCard()
    {
        _editingCardId = null;
        _editingCardBalanceDate = null;
        _editingCardStatement = null;
        _cardStatementFingerprint = null;
        _cardStatementSource = CreditCardStatementSource.Manual;
        IsEditingCard = false;
        HasActiveForm = false;
        SaveButtonText = "Kaydet";
        CardFutureCharges.Clear();
        CardPaymentPlans.Clear();
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

        return new Loan
        {
            Name = RequireName(),
            Bank = Bank.Trim(),
            MonthlyPayment = RequirePositive(ParseMoney(Amount, "Aylık ödeme"), "Aylık ödeme"),
            PaymentDay = day,
            NextPaymentDate = DateOnly.FromDateTime(NextPaymentDate),
            RemainingInstallmentCount = count,
            RemainingDebt = ParseOptionalMoney(RemainingDebt),
            EarlyClosureAmount = ParseOptionalMoney(EarlyClosureAmount)
        };
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
            Kind = SelectedRecordType?.Value switch
            {
                "temporary" => PaymentPlanKind.Temporary,
                "recurring" => PaymentPlanKind.Recurring,
                _ => PaymentPlanKind.Installment
            },
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
            PaymentPlans = CardPaymentPlans
                .OrderBy(x => x.DueDate)
                .Select(x => new CreditCardPaymentPlan
                {
                    Id = x.Id,
                    CreditCardId = cardId,
                    DueDate = x.DueDate,
                    PaymentType = x.PaymentType,
                    Amount = x.Amount
                })
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
            NextStatementDate = ParseOptionalDate(
                CardNextStatementDate,
                "Bir sonraki kesim tarihi"),
            NextDueDate = ParseOptionalDate(
                CardNextDueDate,
                "Bir sonraki son ödeme tarihi"),
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
            RecordTypes.Add(new SelectionOption<string>("Tek seferlik gelir", "income"));
        }
        else
        {
            RecordTypes.Add(new SelectionOption<string>("Kredi", "loan"));
            RecordTypes.Add(new SelectionOption<string>("Kredi kartı", "card"));
            RecordTypes.Add(new SelectionOption<string>("Geçici ödeme planı", "temporary"));
            RecordTypes.Add(new SelectionOption<string>("Düzenli ödeme", "recurring"));
            RecordTypes.Add(new SelectionOption<string>("Taksit / finansman", "installment"));
            RecordTypes.Add(new SelectionOption<string>("Tek seferlik ödeme", "large"));
        }

        SelectedRecordType =
            RecordTypes.FirstOrDefault(x => x.Value == selected) ??
            RecordTypes.FirstOrDefault();
    }

    private void RefreshVisibleItems()
    {
        var section = IsIncomeSection
            ? ManagementSection.Income
            : ManagementSection.Payment;
        Items.Clear();
        foreach (var item in _allItems.Where(x => x.Section == section))
        {
            Items.Add(item);
        }
    }

    private void ResetForm()
    {
        Name = string.Empty;
        Bank = string.Empty;
        Amount = string.Empty;
        Note = string.Empty;
        RemainingDebt = string.Empty;
        EarlyClosureAmount = string.Empty;
        PlanInstallments.Clear();
        CardFutureCharges.Clear();
        CardPaymentPlans.Clear();
        _cardChargeDescriptions.Clear();
        CardHasActualStatement = false;
        CardStatementAmount = string.Empty;
        CardStatementMinimum = string.Empty;
        CardStatementDate = DateTime.Today;
        CardStatementDueDate = DateTime.Today;
        CardNextStatementDate = string.Empty;
        CardNextDueDate = string.Empty;
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

        HasIncomeItems = IncomeItems.Count > 0;
        HasCreditCardItems = CreditCardItems.Count > 0;
        HasLoanItems = LoanItems.Count > 0;
        HasRegularPaymentItems = RegularPaymentItems.Count > 0;
        HasOneTimePaymentItems = OneTimePaymentItems.Count > 0;
        FirstCardId = CreditCardItems.FirstOrDefault()?.Id;
        StructureSummary =
            $"{IncomeItems.Count} gelir • {CreditCardItems.Count} kart • " +
            $"{LoanItems.Count} kredi • {RegularPaymentItems.Count + OneTimePaymentItems.Count} ödeme";
    }

    private void ApplyStatementImport(
        CreditCardStatementImportResult result)
    {
        CardHasActualStatement = true;
        _cardStatementSource = CreditCardStatementSource.PdfImport;
        _cardStatementFingerprint = result.SourceDocumentFingerprint;
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
        CardNextStatementDate =
            result.NextStatementDate?.ToString("dd.MM.yyyy") ??
            CardNextStatementDate;
        CardNextDueDate =
            result.NextDueDate?.ToString("dd.MM.yyyy") ??
            CardNextDueDate;
        SelectedCurrentStatementPaymentMode ??=
            CurrentStatementPaymentModes[0];
        CardStatementImportWarnings =
            string.Join(Environment.NewLine, result.Warnings);
        HasCardStatementImportWarnings = result.Warnings.Count > 0;
    }

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

    private static DateOnly? ParseOptionalDate(
        string? value,
        string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateOnly.TryParseExact(
                value.Trim(),
                "dd.MM.yyyy",
                TurkishCulture,
                System.Globalization.DateTimeStyles.None,
                out var date) ||
            DateOnly.TryParse(
                value,
                TurkishCulture,
                System.Globalization.DateTimeStyles.None,
                out date))
        {
            return date;
        }

        throw new InvalidOperationException(
            $"{field} gg.aa.yyyy formatında olmalıdır.");
    }
}
