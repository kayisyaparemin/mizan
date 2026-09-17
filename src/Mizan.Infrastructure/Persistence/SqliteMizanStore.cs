using System.Globalization;
using Mizan.Application.Abstractions;
using Mizan.Application.Models;
using Mizan.Domain.Calculations;
using Mizan.Domain.Models;
using SQLite;

namespace Mizan.Infrastructure.Persistence;

public sealed partial class SqliteMizanStore : IMizanStore, IAsyncDisposable
{
    private const string DateFormat = "yyyy-MM-dd";
    // v12: kaydedilmiş simülasyon taslakları.
    // v13: açık dönemin gözlem defteri (I14/I15). Her ikisi de yalnız yeni
    // tablo ekler; mevcut tabloların hiçbirine dokunmaz, veri taşınmaz.
    // v17: hatırlatıcı defteri (payment_reminder_responses); yalnız yeni tablo.
    public const int CurrentSchemaVersion = 17;
    private const int CurrentCardStatementModelVersion = 7;
    private const decimal DefaultPlanningInterestRate = 0.05m;
    private static readonly Guid LegacyInitialAssignmentStrategyId =
        Guid.Parse("50000000-0000-0000-0000-000000000001");
    private readonly SQLiteAsyncConnection _database;
    private readonly bool _developmentFeaturesEnabled;
    private readonly DateOnly _migrationDate;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private bool _initialized;

