using SQLite;

namespace CoinFlow.Infrastructure.Persistence;

[Table("financial_snapshots")]
internal sealed class FinancialSnapshotRow
{
    [PrimaryKey] public string Id { get; set; } = string.Empty;
    [Indexed] public string SnapshotDate { get; set; } = string.Empty;
    public string ProjectionAnchorDate { get; set; } = string.Empty;
    [Indexed] public string NextReviewDate { get; set; } = string.Empty;
    public decimal ProjectionStartingSavings { get; set; }
    public int SalaryDay { get; set; }
    public string? PreviousSnapshotId { get; set; }
    public int Source { get; set; }
    [Indexed] public bool IsCurrent { get; set; }
    public string CreatedAtUtc { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
}

[Table("period_plan_snapshots")]
internal sealed class PeriodPlanSnapshotRow
{
    [PrimaryKey] public string Id { get; set; } = string.Empty;
    [Indexed] public string FinancialSnapshotId { get; set; } = string.Empty;
    [Indexed] public string PeriodStart { get; set; } = string.Empty;
    public string PeriodEnd { get; set; } = string.Empty;
    [Indexed] public string ReviewAvailableFrom { get; set; } = string.Empty;
    public string CreatedAtUtc { get; set; } = string.Empty;
    public int StrategyUsed { get; set; }
    public string PaymentWindowStart { get; set; } = string.Empty;
    public string PaymentWindowEnd { get; set; } = string.Empty;
    public decimal OpeningSavings { get; set; }
    public decimal PlannedIncome { get; set; }
    public decimal PlannedLoanPayments { get; set; }
    public decimal PlannedCardPayments { get; set; }
    public decimal PlannedTemporaryPayments { get; set; }
    public decimal PlannedInstallmentPayments { get; set; }
    public decimal PlannedOtherScheduledPayments { get; set; }
    public decimal PlannedMandatoryPayments { get; set; }
    public decimal PlannedLivingBudget { get; set; }
    public decimal PlannedLargeExpenses { get; set; }
    public decimal PlannedCardInterest { get; set; }
    public decimal PlannedDeficitInterest { get; set; }
    public decimal PlannedEndingSavings { get; set; }
}

[Table("period_plan_payment_lines")]
internal sealed class PeriodPlanPaymentLineRow
{
    [PrimaryKey] public string Id { get; set; } = string.Empty;
    [Indexed] public string PeriodPlanSnapshotId { get; set; } = string.Empty;
    public string SourceEntityId { get; set; } = string.Empty;
    public int SourceType { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PlannedDate { get; set; } = string.Empty;
    public decimal? PlannedAmount { get; set; }
    public bool IsEstimate { get; set; }
    public string Detail { get; set; } = string.Empty;
}

[Table("period_plan_revisions")]
internal sealed class PeriodPlanRevisionRow
{
    [PrimaryKey] public string Id { get; set; } = string.Empty;
    [Indexed] public string PeriodPlanSnapshotId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public string CreatedAtUtc { get; set; } = string.Empty;
    public string Trigger { get; set; } = string.Empty;
    public int StrategyUsed { get; set; }
    public decimal PlannedIncome { get; set; }
    public decimal PlannedLoanPayments { get; set; }
    public decimal PlannedCardPayments { get; set; }
    public decimal PlannedTemporaryPayments { get; set; }
    public decimal PlannedInstallmentPayments { get; set; }
    public decimal PlannedOtherScheduledPayments { get; set; }
    public decimal PlannedMandatoryPayments { get; set; }
    public decimal PlannedLivingBudget { get; set; }
    public decimal PlannedLargeExpenses { get; set; }
    public decimal PlannedCardInterest { get; set; }
    public decimal PlannedDeficitInterest { get; set; }
    public decimal PlannedInterest { get; set; }
    public decimal PlannedEndingSavings { get; set; }
    public string Note { get; set; } = string.Empty;
}

[Table("period_plan_revision_payment_lines")]
internal sealed class PeriodPlanRevisionPaymentLineRow
{
    [PrimaryKey] public string Id { get; set; } = string.Empty;
    [Indexed] public string PeriodPlanRevisionId { get; set; } = string.Empty;
    public string SourceEntityId { get; set; } = string.Empty;
    public int SourceType { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PlannedDate { get; set; } = string.Empty;
    public decimal? PlannedAmount { get; set; }
    public bool IsEstimate { get; set; }
    public string Detail { get; set; } = string.Empty;
}

[Table("period_actuals")]
internal sealed class PeriodActualRow
{
    [PrimaryKey] public string Id { get; set; } = string.Empty;
    [Indexed(Unique = true)] public string PeriodPlanSnapshotId { get; set; } = string.Empty;
    public string SourceFinancialSnapshotId { get; set; } = string.Empty;
    public string ResultFinancialSnapshotId { get; set; } = string.Empty;
    [Indexed] public string PeriodStart { get; set; } = string.Empty;
    public string PeriodEnd { get; set; } = string.Empty;
    public string FinalizedAtUtc { get; set; } = string.Empty;
    public decimal ActualIncome { get; set; }
    public decimal ActualLoanPayments { get; set; }
    public decimal ActualCardPayments { get; set; }
    public decimal ActualTemporaryPayments { get; set; }
    public decimal ActualInstallmentPayments { get; set; }
    public decimal ActualOtherScheduledPayments { get; set; }
    public decimal ActualLargeExpenses { get; set; }
    public decimal ActualMandatoryPayments { get; set; }
    public decimal ActualLivingSpend { get; set; }
    public decimal ActualInterest { get; set; }
    public decimal UnplannedIncome { get; set; }
    public decimal UnplannedPayments { get; set; }
    public decimal DerivedEndingSavings { get; set; }
    public decimal ConfirmedEndingSavings { get; set; }
    public decimal ReconciliationAdjustment { get; set; }
    public string ComparisonSummary { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
}

[Table("actual_payments")]
internal sealed class ActualPaymentRow
{
    [PrimaryKey] public string Id { get; set; } = string.Empty;
    [Indexed] public string PeriodActualId { get; set; } = string.Empty;
    public string PeriodPlanPaymentLineId { get; set; } = string.Empty;
    public string SourceEntityId { get; set; } = string.Empty;
    public int SourceType { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PlannedDate { get; set; } = string.Empty;
    public decimal? PlannedAmount { get; set; }
    public string? ActualPaymentDate { get; set; }
    public decimal ActualAmount { get; set; }
    public int Status { get; set; }
    public string Note { get; set; } = string.Empty;
}

[Table("actual_flows")]
internal sealed class ActualFlowRow
{
    [PrimaryKey] public string Id { get; set; } = string.Empty;
    [Indexed] public string PeriodActualId { get; set; } = string.Empty;
    public int Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

[Table("actual_living_breakdowns")]
internal sealed class ActualLivingBreakdownRow
{
    [PrimaryKey] public string Id { get; set; } = string.Empty;
    [Indexed] public string PeriodActualId { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

[Table("simulation_drafts")]
internal sealed class SimulationDraftRow
{
    [PrimaryKey] public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
}

// SimulationRequest alan alan yazılır, JSON blob olarak değil: şemanın geri
// kalanı da böyle ve tek bir alanın anlamı değiştiğinde derleyici uyarır.
[Table("simulation_draft_conditions")]
internal sealed class SimulationDraftConditionRow
{
    [PrimaryKey] public string Id { get; set; } = string.Empty;
    [Indexed] public string DraftId { get; set; } = string.Empty;
    public int Position { get; set; }
    public bool IsEnabled { get; set; }
    public int Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string StartDate { get; set; } = string.Empty;
    public int PaymentCount { get; set; }
    public string? FirstPaymentDate { get; set; }
    public string? CreditCardId { get; set; }
    public decimal? TotalRepaymentAmount { get; set; }
    public int? NewPaymentAssignmentMode { get; set; }
    public string? EffectiveSalaryDate { get; set; }
    public int? CardPaymentType { get; set; }
    public bool AppliesToAllStatements { get; set; }
    public string? LoanId { get; set; }
    public int? PrepaymentMode { get; set; }
}

[Table("period_observations")]
internal sealed class PeriodObservationRow
{
    [PrimaryKey] public string Id { get; set; } = string.Empty;
    [Indexed] public string PeriodPlanSnapshotId { get; set; } = string.Empty;
    public string ObservedOn { get; set; } = string.Empty;
    public decimal? ObservedBalance { get; set; }
    public decimal ObservedLivingSpend { get; set; }
    public string Note { get; set; } = string.Empty;
    public string CreatedAtUtc { get; set; } = string.Empty;
    public string UpdatedAtUtc { get; set; } = string.Empty;
}

[Table("period_observation_payments")]
internal sealed class PeriodObservationPaymentRow
{
    [PrimaryKey] public string Id { get; set; } = string.Empty;
    [Indexed] public string PeriodObservationId { get; set; } = string.Empty;
    public string PeriodPlanPaymentLineId { get; set; } = string.Empty;
    public int Status { get; set; }
    public decimal ActualAmount { get; set; }
    public string? ActualPaymentDate { get; set; }
    public string Note { get; set; } = string.Empty;
}

[Table("period_observation_flows")]
internal sealed class PeriodObservationFlowRow
{
    [PrimaryKey] public string Id { get; set; } = string.Empty;
    [Indexed] public string PeriodObservationId { get; set; } = string.Empty;
    public int Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

/// <summary>Hatırlatıcı defteri; anahtar ödemenin kaynağı + vadesi (v17).</summary>
[Table("payment_reminder_responses")]
internal sealed class PaymentReminderResponseRow
{
    [PrimaryKey] public string DueKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DueDate { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public int Kind { get; set; }
    public string AnsweredAt { get; set; } = string.Empty;
    public string? SnoozedUntil { get; set; }
}
