using CoinFlow.Application.Models;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Abstractions;

public interface IFinancialSnapshotRepository
{
    Task ApplyOnboardingSetupAsync(
        OnboardingPersistenceBatch batch,
        CancellationToken cancellationToken = default);

    Task<FinancialHistoryData> GetFinancialHistoryAsync(
        CancellationToken cancellationToken = default);

    Task SaveCurrentFinancialSnapshotAsync(
        FinancialSnapshot snapshot,
        PeriodPlanSnapshot plan,
        UserSettings? updatedSettings = null,
        CancellationToken cancellationToken = default);

    Task ReplacePendingFinancialSnapshotPlanAsync(
        FinancialSnapshot snapshot,
        PeriodPlanSnapshot plan,
        CancellationToken cancellationToken = default);

    Task SavePeriodPlanRevisionAsync(
        PeriodPlanRevision revision,
        CancellationToken cancellationToken = default);

    Task FinalizeFinancialReviewAsync(
        FinancialReviewCommit commit,
        CancellationToken cancellationToken = default);
}
