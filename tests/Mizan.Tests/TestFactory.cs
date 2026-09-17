using Mizan.Application.Abstractions;
using Mizan.Application.Services;
using Mizan.Domain.Calculations;
using Mizan.Domain.Models;

namespace Mizan.Tests;

internal static class TestFactory
{
    public static FinancialProjectionCalculator ProjectionCalculator()
    {
        var cashFlowPeriods = new CashFlowPeriodCalculator();
        var allocationResolver = new PaymentAllocationStrategyResolver(
            cashFlowPeriods);
        var allocationPlanner = new CashFlowAllocationPlanner(allocationResolver);
        var income = new IncomeProjectionCalculator(new IncomeResolver());
        var scheduled = new ScheduledPaymentCalculator();
        var mandatory = new MandatoryPaymentCalculator(
            LoanScheduleBuilder(),
            scheduled);
        return new FinancialProjectionCalculator(
            cashFlowPeriods,
            income,
            new CreditCardStatementCalculator(),
            mandatory,
            allocationPlanner,
            allocationResolver);
    }

    public static LoanPayoffAdvisor LoanPayoffAdvisor(
        FinancialProjectionCalculator? projection = null)
    {
        var calculator = projection ?? ProjectionCalculator();
        var builder = LoanScheduleBuilder();
        return new LoanPayoffAdvisor(
            calculator,
            new SimulationCalculator(
                calculator,
                new InstallmentScheduleCalculator(),
                builder),
            new LoanAmortizationCalculator(new LoanScheduleCalculator()),
            builder);
    }

    public static LoanPaymentScheduleBuilder LoanScheduleBuilder()
    {
        var schedule = new LoanScheduleCalculator();
        return new LoanPaymentScheduleBuilder(
            schedule,
            new LoanAmortizationCalculator(schedule));
    }

    public static MizanService Service(
        IMizanStore store,
        DateOnly? today = null)
    {
        var projectionCalculator = ProjectionCalculator();
        var projectionService =
            new FinancialProjectionService(projectionCalculator);
        var installments = new InstallmentScheduleCalculator();
        var clock = new FixedClock(
            today ?? new DateOnly(2026, 8, 20),
            new DateTimeOffset(
                (today ?? new DateOnly(2026, 8, 20)).Year,
                (today ?? new DateOnly(2026, 8, 20)).Month,
                (today ?? new DateOnly(2026, 8, 20)).Day,
                12,
                0,
                0,
                TimeSpan.Zero));
        var planSnapshotService = new PeriodPlanSnapshotService(
            projectionCalculator,
            new CashFlowPeriodCalculator(),
            new IncomeResolver());
        var snapshotService = new FinancialSnapshotService(
            store,
            clock,
            planSnapshotService,
            new CashFlowPeriodCalculator());
        var historicalPlanRevisionService =
            new HistoricalPlanRevisionService(
                store,
                clock,
                planSnapshotService);
        var comparison = new PlanActualComparisonCalculator();
        var reviewService = new PeriodReviewService(
            store,
            clock,
            snapshotService,
            new FinancialStateReconciliationService(),
            new FinancialInstrumentReconciliationService(
                new CreditCardActualPaymentReconciler(
                    new CreditCardStatementCalculator()),
                new LoanAmortizationCalculator(new LoanScheduleCalculator()),
                LoanScheduleBuilder()),
            comparison);
        return new MizanService(
            store,
            clock,
            projectionService,
            new SimulationCalculator(
                projectionCalculator,
                installments),
            new TargetAmountCalculator(),
            new PaymentAllocationStrategyResolver(
                new CashFlowPeriodCalculator()),
            new CreditCardPaymentPreferenceResolver(),
            new CashFlowPeriodCalculator(),
            new ProjectionBoundaryResolver(
                new CashFlowPeriodCalculator()),
            snapshotService,
            historicalPlanRevisionService,
            reviewService,
            new PeriodProgressService(
                store,
                clock,
                new CreditCardStatementCalculator()),
            new HistoryQueryService(store, comparison),
            new LoanPayoffService(
                clock,
                new LoanAmortizationCalculator(new LoanScheduleCalculator()),
                LoanScheduleBuilder()),
            LoanPayoffAdvisor(projectionCalculator));
    }

