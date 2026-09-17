using System.Globalization;
using Mizan.Domain.Models;

namespace Mizan.Infrastructure.Persistence;

public sealed partial class SqliteMizanStore
{
    internal static CreditCardRow ToRow(CreditCard value) => new()
    {
        Id = Key(value.Id),
        Name = value.Name,
        Bank = value.Bank,
        Limit = value.Limit,
        CurrentTotalDebt = value.KnownTotalDebt,
        LastStatementDebt = value.CarriedBalance,
        LastStatementRemaining = value.CarriedBalance,
        CurrentCycleSpending = value.UnbilledSpending,
        StatementClosingDay = value.StatementClosingDay,
        PaymentDueDay = value.PaymentDueDay,
        MinimumPaymentRate = value.MinimumPaymentRate,
        PaymentMode = 0,
        ManualPaymentAmount = null,
        CarriedBalance = value.CarriedBalance,
        UnbilledSpending = value.UnbilledSpending,
        BalanceAsOfDate = FormatDate(value.BalanceAsOfDate),
        StatementModelVersion = CurrentCardStatementModelVersion,
        PaymentStrategy = (int)value.PaymentStrategy,
        FixedPaymentAmount = value.FixedPaymentAmount,
        ProjectionFallbackStrategy =
            (int)value.ProjectionFallbackStrategy,
        ProjectionFallbackFixedAmount =
            value.ProjectionFallbackFixedAmount,
        KnownNextStatementDate =
            FormatNullableDate(value.KnownNextStatementDate),
        KnownNextDueDate = FormatNullableDate(value.KnownNextDueDate)
    };

    internal static CreditCardPaymentPreferenceRow ToRow(
        CreditCardPaymentPreference value) => new()
        {
            Id = Key(value.Id),
            CreditCardId = Key(value.CreditCardId),
            Mode = (int)value.Mode,
            CustomAmount = value.CustomAmount,
            EffectiveFromStatementDate =
                FormatDate(value.EffectiveFromStatementDate),
            CreatedAt = value.CreatedAt.ToString(
                "O",
                CultureInfo.InvariantCulture),
            Note = value.Note
        };

    private static CreditCardPaymentPreference FromRow(
        CreditCardPaymentPreferenceRow row) => new()
        {
            Id = ParseKey(row.Id),
            CreditCardId = ParseKey(row.CreditCardId),
            Mode = (CurrentStatementPaymentMode)row.Mode,
            CustomAmount = row.CustomAmount,
            EffectiveFromStatementDate =
                ParseDate(row.EffectiveFromStatementDate),
            CreatedAt = DateTimeOffset.Parse(
                row.CreatedAt,
                CultureInfo.InvariantCulture),
            Note = row.Note
        };

    private static CreditCard FromRow(
        CreditCardRow row,
        IEnumerable<CardInstallmentRow> charges,
        IEnumerable<CreditCardPaymentPlanRow> paymentPlans,
        IEnumerable<CreditCardStatementRow> statements,
        IEnumerable<CreditCardPaymentPreferenceRow> paymentPreferences)
    {
        var currentStatement = statements
            .OrderByDescending(x => x.StatementDate)
            .ThenByDescending(x => x.UpdatedAt)
            .FirstOrDefault();

        return new CreditCard
        {
            Id = ParseKey(row.Id),
            Name = row.Name,
            Bank = row.Bank,
            Limit = row.Limit,
            CarriedBalance = row.CarriedBalance,
            UnbilledSpending = row.UnbilledSpending,
            BalanceAsOfDate = ParseDate(row.BalanceAsOfDate),
            StatementClosingDay = row.StatementClosingDay,
            PaymentDueDay = row.PaymentDueDay,
            MinimumPaymentRate = row.MinimumPaymentRate,
            PaymentStrategy = (CreditCardPaymentStrategy)row.PaymentStrategy,
            FixedPaymentAmount = row.FixedPaymentAmount,
            ProjectionFallbackStrategy =
                (ProjectionFallbackStrategy)row.ProjectionFallbackStrategy,
            ProjectionFallbackFixedAmount =
                row.ProjectionFallbackFixedAmount,
            KnownNextStatementDate =
                ParseNullableDate(row.KnownNextStatementDate),
            KnownNextDueDate = ParseNullableDate(row.KnownNextDueDate),
            CurrentStatement = currentStatement is null
                ? null
                : FromRow(currentStatement),
            CurrentStatementPaymentPlan = currentStatement is null
                ? null
                : FromPaymentPlan(currentStatement),
            Charges = charges
                .Select(FromRow)
                .OrderBy(x => x.PostingDate)
                .ToArray(),
            PaymentPlans = paymentPlans
                .Select(FromRow)
                .OrderBy(x => x.DueDate)
                .ToArray(),
            PaymentPreferences = paymentPreferences
                .Select(FromRow)
                .OrderBy(x => x.EffectiveFromStatementDate)
                .ThenBy(x => x.CreatedAt)
                .ToArray()
        };
    }

