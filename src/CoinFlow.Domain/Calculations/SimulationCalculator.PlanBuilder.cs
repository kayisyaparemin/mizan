using CoinFlow.Domain.Models;

namespace CoinFlow.Domain.Calculations;

public sealed partial class SimulationCalculator
{
    public FinancialPlan BuildScenarioPlan(
        FinancialPlan plan,
        SimulationRequest request) =>
        BuildScenarioPlan(plan, [request]);

    public FinancialPlan BuildScenarioPlan(
        FinancialPlan plan,
        IReadOnlyList<SimulationRequest> requests)
    {
        Validate(requests);
        var scenarioPlan = plan;
        foreach (var request in requests.OrderBy(SortKey))
        {
            scenarioPlan = BuildScenarioPlanCore(
                scenarioPlan,
                request.ScenarioId == Guid.Empty
                    ? request with { ScenarioId = Guid.NewGuid() }
                    : request);
        }

        return scenarioPlan;
    }

    private FinancialPlan BuildScenarioPlanCore(
        FinancialPlan plan,
        SimulationRequest request) =>
        request.Type switch
        {
            SimulationScenarioType.CashPurchase =>
                AddLargeExpense(plan, request),
            SimulationScenarioType.CreditCardSinglePayment =>
                AddCardPurchase(plan, request with { PaymentCount = 1 }),
            SimulationScenarioType.CreditCardInstallmentPurchase =>
                AddCardPurchase(plan, request),
            SimulationScenarioType.FinancingLoan =>
                AddFinancingLoan(plan, request),
            SimulationScenarioType.CashDebt =>
                AddInstallmentPlan(
                    plan,
                    request,
                    request.Amount,
                    PaymentPlanKind.OtherScheduled),
            SimulationScenarioType.FutureOneTimePayment =>
                AddSinglePayment(plan, request),
            SimulationScenarioType.RecurringPayment =>
                AddRecurringPayment(plan, request),
            SimulationScenarioType.FutureIncome =>
                plan with
                {
                    OtherIncomes = plan.OtherIncomes
                        .Append(new OneTimeIncome
                        {
                            Id = request.ScenarioId,
                            Description = request.Name.Trim(),
                            Amount = request.Amount,
                            ExactDate = request.StartDate
                        })
                        .ToArray()
                },
            SimulationScenarioType.SalaryChange =>
                plan with
                {
                    Salaries = plan.Salaries
                        .Where(x => x.EffectiveDate != request.StartDate)
                        .Append(new SalaryScheduleEntry
                        {
                            Id = request.ScenarioId,
                            Description = request.Name.Trim(),
                            Amount = request.Amount,
                            EffectiveDate = request.StartDate
                        })
                        .ToArray()
                },
            SimulationScenarioType.PaymentStrategyChange =>
                AddPaymentStrategy(plan, request),
            SimulationScenarioType.CreditCardPaymentMode =>
                AddCardPaymentMode(plan, request),
            SimulationScenarioType.LoanEarlyClosure or
                SimulationScenarioType.LoanPartialPrepayment =>
                AddLoanPrepayment(plan, request),
            _ => throw new ArgumentOutOfRangeException(nameof(request.Type))
        };

    private static (DateOnly Date, int Type, string Name, decimal Amount, Guid Id)
        SortKey(SimulationRequest request) => (
            request.EffectiveSalaryDate ?? request.StartDate,
            (int)request.Type,
            request.Name.Trim(),
            request.Amount,
            request.ScenarioId);

    private static FinancialPlan AddPaymentStrategy(
        FinancialPlan plan,
        SimulationRequest request)
    {
        var effectiveDate = request.EffectiveSalaryDate ?? request.StartDate;
        if (CalendarRules.ResolveDay(
                effectiveDate.Year,
                effectiveDate.Month,
                plan.Settings.SalaryDay) != effectiveDate)
        {
            throw new InvalidOperationException(
                "Düzen değişikliği yalnızca bir dönem tarihinde başlayabilir.");
        }

        var mode = request.NewPaymentAssignmentMode ??
                   throw new InvalidOperationException(
                       "Yeni dönem kullanım düzeni seçilmelidir.");
        return plan with
        {
            PaymentAssignmentStrategies = plan.PaymentAssignmentStrategies
                .Where(x => x.EffectiveFromSalaryDate != effectiveDate)
                .Append(new PaymentAssignmentStrategy
                {
                    Id = request.ScenarioId,
                    Mode = mode,
                    EffectiveFromSalaryDate = effectiveDate,
                    Note = request.Name.Trim()
                })
                .OrderBy(x => x.EffectiveFromSalaryDate)
                .ToArray()
        };
    }

    private static FinancialPlan AddLargeExpense(
        FinancialPlan plan,
        SimulationRequest request) => plan with
    {
        PlannedLargeExpenses = plan.PlannedLargeExpenses
            .Append(new PlannedLargeExpense
            {
                Id = request.ScenarioId,
                Name = request.Name.Trim(),
                Amount = request.Amount,
                ExactDate = request.StartDate,
                Status = PlannedExpenseStatus.Planned
            })
            .ToArray()
    };

