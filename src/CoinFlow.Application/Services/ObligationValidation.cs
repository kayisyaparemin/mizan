using CoinFlow.Application.Abstractions;
using CoinFlow.Application.Models;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Services;

internal static class ObligationValidation
{
    public static void ValidateOnboardingDraft(OnboardingDraft draft)
    {
        CalendarRules.ValidateDay(draft.Settings.SalaryDay);
        if (draft.Settings.MonthlyLivingBudget < 0m)
        {
            throw new InvalidOperationException(
                "Yaşam gideri negatif olamaz.");
        }

        if (draft.Settings.CreditCardCarryInterestRate is < 0m or > 1m ||
            draft.Settings.DeficitFinancingInterestRate is < 0m or > 1m)
        {
            throw new InvalidOperationException(
                "Faiz varsayımları %0 ile %100 arasında olmalıdır.");
        }

        if (draft.Salaries.Count == 0)
        {
            throw new InvalidOperationException(
                "Başlamak için en az bir gelir eklemelisin.");
        }

        if (draft.Salaries.Any(x => x.Amount <= 0m))
        {
            throw new InvalidOperationException(
                "Gelir tutarı sıfırdan büyük olmalıdır.");
        }

        if (draft.OtherIncomes.Any(x => x.Amount <= 0m))
        {
            throw new InvalidOperationException(
                "Tek seferlik gelir tutarı sıfırdan büyük olmalıdır.");
        }

        foreach (var loan in draft.Loans)
        {
            if (loan.MonthlyPayment <= 0m ||
                loan.RemainingInstallmentCount < 1)
            {
                throw new InvalidOperationException(
                    "Kredi taksiti ve kalan taksit sayısı pozitif olmalıdır.");
            }

            CalendarRules.ValidateDay(loan.PaymentDay);
        }

        foreach (var plan in draft.PaymentPlans)
        {
            if (plan.Installments.Count == 0 ||
                plan.Installments.Any(x => x.Amount <= 0m))
            {
                throw new InvalidOperationException(
                    "Ödeme planında en az bir pozitif ödeme olmalıdır.");
            }
        }

        foreach (var card in draft.CreditCards)
        {
            if (card.Limit <= 0m)
            {
                throw new InvalidOperationException(
                    "Kart limiti sıfırdan büyük olmalıdır.");
            }

            if (card.CarriedBalance < 0m ||
                card.UnbilledSpending < 0m ||
                card.MinimumPaymentRate is <= 0m or > 1m ||
                card.Charges.Any(x => x.Amount <= 0m))
            {
                throw new InvalidOperationException(
                    "Kart tutarları ve asgari oran geçersiz.");
            }

            ValidateCreditCardPaymentSettings(card);
        }

        if (draft.PlannedLargeExpenses.Any(x => x.Amount <= 0m))
        {
            throw new InvalidOperationException(
                "Planlı ödeme tutarı sıfırdan büyük olmalıdır.");
        }

        if (!Enum.IsDefined(draft.InitialPaymentAssignmentMode))
        {
            throw new InvalidOperationException(
                "Gelir kullanım düzeni geçersiz.");
        }
    }

    public static TemporaryPaymentPlan NormalizePaymentPlan(
        TemporaryPaymentPlan plan) => plan with
        {
            Installments = plan.Installments
                .OrderBy(x => x.DueDate)
                .Select(x => x with { PlanId = plan.Id })
                .ToArray()
        };

    public static CreditCard NormalizeCreditCard(CreditCard card, IClock clock) => card with
    {
        BalanceAsOfDate = card.BalanceAsOfDate == default
            ? clock.Today
            : card.BalanceAsOfDate,
        CurrentStatement = card.CurrentStatement is null
            ? null
            : card.CurrentStatement with
            {
                CreditCardId = card.Id,
                UpdatedAt = card.CurrentStatement.UpdatedAt == default
                    ? clock.UtcNow
                    : card.CurrentStatement.UpdatedAt
            },
        CurrentStatementPaymentPlan =
            card.CurrentStatementPaymentPlan is null ||
            card.CurrentStatementPaymentPlan.Mode ==
            CurrentStatementPaymentMode.Custom
                ? card.CurrentStatementPaymentPlan
                : card.CurrentStatementPaymentPlan with
                {
                    CustomAmount = null
                },
        Charges = card.Charges
            .OrderBy(x => x.PostingDate)
            .Select(x => x with { CreditCardId = card.Id })
            .ToArray(),
        PaymentPlans = card.PaymentPlans
            .OrderBy(x => x.DueDate)
            .Select(x => x with { CreditCardId = card.Id })
            .ToArray()
    };

    public static void ValidateCreditCardPaymentSettings(CreditCard card)
    {
        CalendarRules.ValidateDay(card.StatementClosingDay);
        CalendarRules.ValidateDay(card.PaymentDueDay);
        if (card.PaymentStrategy == CreditCardPaymentStrategy.FixedAmount &&
            card.FixedPaymentAmount is null or <= 0m)
        {
            throw new InvalidOperationException(
                "Sabit ödeme stratejisi için pozitif tutar gereklidir.");
        }

        if (card.ProjectionFallbackStrategy ==
                ProjectionFallbackStrategy.FixedAmount &&
            card.ProjectionFallbackFixedAmount is null or <= 0m)
        {
            throw new InvalidOperationException(
                "Gelecek hesaplamalarda sabit tutar kullanmak için 0'dan büyük bir tutar gereklidir.");
        }

        if (card.PaymentPlans.Any(x =>
                x.PaymentType == CreditCardPaymentType.FixedAmount &&
                x.Amount is null or <= 0m))
        {
            throw new InvalidOperationException(
                "Özel kart ödeme tutarı sıfırdan büyük olmalıdır.");
        }

        if (card.CurrentStatement is { } statement)
        {
            if (statement.CreditCardId != card.Id)
            {
                throw new InvalidOperationException(
                    "Ekstre kartla eşleşmiyor.");
            }

            if (statement.StatementDate == default ||
                statement.DueDate == default ||
                statement.StatementAmount < 0m ||
                statement.MinimumPaymentAmount < 0m ||
                statement.MinimumPaymentAmount > statement.StatementAmount)
            {
                throw new InvalidOperationException(
                    "Kesilmiş ekstre bilgileri geçersiz.");
            }

            if (statement.NextStatementDate is { } nextStatementDate &&
                nextStatementDate <= statement.StatementDate)
            {
                throw new InvalidOperationException(
                    "Bir sonraki kesim tarihi mevcut ekstre tarihinden sonra olmalıdır.");
            }

            if (statement.NextDueDate is { } nextDueDate &&
                nextDueDate <= statement.DueDate)
            {
                throw new InvalidOperationException(
                    "Bir sonraki son ödeme tarihi mevcut son ödeme tarihinden sonra olmalıdır.");
            }
        }

        if (card.CurrentStatement is null &&
            card.CurrentStatementPaymentPlan is not null)
        {
            throw new InvalidOperationException(
                "Kesilmiş ekstre planı için önce ekstre bilgisi gereklidir.");
        }

        if (card is
            {
                CurrentStatement: { } currentStatement,
                CurrentStatementPaymentPlan:
                {
                    Mode: CurrentStatementPaymentMode.Custom
                } currentPlan
            } &&
            (currentPlan.CustomAmount is null or < 0m ||
             currentPlan.CustomAmount > currentStatement.StatementAmount))
        {
            throw new InvalidOperationException(
                "Bu ekstre için özel ödeme tutarı 0 ile ekstre tutarı arasında olmalıdır.");
        }
    }
}
