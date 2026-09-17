using Mizan.Domain.Models;

namespace Mizan.Infrastructure.Persistence;

public sealed partial class SqliteMizanStore
{
    private static FinancialSnapshotRow ToRow(FinancialSnapshot value) => new()
    {
        Id = Key(value.Id),
        SnapshotDate = FormatDate(value.SnapshotDate),
        ProjectionAnchorDate = FormatDate(value.ProjectionAnchorDate),
        NextSettlementDate = FormatDate(value.NextSettlementDate),
        ProjectionOpeningBalance = value.ProjectionOpeningBalance,
        IncomeDay = value.IncomeDay,
        PreviousSnapshotId = value.PreviousSnapshotId?.ToString("D"),
        Source = (int)value.Source,
        IsCurrent = value.IsCurrent,
        CreatedAtUtc = FormatInstant(value.CreatedAtUtc),
        Note = value.Note
    };

    private static FinancialSnapshot FromRow(FinancialSnapshotRow row) => new()
    {
        Id = ParseKey(row.Id),
        SnapshotDate = ParseDate(row.SnapshotDate),
        ProjectionAnchorDate = ParseDate(row.ProjectionAnchorDate),
        NextSettlementDate = ParseDate(row.NextSettlementDate),
        ProjectionOpeningBalance = row.ProjectionOpeningBalance,
        IncomeDay = row.IncomeDay,
        PreviousSnapshotId = string.IsNullOrWhiteSpace(row.PreviousSnapshotId) ? null : ParseKey(row.PreviousSnapshotId),
        Source = (FinancialSnapshotSource)row.Source,
        IsCurrent = row.IsCurrent,
        CreatedAtUtc = ParseInstant(row.CreatedAtUtc),
        Note = row.Note
    };

    private static PeriodPlanSnapshotRow ToRow(PeriodPlanSnapshot value) => new()
    {
        Id = Key(value.Id),
        FinancialSnapshotId = Key(value.FinancialSnapshotId),
        PeriodStart = FormatDate(value.PeriodStart),
        PeriodEnd = FormatDate(value.PeriodEnd),
        SettlementAvailableFrom = FormatDate(value.SettlementAvailableFrom),
        CreatedAtUtc = FormatInstant(value.CreatedAtUtc),
        StrategyUsed = (int)value.StrategyUsed,
        PaymentWindowStart = FormatDate(value.PaymentWindowStart),
        PaymentWindowEnd = FormatDate(value.PaymentWindowEnd),
        OpeningBalance = value.OpeningBalance,
        PlannedIncome = value.PlannedIncome,
        PlannedLoanPayments = value.PlannedLoanPayments,
        PlannedCardPayments = value.PlannedCardPayments,
        PlannedTemporaryPayments = value.PlannedTemporaryPayments,
        PlannedInstallmentPayments = value.PlannedInstallmentPayments,
        PlannedOtherScheduledPayments = value.PlannedOtherScheduledPayments,
        PlannedMandatoryPayments = value.PlannedMandatoryPayments,
        PlannedVariableExpenseAllowance = value.PlannedVariableExpenseAllowance,
        PlannedLargeExpenses = value.PlannedLargeExpenses,
        PlannedCardInterest = value.PlannedCardInterest,
        PlannedDeficitInterest = value.PlannedDeficitInterest,
        PlannedEndingBalance = value.PlannedEndingBalance
    };