    // Kredi iki taraflıdır: anapara çekildiği gün hesaba girer, geri ödeme
    // taksitlerle çıkar. Yalnızca taksitleri modellemek krediyi saf maliyet
    // gibi gösterir ve "kredi çeksem açığımı kapatır mıyım" sorusunu
    // cevaplanamaz kılar. Anapara, taksit planıyla aynı ScenarioId'yi taşır;
    // uygulama katmanı senaryo planını bu kimlikle difflediği için gelir
    // kalemi de kendiliğinden kalıcılaşır.
    private FinancialPlan AddFinancingLoan(
        FinancialPlan plan,
        SimulationRequest request)
    {
        var withRepayment = AddInstallmentPlan(
            plan,
            request,
            request.TotalRepaymentAmount ?? request.Amount,
            PaymentPlanKind.Installment);
        return withRepayment with
        {
            OtherIncomes = withRepayment.OtherIncomes
                .Append(new OneTimeIncome
                {
                    Id = request.ScenarioId,
                    Description = request.Name.Trim(),
                    Amount = request.Amount,
                    ExactDate = request.StartDate
                })
                .ToArray()
        };
    }

    private FinancialPlan AddInstallmentPlan(
        FinancialPlan plan,
        SimulationRequest request,
        decimal repaymentTotal,
        PaymentPlanKind kind)
    {
        if (repaymentTotal < request.Amount)
        {
            throw new InvalidOperationException(
                "Toplam geri ödeme ana tutardan düşük olamaz.");
        }

        var firstPaymentDate = request.FirstPaymentDate ?? request.StartDate;
        var schedule = installmentScheduleCalculator.Split(
            repaymentTotal,
            request.PaymentCount,
            firstPaymentDate);
        return AddPaymentPlan(
            plan,
            request,
            kind,
            schedule,
            request.Amount,
            repaymentTotal);
    }

    private static FinancialPlan AddRecurringPayment(
        FinancialPlan plan,
        SimulationRequest request)
    {
        var firstPaymentDate = request.FirstPaymentDate ?? request.StartDate;
        var schedule = Enumerable.Range(0, request.PaymentCount)
            .Select(index => new ScheduledAmount(
                CalendarRules.AddMonthsKeepingDay(
                    firstPaymentDate,
                    index,
                    firstPaymentDate.Day),
                request.Amount))
            .ToArray();
        return AddPaymentPlan(
            plan,
            request,
            PaymentPlanKind.Recurring,
            schedule,
            request.Amount,
            request.Amount * request.PaymentCount);
    }

    private static FinancialPlan AddSinglePayment(
        FinancialPlan plan,
        SimulationRequest request) => AddPaymentPlan(
            plan,
            request,
            PaymentPlanKind.OtherScheduled,
            [new ScheduledAmount(request.StartDate, request.Amount)],
            request.Amount,
            request.Amount);

    private static FinancialPlan AddPaymentPlan(
        FinancialPlan plan,
        SimulationRequest request,
        PaymentPlanKind kind,
        IReadOnlyList<ScheduledAmount> schedule,
        decimal originalAmount,
        decimal totalRepaymentAmount)
    {
        var planId = request.ScenarioId;
        var paymentPlan = new TemporaryPaymentPlan
        {
            Id = planId,
            Name = request.Name.Trim(),
            Kind = kind,
            OriginalAmount = originalAmount,
            TotalRepaymentAmount = totalRepaymentAmount,
            Installments = schedule
                .Select((x, index) => new TemporaryPaymentInstallment
                {
                    Id = ChildId(planId, index),
                    PlanId = planId,
                    DueDate = x.Date,
                    Amount = x.Amount
                })
                .ToArray()
        };
        return plan with
        {
            PaymentPlans = plan.PaymentPlans.Append(paymentPlan).ToArray()
        };
    }

    private static Guid ChildId(Guid parentId, int index)
    {
        var bytes = parentId.ToByteArray();
        var ordinal = BitConverter.GetBytes(index + 1);
        for (var offset = 0; offset < ordinal.Length; offset++)
        {
            bytes[12 + offset] ^= ordinal[offset];
        }

        return new Guid(bytes);
    }

    private static decimal ResolveTotalCost(SimulationRequest request) =>
        request.Type switch
        {
            SimulationScenarioType.FutureIncome or
                SimulationScenarioType.SalaryChange or
                SimulationScenarioType.PaymentStrategyChange => 0m,
            SimulationScenarioType.CreditCardPaymentMode => 0m,
            // Erken ödemenin tutarı kredinin o günkü durumundan hesaplanır;
            // senaryo maliyetine BuildLoanImpacts'tan eklenir.
            SimulationScenarioType.LoanEarlyClosure or
                SimulationScenarioType.LoanPartialPrepayment => 0m,
            SimulationScenarioType.FinancingLoan =>
                request.TotalRepaymentAmount ?? request.Amount,
            SimulationScenarioType.RecurringPayment =>
                request.Amount * request.PaymentCount,
            _ => request.Amount
        };
}
