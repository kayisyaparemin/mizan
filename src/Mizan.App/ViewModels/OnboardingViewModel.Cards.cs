using CommunityToolkit.Mvvm.Input;
using Mizan.App.Models;
using Mizan.App.Services;
using Mizan.Application.Models;
using Mizan.Domain.Calculations;
using Mizan.Domain.Models;

namespace Mizan.App.ViewModels;

public partial class OnboardingViewModel
{
    [RelayCommand]
    private void ToggleAdvancedCardOptions() =>
        ShowAdvancedCardOptions = !ShowAdvancedCardOptions;

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
            CardCarriedBalance = string.Empty;
            CardUnbilledSpending = string.Empty;
            CardClosingDay = string.Empty;
            CardDueDay = string.Empty;
            CardMinimumRate = string.Empty;
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
}
