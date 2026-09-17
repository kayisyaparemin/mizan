using Mizan.Application.Models;
using Mizan.Domain.Models;

namespace Mizan.Application.Services;

public sealed partial class PeriodReviewService
{
    private PeriodActual BuildActual(
        PeriodPlanSnapshot plan,
        FinancialSnapshot snapshot,
        PeriodPlanRevision? revision,
        IReadOnlyList<PeriodPlanPaymentLine> paymentLines,
        PeriodReviewDraft draft,
        Guid resultSnapshotId,
        bool validateReviewDate)
    {
        if (draft.ActualLivingSpend < 0m || draft.ActualInterest < 0m)
        {
            throw new InvalidOperationException(
                "Gerçekleşen tutarlar negatif olamaz.");
        }

        if (draft.LivingBreakdown.Any(x => x.Amount < 0m) ||
            draft.LivingBreakdown.Sum(x => x.Amount) >
            draft.ActualLivingSpend)
        {
            throw new InvalidOperationException(
                "Yaşam ayrıntıları toplam yaşam giderini aşamaz.");
        }

        if (draft.Flows.Any(x =>
                x.Amount <= 0m || string.IsNullOrWhiteSpace(x.Name)))
        {
            throw new InvalidOperationException(
                "Plan dışı gelir ve ödemelerde ad ile pozitif tutar gereklidir.");
        }

        if (validateReviewDate && draft.Flows.Any(x =>
                x.Date <= snapshot.SnapshotDate ||
                x.Date > plan.SettlementAvailableFrom))
        {
            throw new InvalidOperationException(
                "Plan dışı hareket tarihi dönem aralığında olmalıdır.");
        }

        var actualId = Guid.NewGuid();
        var paymentDrafts = draft.Payments
            .GroupBy(x => x.PeriodPlanPaymentLineId)
            .ToDictionary(x => x.Key, x => x.Single());
        var payments = paymentLines.Select(line =>
        {
            var input = paymentDrafts.GetValueOrDefault(line.Id)
                ?? new ActualPaymentDraft(
                    line.Id,
                    line.PlannedAmount is null
                        ? ActualPaymentStatus.Unpaid
                        : ActualPaymentStatus.Paid,
                    line.PlannedAmount.GetValueOrDefault(),
                    line.PlannedAmount is null ? null : line.PlannedDate);
            var amount = input.Status == ActualPaymentStatus.Unpaid
                ? 0m
                : input.ActualAmount;
            if (amount < 0m)
            {
                throw new InvalidOperationException(
                    "Gerçek ödeme tutarı negatif olamaz.");
            }
            if (input.Status != ActualPaymentStatus.Unpaid && amount <= 0m)
            {
                throw new InvalidOperationException(
                    "Ödendi olarak işaretlenen tutar sıfırdan büyük olmalıdır.");
            }

            if (validateReviewDate && input.ActualPaymentDate is DateOnly date &&
                (date <= snapshot.SnapshotDate ||
                 date > plan.SettlementAvailableFrom))
            {
                throw new InvalidOperationException(
                    "Gerçek ödeme tarihi dönem aralığında olmalıdır.");
            }

            var normalizedStatus =
                input.Status == ActualPaymentStatus.Paid &&
                line.PlannedAmount is decimal plannedAmount &&
                plannedAmount != amount
                    ? ActualPaymentStatus.DifferentAmount
                    : input.Status;
            return new ActualPayment
            {
                PeriodActualId = actualId,
                PeriodPlanPaymentLineId = line.Id,
                SourceEntityId = line.SourceEntityId,
                SourceType = line.SourceType,
                Name = line.Name,
                PlannedDate = line.PlannedDate,
                PlannedAmount = line.PlannedAmount,
                ActualPaymentDate = input.Status ==
                                    ActualPaymentStatus.Unpaid
                    ? null
                    : input.ActualPaymentDate ?? line.PlannedDate,
                ActualAmount = amount,
                Status = normalizedStatus,
                Note = input.Note.Trim()
            };
        }).ToArray();
        var flows = draft.Flows.Select(x => new ActualFlow
        {
            PeriodActualId = actualId,
            Type = x.Type,
            Name = x.Name.Trim(),
            Category = x.Category.Trim(),
            Date = x.Date,
            Amount = x.Amount
        }).ToArray();
        var breakdown = draft.LivingBreakdown
            .Where(x => x.Amount > 0m)
            .Select(x => new ActualLivingBreakdown
            {
                PeriodActualId = actualId,
                Category = x.Category.Trim(),
                Amount = x.Amount
            }).ToArray();
        var baselineIncome = revision?.PlannedIncome ?? plan.PlannedIncome;
        var derived = reconciliationService.CalculateSuggestedSavings(
            snapshot,
            baselineIncome,
            payments,
            draft.ActualLivingSpend,
            draft.ActualInterest,
            flows);
        var confirmed = draft.ConfirmedStartingSavings ?? derived;
        decimal Sum(PlanPaymentSourceType type) => payments
            .Where(x => x.SourceType == type)
            .Sum(x => x.ActualAmount);
        var unplannedIncome = flows
            .Where(x => x.Type == ActualFlowType.UnplannedIncome)
            .Sum(x => x.Amount);
        var unplannedPayments = flows
            .Where(x => x.Type == ActualFlowType.UnplannedPayment)
            .Sum(x => x.Amount);
        var mandatory = payments
            .Where(x => x.SourceType !=
                        PlanPaymentSourceType.PlannedLargeExpense)
            .Sum(x => x.ActualAmount);

        return new PeriodActual
        {
            Id = actualId,
            PeriodPlanSnapshotId = plan.Id,
            SourceFinancialSnapshotId = snapshot.Id,
            ResultFinancialSnapshotId = resultSnapshotId,
            PeriodStart = plan.PeriodStart,
            PeriodEnd = plan.PeriodEnd,
            FinalizedAtUtc = clock.UtcNow,
            ActualIncome = baselineIncome + unplannedIncome,
            ActualLoanPayments = Sum(PlanPaymentSourceType.Loan),
            ActualCardPayments = Sum(PlanPaymentSourceType.CreditCard),
            ActualTemporaryPayments =
                Sum(PlanPaymentSourceType.TemporaryPayment),
            ActualInstallmentPayments =
                Sum(PlanPaymentSourceType.InstallmentPayment),
            ActualOtherScheduledPayments =
                Sum(PlanPaymentSourceType.OtherScheduledPayment),
            ActualLargeExpenses =
                Sum(PlanPaymentSourceType.PlannedLargeExpense),
            ActualMandatoryPayments = mandatory,
            ActualLivingSpend = draft.ActualLivingSpend,
            ActualInterest = draft.ActualInterest,
            UnplannedIncome = unplannedIncome,
            UnplannedPayments = unplannedPayments,
            DerivedEndingBalance = derived,
            ConfirmedEndingBalance = confirmed,
            ReconciliationAdjustment = confirmed - derived,
            Note = draft.ActualNote.Trim(),
            Payments = payments,
            Flows = flows,
            LivingBreakdown = breakdown
        };
    }
}
