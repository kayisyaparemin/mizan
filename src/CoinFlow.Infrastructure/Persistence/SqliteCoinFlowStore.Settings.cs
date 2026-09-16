using System.Globalization;
using CoinFlow.Application.Abstractions;
using CoinFlow.Application.Models;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;
using SQLite;

namespace CoinFlow.Infrastructure.Persistence;

public sealed partial class SqliteCoinFlowStore
{
    public async Task<UserSettings> GetSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var row = await _database.Table<SettingsRow>().FirstAsync();
        return new UserSettings
        {
            SalaryDay = row.SalaryDay,
            MonthlyLivingBudget = row.MonthlyLivingBudget,
            ProjectionStartingSavings = row.ProjectionStartingSavings,
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
        row.SalaryDay = settings.SalaryDay;
        row.MonthlyLivingBudget = settings.MonthlyLivingBudget;
        row.ProjectionStartingSavings =
            settings.ProjectionStartingSavings;
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

    public async Task<IReadOnlyList<PaymentAssignmentStrategy>>
        GetPaymentAssignmentStrategiesAsync(
            CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        return (await _database
                .Table<PaymentAssignmentStrategyRow>()
                .OrderBy(x => x.EffectiveFromSalaryDate)
                .ToListAsync())
            .Select(FromRow)
            .ToArray();
    }

    public async Task UpsertPaymentAssignmentStrategyAsync(
        PaymentAssignmentStrategy strategy,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.InsertOrReplaceAsync(ToRow(strategy));
    }

    public async Task DeletePaymentAssignmentStrategyAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.DeleteAsync<PaymentAssignmentStrategyRow>(
            Key(id));
    }

    private static void UpdateSettings(SQLiteConnection connection, UserSettings settings)
    {
        var row = connection.Table<SettingsRow>().First();
        row.SalaryDay = settings.SalaryDay;
        row.MonthlyLivingBudget = settings.MonthlyLivingBudget;
        row.ProjectionStartingSavings = settings.ProjectionStartingSavings;
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
        SalaryDay = 10,
        MonthlyLivingBudget = 0m,
        ProjectionStartingSavings = 0m,
        ProjectionAnchorDate = null,
        CreditCardCarryInterestRate = DefaultPlanningInterestRate,
        DeficitFinancingInterestRate = DefaultPlanningInterestRate,
        PaymentAssignmentMode =
            (int)PaymentAssignmentMode.UpcomingPeriod,
        SchemaVersion = CurrentSchemaVersion,
        DevelopmentSeedVersion = 0,
        DevelopmentSeedEnabled = _developmentFeaturesEnabled,
        LegacyRemovedFeatureFlag = false,
        TrackingStartedDate = null
    };

    private async Task EnsureInitialPaymentAssignmentStrategyAsync(
        SettingsRow settings)
    {
        if (await _database
                .Table<PaymentAssignmentStrategyRow>()
                .CountAsync() > 0)
        {
            return;
        }

        var anchor = ParseDate(
            settings.ProjectionAnchorDate ?? FormatDate(_migrationDate));
        var firstSalary = new SalaryPeriodCalculator()
            .GetFirstSalaryOnOrAfter(anchor, settings.SalaryDay);
        var legacyMode = Enum.IsDefined(
            typeof(PaymentAssignmentMode),
            settings.PaymentAssignmentMode)
            ? (PaymentAssignmentMode)settings.PaymentAssignmentMode
            : PaymentAssignmentMode.UpcomingPeriod;
        await _database.InsertAsync(ToRow(
            new PaymentAssignmentStrategy
            {
                Id = LegacyInitialAssignmentStrategyId,
                Mode = legacyMode,
                EffectiveFromSalaryDate = firstSalary,
                CreatedAt = new DateTimeOffset(
                    _migrationDate.ToDateTime(TimeOnly.MinValue),
                    TimeSpan.Zero),
                Note = "İlk gelir kullanım düzeni"
            }));
    }

    private static PaymentAssignmentStrategyRow ToRow(
        PaymentAssignmentStrategy value) => new()
        {
            Id = Key(value.Id),
            Mode = (int)value.Mode,
            EffectiveFromSalaryDate =
            FormatDate(value.EffectiveFromSalaryDate),
            CreatedAt = value.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
            Note = value.Note
        };

    private static PaymentAssignmentStrategy FromRow(
        PaymentAssignmentStrategyRow row) => new()
        {
            Id = ParseKey(row.Id),
            Mode = (PaymentAssignmentMode)row.Mode,
            EffectiveFromSalaryDate =
            ParseDate(row.EffectiveFromSalaryDate),
            CreatedAt = DateTimeOffset.Parse(
            row.CreatedAt,
            CultureInfo.InvariantCulture),
            Note = row.Note
        };
}
