using CoinFlow.Application.Abstractions;
using CoinFlow.Application.Models;
using CoinFlow.Domain.Models;

namespace CoinFlow.Infrastructure.Persistence;

/// <summary>
/// Uygulamanın gördüğü tek <see cref="ICoinFlowStore"/>. Her çağrıyı o an
/// açık profilin veritabanına iletir.
/// </summary>
/// <remarks>
/// Servisler singleton olduğu için store'u bir kez alıp saklıyorlar; profil
/// değişince onları yeniden kurmak yerine altlarındaki veritabanı değişir.
/// Hiçbir profil açık değilken yapılan çağrı hata verir — kapatılmış bir
/// ekrandan gelen geç bir çağrı sessizce başka profile yazamaz.
/// </remarks>
public sealed class ProfileScopedCoinFlowStore(
    Func<Guid, ICoinFlowStore> storeFactory) :
    ICoinFlowStore,
    IProfileStoreSwitch,
    IAsyncDisposable
{
    private ICoinFlowStore? _current;

    public async Task OpenAsync(
        Guid profileId,
        CancellationToken cancellationToken = default)
    {
        await CloseAsync();
        var store = storeFactory(profileId);
        try
        {
            await store.InitializeAsync(cancellationToken);
        }
        catch
        {
            await DisposeStoreAsync(store);
            throw;
        }

        _current = store;
    }

    public Task CloseAsync() =>
        DisposeStoreAsync(Interlocked.Exchange(ref _current, null));

    public async ValueTask DisposeAsync() => await CloseAsync();

    private ICoinFlowStore Current =>
        _current ?? throw new InvalidOperationException(
            "Açık bir profil yok. Devam etmek için bir profil seç.");

    private static async Task DisposeStoreAsync(ICoinFlowStore? store)
    {
        if (store is IAsyncDisposable disposable)
        {
            await disposable.DisposeAsync();
        }
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        Current.InitializeAsync(cancellationToken);

    public Task ClearAllFinancialDataAsync(
        CancellationToken cancellationToken = default) =>
        Current.ClearAllFinancialDataAsync(cancellationToken);

    public Task LoadCanonicalDevelopmentDataAsync(
        CancellationToken cancellationToken = default) =>
        Current.LoadCanonicalDevelopmentDataAsync(cancellationToken);

    public Task<UserSettings> GetSettingsAsync(
        CancellationToken cancellationToken = default) =>
        Current.GetSettingsAsync(cancellationToken);

    public Task SaveSettingsAsync(
        UserSettings settings,
        CancellationToken cancellationToken = default) =>
        Current.SaveSettingsAsync(settings, cancellationToken);

    public Task<IReadOnlyList<PaymentAssignmentStrategy>>
        GetPaymentAssignmentStrategiesAsync(
            CancellationToken cancellationToken = default) =>
        Current.GetPaymentAssignmentStrategiesAsync(cancellationToken);

    public Task UpsertPaymentAssignmentStrategyAsync(
        PaymentAssignmentStrategy strategy,
        CancellationToken cancellationToken = default) =>
        Current.UpsertPaymentAssignmentStrategyAsync(strategy, cancellationToken);

    public Task DeletePaymentAssignmentStrategyAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        Current.DeletePaymentAssignmentStrategyAsync(id, cancellationToken);

    public Task<IReadOnlyList<SalaryScheduleEntry>> GetSalaryScheduleAsync(
        CancellationToken cancellationToken = default) =>
        Current.GetSalaryScheduleAsync(cancellationToken);

    public Task UpsertSalaryAsync(
        SalaryScheduleEntry entry,
        CancellationToken cancellationToken = default) =>
        Current.UpsertSalaryAsync(entry, cancellationToken);

    public Task DeleteSalaryAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        Current.DeleteSalaryAsync(id, cancellationToken);

    public Task<IReadOnlyList<OneTimeIncome>> GetOtherIncomesAsync(
        CancellationToken cancellationToken = default) =>
        Current.GetOtherIncomesAsync(cancellationToken);

    public Task UpsertOtherIncomeAsync(
        OneTimeIncome income,
        CancellationToken cancellationToken = default) =>
        Current.UpsertOtherIncomeAsync(income, cancellationToken);

    public Task DeleteOtherIncomeAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        Current.DeleteOtherIncomeAsync(id, cancellationToken);

    public Task<IReadOnlyList<Loan>> GetLoansAsync(
        CancellationToken cancellationToken = default) =>
        Current.GetLoansAsync(cancellationToken);

    public Task UpsertLoanAsync(
        Loan loan,
        CancellationToken cancellationToken = default) =>
        Current.UpsertLoanAsync(loan, cancellationToken);

    public Task DeleteLoanAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        Current.DeleteLoanAsync(id, cancellationToken);

    public Task<IReadOnlyList<LoanPrepayment>> GetLoanPrepaymentsAsync(
        CancellationToken cancellationToken = default) =>
        Current.GetLoanPrepaymentsAsync(cancellationToken);

    public Task DeleteLoanPrepaymentAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        Current.DeleteLoanPrepaymentAsync(id, cancellationToken);

    public Task<IReadOnlyList<TemporaryPaymentPlan>> GetPaymentPlansAsync(
        CancellationToken cancellationToken = default) =>
        Current.GetPaymentPlansAsync(cancellationToken);

    public Task UpsertPaymentPlanAsync(
        TemporaryPaymentPlan plan,
        CancellationToken cancellationToken = default) =>
        Current.UpsertPaymentPlanAsync(plan, cancellationToken);

    public Task DeletePaymentPlanAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        Current.DeletePaymentPlanAsync(id, cancellationToken);

    public Task<IReadOnlyList<CreditCard>> GetCreditCardsAsync(
        CancellationToken cancellationToken = default) =>
        Current.GetCreditCardsAsync(cancellationToken);

    public Task UpsertCreditCardAsync(
        CreditCard card,
        CancellationToken cancellationToken = default) =>
        Current.UpsertCreditCardAsync(card, cancellationToken);

    public Task DeleteCreditCardAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        Current.DeleteCreditCardAsync(id, cancellationToken);

    public Task<IReadOnlyList<PlannedLargeExpense>> GetPlannedLargeExpensesAsync(
        CancellationToken cancellationToken = default) =>
        Current.GetPlannedLargeExpensesAsync(cancellationToken);

    public Task UpsertPlannedLargeExpenseAsync(
        PlannedLargeExpense expense,
        CancellationToken cancellationToken = default) =>
        Current.UpsertPlannedLargeExpenseAsync(expense, cancellationToken);

    public Task DeletePlannedLargeExpenseAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        Current.DeletePlannedLargeExpenseAsync(id, cancellationToken);

    public Task<PeriodObservation?> GetPeriodObservationAsync(
        Guid periodPlanSnapshotId,
        CancellationToken cancellationToken = default) =>
        Current.GetPeriodObservationAsync(periodPlanSnapshotId, cancellationToken);

    public Task UpsertPeriodObservationAsync(
        PeriodObservation observation,
        CancellationToken cancellationToken = default) =>
        Current.UpsertPeriodObservationAsync(observation, cancellationToken);

    public Task DeletePeriodObservationAsync(
        Guid periodPlanSnapshotId,
        CancellationToken cancellationToken = default) =>
        Current.DeletePeriodObservationAsync(periodPlanSnapshotId, cancellationToken);

    public Task<IReadOnlyList<SimulationDraft>> GetSimulationDraftsAsync(
        CancellationToken cancellationToken = default) =>
        Current.GetSimulationDraftsAsync(cancellationToken);

    public Task UpsertSimulationDraftAsync(
        SimulationDraft draft,
        CancellationToken cancellationToken = default) =>
        Current.UpsertSimulationDraftAsync(draft, cancellationToken);

    public Task DeleteSimulationDraftAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        Current.DeleteSimulationDraftAsync(id, cancellationToken);

    public Task ApplySimulationBatchAsync(
        SimulationPersistenceBatch batch,
        CancellationToken cancellationToken = default) =>
        Current.ApplySimulationBatchAsync(batch, cancellationToken);

    public Task ApplyOnboardingSetupAsync(
        OnboardingPersistenceBatch batch,
        CancellationToken cancellationToken = default) =>
        Current.ApplyOnboardingSetupAsync(batch, cancellationToken);

    public Task<FinancialHistoryData> GetFinancialHistoryAsync(
        CancellationToken cancellationToken = default) =>
        Current.GetFinancialHistoryAsync(cancellationToken);

    public Task SaveCurrentFinancialSnapshotAsync(
        FinancialSnapshot snapshot,
        PeriodPlanSnapshot plan,
        UserSettings? updatedSettings = null,
        CancellationToken cancellationToken = default) =>
        Current.SaveCurrentFinancialSnapshotAsync(
            snapshot,
            plan,
            updatedSettings,
            cancellationToken);

    public Task ReplacePendingFinancialSnapshotPlanAsync(
        FinancialSnapshot snapshot,
        PeriodPlanSnapshot plan,
        CancellationToken cancellationToken = default) =>
        Current.ReplacePendingFinancialSnapshotPlanAsync(
            snapshot,
            plan,
            cancellationToken);

    public Task SavePeriodPlanRevisionAsync(
        PeriodPlanRevision revision,
        CancellationToken cancellationToken = default) =>
        Current.SavePeriodPlanRevisionAsync(revision, cancellationToken);

    public Task FinalizeFinancialReviewAsync(
        FinancialReviewCommit commit,
        CancellationToken cancellationToken = default) =>
        Current.FinalizeFinancialReviewAsync(commit, cancellationToken);
}
