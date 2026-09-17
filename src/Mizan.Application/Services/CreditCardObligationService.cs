using Mizan.Application.Abstractions;
using Mizan.Application.Models;
using Mizan.Domain.Calculations;
using Mizan.Domain.Models;

namespace Mizan.Application.Services;

public sealed class CreditCardObligationService(
    IMizanStore store,
    IClock clock,
    CreditCardPaymentPreferenceResolver paymentPreferenceResolver,
    IFinancialPlanQueryService queryService)
{
    public async Task SaveCreditCardAsync(
        CreditCard card,
        CancellationToken cancellationToken = default)
    {
        var normalized = ObligationValidation.NormalizeCreditCard(card, clock);
        ObligationValidation.ValidateCreditCardPaymentSettings(normalized);
        normalized = await AppendPaymentPreferenceHistoryAsync(
            normalized,
            cancellationToken);
        CreditCardPaymentPreferenceResolver.Validate(
            normalized.PaymentPreferences);
        await store.UpsertCreditCardAsync(normalized, cancellationToken);
        await queryService.CapturePlanningChangeAsync(
            "Kart planı değişti",
            cancellationToken);
    }

    public async Task SaveCreditCardStatementAsync(
        Guid creditCardId,
        CreditCardStatement statement,
        CurrentStatementPaymentPlan paymentPlan,
        CancellationToken cancellationToken = default)
    {
        var card = (await store.GetCreditCardsAsync(cancellationToken))
            .SingleOrDefault(x => x.Id == creditCardId)
            ?? throw new InvalidOperationException("Kredi kartı bulunamadı.");
        var now = clock.UtcNow;
        var normalizedStatement = statement with
        {
            CreditCardId = creditCardId,
            CreatedAt = statement.CreatedAt == default
                ? now
                : statement.CreatedAt,
            UpdatedAt = now,
            ImportedAt = statement.Source == CreditCardStatementSource.PdfImport
                ? statement.ImportedAt ?? now
                : statement.ImportedAt
        };
        var normalizedPlan = paymentPlan.Mode == CurrentStatementPaymentMode.Custom
            ? paymentPlan
            : paymentPlan with { CustomAmount = null };
        await SaveCreditCardAsync(card with
        {
            CurrentStatement = normalizedStatement,
            CurrentStatementPaymentPlan = normalizedPlan
        }, cancellationToken);
    }

    public async Task DeleteCreditCardAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await store.DeleteCreditCardAsync(id, cancellationToken);
        await queryService.CapturePlanningChangeAsync(
            "Kart planı değişti",
            cancellationToken);
    }

    public async Task SaveCreditCardPaymentPlanAsync(
        Guid creditCardId,
        DateOnly dueDate,
        CreditCardPaymentType paymentType,
        decimal? amount = null,
        CancellationToken cancellationToken = default)
    {
        var card = (await store.GetCreditCardsAsync(cancellationToken))
            .SingleOrDefault(x => x.Id == creditCardId)
            ?? throw new InvalidOperationException("Kredi kartı bulunamadı.");
        if (paymentType == CreditCardPaymentType.FixedAmount &&
            amount is null or <= 0m)
        {
            throw new InvalidOperationException(
                "Özel ödeme tutarı sıfırdan büyük olmalıdır.");
        }

        var existing = card.PaymentPlans
            .FirstOrDefault(x => x.DueDate == dueDate);
        var paymentPlan = new CreditCardPaymentPlan
        {
            Id = existing?.Id ?? Guid.NewGuid(),
            CreditCardId = creditCardId,
            DueDate = dueDate,
            PaymentType = paymentType,
            Amount = paymentType == CreditCardPaymentType.FixedAmount
                ? amount
                : null
        };
        await SaveCreditCardAsync(card with
        {
            PaymentPlans = card.PaymentPlans
                .Where(x => x.DueDate != dueDate)
                .Append(paymentPlan)
                .OrderBy(x => x.DueDate)
                .ToArray()
        }, cancellationToken);
    }

    public async Task SetStatementPaymentModeAsync(
        Guid creditCardId,
        DateOnly dueDate,
        CreditCardPaymentType paymentType,
        CancellationToken cancellationToken = default)
    {
        if (paymentType == CreditCardPaymentType.FixedAmount)
        {
            throw new InvalidOperationException(
                "Bu ekrandan yalnızca asgari veya tamamı seçilebilir.");
        }

        var card = (await store.GetCreditCardsAsync(cancellationToken))
            .SingleOrDefault(x => x.Id == creditCardId)
            ?? throw new InvalidOperationException("Kredi kartı bulunamadı.");

        if (card.CurrentStatement is { } statement &&
            statement.DueDate == dueDate)
        {
            await SaveCreditCardAsync(
                card with
                {
                    CurrentStatementPaymentPlan =
                        new CurrentStatementPaymentPlan
                        {
                            Mode = paymentType ==
                                   CreditCardPaymentType.Minimum
                                ? CurrentStatementPaymentMode.Minimum
                                : CurrentStatementPaymentMode.Full
                        }
                },
                cancellationToken);
            return;
        }

        await SaveCreditCardPaymentPlanAsync(
            creditCardId,
            dueDate,
            paymentType,
            cancellationToken: cancellationToken);
    }

    public async Task RemoveCreditCardPaymentPlanAsync(
        Guid creditCardId,
        DateOnly dueDate,
        CancellationToken cancellationToken = default)
    {
        var card = (await store.GetCreditCardsAsync(cancellationToken))
            .SingleOrDefault(x => x.Id == creditCardId)
            ?? throw new InvalidOperationException("Kredi kartı bulunamadı.");
        await SaveCreditCardAsync(card with
        {
            PaymentPlans = card.PaymentPlans
                .Where(x => x.DueDate != dueDate)
                .ToArray()
        }, cancellationToken);
    }

    private async Task<CreditCard> AppendPaymentPreferenceHistoryAsync(
        CreditCard card,
        CancellationToken cancellationToken)
    {
        var existing = (await store.GetCreditCardsAsync(cancellationToken))
            .FirstOrDefault(x => x.Id == card.Id);
        var history = existing?.PaymentPreferences ?? [];

        if (card.CurrentStatement is not { } statement ||
            card.CurrentStatementPaymentPlan is not { } plan)
        {
            return card with { PaymentPreferences = history };
        }

        var effective = paymentPreferenceResolver.Resolve(
            statement.StatementDate,
            history);
        if (CreditCardPaymentPreferenceResolver.RepresentsSameDecision(
                effective,
                plan))
        {
            return card with { PaymentPreferences = history };
        }

        var appended = new CreditCardPaymentPreference
        {
            CreditCardId = card.Id,
            Mode = plan.Mode,
            CustomAmount = plan.Mode == CurrentStatementPaymentMode.Custom
                ? plan.CustomAmount
                : null,
            EffectiveFromStatementDate = statement.StatementDate,
            CreatedAt = clock.UtcNow
        };
        return card with
        {
            PaymentPreferences = history.Append(appended).ToArray()
        };
    }
}