    private static PeriodPlanSnapshot FromRow(PeriodPlanSnapshotRow row, IEnumerable<PeriodPlanPaymentLineRow> lines) => new()
    {
        Id = ParseKey(row.Id),
        FinancialSnapshotId = ParseKey(row.FinancialSnapshotId),
        PeriodStart = ParseDate(row.PeriodStart),
        PeriodEnd = ParseDate(row.PeriodEnd),
        SettlementAvailableFrom = ParseDate(row.SettlementAvailableFrom),
        CreatedAtUtc = ParseInstant(row.CreatedAtUtc),
        StrategyUsed = (CashFlowAllocationMode)row.StrategyUsed,
        PaymentWindowStart = ParseDate(row.PaymentWindowStart),
        PaymentWindowEnd = ParseDate(row.PaymentWindowEnd),
        OpeningBalance = row.OpeningBalance,
        PlannedIncome = row.PlannedIncome,
        PlannedLoanPayments = row.PlannedLoanPayments,
        PlannedCardPayments = row.PlannedCardPayments,
        PlannedTemporaryPayments = row.PlannedTemporaryPayments,
        PlannedInstallmentPayments = row.PlannedInstallmentPayments,
        PlannedOtherScheduledPayments = row.PlannedOtherScheduledPayments,
        PlannedMandatoryPayments = row.PlannedMandatoryPayments,
        PlannedVariableExpenseAllowance = row.PlannedVariableExpenseAllowance,
        PlannedLargeExpenses = row.PlannedLargeExpenses,
        PlannedCardInterest = row.PlannedCardInterest,
        PlannedDeficitInterest = row.PlannedDeficitInterest,
        PlannedEndingBalance = row.PlannedEndingBalance,
        PaymentLines = lines.Select(FromRow).OrderBy(x => x.PlannedDate).ThenBy(x => x.Name).ToArray()
    };

    private static PeriodPlanPaymentLineRow ToRow(PeriodPlanPaymentLine value) => new()
    {
        Id = Key(value.Id),
        PeriodPlanSnapshotId = Key(value.PeriodPlanSnapshotId),
        SourceEntityId = Key(value.SourceEntityId),
        SourceType = (int)value.SourceType,
        Name = value.Name,
        PlannedDate = FormatDate(value.PlannedDate),
        PlannedAmount = value.PlannedAmount,
        IsEstimate = value.IsEstimate,
        Detail = value.Detail
    };

    private static PeriodPlanPaymentLine FromRow(PeriodPlanPaymentLineRow row) => new()
    {
        Id = ParseKey(row.Id),
        PeriodPlanSnapshotId = ParseKey(row.PeriodPlanSnapshotId),
        SourceEntityId = ParseKey(row.SourceEntityId),
        SourceType = (PlanPaymentSourceType)row.SourceType,
        Name = row.Name,
        PlannedDate = ParseDate(row.PlannedDate),
        PlannedAmount = row.PlannedAmount,
        IsEstimate = row.IsEstimate,
        Detail = row.Detail
    };

    private static PeriodPlanRevisionRow ToRow(PeriodPlanRevision value) => new()
    {
        Id = Key(value.Id),
        PeriodPlanSnapshotId = Key(value.PeriodPlanSnapshotId),
        RevisionNumber = value.RevisionNumber,
        CreatedAtUtc = FormatInstant(value.CreatedAtUtc),
        Trigger = value.Trigger,
        StrategyUsed = (int)value.StrategyUsed,
        PlannedIncome = value.PlannedIncome,
        PlannedLoanPayments = value.PlannedLoanPayments,
        PlannedCardPayments = value.PlannedCardPayments,
        PlannedTemporaryPayments = value.PlannedTemporaryPayments,
        PlannedInstallmentPayments = value.PlannedInstallmentPayments,
        PlannedOtherScheduledPayments = value.PlannedOtherScheduledPayments,
        PlannedMandatoryPayments = value.PlannedMandatoryPayments,
        PlannedVariableExpenseAllowance = value.PlannedVariableExpenseAllowance,
        PlannedLargeExpenses = value.PlannedLargeExpenses,
        PlannedCardInterest = value.PlannedCardInterest,
        PlannedDeficitInterest = value.PlannedDeficitInterest,
        PlannedInterest = value.PlannedInterest,
        PlannedEndingBalance = value.PlannedEndingBalance,
        Note = value.Note
    };