    internal static CardInstallmentRow ToRow(CardCharge value) => new()
    {
        Id = Key(value.Id),
        CreditCardId = Key(value.CreditCardId),
        Description = value.Description,
        DueDate = FormatDate(value.PostingDate),
        Amount = value.Amount
    };

    private static CardCharge FromRow(CardInstallmentRow row) => new()
    {
        Id = ParseKey(row.Id),
        CreditCardId = ParseKey(row.CreditCardId),
        Description = row.Description,
        PostingDate = ParseDate(row.DueDate),
        Amount = row.Amount
    };

    private static CreditCardPaymentPlanRow ToRow(
        CreditCardPaymentPlan value) => new()
        {
            Id = Key(value.Id),
            CreditCardId = Key(value.CreditCardId),
            DueDate = FormatDate(value.DueDate),
            PlannedPaymentAmount = value.Amount ?? 0m,
            PaymentType = (int)value.PaymentType,
            Amount = value.Amount
        };

    private static CreditCardPaymentPlan FromRow(
        CreditCardPaymentPlanRow row) => new()
        {
            Id = ParseKey(row.Id),
            CreditCardId = ParseKey(row.CreditCardId),
            DueDate = ParseDate(row.DueDate),
            PaymentType = (CreditCardPaymentType)row.PaymentType,
            Amount = row.Amount ??
                 (row.PlannedPaymentAmount > 0m
                     ? row.PlannedPaymentAmount
                     : null)
        };

    internal static CreditCardStatementRow ToRow(
        CreditCardStatement value,
        CurrentStatementPaymentPlan? paymentPlan) => new()
        {
            Id = Key(value.Id),
            CreditCardId = Key(value.CreditCardId),
            StatementDate = FormatDate(value.StatementDate),
            DueDate = FormatDate(value.DueDate),
            StatementAmount = value.StatementAmount,
            MinimumPaymentAmount = value.MinimumPaymentAmount,
            NextStatementDate = FormatNullableDate(value.NextStatementDate),
            NextDueDate = FormatNullableDate(value.NextDueDate),
            Source = (int)value.Source,
            SourceDocumentFingerprint = value.SourceDocumentFingerprint,
            ImportedAt = value.ImportedAt is null
                ? null
                : FormatInstant(value.ImportedAt.Value),
            CreatedAt = FormatInstant(value.CreatedAt),
            UpdatedAt = FormatInstant(value.UpdatedAt),
            CurrentPaymentMode = (int)(paymentPlan?.Mode ??
                                       CurrentStatementPaymentMode.Minimum),
            CurrentPaymentCustomAmount = paymentPlan?.Mode ==
                                         CurrentStatementPaymentMode.Custom
                ? paymentPlan.CustomAmount
                : null
        };

    private static CreditCardStatement FromRow(
        CreditCardStatementRow row) => new()
        {
            Id = ParseKey(row.Id),
            CreditCardId = ParseKey(row.CreditCardId),
            StatementDate = ParseDate(row.StatementDate),
            DueDate = ParseDate(row.DueDate),
            StatementAmount = row.StatementAmount,
            MinimumPaymentAmount = row.MinimumPaymentAmount,
            NextStatementDate = ParseNullableDate(row.NextStatementDate),
            NextDueDate = ParseNullableDate(row.NextDueDate),
            Source = Enum.IsDefined(
                typeof(CreditCardStatementSource),
                row.Source)
                ? (CreditCardStatementSource)row.Source
                : CreditCardStatementSource.Manual,
            SourceDocumentFingerprint = row.SourceDocumentFingerprint,
            ImportedAt = string.IsNullOrWhiteSpace(row.ImportedAt)
                ? null
                : ParseInstant(row.ImportedAt),
            CreatedAt = string.IsNullOrWhiteSpace(row.CreatedAt)
                ? DateTimeOffset.UnixEpoch
                : ParseInstant(row.CreatedAt),
            UpdatedAt = string.IsNullOrWhiteSpace(row.UpdatedAt)
                ? DateTimeOffset.UnixEpoch
                : ParseInstant(row.UpdatedAt)
        };

    private static CurrentStatementPaymentPlan FromPaymentPlan(
        CreditCardStatementRow row)
    {
        var mode = Enum.IsDefined(
            typeof(CurrentStatementPaymentMode),
            row.CurrentPaymentMode)
            ? (CurrentStatementPaymentMode)row.CurrentPaymentMode
            : CurrentStatementPaymentMode.Minimum;
        return new CurrentStatementPaymentPlan
        {
            Mode = mode,
            CustomAmount = mode == CurrentStatementPaymentMode.Custom
                ? row.CurrentPaymentCustomAmount
                : null
        };
    }
}