    public static FinancialPlan CanonicalPlan() => new()
    {
        Settings = new UserSettings
        {
            IncomeDay = 10,
            MonthlyVariableExpenseAllowance = 30_000m,
            ProjectionOpeningBalance = 0m,
            ProjectionAnchorDate = new DateOnly(2026, 8, 20)
        },
        Salaries =
        [
            new SalaryScheduleEntry
            {
                Amount = 115_000m,
                EffectiveDate = new DateOnly(2026, 1, 1),
                Description = "Maaş"
            },
            new SalaryScheduleEntry
            {
                Amount = 132_250m,
                EffectiveDate = new DateOnly(2027, 1, 1),
                Description = "Planlanan maaş"
            }
        ],
        Loans =
        [
            new Loan
            {
                Name = "borç kapama",
                Bank = "Garanti BBVA",
                MonthlyPayment = 14_501.23m,
                PaymentDay = 7,
                NextPaymentDate = new DateOnly(2026, 9, 7),
                RemainingInstallmentCount = 22
            },
            new Loan
            {
                Name = "On Dijital",
                Bank = "Burgan Bank",
                MonthlyPayment = 7_374.59m,
                PaymentDay = 18,
                NextPaymentDate = new DateOnly(2026, 9, 18),
                RemainingInstallmentCount = 9
            }
        ],
        PaymentPlans = [EminevimPlan()],
        CreditCards = [AxessCard()],
        PaymentAssignmentStrategies =
        [
            new CashFlowAllocationStrategy
            {
                Id = Guid.Parse("50000000-0000-0000-0000-000000000001"),
                Mode = CashFlowAllocationMode.UpcomingPeriod,
                EffectiveFromPeriodDate = new DateOnly(2026, 9, 10),
                CreatedAt = new DateTimeOffset(
                    2026, 8, 20, 0, 0, 0, TimeSpan.Zero),
                Note = "İlk gelir kullanım düzeni"
            }
        ]
    };

    public static TemporaryPaymentPlan EminevimPlan()
    {
        var id = Guid.NewGuid();
        return new TemporaryPaymentPlan
        {
            Id = id,
            Name = "Eminevim",
            Kind = PaymentPlanKind.Temporary,
            Installments =
            [
                new TemporaryPaymentInstallment
                {
                    PlanId = id,
                    DueDate = new DateOnly(2026, 9, 20),
                    Amount = 28_167.40m
                },
                new TemporaryPaymentInstallment
                {
                    PlanId = id,
                    DueDate = new DateOnly(2026, 10, 20),
                    Amount = 28_167.40m
                },
                new TemporaryPaymentInstallment
                {
                    PlanId = id,
                    DueDate = new DateOnly(2026, 11, 20),
                    Amount = 55_492.20m
                }
            ]
        };
    }

    public static CreditCard AxessCard()
    {
        var id = Guid.NewGuid();
        return new CreditCard
        {
            Id = id,
            Name = "Axess",
            Bank = "Akbank",
            Limit = 607_350m,
            CarriedBalance = 35_201.77m,
            UnbilledSpending = 61_283.91m,
            BalanceAsOfDate = new DateOnly(2026, 8, 20),
            StatementClosingDay = 25,
            PaymentDueDay = 5,
            MinimumPaymentRate = 0.40m,
            PaymentStrategy = CreditCardPaymentStrategy.AskEachStatement,
            ProjectionFallbackStrategy =
                ProjectionFallbackStrategy.Minimum,
            Charges =
            [
                new CardCharge
                {
                    CreditCardId = id,
                    PostingDate = new DateOnly(2026, 9, 28),
                    Amount = 15_538.36m
                },
                new CardCharge
                {
                    CreditCardId = id,
                    PostingDate = new DateOnly(2026, 10, 30),
                    Amount = 9_102.90m
                },
                new CardCharge
                {
                    CreditCardId = id,
                    PostingDate = new DateOnly(2026, 11, 28),
                    Amount = 2_624.55m
                }
            ]
        };
    }

    private sealed class FixedClock(
        DateOnly today,
        DateTimeOffset utcNow) : IClock
    {
        public DateOnly Today { get; } = today;
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