    private static PeriodPlanRevision FromRow(
        PeriodPlanRevisionRow row,
        IEnumerable<PeriodPlanRevisionPaymentLineRow> lines) => new()
    {
        Id = ParseKey(row.Id),
        PeriodPlanSnapshotId = ParseKey(row.PeriodPlanSnapshotId),
        RevisionNumber = row.RevisionNumber,
        CreatedAtUtc = ParseInstant(row.CreatedAtUtc),
        Trigger = row.Trigger,
        StrategyUsed = (CashFlowAllocationMode)row.StrategyUsed,
        PlannedIncome = row.PlannedIncome,
        PlannedLoanPayments = row.PlannedLoanPayments,
        PlannedCardPayments = row.PlannedCardPayments,
        PlannedTemporaryPayments = row.PlannedTemporaryPayments,
        PlannedInstallmentPayments = row.PlannedInstallmentPayments,
        PlannedOtherScheduledPayments = row.PlannedOtherScheduledPayments,
        PlannedMandatoryPayments = row.PlannedMandatoryPayments,
        PlannedVariableExpenseAllowance = row.PlannedVariableExpenseAllowance,
        PlannedLargeExpenses = row.PlannedLargeExpenses,
        PlannedCardInterest = row.PlannedCardInterest,
        PlannedDeficitInterest = row.PlannedDeficitInterest,
        PlannedInterest = row.PlannedInterest,
        PlannedEndingBalance = row.PlannedEndingBalance,
        Note = row.Note,
        PaymentLines = lines
            .Select(line => FromRow(line, ParseKey(row.PeriodPlanSnapshotId)))
            .OrderBy(x => x.PlannedDate)
            .ThenBy(x => x.Name)
            .ToArray()
    };

    private static PeriodPlanRevisionPaymentLineRow ToRevisionRow(
        Guid revisionId,
        PeriodPlanPaymentLine value) => new()
    {
        Id = Key(value.Id),
        PeriodPlanRevisionId = Key(revisionId),
        SourceEntityId = Key(value.SourceEntityId),
        SourceType = (int)value.SourceType,
        Name = value.Name,
        PlannedDate = FormatDate(value.PlannedDate),
        PlannedAmount = value.PlannedAmount,
        IsEstimate = value.IsEstimate,
        Detail = value.Detail
    };

    private static PeriodPlanPaymentLine FromRow(
        PeriodPlanRevisionPaymentLineRow row,
        Guid periodPlanSnapshotId) => new()
    {
        Id = ParseKey(row.Id),
        PeriodPlanSnapshotId = periodPlanSnapshotId,
        SourceEntityId = ParseKey(row.SourceEntityId),
        SourceType = (PlanPaymentSourceType)row.SourceType,
        Name = row.Name,
        PlannedDate = ParseDate(row.PlannedDate),
        PlannedAmount = row.PlannedAmount,
        IsEstimate = row.IsEstimate,
        Detail = row.Detail
    };

    private static PeriodActualRow ToRow(PeriodActual value) => new()
    {
        Id = Key(value.Id),
        PeriodPlanSnapshotId = Key(value.PeriodPlanSnapshotId),
        SourceFinancialSnapshotId = Key(value.SourceFinancialSnapshotId),
        ResultFinancialSnapshotId = Key(value.ResultFinancialSnapshotId),
        PeriodStart = FormatDate(value.PeriodStart),
        PeriodEnd = FormatDate(value.PeriodEnd),
        FinalizedAtUtc = FormatInstant(value.FinalizedAtUtc),
        ActualIncome = value.ActualIncome,
        ActualLoanPayments = value.ActualLoanPayments,
        ActualCardPayments = value.ActualCardPayments,
        ActualTemporaryPayments = value.ActualTemporaryPayments,
        ActualInstallmentPayments = value.ActualInstallmentPayments,
        ActualOtherScheduledPayments = value.ActualOtherScheduledPayments,
        ActualLargeExpenses = value.ActualLargeExpenses,
        ActualMandatoryPayments = value.ActualMandatoryPayments,
        ActualLivingSpend = value.ActualLivingSpend,
        ActualInterest = value.ActualInterest,
        UnplannedIncome = value.UnplannedIncome,
        UnplannedPayments = value.UnplannedPayments,
        DerivedEndingBalance = value.DerivedEndingBalance,
        ConfirmedEndingBalance = value.ConfirmedEndingBalance,
        ReconciliationAdjustment = value.ReconciliationAdjustment,
        ComparisonSummary = value.ComparisonSummary,
        Note = value.Note
    };

