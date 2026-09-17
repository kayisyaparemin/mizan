using System.Globalization;
using Mizan.Application.Abstractions;
using Mizan.Application.Models;
using Mizan.Domain.Calculations;
using Mizan.Domain.Models;
using SQLite;

namespace Mizan.Infrastructure.Persistence;

public sealed partial class SqliteMizanStore
{
    public async Task<UserSettings> GetSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var row = await _database.Table<SettingsRow>().FirstAsync();
        return new UserSettings
        {
            IncomeDay = row.IncomeDay,
            MonthlyVariableExpenseAllowance = row.MonthlyVariableExpenseAllowance,
            ProjectionOpeningBalance = row.ProjectionOpeningBalance,
            CreditCardCarryInterestRate =
                row.CreditCardCarryInterestRate,
            DeficitFinancingInterestRate =
                row.DeficitFinancingInterestRate,
            ProjectionAnchorDate = string.IsNullOrWhiteSpace(
                row.ProjectionAnchorDate)
                ? default
                : ParseDate(row.ProjectionAnchorDate)
        };
    }

    public async Task SaveSettingsAsync(
        UserSettings settings,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var row = await _database.Table<SettingsRow>().FirstAsync();
        row.IncomeDay = settings.IncomeDay;
        row.MonthlyVariableExpenseAllowance = settings.MonthlyVariableExpenseAllowance;
        row.ProjectionOpeningBalance =
            settings.ProjectionOpeningBalance;
        row.CreditCardCarryInterestRate =
            settings.CreditCardCarryInterestRate;
        row.DeficitFinancingInterestRate =
            settings.DeficitFinancingInterestRate;
        row.ProjectionAnchorDate = settings.ProjectionAnchorDate == default
            ? null
            : FormatDate(settings.ProjectionAnchorDate);
        await _database.UpdateAsync(row);
    }

    public async Task<PaymentReminderMode> GetPaymentReminderModeAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var row = await _database.Table<SettingsRow>().FirstAsync();
        return Enum.IsDefined(typeof(PaymentReminderMode), row.PaymentReminderMode)
            ? (PaymentReminderMode)row.PaymentReminderMode
            : PaymentReminderMode.Off;
    }

    public async Task SavePaymentReminderModeAsync(
        PaymentReminderMode mode,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        // Yalnız bu sütun yazılır: finans ayarlarını taşıyan satırın geri
        // kalanına dokunulmaz.
        await _database.ExecuteAsync(
            "UPDATE settings SET PaymentReminderMode = ?",
            (int)mode);
    }

    public async Task<IReadOnlyList<CashFlowAllocationStrategy>>
        GetPaymentAssignmentStrategiesAsync(
            CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        return (await _database
                .Table<CashFlowAllocationStrategyRow>()
                .OrderBy(x => x.EffectiveFromPeriodDate)
                .ToListAsync())
            .Select(FromRow)
            .ToArray();
    }

    public async Task UpsertCashFlowAllocationStrategyAsync(
        CashFlowAllocationStrategy strategy,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.InsertOrReplaceAsync(ToRow(strategy));
    }

    public async Task DeleteCashFlowAllocationStrategyAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.DeleteAsync<CashFlowAllocationStrategyRow>(
            Key(id));
    }

    private static void UpdateSettings(SQLiteConnection connection, UserSettings settings)
    {
        var row = connection.Table<SettingsRow>().First();
        row.IncomeDay = settings.IncomeDay;
        row.MonthlyVariableExpenseAllowance = settings.MonthlyVariableExpenseAllowance;
        row.ProjectionOpeningBalance = settings.ProjectionOpeningBalance;
        row.ProjectionAnchorDate = settings.ProjectionAnchorDate == default
            ? null
            : FormatDate(settings.ProjectionAnchorDate);
        row.CreditCardCarryInterestRate = settings.CreditCardCarryInterestRate;
        row.DeficitFinancingInterestRate = settings.DeficitFinancingInterestRate;
        row.SchemaVersion = CurrentSchemaVersion;
        connection.Update(row);
    }

    private SettingsRow DefaultSettingsRow() => new()
    {
        IncomeDay = 10,
        MonthlyVariableExpenseAllowance = 0m,
        ProjectionOpeningBalance = 0m,
        ProjectionAnchorDate = null,
        CreditCardCarryInterestRate = DefaultPlanningInterestRate,
        DeficitFinancingInterestRate = DefaultPlanningInterestRate,
        CashFlowAllocationMode =
            (int)CashFlowAllocationMode.UpcomingPeriod,
        SchemaVersion = CurrentSchemaVersion,
        DevelopmentSeedVersion = 0,
        DevelopmentSeedEnabled = _developmentFeaturesEnabled,
        LegacyRemovedFeatureFlag = false,
        TrackingStartedDate = null
    };

    private async Task EnsureInitialCashFlowAllocationStrategyAsync(
        SettingsRow settings)
    {
        if (await _database
                .Table<CashFlowAllocationStrategyRow>()
                .CountAsync() > 0)
        {
            return;
        }

        var anchor = ParseDate(
            settings.ProjectionAnchorDate ?? FormatDate(_migrationDate));
        var firstPeriodStart = new CashFlowPeriodCalculator()
            .GetFirstPeriodStartOnOrAfter(anchor, settings.IncomeDay);
        var legacyMode = Enum.IsDefined(
            typeof(CashFlowAllocationMode),
            settings.CashFlowAllocationMode)
            ? (CashFlowAllocationMode)settings.CashFlowAllocationMode
            : CashFlowAllocationMode.UpcomingPeriod;
        await _database.InsertAsync(ToRow(
            new CashFlowAllocationStrategy
            {
                Id = LegacyInitialAssignmentStrategyId,
                Mode = legacyMode,
                EffectiveFromPeriodDate = firstPeriodStart,
                CreatedAt = new DateTimeOffset(
                    _migrationDate.ToDateTime(TimeOnly.MinValue),
                    TimeSpan.Zero),
                Note = "İlk gelir kullanım düzeni"
            }));
    }

    private static CashFlowAllocationStrategyRow ToRow(
        CashFlowAllocationStrategy value) => new()
        {
            Id = Key(value.Id),
            Mode = (int)value.Mode,
            EffectiveFromPeriodDate =
            FormatDate(value.EffectiveFromPeriodDate),
            CreatedAt = value.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
            Note = value.Note
        };

    private static CashFlowAllocationStrategy FromRow(
        CashFlowAllocationStrategyRow row) => new()
        {
            Id = ParseKey(row.Id),
            Mode = (CashFlowAllocationMode)row.Mode,
            EffectiveFromPeriodDate =
            ParseDate(row.EffectiveFromPeriodDate),
            CreatedAt = DateTimeOffset.Parse(
            row.CreatedAt,
            CultureInfo.InvariantCulture),
            Note = row.Note
        };
}
