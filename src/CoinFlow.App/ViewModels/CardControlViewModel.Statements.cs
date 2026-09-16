using CommunityToolkit.Mvvm.Input;
using CoinFlow.Application.Models;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.App.ViewModels;

public partial class CardControlViewModel
{
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
}
