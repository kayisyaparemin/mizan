using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoinFlow.App.Models;
using CoinFlow.Application.Models;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.App.ViewModels;

public partial class CommitmentsViewModel
{
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

    public ObservableCollection<SelectionOption<CurrentStatementPaymentMode>>
        CurrentStatementPaymentModes { get; } =
    [
        new("Asgari", CurrentStatementPaymentMode.Minimum),
        new("Tamamı", CurrentStatementPaymentMode.Full),
        new("Başka tutar", CurrentStatementPaymentMode.Custom)
    ];

    [RelayCommand]
    private void ToggleAdvancedCardOptions() =>
        ShowAdvancedCardOptions = !ShowAdvancedCardOptions;

    partial void OnSelectedPaymentStrategyChanged(SelectionOption<CreditCardPaymentStrategy>? value) =>
        IsFixedPaymentStrategy = value?.Value == CreditCardPaymentStrategy.FixedAmount;

    partial void OnSelectedProjectionFallbackStrategyChanged(SelectionOption<ProjectionFallbackStrategy>? value) =>
        IsFixedProjectionFallback = value?.Value == ProjectionFallbackStrategy.FixedAmount;

    partial void OnCardHasActualStatementChanged(bool value) => IsLegacyCardSetup = !value;

    partial void OnSelectedCurrentStatementPaymentModeChanged(SelectionOption<CurrentStatementPaymentMode>? value) =>
        IsCurrentStatementCustomPayment = value?.Value == CurrentStatementPaymentMode.Custom;

    partial void OnCardStatementDateChanged(DateTime value) => RefreshCardNextDates();
    partial void OnClosingDayChanged(string value) => RefreshCardNextDates();
    partial void OnDueDayChanged(string value) => RefreshCardNextDates();

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

            ApplyStatementImport(attempt.Result);
            SetStatus("Ekstre bilgileri aktarıldı. Bilgileri kontrol edip kaydedebilirsin.");
        }
        catch (Exception exception)
        {
            CardHasActualStatement = true;
            await ShowManualFallbackAsync();
            SetStatus(UserFacingMessages.FromException(exception));
        }
        finally
        {
            _statementImportCancellation = null;
            IsStatementImporting = false;
            IsBusy = false;
        }
    }

    public void CancelStatementImport() =>
        _statementImportCancellation?.Cancel();

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
        return new CreditCard
        {
            Id = cardId,
            Name = RequireName(),
            Bank = Bank.Trim(),
            Limit = RequirePositive(ParseMoney(CardLimit, "Kart limiti"), "Kart limiti"),
            CarriedBalance = actualStatement is null
                ? Math.Max(0m, ParseOptionalMoney(CarriedBalance) ?? 0m)
                : 0m,
            UnbilledSpending = actualStatement is null
                ? Math.Max(0m, ParseOptionalMoney(UnbilledSpending) ?? 0m)
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
            throw new InvalidOperationException("Kart kesim günü geçerli olmalıdır.");
        return CreditCardStatementCalculator.ResolveNextStatementDate(
            DateOnly.FromDateTime(CardStatementDate), closingDay, _cardExactNextStatementDate);
    }

    private DateOnly ResolveCardNextDueDate()
    {
        if (!int.TryParse(DueDay, out var dueDay))
            throw new InvalidOperationException("Kart son ödeme günü geçerli olmalıdır.");
        return CreditCardStatementCalculator.ResolveNextDueDate(
            ResolveCardNextStatementDate(), dueDay, _cardExactNextDueDate);
    }
}
