using CoinFlow.Application.Models;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Services;

internal static class SimulationPersistenceBatchBuilder
{
    public static void ValidateSimulationApplyConflicts(
        FinancialPlan current,
        IReadOnlyList<SimulationRequest> requests)
    {
        var conflictingSalary = requests.FirstOrDefault(request =>
            request.Type == SimulationScenarioType.SalaryChange &&
            current.Salaries.Any(x => x.EffectiveDate == request.StartDate));
        if (conflictingSalary is not null)
        {
            throw new InvalidOperationException(
                "Bu tarihte zaten bir gelir kaydı var. Geçmişi korumak için farklı bir geçerlilik tarihi seçin.");
        }

        var conflictingStrategy = requests.FirstOrDefault(request =>
            request.Type == SimulationScenarioType.PaymentStrategyChange &&
            current.PaymentAssignmentStrategies.Any(x =>
                x.EffectiveFromSalaryDate ==
                (request.EffectiveSalaryDate ?? request.StartDate)));
        if (conflictingStrategy is not null)
        {
            throw new InvalidOperationException(
                "Bu dönem tarihinde zaten bir kullanım düzeni var. Önceki kayıt değiştirilemez.");
        }
    }

    public static SimulationPersistenceBatch Build(
        FinancialPlan scenario,
        IReadOnlyList<SimulationRequest> requests)
    {
        var requestIds = requests.Select(x => x.ScenarioId).ToHashSet();
        var cardIds = requests
            .Where(x => x.Type is
                SimulationScenarioType.CreditCardSinglePayment or
                SimulationScenarioType.CreditCardInstallmentPurchase or
                SimulationScenarioType.CreditCardPaymentMode)
            .Select(x => x.CreditCardId ?? throw new InvalidOperationException(
                "Kart koşulunda kredi kartı bulunamadı."))
            .Distinct()
            .ToHashSet();

        return new SimulationPersistenceBatch(
            scenario.PlannedLargeExpenses
                .Where(x => requestIds.Contains(x.Id))
                .ToArray(),
            scenario.PaymentPlans
                .Where(x => requestIds.Contains(x.Id))
                .ToArray(),
            scenario.CreditCards
                .Where(x => cardIds.Contains(x.Id))
                .ToArray(),
            scenario.OtherIncomes
                .Where(x => requestIds.Contains(x.Id))
                .ToArray(),
            scenario.Salaries
                .Where(x => requestIds.Contains(x.Id))
                .ToArray(),
            scenario.PaymentAssignmentStrategies
                .Where(x => requestIds.Contains(x.Id))
                .ToArray(),
            scenario.LoanPrepayments
                .Where(x => requestIds.Contains(x.Id))
                .ToArray());
    }

    public static SimulationApplyResult AppliedResult(
        IReadOnlyList<SimulationRequest> requests,
        SimulationPersistenceBatch batch)
    {
        if (requests.Count == 1)
        {
            var request = requests[0];
            return request.Type switch
            {
                SimulationScenarioType.CashPurchase =>
                    AppliedResult(
                        request,
                        batch.PlannedLargeExpenses.Single().Id,
                        SimulationApplyDestination.Payments,
                        "Plan finans planına eklendi."),
                SimulationScenarioType.CreditCardSinglePayment or
                    SimulationScenarioType.CreditCardInstallmentPurchase or
                    SimulationScenarioType.CreditCardPaymentMode =>
                    AppliedResult(
                        request,
                        batch.CreditCards.Single().Id,
                        SimulationApplyDestination.CreditCard,
                        $"Plan {batch.CreditCards.Single().Bank} {batch.CreditCards.Single().Name} kartına eklendi."),
                SimulationScenarioType.FinancingLoan or
                    SimulationScenarioType.CashDebt or
                    SimulationScenarioType.FutureOneTimePayment or
                    SimulationScenarioType.RecurringPayment =>
                    AppliedResult(
                        request,
                        batch.PaymentPlans.Single().Id,
                        SimulationApplyDestination.Payments,
                        "Plan finans planına eklendi."),
                SimulationScenarioType.FutureIncome =>
                    AppliedResult(
                        request,
                        batch.OtherIncomes.Single().Id,
                        SimulationApplyDestination.Income,
                        "Gelir finans planına eklendi."),
                SimulationScenarioType.SalaryChange =>
                    AppliedResult(
                        request,
                        batch.Salaries.Single().Id,
                        SimulationApplyDestination.SalaryHistory,
                        "Gelir değişikliği kaydedildi."),
                SimulationScenarioType.PaymentStrategyChange =>
                    AppliedResult(
                        request,
                        batch.PaymentAssignmentStrategies.Single().Id,
                        SimulationApplyDestination.Settings,
                        "Gelir kullanım düzeni kaydedildi."),
                SimulationScenarioType.LoanEarlyClosure or
                    SimulationScenarioType.LoanPartialPrepayment =>
                    AppliedResult(
                        request,
                        batch.LoanPrepayments.Single().Id,
                        SimulationApplyDestination.Payments,
                        "Erken ödeme kredinin planına eklendi."),
                _ => throw new ArgumentOutOfRangeException(nameof(requests))
            };
        }

        return new SimulationApplyResult(
            requests[0].ScenarioId,
            Guid.Empty,
            SimulationApplyDestination.Payments,
            AlreadyApplied: false,
            $"{requests.Count} koşul finans planına eklendi.");
    }

