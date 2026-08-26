using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Models;

public sealed record OnboardingDraft
{
    public UserSettings Settings { get; init; } = new();
    public IReadOnlyList<SalaryScheduleEntry> Salaries { get; init; } = [];
    public IReadOnlyList<OneTimeIncome> OtherIncomes { get; init; } = [];
    public IReadOnlyList<Loan> Loans { get; init; } = [];
    public IReadOnlyList<TemporaryPaymentPlan> PaymentPlans { get; init; } = [];
    public IReadOnlyList<CreditCard> CreditCards { get; init; } = [];
    public IReadOnlyList<PlannedLargeExpense> PlannedLargeExpenses { get; init; } = [];
    public PaymentAssignmentMode InitialPaymentAssignmentMode { get; init; } =
        PaymentAssignmentMode.UpcomingPeriod;
    public string SnapshotNote { get; init; } = "İlk kurulum tamamlandı";
}

public sealed record OnboardingPersistenceBatch(
    UserSettings Settings,
    IReadOnlyList<SalaryScheduleEntry> Salaries,
    IReadOnlyList<OneTimeIncome> OtherIncomes,
    IReadOnlyList<Loan> Loans,
    IReadOnlyList<TemporaryPaymentPlan> PaymentPlans,
    IReadOnlyList<CreditCard> CreditCards,
    IReadOnlyList<PlannedLargeExpense> PlannedLargeExpenses,
    IReadOnlyList<PaymentAssignmentStrategy> PaymentAssignmentStrategies,
    FinancialSnapshot CurrentSnapshot,
    PeriodPlanSnapshot CurrentPlan);

public static class CanonicalDevelopmentOnboardingFixture
{
    public static OnboardingDraft Create() => new()
    {
        Settings = new UserSettings
        {
            SalaryDay = 10,
            MonthlyLivingBudget = 30_000m,
            ProjectionStartingSavings = 0m,
            ProjectionAnchorDate = new DateOnly(2026, 8, 20),
            CreditCardCarryInterestRate = 0.05m,
            DeficitFinancingInterestRate = 0.05m
        },
        Salaries =
        [
            new SalaryScheduleEntry
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000001"),
                Amount = 115_000m,
                EffectiveDate = new DateOnly(2026, 1, 1),
                Description = "Maaş"
            },
            new SalaryScheduleEntry
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000002"),
                Amount = 132_250m,
                EffectiveDate = new DateOnly(2027, 1, 1),
                Description = "Planlanan maaş"
            }
        ],
        Loans =
        [
            new Loan
            {
                Id = Guid.Parse("20000000-0000-0000-0000-000000000001"),
                Name = "borç kapama",
                Bank = "Garanti BBVA",
                MonthlyPayment = 14_501.23m,
                PaymentDay = 7,
                NextPaymentDate = new DateOnly(2026, 9, 7),
                RemainingInstallmentCount = 22,
                RemainingDebt = 190_188m
            },
            new Loan
            {
                Id = Guid.Parse("20000000-0000-0000-0000-000000000002"),
                Name = "On Dijital",
                Bank = "Burgan Bank",
                MonthlyPayment = 7_374.59m,
                PaymentDay = 18,
                NextPaymentDate = new DateOnly(2026, 9, 18),
                RemainingInstallmentCount = 9,
                RemainingDebt = 55_777m
            }
        ],
        PaymentPlans =
        [
            new TemporaryPaymentPlan
            {
                Id = Guid.Parse("30000000-0000-0000-0000-000000000001"),
                Name = "Eminevim",
                Kind = PaymentPlanKind.Temporary,
                Installments =
                [
                    new TemporaryPaymentInstallment
                    {
                        Id = Guid.Parse("30000000-0000-0000-0000-000000000011"),
                        PlanId = Guid.Parse("30000000-0000-0000-0000-000000000001"),
                        DueDate = new DateOnly(2026, 9, 20),
                        Amount = 28_167.40m
                    },
                    new TemporaryPaymentInstallment
                    {
                        Id = Guid.Parse("30000000-0000-0000-0000-000000000012"),
                        PlanId = Guid.Parse("30000000-0000-0000-0000-000000000001"),
                        DueDate = new DateOnly(2026, 10, 20),
                        Amount = 28_167.40m
                    },
                    new TemporaryPaymentInstallment
                    {
                        Id = Guid.Parse("30000000-0000-0000-0000-000000000013"),
                        PlanId = Guid.Parse("30000000-0000-0000-0000-000000000001"),
                        DueDate = new DateOnly(2026, 11, 20),
                        Amount = 55_492.20m
                    }
                ]
            }
        ],
        CreditCards =
        [
            new CreditCard
            {
                Id = Guid.Parse("40000000-0000-0000-0000-000000000001"),
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
                ProjectionFallbackStrategy = ProjectionFallbackStrategy.Minimum,
                Charges =
                [
                    new CardCharge
                    {
                        Id = Guid.Parse("40000000-0000-0000-0000-000000000011"),
                        CreditCardId = Guid.Parse("40000000-0000-0000-0000-000000000001"),
                        Description = "Gelecek taksit",
                        PostingDate = new DateOnly(2026, 9, 28),
                        Amount = 15_538.36m
                    },
                    new CardCharge
                    {
                        Id = Guid.Parse("40000000-0000-0000-0000-000000000012"),
                        CreditCardId = Guid.Parse("40000000-0000-0000-0000-000000000001"),
                        Description = "Gelecek taksit",
                        PostingDate = new DateOnly(2026, 10, 30),
                        Amount = 9_102.90m
                    },
                    new CardCharge
                    {
                        Id = Guid.Parse("40000000-0000-0000-0000-000000000013"),
                        CreditCardId = Guid.Parse("40000000-0000-0000-0000-000000000001"),
                        Description = "Gelecek taksit",
                        PostingDate = new DateOnly(2026, 11, 28),
                        Amount = 2_624.55m
                    }
                ]
            }
        ],
        SnapshotNote = "İlk güncel finansal durum"
    };
}
