namespace Mizan.Application.Abstractions;

public interface IMizanStore :
    ISettingsRepository,
    ISalaryRepository,
    IIncomeRepository,
    ILoanRepository,
    IPaymentPlanRepository,
    ICreditCardRepository,
    IExpenseRepository,
    IObservationRepository,
    ISimulationRepository,
    IFinancialSnapshotRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task ClearAllFinancialDataAsync(
        CancellationToken cancellationToken = default);
    Task LoadCanonicalDevelopmentDataAsync(
        CancellationToken cancellationToken = default);
}