    public static SimulationApplyResult? FindAppliedSimulation(
        FinancialPlan plan,
        SimulationRequest request)
    {
        var entityId = request.ScenarioId;
        return request.Type switch
        {
            SimulationScenarioType.CashPurchase
                when plan.PlannedLargeExpenses.Any(x => x.Id == entityId) =>
                AppliedResult(request, entityId, SimulationApplyDestination.Payments,
                    "Plan daha önce finans planına eklendi."),
            SimulationScenarioType.CreditCardSinglePayment or
                SimulationScenarioType.CreditCardInstallmentPurchase
                when plan.CreditCards.Any(card =>
                    card.Id == request.CreditCardId &&
                    card.Charges.Any(charge => charge.Id == entityId)) =>
                AppliedResult(request, request.CreditCardId!.Value,
                    SimulationApplyDestination.CreditCard,
                    "Plan daha önce kredi kartına eklendi."),
            SimulationScenarioType.CreditCardPaymentMode
                when plan.CreditCards.Any(card =>
                    card.Id == request.CreditCardId &&
                    card.PaymentPlans.Any(payment =>
                        payment.Id == entityId)) =>
                AppliedResult(request, request.CreditCardId!.Value,
                    SimulationApplyDestination.CreditCard,
                    "Tam ödeme planı daha önce kredi kartına eklendi."),
            SimulationScenarioType.FinancingLoan or
                SimulationScenarioType.CashDebt or
                SimulationScenarioType.FutureOneTimePayment or
                SimulationScenarioType.RecurringPayment
                when plan.PaymentPlans.Any(x => x.Id == entityId) =>
                AppliedResult(request, entityId, SimulationApplyDestination.Payments,
                    "Plan daha önce finans planına eklendi."),
            SimulationScenarioType.FutureIncome
                when plan.OtherIncomes.Any(x => x.Id == entityId) =>
                AppliedResult(request, entityId, SimulationApplyDestination.Income,
                    "Gelir daha önce finans planına eklendi."),
            SimulationScenarioType.SalaryChange
                when plan.Salaries.Any(x => x.Id == entityId) =>
                AppliedResult(request, entityId, SimulationApplyDestination.SalaryHistory,
                    "Gelir değişikliği daha önce kaydedildi."),
            SimulationScenarioType.PaymentStrategyChange
                when plan.PaymentAssignmentStrategies.Any(x => x.Id == entityId) =>
                AppliedResult(request, entityId, SimulationApplyDestination.Settings,
                    "Gelir kullanım düzeni değişikliği daha önce kaydedildi."),
            SimulationScenarioType.LoanEarlyClosure or
                SimulationScenarioType.LoanPartialPrepayment
                when plan.LoanPrepayments.Any(x => x.Id == entityId) =>
                AppliedResult(request, entityId, SimulationApplyDestination.Payments,
                    "Erken ödeme daha önce kredinin planına eklendi."),
            _ => null
        };
    }

    private static SimulationApplyResult AppliedResult(
        SimulationRequest request,
        Guid entityId,
        SimulationApplyDestination destination,
        string message) => new(
            request.ScenarioId,
            entityId,
            destination,
            AlreadyApplied: false,
            message);
}