    private static PeriodActual FromRow(PeriodActualRow row, IEnumerable<ActualPaymentRow> payments, IEnumerable<ActualFlowRow> flows, IEnumerable<ActualLivingBreakdownRow> breakdown) => new()
    {
        Id = ParseKey(row.Id),
        PeriodPlanSnapshotId = ParseKey(row.PeriodPlanSnapshotId),
        SourceFinancialSnapshotId = ParseKey(row.SourceFinancialSnapshotId),
        ResultFinancialSnapshotId = ParseKey(row.ResultFinancialSnapshotId),
        PeriodStart = ParseDate(row.PeriodStart),
        PeriodEnd = ParseDate(row.PeriodEnd),
        FinalizedAtUtc = ParseInstant(row.FinalizedAtUtc),
        ActualIncome = row.ActualIncome,
        ActualLoanPayments = row.ActualLoanPayments,
        ActualCardPayments = row.ActualCardPayments,
        ActualTemporaryPayments = row.ActualTemporaryPayments,
        ActualInstallmentPayments = row.ActualInstallmentPayments,
        ActualOtherScheduledPayments = row.ActualOtherScheduledPayments,
        ActualLargeExpenses = row.ActualLargeExpenses,
        ActualMandatoryPayments = row.ActualMandatoryPayments,
        ActualLivingSpend = row.ActualLivingSpend,
        ActualInterest = row.ActualInterest,
        UnplannedIncome = row.UnplannedIncome,
        UnplannedPayments = row.UnplannedPayments,
        DerivedEndingBalance = row.DerivedEndingBalance,
        ConfirmedEndingBalance = row.ConfirmedEndingBalance,
        ReconciliationAdjustment = row.ReconciliationAdjustment,
        ComparisonSummary = row.ComparisonSummary,
        Note = row.Note,
        Payments = payments.Select(FromRow).OrderBy(x => x.PlannedDate).ToArray(),
        Flows = flows.Select(FromRow).OrderBy(x => x.Date).ToArray(),
        LivingBreakdown = breakdown.Select(FromRow).OrderBy(x => x.Category).ToArray()
    };

    private static ActualPaymentRow ToRow(ActualPayment value) => new()
    {
        Id = Key(value.Id),
        PeriodActualId = Key(value.PeriodActualId),
        PeriodPlanPaymentLineId = Key(value.PeriodPlanPaymentLineId),
        SourceEntityId = Key(value.SourceEntityId),
        SourceType = (int)value.SourceType,
        Name = value.Name,
        PlannedDate = FormatDate(value.PlannedDate),
        PlannedAmount = value.PlannedAmount,
        ActualPaymentDate = FormatNullableDate(value.ActualPaymentDate),
        ActualAmount = value.ActualAmount,
        Status = (int)value.Status,
        Note = value.Note
    };

    private static ActualPayment FromRow(ActualPaymentRow row) => new()
    {
        Id = ParseKey(row.Id),
        PeriodActualId = ParseKey(row.PeriodActualId),
        PeriodPlanPaymentLineId = ParseKey(row.PeriodPlanPaymentLineId),
        SourceEntityId = ParseKey(row.SourceEntityId),
        SourceType = (PlanPaymentSourceType)row.SourceType,
        Name = row.Name,
        PlannedDate = ParseDate(row.PlannedDate),
        PlannedAmount = row.PlannedAmount,
        ActualPaymentDate = ParseNullableDate(row.ActualPaymentDate),
        ActualAmount = row.ActualAmount,
        Status = (ActualPaymentStatus)row.Status,
        Note = row.Note
    };

    private static ActualFlowRow ToRow(ActualFlow value) => new()
    {
        Id = Key(value.Id),
        PeriodActualId = Key(value.PeriodActualId),
        Type = (int)value.Type,
        Name = value.Name,
        Category = value.Category,
        Date = FormatDate(value.Date),
        Amount = value.Amount
    };

    private static ActualFlow FromRow(ActualFlowRow row) => new()
    {
        Id = ParseKey(row.Id),
        PeriodActualId = ParseKey(row.PeriodActualId),
        Type = (ActualFlowType)row.Type,
        Name = row.Name,
        Category = row.Category,
        Date = ParseDate(row.Date),
        Amount = row.Amount
    };

    private static ActualLivingBreakdownRow ToRow(ActualLivingBreakdown value) => new()
    {
        Id = Key(value.Id),
        PeriodActualId = Key(value.PeriodActualId),
        Category = value.Category,
        Amount = value.Amount
    };

    private static ActualLivingBreakdown FromRow(ActualLivingBreakdownRow row) => new()
    {
        Id = ParseKey(row.Id),
        PeriodActualId = ParseKey(row.PeriodActualId),
        Category = row.Category,
        Amount = row.Amount
    };
}