    public SqliteMizanStore(
        string databasePath,
        bool developmentFeaturesEnabled,
        DateOnly migrationDate)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException(
                "Veritabanı yolu gereklidir.",
                nameof(databasePath));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(databasePath) ?? ".");
        SQLitePCL.Batteries_V2.Init();
        _database = new SQLiteAsyncConnection(
            databasePath,
            SQLiteOpenFlags.ReadWrite |
            SQLiteOpenFlags.Create |
            SQLiteOpenFlags.SharedCache);
        _developmentFeaturesEnabled = developmentFeaturesEnabled;
        _migrationDate = migrationDate;
    }

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _initializeLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            await _database.CreateTableAsync<SalaryRow>();
            await _database.CreateTableAsync<OtherIncomeRow>();
            await _database.CreateTableAsync<LoanRow>();
            await _database.CreateTableAsync<LoanPrepaymentRow>();
            await _database.CreateTableAsync<PaymentPlanRow>();
            await _database.CreateTableAsync<PaymentInstallmentRow>();
            await _database.CreateTableAsync<CreditCardRow>();
            await _database.CreateTableAsync<CardInstallmentRow>();
            await _database.CreateTableAsync<CreditCardPaymentPlanRow>();
            await _database.CreateTableAsync<CreditCardPaymentPreferenceRow>();
            await _database.CreateTableAsync<CreditCardStatementRow>();
            await _database.CreateTableAsync<PlannedLargeExpenseRow>();
            await _database.CreateTableAsync<SettingsRow>();
            await _database.CreateTableAsync<CashFlowAllocationStrategyRow>();
            await _database.CreateTableAsync<FinancialSnapshotRow>();
            await _database.CreateTableAsync<PeriodPlanSnapshotRow>();
            await _database.CreateTableAsync<PeriodPlanPaymentLineRow>();
            await _database.CreateTableAsync<PeriodPlanRevisionRow>();
            await _database.CreateTableAsync<PeriodPlanRevisionPaymentLineRow>();
            await _database.CreateTableAsync<PeriodActualRow>();
            await _database.CreateTableAsync<ActualPaymentRow>();
            await _database.CreateTableAsync<ActualFlowRow>();
            await _database.CreateTableAsync<ActualLivingBreakdownRow>();
            await _database.CreateTableAsync<SimulationDraftRow>();
            await _database.CreateTableAsync<SimulationDraftConditionRow>();
            await _database.CreateTableAsync<PeriodObservationRow>();
            await _database.CreateTableAsync<PeriodObservationPaymentRow>();
            await _database.CreateTableAsync<PeriodObservationFlowRow>();
            await _database.CreateTableAsync<PaymentReminderResponseRow>();

            await MigratePeriodPlanRevisionSchemaAsync();
            await MigrateLegacyCreditCardsAsync();
            await RemoveObsoleteDailyTrackingTablesAsync();

            var settings = await _database
                .Table<SettingsRow>()
                .FirstOrDefaultAsync();
            var isNewSettings = settings is null;
            if (settings is null)
            {
                settings = DefaultSettingsRow();
                await _database.InsertAsync(settings);
            }

            var needsStrategyMigration = !isNewSettings &&
                                         settings.SchemaVersion <
                                         CurrentSchemaVersion;
            if (needsStrategyMigration &&
                string.IsNullOrWhiteSpace(settings.ProjectionAnchorDate))
            {
                settings.ProjectionAnchorDate = FormatDate(_migrationDate);
            }

            if (needsStrategyMigration)
            {
                await EnsureInitialCashFlowAllocationStrategyAsync(settings);
            }

            if (!isNewSettings && settings.SchemaVersion < 7)
            {
                settings.CreditCardCarryInterestRate =
                    DefaultPlanningInterestRate;
                settings.DeficitFinancingInterestRate =
                    DefaultPlanningInterestRate;
            }

            settings.SchemaVersion = CurrentSchemaVersion;
            settings.DevelopmentSeedEnabled = _developmentFeaturesEnabled;
            settings.LegacyRemovedFeatureFlag = false;
            settings.TrackingStartedDate = null;
            await _database.UpdateAsync(settings);
            _initialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    public async Task ClearAllFinancialDataAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        await _database.RunInTransactionAsync(connection =>
        {
            connection.DeleteAll<ActualLivingBreakdownRow>();
            connection.DeleteAll<ActualFlowRow>();
            connection.DeleteAll<ActualPaymentRow>();
            connection.DeleteAll<PeriodActualRow>();
            connection.DeleteAll<PeriodPlanRevisionPaymentLineRow>();
            connection.DeleteAll<PeriodPlanRevisionRow>();
            connection.DeleteAll<PeriodPlanPaymentLineRow>();
            connection.DeleteAll<PeriodPlanSnapshotRow>();
            connection.DeleteAll<FinancialSnapshotRow>();
            connection.DeleteAll<PaymentInstallmentRow>();
            connection.DeleteAll<PaymentPlanRow>();
            connection.DeleteAll<CardInstallmentRow>();
            connection.DeleteAll<CreditCardPaymentPlanRow>();
            connection.DeleteAll<CreditCardPaymentPreferenceRow>();
            connection.DeleteAll<CreditCardStatementRow>();
            connection.DeleteAll<CreditCardRow>();
            connection.DeleteAll<PlannedLargeExpenseRow>();
            connection.DeleteAll<OtherIncomeRow>();
            connection.DeleteAll<SalaryRow>();
            connection.DeleteAll<LoanPrepaymentRow>();
            connection.DeleteAll<LoanRow>();
            connection.DeleteAll<CashFlowAllocationStrategyRow>();
            connection.DeleteAll<SimulationDraftConditionRow>();
            connection.DeleteAll<SimulationDraftRow>();
            connection.DeleteAll<PeriodObservationPaymentRow>();
            connection.DeleteAll<PeriodObservationFlowRow>();
            connection.DeleteAll<PeriodObservationRow>();
            connection.DeleteAll<PaymentReminderResponseRow>();
            var settings = connection.Table<SettingsRow>().First();
            settings.IncomeDay = 10;
            settings.MonthlyVariableExpenseAllowance = 0m;
            settings.ProjectionOpeningBalance = 0m;
            settings.ProjectionAnchorDate = null;
            settings.CreditCardCarryInterestRate =
                DefaultPlanningInterestRate;
            settings.DeficitFinancingInterestRate =
                DefaultPlanningInterestRate;
            settings.CashFlowAllocationMode =
                (int)CashFlowAllocationMode.UpcomingPeriod;
            settings.DevelopmentSeedVersion = 0;
            settings.PaymentReminderMode = (int)PaymentReminderMode.Off;
            settings.SchemaVersion = CurrentSchemaVersion;
            connection.Update(settings);
        });
    }

    public async Task LoadCanonicalDevelopmentDataAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        if (!_developmentFeaturesEnabled)
        {
            throw new InvalidOperationException(
                "Test verisi yalnızca geliştirme sürümünde yüklenebilir.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        await DevelopmentDataSeeder.SeedAsync(_database);
        var settings = await _database.Table<SettingsRow>().FirstAsync();
        settings.IncomeDay = 10;
        settings.MonthlyVariableExpenseAllowance = 30_000m;
        settings.ProjectionOpeningBalance = 0m;
        settings.ProjectionAnchorDate = FormatDate(
            new DateOnly(2026, 8, 20));
        settings.CreditCardCarryInterestRate =
            DefaultPlanningInterestRate;
        settings.DeficitFinancingInterestRate =
            DefaultPlanningInterestRate;
        settings.CashFlowAllocationMode =
            (int)CashFlowAllocationMode.UpcomingPeriod;
        settings.DevelopmentSeedVersion =
            DevelopmentDataSeeder.CurrentSeedVersion;
        await _database.UpdateAsync(settings);
    }

    public async ValueTask DisposeAsync()
    {
        if (_initialized)
        {
            await _database.CloseAsync();
            _initialized = false;
        }

        _initializeLock.Dispose();
    }

    internal static string FormatDate(DateOnly date) =>
        date.ToString(DateFormat, CultureInfo.InvariantCulture);

    internal static DateOnly ParseDate(string value) =>
        DateOnly.ParseExact(value, DateFormat, CultureInfo.InvariantCulture);

    private static DateOnly? ParseNullableDate(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : ParseDate(value);

    private static string? FormatNullableDate(DateOnly? value) =>
        value is null ? null : FormatDate(value.Value);

    private static string Key(Guid id) => id.ToString("D");
    private static Guid ParseKey(string value) => Guid.Parse(value);
    private static string FormatInstant(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset ParseInstant(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static string Timestamp(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;

    private async Task EnsureColumnAsync(
        string table,
        string column,
        string definition)
    {
        var columns = await _database.QueryAsync<TableInfoRow>(
            $"PRAGMA table_info({table})");
        if (columns.Any(x => string.Equals(
                x.Name,
                column,
                StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        await _database.ExecuteAsync(
            $"ALTER TABLE {table} ADD COLUMN {definition}");
    }

    private sealed class TableInfoRow
    {
        [Column("name")]
        public string Name { get; set; } = string.Empty;
    }
}
