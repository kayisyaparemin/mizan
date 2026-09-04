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

public partial class CardControlViewModel(
    CoinFlowService service,
    CreditCardStatementCalculator cardCalculator,
    CreditCardStatementImportWorkflow statementImportWorkflow,
    IUserFeedbackService feedback) : ViewModelBase
{
    public const string CardIdQueryKey = "cardId";
    private readonly Dictionary<Guid, string> _chargeDescriptions = [];
    private CreditCard? _card;
    private FinancialPlan? _plan;
    private string? _statementDraftFingerprint;
    private CreditCardStatementSource _statementDraftSource =
        CreditCardStatementSource.Manual;
    private CurrentStatementPaymentMode _statementDraftPaymentMode =
        CurrentStatementPaymentMode.Minimum;
    private DateOnly? _statementDraftExactNextStatementDate;
    private DateOnly? _statementDraftExactNextDueDate;
    private CancellationTokenSource? _statementImportCancellation;

    // En yakın ekstrenin ötesinde, ay bazlı planlanabilen ekstre sayısı.
    private const int UpcomingStatementCount = 6;

    public ObservableCollection<DatedAmountLine> FutureCharges { get; } = [];

    public ObservableCollection<UpcomingStatementLine> UpcomingStatements
    { get; } = [];

    [ObservableProperty] private string title = "Kart";
    [ObservableProperty] private string subtitle = string.Empty;
    [ObservableProperty] private string knownDebt = "-";
    [ObservableProperty] private string limitText = "-";
    [ObservableProperty] private string statementText = "-";
    [ObservableProperty] private string statementDateText = "-";
    [ObservableProperty] private string statementDueText = "-";
    [ObservableProperty] private string statementMinimumText = "-";
    [ObservableProperty] private string currentStatementPlanText = "-";
    [ObservableProperty] private string currentStatementPaymentText = "-";
    [ObservableProperty] private string nextStatementEstimateText = "-";
    [ObservableProperty] private string nextStatementDateText = "-";
    [ObservableProperty] private string nextStatementBreakdownText = string.Empty;
    [ObservableProperty] private string paymentPreferenceText = "-";
    [ObservableProperty] private string fallbackText = "-";
    [ObservableProperty] private bool hasActualStatement;
    [ObservableProperty] private bool hasNoActualStatement = true;
    [ObservableProperty] private bool hasFutureCharges;
    [ObservableProperty] private bool hasUpcomingStatements;
    [ObservableProperty] private bool isCurrentStatementCustom;
    [ObservableProperty] private string currentStatementCustomAmount = string.Empty;

    [ObservableProperty] private bool hasStatementDraft;
    [ObservableProperty] private string statementDraftTitle = "Ekstreyi Kontrol Et";
    [ObservableProperty] private string statementDraftBank = "-";
    [ObservableProperty] private string statementDraftLast4 = string.Empty;
    [ObservableProperty] private bool hasStatementDraftLast4;
    [ObservableProperty] private string statementDraftWarnings = string.Empty;
    [ObservableProperty] private bool hasStatementDraftWarnings;
    [ObservableProperty] private string statementDraftDuplicate = string.Empty;
    [ObservableProperty] private bool isStatementDraftDuplicate;
    [ObservableProperty] private DateTime statementDraftDate = DateTime.Today;
    [ObservableProperty] private DateTime statementDraftDueDate = DateTime.Today;
    [ObservableProperty] private string statementDraftAmount = string.Empty;
    [ObservableProperty] private string statementDraftMinimum = string.Empty;
    [ObservableProperty] private string statementDraftNextStatementDate = string.Empty;
    [ObservableProperty] private string statementDraftNextDueDate = string.Empty;
    [ObservableProperty] private string statementDraftPaymentPlanText = "Asgari";
    [ObservableProperty] private bool isStatementDraftCustom;
    [ObservableProperty] private string statementDraftCustomAmount = string.Empty;

    [ObservableProperty] private DateTime chargeDate = DateTime.Today.AddMonths(1);
    [ObservableProperty] private string chargeAmount = string.Empty;
    [ObservableProperty] private string chargeDescription = string.Empty;

    public async Task LoadAsync(Guid cardId)
    {
        _plan = await service.GetFinancialPlanAsync();
        _card = _plan.CreditCards.Single(x => x.Id == cardId);
        RefreshCardState();
        SetStatus(string.Empty);
    }

    [RelayCommand]
    private async Task ImportStatementAsync()
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
                StartManualStatement();
                await ShowManualFallbackAsync(
                    attempt.Outcome ==
                    CreditCardStatementImportOutcome.TimedOut);
                return;
            }

            var result = attempt.Result;
            statementImportWorkflow.NotifyPreviewStarted();
            StartStatementDraft(result);
            if (!result.HasRequiredFields)
            {
                await ShowManualFallbackAsync();
            }
        }
        catch (Exception)
        {
            StartManualStatement();
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
    private void StartManualStatement()
    {
        var card = RequiredCard();
        var statement = card.CurrentStatement;
        _statementDraftSource = CreditCardStatementSource.Manual;
        _statementDraftFingerprint = null;
        _statementDraftExactNextStatementDate = statement?.NextStatementDate;
        _statementDraftExactNextDueDate = statement?.NextDueDate;
        _statementDraftPaymentMode =
            card.CurrentStatementPaymentPlan?.Mode ??
            CurrentStatementPaymentMode.Minimum;
        StatementDraftTitle = statement is null
            ? "Ekstreyi Elle Gir"
            : "Bu Ekstreyi Düzenle";
        StatementDraftBank = $"{card.Bank} {card.Name}".Trim();
        StatementDraftLast4 = string.Empty;
        HasStatementDraftLast4 = false;
        StatementDraftWarnings = string.Empty;
        HasStatementDraftWarnings = false;
        StatementDraftDuplicate = string.Empty;
        IsStatementDraftDuplicate = false;
        StatementDraftDate = (statement?.StatementDate ?? DateOnly.FromDateTime(DateTime.Today))
            .ToDateTime(TimeOnly.MinValue);
        StatementDraftDueDate = (statement?.DueDate ?? DateOnly.FromDateTime(DateTime.Today))
            .ToDateTime(TimeOnly.MinValue);
        StatementDraftAmount = statement?.StatementAmount.ToString("N2", TurkishCulture) ?? string.Empty;
        StatementDraftMinimum = statement?.MinimumPaymentAmount.ToString("N2", TurkishCulture) ?? string.Empty;
        RefreshStatementDraftNextDates();
        StatementDraftCustomAmount =
            card.CurrentStatementPaymentPlan?.CustomAmount?.ToString("N2", TurkishCulture) ??
            string.Empty;
        RefreshStatementDraftPaymentMode();
        HasStatementDraft = true;
    }

    [RelayCommand]
    private void CancelStatementDraft()
    {
        HasStatementDraft = false;
        StatementDraftWarnings = string.Empty;
        HasStatementDraftWarnings = false;
        IsStatementDraftDuplicate = false;
        StatementDraftDuplicate = string.Empty;
    }

    [RelayCommand]
    private void DraftPayMinimum()
    {
        _statementDraftPaymentMode = CurrentStatementPaymentMode.Minimum;
        RefreshStatementDraftPaymentMode();
    }

    [RelayCommand]
    private void DraftPayFull()
    {
        _statementDraftPaymentMode = CurrentStatementPaymentMode.Full;
        RefreshStatementDraftPaymentMode();
    }

    [RelayCommand]
    private void DraftPayCustom()
    {
        _statementDraftPaymentMode = CurrentStatementPaymentMode.Custom;
        RefreshStatementDraftPaymentMode();
    }

    [RelayCommand]
    private async Task SaveStatementDraftAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var card = RequiredCard();
            var amount = ParsePositiveMoney(
                StatementDraftAmount,
                "Ekstre tutarı");
            var minimum = ParseMoney(
                StatementDraftMinimum,
                "Asgari ödeme");
            if (minimum < 0m || minimum > amount)
            {
                throw new InvalidOperationException(
                    "Asgari ödeme 0 ile ekstre tutarı arasında olmalıdır.");
            }

            var statement = new CreditCardStatement
            {
                Id = card.CurrentStatement?.Id ?? Guid.NewGuid(),
                CreditCardId = card.Id,
                StatementDate = DateOnly.FromDateTime(StatementDraftDate),
                DueDate = DateOnly.FromDateTime(StatementDraftDueDate),
                StatementAmount = amount,
                MinimumPaymentAmount = minimum,
                NextStatementDate = ResolveDraftNextStatementDate(card),
                NextDueDate = ResolveDraftNextDueDate(card),
                Source = _statementDraftSource,
                SourceDocumentFingerprint = _statementDraftFingerprint,
                ImportedAt = _statementDraftSource ==
                             CreditCardStatementSource.PdfImport
                    ? DateTimeOffset.UtcNow
                    : null,
                CreatedAt = card.CurrentStatement?.CreatedAt ??
                            DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await service.SaveCreditCardStatementAsync(
                card.Id,
                statement,
                BuildDraftPaymentPlan(amount));
            HasStatementDraft = false;
            await LoadAsync(card.Id);
            await feedback.ShowSuccessAsync(
                "Kart projeksiyonun yeni kesilmiş ekstreye göre güncellendi.",
                "Ekstre Kaydedildi");
        }
        catch (Exception exception)
        {
            var message = UserFacingMessages.FromException(exception);
            SetStatus(message);
            await feedback.ShowErrorAsync(message, "Ekstre Kaydedilemedi");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task SetCurrentStatementMinimumAsync() =>
        SaveCurrentStatementPlanAsync(CurrentStatementPaymentMode.Minimum);

    [RelayCommand]
    private Task SetCurrentStatementFullAsync() =>
        SaveCurrentStatementPlanAsync(CurrentStatementPaymentMode.Full);

    [RelayCommand]
    private void ShowCurrentStatementCustom() =>
        IsCurrentStatementCustom = true;

    [RelayCommand]
    private async Task SaveCurrentStatementCustomAsync()
    {
        var amount = ParseMoney(
            CurrentStatementCustomAmount,
            "Bu ekstre için özel ödeme");
        await SaveCurrentStatementPlanAsync(
            CurrentStatementPaymentMode.Custom,
            amount);
    }

    [RelayCommand]
    private Task PayMinimumAsync() =>
        SavePreferenceAsync(
            CreditCardPaymentStrategy.Minimum,
            "Gelecek ekstrelerde asgari ödeme seçildi.");

    [RelayCommand]
    private Task PayFullAsync() =>
        SavePreferenceAsync(
            CreditCardPaymentStrategy.FullStatement,
            "Gelecek ekstrelerde tamamı seçildi.");

    [RelayCommand]
    private Task AskEachStatementAsync() =>
        SavePreferenceAsync(
            CreditCardPaymentStrategy.AskEachStatement,
            "Gelecek ekstrelerde sor seçildi.");

    [RelayCommand]
    private Task FallbackMinimumAsync() =>
        SaveFallbackAsync(
            ProjectionFallbackStrategy.Minimum,
            "Kararsız ekstrelerde asgari ödeme kullanılacak.");

    [RelayCommand]
    private Task FallbackFullAsync() =>
        SaveFallbackAsync(
            ProjectionFallbackStrategy.FullStatement,
            "Kararsız ekstrelerde tamamı kullanılacak.");

    [RelayCommand]
    private void AddCharge()
    {
        try
        {
            var amount = ParsePositiveMoney(
                ChargeAmount,
                "Kart harcaması");
            var id = Guid.NewGuid();
            var description = string.IsNullOrWhiteSpace(ChargeDescription)
                ? "Gelecek taksit"
                : ChargeDescription.Trim();
            FutureCharges.Add(new DatedAmountLine(
                id,
                DateOnly.FromDateTime(ChargeDate),
                amount,
                description));
            _chargeDescriptions[id] = description;
            ChargeAmount = string.Empty;
            ChargeDescription = string.Empty;
            HasFutureCharges = FutureCharges.Count > 0;
            SetStatus(string.Empty);
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(exception));
        }
    }

    public void RemoveCharge(DatedAmountLine line)
    {
        FutureCharges.Remove(line);
        _chargeDescriptions.Remove(line.Id);
        HasFutureCharges = FutureCharges.Count > 0;
    }

    [RelayCommand]
    private async Task SaveChargesAsync()
    {
        try
        {
            var card = RequiredCard();
            await service.SaveCreditCardAsync(card with
            {
                Charges = FutureCharges
                    .OrderBy(x => x.Date)
                    .Select(x => new CardCharge
                    {
                        Id = x.Id,
                        CreditCardId = card.Id,
                        PostingDate = x.Date,
                        Amount = x.Amount,
                        Description = _chargeDescriptions
                            .GetValueOrDefault(x.Id, x.Description)
                    })
                    .ToArray()
            });
            await LoadAsync(card.Id);
            await feedback.ShowSuccessAsync(
                "Gelecek kart harcamaları kaydedildi.");
        }
        catch (Exception exception)
        {
            var message = UserFacingMessages.FromException(exception);
            SetStatus(message);
            await feedback.ShowErrorAsync(message);
        }
    }

    [RelayCommand]
    private Task OpenCardDetailsAsync()
    {
        var card = RequiredCard();
        return Shell.Current.GoToAsync(
            "//commitments/commitments-content",
            new ShellNavigationQueryParameters
            {
                ["cardId"] = card.Id.ToString("D"),
                ["editCard"] = "true"
            });
    }

    private async Task SaveCurrentStatementPlanAsync(
        CurrentStatementPaymentMode mode,
        decimal? customAmount = null)
    {
        try
        {
            var card = RequiredCard();
            var statement = card.CurrentStatement ??
                            throw new InvalidOperationException(
                                "Önce kesilmiş ekstre bilgisini gir.");
            await service.SaveCreditCardStatementAsync(
                card.Id,
                statement,
                new CurrentStatementPaymentPlan
                {
                    Mode = mode,
                    CustomAmount = mode == CurrentStatementPaymentMode.Custom
                        ? customAmount
                        : null
                });
            IsCurrentStatementCustom = false;
            await LoadAsync(card.Id);
            await feedback.ShowSuccessAsync(
                "Bu ekstre için ödeme planı kaydedildi.");
        }
        catch (Exception exception)
        {
            var message = UserFacingMessages.FromException(exception);
            SetStatus(message);
            await feedback.ShowErrorAsync(message);
        }
    }

    private async Task SavePreferenceAsync(
        CreditCardPaymentStrategy strategy,
        string message)
    {
        try
        {
            var card = RequiredCard();
            await service.SaveCreditCardAsync(card with
            {
                PaymentStrategy = strategy,
                FixedPaymentAmount = null
            });
            await LoadAsync(card.Id);
            await feedback.ShowSuccessAsync(message);
        }
        catch (Exception exception)
        {
            var error = UserFacingMessages.FromException(exception);
            SetStatus(error);
            await feedback.ShowErrorAsync(error);
        }
    }

    private async Task SaveFallbackAsync(
        ProjectionFallbackStrategy strategy,
        string message)
    {
        try
        {
            var card = RequiredCard();
            await service.SaveCreditCardAsync(card with
            {
                ProjectionFallbackStrategy = strategy,
                ProjectionFallbackFixedAmount = null
            });
            await LoadAsync(card.Id);
            await feedback.ShowSuccessAsync(message);
        }
        catch (Exception exception)
        {
            var error = UserFacingMessages.FromException(exception);
            SetStatus(error);
            await feedback.ShowErrorAsync(error);
        }
    }

    private void StartStatementDraft(
        CreditCardStatementImportResult result)
    {
        var card = RequiredCard();
        _statementDraftSource = CreditCardStatementSource.PdfImport;
        _statementDraftFingerprint = result.SourceDocumentFingerprint;
        _statementDraftExactNextStatementDate = result.NextStatementDate;
        _statementDraftExactNextDueDate = result.NextDueDate;
        _statementDraftPaymentMode =
            card.CurrentStatementPaymentPlan?.Mode ??
            CurrentStatementPaymentMode.Minimum;
        StatementDraftTitle = result.HasRequiredFields
            ? "Ekstreyi Kontrol Et"
            : "Ekstreyi Elle Gir";
        StatementDraftBank = string.IsNullOrWhiteSpace(result.DetectedBank)
            ? $"{card.Bank} {card.Name}".Trim()
            : result.DetectedBank;
        StatementDraftLast4 = result.CardLast4 is null
            ? string.Empty
            : $"**** {result.CardLast4}";
        HasStatementDraftLast4 = !string.IsNullOrWhiteSpace(
            StatementDraftLast4);
        StatementDraftDate = (result.StatementDate ?? DateOnly.FromDateTime(DateTime.Today))
            .ToDateTime(TimeOnly.MinValue);
        StatementDraftDueDate = (result.DueDate ?? DateOnly.FromDateTime(DateTime.Today))
            .ToDateTime(TimeOnly.MinValue);
        StatementDraftAmount = result.StatementAmount?.ToString("N2", TurkishCulture) ?? string.Empty;
        StatementDraftMinimum = result.MinimumPaymentAmount?.ToString("N2", TurkishCulture) ?? string.Empty;
        RefreshStatementDraftNextDates();
        StatementDraftWarnings = string.Join(Environment.NewLine, result.Warnings);
        HasStatementDraftWarnings = result.Warnings.Count > 0;
        IsStatementDraftDuplicate = card.CurrentStatement is not null &&
                                    (card.CurrentStatement.StatementDate ==
                                     result.StatementDate ||
                                     (!string.IsNullOrWhiteSpace(
                                          result.SourceDocumentFingerprint) &&
                                      card.CurrentStatement.SourceDocumentFingerprint ==
                                      result.SourceDocumentFingerprint));
        StatementDraftDuplicate = IsStatementDraftDuplicate
            ? "Bu ekstre daha önce içeri aktarılmış. Kaydedersen mevcut ekstre güncellenecek."
            : string.Empty;
        StatementDraftCustomAmount =
            card.CurrentStatementPaymentPlan?.CustomAmount?.ToString("N2", TurkishCulture) ??
            string.Empty;
        RefreshStatementDraftPaymentMode();
        HasStatementDraft = true;
    }

    private CurrentStatementPaymentPlan BuildDraftPaymentPlan(
        decimal statementAmount)
    {
        var customAmount = _statementDraftPaymentMode ==
                           CurrentStatementPaymentMode.Custom
            ? ParseMoney(
                StatementDraftCustomAmount,
                "Bu ekstre için özel ödeme")
            : (decimal?)null;
        if (customAmount is decimal parsedCustomAmount &&
            (parsedCustomAmount < 0m ||
             parsedCustomAmount > statementAmount))
        {
            throw new InvalidOperationException(
                "Bu ekstre için ödeme tutarı 0 ile ekstre tutarı arasında olmalıdır.");
        }

        return new CurrentStatementPaymentPlan
        {
            Mode = _statementDraftPaymentMode,
            CustomAmount = customAmount
        };
    }

    private void RefreshStatementDraftPaymentMode()
    {
        StatementDraftPaymentPlanText = _statementDraftPaymentMode switch
        {
            CurrentStatementPaymentMode.Full => "Tamamı",
            CurrentStatementPaymentMode.Custom => "Başka Tutar",
            _ => "Asgari"
        };
        IsStatementDraftCustom =
            _statementDraftPaymentMode == CurrentStatementPaymentMode.Custom;
    }

    partial void OnStatementDraftDateChanged(DateTime value) =>
        RefreshStatementDraftNextDates();

    private void RefreshStatementDraftNextDates()
    {
        if (_card is null)
        {
            return;
        }

        var nextStatementDate = ResolveDraftNextStatementDate(_card);
        var nextDueDate = ResolveDraftNextDueDate(_card);
        StatementDraftNextStatementDate = nextStatementDate
            .ToString("dd.MM.yyyy", TurkishCulture);
        StatementDraftNextDueDate = nextDueDate
            .ToString("dd.MM.yyyy", TurkishCulture);
    }

    private DateOnly ResolveDraftNextStatementDate(CreditCard card) =>
        CreditCardStatementCalculator.ResolveNextStatementDate(
            DateOnly.FromDateTime(StatementDraftDate),
            card.StatementClosingDay,
            _statementDraftExactNextStatementDate);

    private DateOnly ResolveDraftNextDueDate(CreditCard card) =>
        CreditCardStatementCalculator.ResolveNextDueDate(
            ResolveDraftNextStatementDate(card),
            card.PaymentDueDay,
            _statementDraftExactNextDueDate);

    private Task ShowManualFallbackAsync(bool timedOut = false) =>
        feedback.ShowErrorAsync(
        timedOut
            ? "Ekstreyi otomatik okumak uzun sürdü. Bilgileri elle girebilirsin."
            : "Bilgileri elle girebilirsin.",
        "Ekstre Otomatik Okunamadı",
        "Elle Gir");

    private CreditCard RequiredCard() =>
        _card ?? throw new InvalidOperationException("Kredi kartı bulunamadı.");

    private FinancialPlan RequiredPlan() =>
        _plan ?? throw new InvalidOperationException("Finans planı bulunamadı.");

    private void RefreshCardState()
    {
        var card = RequiredCard();
        var plan = RequiredPlan();
        var projections = cardCalculator.Project(
            card,
            UpcomingStatementCount + 1,
            useProjectionFallback: true,
            plan.Settings.CreditCardCarryInterestRate);
        var currentProjection = projections[0];
        var nextProjection = projections.Count > 1 ? projections[1] : null;

        Title = card.Name.Length == 0 ? "Kart" : card.Name;
        Subtitle =
            $"{card.Bank} • Kesim {card.StatementClosingDay}. gün • Son ödeme {card.PaymentDueDay}. gün";
        KnownDebt = Money(card.KnownTotalDebt, 2);
        LimitText = Money(card.Limit, 2);
        HasActualStatement = card.CurrentStatement is not null;
        HasNoActualStatement = !HasActualStatement;

        if (card.CurrentStatement is { } statement)
        {
            StatementText = Money(statement.StatementAmount, 2);
            StatementDateText = statement.StatementDate.ToString(
                "dd MMMM",
                TurkishCulture);
            StatementDueText = statement.DueDate.ToString(
                "dd MMMM",
                TurkishCulture);
            StatementMinimumText = Money(
                statement.MinimumPaymentAmount,
                2);
            CurrentStatementPlanText = CurrentPlanLabel(
                card.CurrentStatementPaymentPlan);
            CurrentStatementPaymentText = currentProjection.Payment is decimal payment
                ? Money(payment, 2)
                : "Henüz belirlenmedi";
            CurrentStatementCustomAmount =
                card.CurrentStatementPaymentPlan?.CustomAmount
                    ?.ToString("N2", TurkishCulture) ?? string.Empty;
            NextStatementEstimateText = nextProjection?.StatementBalance is decimal nextAmount
                ? Money(nextAmount, 2)
                : "-";
            NextStatementDateText =
                (statement.NextStatementDate ?? nextProjection?.StatementCloseDate)
                ?.ToString("dd MMMM", TurkishCulture) ?? "-";
            NextStatementBreakdownText =
                $"Devreden {Money(currentProjection.CarriedAfterPayment ?? 0m, 2)} • " +
                $"finansman {Money(currentProjection.CarryInterest, 2)} • " +
                $"bilinen yeni harcama {Money(nextProjection?.NewCharges ?? 0m, 2)}";
        }
        else
        {
            StatementText =
                $"Devreden {Money(card.CarriedBalance, 2)} • Ekstreleşmemiş {Money(card.UnbilledSpending, 2)}";
            StatementDateText = card.BalanceAsOfDate.ToString(
                "dd MMMM",
                TurkishCulture);
            StatementDueText = currentProjection.PaymentDueDate.ToString(
                "dd MMMM",
                TurkishCulture);
            StatementMinimumText = currentProjection.MinimumPayment is decimal minimum
                ? Money(minimum, 2)
                : "-";
            CurrentStatementPlanText = "Legacy başlangıç";
            CurrentStatementPaymentText = currentProjection.Payment is decimal payment
                ? Money(payment, 2)
                : "Henüz belirlenmedi";
            NextStatementEstimateText = nextProjection?.StatementBalance is decimal nextAmount
                ? Money(nextAmount, 2)
                : "-";
            NextStatementDateText = nextProjection?.StatementCloseDate
                .ToString("dd MMMM", TurkishCulture) ?? "-";
            NextStatementBreakdownText =
                "Kesilmiş ekstre eklenince bankanın gerçek tutarı esas alınır.";
        }

        PaymentPreferenceText = card.PaymentStrategy switch
        {
            CreditCardPaymentStrategy.Minimum => "Gelecekte asgari",
            CreditCardPaymentStrategy.FullStatement => "Gelecekte tamamı",
            CreditCardPaymentStrategy.FixedAmount =>
                $"Sabit {Money(card.FixedPaymentAmount.GetValueOrDefault(), 2)}",
            _ => "Her ekstrede sor"
        };
        FallbackText = card.ProjectionFallbackStrategy switch
        {
            ProjectionFallbackStrategy.FullStatement => "Tamamı",
            ProjectionFallbackStrategy.FixedAmount =>
                $"Sabit {Money(card.ProjectionFallbackFixedAmount.GetValueOrDefault(), 2)}",
            ProjectionFallbackStrategy.None => "Hesaba katma",
            _ => "Asgari"
        };

        FutureCharges.Clear();
        _chargeDescriptions.Clear();
        foreach (var charge in card.Charges.OrderBy(x => x.PostingDate))
        {
            FutureCharges.Add(new DatedAmountLine(
                charge.Id,
                charge.PostingDate,
                charge.Amount,
                charge.Description));
            _chargeDescriptions[charge.Id] = charge.Description;
        }

        HasFutureCharges = FutureCharges.Count > 0;

        // En yakın (kesilmiş) ekstrenin kendi planı var; burada yalnızca
        // gelecek ekstreler ay bazlı planlanır.
        UpcomingStatements.Clear();
        foreach (var upcoming in projections.Where(x => !x.IsActualStatement))
        {
            UpcomingStatements.Add(new UpcomingStatementLine(
                upcoming.PaymentDueDate,
                $"Kesim {upcoming.StatementCloseDate.ToString("dd MMMM", TurkishCulture)} • " +
                $"Son ödeme {upcoming.PaymentDueDate.ToString("dd MMMM", TurkishCulture)}",
                upcoming.StatementBalance is decimal balance
                    ? Money(balance, 2)
                    : "-",
                UpcomingPlanLabel(upcoming),
                upcoming.PaymentResolution ==
                CreditCardPaymentResolution.DueDateOverride));
        }

        HasUpcomingStatements = UpcomingStatements.Count > 0;
    }

    private static string UpcomingPlanLabel(
        CreditCardStatementProjection projection) =>
        projection.PaymentResolution switch
        {
            CreditCardPaymentResolution.DueDateOverride =>
                $"Bu ay: {PaymentTypeLabel(projection.AppliedPaymentType)}",
            CreditCardPaymentResolution.GeneralStrategy =>
                $"Genel plan: {PaymentTypeLabel(projection.AppliedPaymentType)}",
            CreditCardPaymentResolution.ProjectionFallback =>
                $"Kararsızda: {PaymentTypeLabel(projection.AppliedPaymentType)}",
            _ => "Henüz belirlenmedi"
        };

    private static string PaymentTypeLabel(
        CreditCardPaymentType? paymentType) => paymentType switch
    {
        CreditCardPaymentType.Minimum => "Asgari",
        CreditCardPaymentType.FullStatement => "Tamamı",
        CreditCardPaymentType.FixedAmount => "Sabit tutar",
        _ => "Belirlenmedi"
    };

    public Task SetUpcomingStatementMinimumAsync(UpcomingStatementLine line) =>
        SaveUpcomingStatementPlanAsync(
            line.DueDate,
            CreditCardPaymentType.Minimum,
            "Bu ekstre için asgari ödeme seçildi.");

    public Task SetUpcomingStatementFullAsync(UpcomingStatementLine line) =>
        SaveUpcomingStatementPlanAsync(
            line.DueDate,
            CreditCardPaymentType.FullStatement,
            "Bu ekstre için tamamı seçildi.");

    public async Task ClearUpcomingStatementPlanAsync(
        UpcomingStatementLine line)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var card = RequiredCard();
            await service.RemoveCreditCardPaymentPlanAsync(
                card.Id,
                line.DueDate);
            await LoadAsync(card.Id);
            await feedback.ShowSuccessAsync(
                "Bu ekstre yeniden genel plana bırakıldı.");
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

    private async Task SaveUpcomingStatementPlanAsync(
        DateOnly dueDate,
        CreditCardPaymentType paymentType,
        string successMessage)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var card = RequiredCard();
            await service.SaveCreditCardPaymentPlanAsync(
                card.Id,
                dueDate,
                paymentType);
            await LoadAsync(card.Id);
            await feedback.ShowSuccessAsync(successMessage);
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

    private static string CurrentPlanLabel(
        CurrentStatementPaymentPlan? plan) => plan?.Mode switch
    {
        CurrentStatementPaymentMode.Full => "Tamamı",
        CurrentStatementPaymentMode.Custom =>
            $"Başka tutar: {Money(plan.CustomAmount.GetValueOrDefault(), 2)}",
        CurrentStatementPaymentMode.Minimum => "Asgari",
        _ => "Henüz seçilmedi"
    };

}
