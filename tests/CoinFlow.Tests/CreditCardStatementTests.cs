using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Tests;

public sealed class CreditCardStatementTests
{
    private readonly CreditCardStatementCalculator _calculator = new();

    [Fact]
    public void NewCard_DefaultsToAskEachStatement()
    {
        Assert.Equal(
            CreditCardPaymentStrategy.AskEachStatement,
            new CreditCard().PaymentStrategy);
    }

    [Fact]
    public void September24Charge_EntersSeptember25Statement()
    {
        var card = Card() with
        {
            BalanceAsOfDate = new DateOnly(2026, 8, 26),
            Charges = [Charge(new DateOnly(2026, 9, 24), 1_000m)]
        };

        var statement = _calculator.Project(card, 2)
            .Single(x =>
                x.StatementCloseDate == new DateOnly(2026, 9, 25));

        Assert.Equal(1_000m, statement.NewCharges);
    }

    [Fact]
    public void September28Charge_EntersOctober25Statement()
    {
        var card = Card() with
        {
            BalanceAsOfDate = new DateOnly(2026, 8, 26),
            Charges = [Charge(new DateOnly(2026, 9, 28), 1_000m)]
        };

        var statements = _calculator.Project(card, 3);

        Assert.Equal(
            0m,
            statements.Single(x =>
                x.StatementCloseDate == new DateOnly(2026, 9, 25))
                .NewCharges);
        Assert.Equal(
            1_000m,
            statements.Single(x =>
                x.StatementCloseDate == new DateOnly(2026, 10, 25))
                .NewCharges);
    }

    [Fact]
    public void ClosingDayAtMonthEnd_UsesRealCalendar()
    {
        var close = CreditCardStatementCalculator
            .ResolveStatementCloseOnOrAfter(
                new DateOnly(2027, 2, 1),
                31);

        Assert.Equal(new DateOnly(2027, 2, 28), close);
    }

    [Fact]
    public void SeptemberStatement_IsDueOctoberFifth()
    {
        var due = CreditCardStatementCalculator.ResolvePaymentDueDate(
            new DateOnly(2026, 9, 25),
            5);

        Assert.Equal(new DateOnly(2026, 10, 5), due);
        Assert.True(new SalaryPeriod(
            new DateOnly(2026, 9, 10),
            new DateOnly(2026, 10, 10)).Contains(due));
    }

    [Fact]
    public void CarriedPlusUnbilled_DrivesStatementAndMinimum()
    {
        var statement = Assert.Single(_calculator.Project(
            Card() with
            {
                CarriedBalance = 35_000m,
                UnbilledSpending = 59_000m,
                PaymentStrategy = CreditCardPaymentStrategy.Minimum
            },
            1));

        // 35.000 devreden + %5 faizi + 59.000 harcama.
        Assert.Equal(1_750m, statement.CarryInterest);
        Assert.Equal(95_750m, statement.StatementBalance);
        Assert.Equal(38_300m, statement.MinimumPayment);
        Assert.Equal(38_300m, statement.Payment);
        Assert.Equal(57_450m, statement.CarriedAfterPayment);
        Assert.Equal(57_450m, statement.NextCarriedBalance);
    }

    [Fact]
    public void MinimumPayment_UsesAwayFromZeroRounding()
    {
        var statement = Assert.Single(_calculator.Project(
            Card() with
            {
                CarriedBalance = 10.0125m,
                MinimumPaymentRate = 0.40m,
                PaymentStrategy = CreditCardPaymentStrategy.Minimum
            },
            1));

        // 10,0125 + 0,50 faiz = 10,5125; %40 = 4,205 → 4,21.
        Assert.Equal(4.21m, statement.MinimumPayment);
    }

    [Fact]
    public void FullStatement_LeavesNoCarriedBalance()
    {
        var statement = Assert.Single(_calculator.Project(
            Card() with
            {
                CarriedBalance = 94_000m,
                PaymentStrategy = CreditCardPaymentStrategy.FullStatement
            },
            1));

        // Tamamını ödeyen de devreden bakiyenin faizini öder: ekstre
        // 94.000 değil 98.700 kesilir.
        Assert.Equal(4_700m, statement.CarryInterest);
        Assert.Equal(98_700m, statement.StatementBalance);
        Assert.Equal(98_700m, statement.Payment);
        Assert.Equal(0m, statement.CarriedAfterPayment);
        Assert.Equal(0m, statement.NextCarriedBalance);
    }

    [Fact]
    public void FullPaymentEveryStatement_ChargesInterestWhileCarryExists()
    {
        // BULGU-KART-FAIZI: tamamını ödeyen kullanıcıda devreden borcun
        // faizi hiç görünmüyordu. Faiz bir kez, girdiği ekstrede işlenir.
        var statements = _calculator.Project(
            Card() with
            {
                CarriedBalance = 60_000m,
                UnbilledSpending = 17_900m,
                PaymentStrategy = CreditCardPaymentStrategy.FullStatement
            },
            2,
            carryInterestRate: 0.05m);

        Assert.Equal(3_000m, statements[0].CarryInterest);
        Assert.Equal(80_900m, statements[0].StatementBalance);
        Assert.Equal(80_900m, statements[0].Payment);
        Assert.Equal(0m, statements[0].NextCarriedBalance);
        // Borç kapandıktan sonra faiz üremez.
        Assert.Equal(0m, statements[1].CarryInterest);
        Assert.Equal(0m, statements[1].StatementBalance);
    }

    [Fact]
    public void MinimumPayment_AddsFivePercentCarryInterest()
    {
        var statement = Assert.Single(_calculator.Project(
            Card() with
            {
                CarriedBalance = 100_000m,
                PaymentStrategy = CreditCardPaymentStrategy.Minimum
            },
            1,
            carryInterestRate: 0.05m));

        Assert.Equal(5_000m, statement.CarryInterest);
        Assert.Equal(105_000m, statement.StatementBalance);
        Assert.Equal(42_000m, statement.Payment);
        Assert.Equal(63_000m, statement.CarriedAfterPayment);
        Assert.Equal(63_000m, statement.NextCarriedBalance);
        Assert.Equal(0.05m, statement.AppliedInterestRate);
    }

    [Fact]
    public void CarryInterest_CompoundsIntoNextStatement()
    {
        var statements = _calculator.Project(
            Card() with
            {
                CarriedBalance = 100_000m,
                PaymentStrategy = CreditCardPaymentStrategy.Minimum
            },
            2,
            carryInterestRate: 0.05m);

        Assert.Equal(63_000m, statements[0].NextCarriedBalance);
        // İkinci ekstrenin faizi, birincinin faizini de içeren 63.000
        // üzerinden işler: bileşik etki korunur.
        Assert.Equal(3_150m, statements[1].CarryInterest);
        Assert.Equal(66_150m, statements[1].StatementBalance);
        Assert.Equal(26_460m, statements[1].Payment);
        Assert.Equal(39_690m, statements[1].CarriedAfterPayment);
        Assert.Equal(39_690m, statements[1].NextCarriedBalance);
    }

    [Fact]
    public void ZeroInterestRate_DoesNotIncreaseCarry()
    {
        var statement = Assert.Single(_calculator.Project(
            Card() with
            {
                CarriedBalance = 100_000m,
                PaymentStrategy = CreditCardPaymentStrategy.Minimum
            },
            1,
            carryInterestRate: 0m));

        Assert.Equal(60_000m, statement.CarriedAfterPayment);
        Assert.Equal(0m, statement.CarryInterest);
        Assert.Equal(60_000m, statement.NextCarriedBalance);
    }

    [Fact]
    public void InvalidInterestRate_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _calculator.Project(Card(), 1, carryInterestRate: -0.01m));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _calculator.Project(Card(), 1, carryInterestRate: 1.01m));
    }

    [Theory]
    [InlineData(50000, 50000)]
    [InlineData(20000, 38300)]
    public void FixedAmount_NeverFallsBelowMinimum(
        double fixedAmount,
        double expectedPayment)
    {
        var statement = Assert.Single(_calculator.Project(
            Card() with
            {
                CarriedBalance = 35_000m,
                UnbilledSpending = 59_000m,
                PaymentStrategy = CreditCardPaymentStrategy.FixedAmount,
                FixedPaymentAmount = (decimal)fixedAmount
            },
            1));

        Assert.Equal((decimal)expectedPayment, statement.Payment);
    }

    [Fact]
    public void DueDateOverride_HasPriorityOverGlobalStrategy()
    {
        var statement = Assert.Single(_calculator.Project(
            Card() with
            {
                CarriedBalance = 94_000m,
                PaymentStrategy = CreditCardPaymentStrategy.Minimum,
                PaymentPlans =
                [
                    new CreditCardPaymentPlan
                    {
                        DueDate = new DateOnly(2026, 9, 5),
                        PaymentType = CreditCardPaymentType.FixedAmount,
                        Amount = 50_000m
                    }
                ]
            },
            1));

        Assert.Equal(50_000m, statement.Payment);
        Assert.Equal(
            CreditCardPaymentResolution.DueDateOverride,
            statement.PaymentResolution);
    }

    [Fact]
    public void ProjectionFallback_IsEstimateAndDoesNotCreatePlan()
    {
        var card = Card() with
        {
            CarriedBalance = 94_000m,
            ProjectionFallbackStrategy =
                ProjectionFallbackStrategy.Minimum
        };

        var statement = Assert.Single(
            _calculator.Project(card, 1, useProjectionFallback: true));

        Assert.Equal(39_480m, statement.Payment);
        Assert.Equal(
            CreditCardPaymentResolution.ProjectionFallback,
            statement.PaymentResolution);
        Assert.Empty(card.PaymentPlans);
        Assert.Equal(
            CreditCardPaymentStrategy.AskEachStatement,
            card.PaymentStrategy);
    }

    [Fact]
    public void AskEachWithoutFallback_RemainsUndetermined()
    {
        var statement = Assert.Single(_calculator.Project(
            Card() with { CarriedBalance = 94_000m },
            1,
            useProjectionFallback: true));

        Assert.Null(statement.Payment);
        Assert.Equal(
            CreditCardPaymentResolution.Undetermined,
            statement.PaymentResolution);
    }

    [Fact]
    public void AxessCanonicalStatements_AreExact()
    {
        var statements = _calculator.Project(
            TestFactory.AxessCard(),
            3,
            useProjectionFallback: true);

        AssertStatement(
            statements[0],
            new DateOnly(2026, 8, 25),
            new DateOnly(2026, 9, 5),
            98_245.77m,
            39_298.31m,
            58_947.46m,
            1_760.09m,
            58_947.46m);
        AssertStatement(
            statements[1],
            new DateOnly(2026, 9, 25),
            new DateOnly(2026, 10, 5),
            61_894.83m,
            24_757.93m,
            37_136.90m,
            2_947.37m,
            37_136.90m);
        AssertStatement(
            statements[2],
            new DateOnly(2026, 10, 25),
            new DateOnly(2026, 11, 5),
            54_532.11m,
            21_812.84m,
            32_719.27m,
            1_856.85m,
            32_719.27m);
    }

    [Fact]
    public void KnownDebt_IsSumOfDistinctOutstandingComponents()
    {
        Assert.Equal(
            123_751.49m,
            TestFactory.AxessCard().KnownTotalDebt);
    }

    [Fact]
    public void ActualAxessMinimumPayment_UsesBankMinimumAndCarriesPrincipal()
    {
        var statement = Assert.Single(_calculator.Project(
            ActualCard(
                100_804.94m,
                40_321.97m,
                CurrentStatementPaymentMode.Minimum),
            1));

        Assert.True(statement.IsActualStatement);
        Assert.Equal(new DateOnly(2026, 8, 28),
            statement.StatementCloseDate);
        Assert.Equal(new DateOnly(2026, 9, 7),
            statement.PaymentDueDate);
        Assert.Equal(100_804.94m, statement.StatementBalance);
        Assert.Equal(40_321.97m, statement.MinimumPayment);
        Assert.Equal(40_321.97m, statement.Payment);
        Assert.Equal(60_482.97m, statement.CarriedAfterPayment);
    }

    [Fact]
    public void ActualGarantiMinimumPayment_UsesExactActualDateAndMinimum()
    {
        var statement = Assert.Single(_calculator.Project(
            ActualCard(
                15_000m,
                6_000m,
                CurrentStatementPaymentMode.Minimum) with
            {
                CurrentStatement = new CreditCardStatement
                {
                    StatementDate = new DateOnly(2026, 8, 24),
                    DueDate = new DateOnly(2026, 9, 3),
                    StatementAmount = 15_000m,
                    MinimumPaymentAmount = 6_000m,
                    NextStatementDate = new DateOnly(2026, 9, 25),
                    NextDueDate = new DateOnly(2026, 10, 5)
                }
            },
            1));

        Assert.Equal(new DateOnly(2026, 8, 24),
            statement.StatementCloseDate);
        Assert.Equal(new DateOnly(2026, 9, 3),
            statement.PaymentDueDate);
        Assert.Equal(6_000m, statement.Payment);
        Assert.Equal(9_000m, statement.CarriedAfterPayment);
    }

    [Fact]
    public void ActualFullPayment_LeavesNoCarryInterest()
    {
        var statement = Assert.Single(_calculator.Project(
            ActualCard(
                15_000m,
                6_000m,
                CurrentStatementPaymentMode.Full),
            1));

        Assert.Equal(15_000m, statement.Payment);
        Assert.Equal(0m, statement.CarriedAfterPayment);
        Assert.Equal(0m, statement.CarryInterest);
        Assert.Equal(0m, statement.NextCarriedBalance);
    }

    [Fact]
    public void ActualCustomPayment_CarriesRemainderWithoutMinimumFloor()
    {
        var statement = Assert.Single(_calculator.Project(
            ActualCard(
                15_000m,
                6_000m,
                CurrentStatementPaymentMode.Custom,
                10_000m),
            1));

        Assert.Equal(10_000m, statement.Payment);
        Assert.Equal(5_000m, statement.CarriedAfterPayment);
    }

    [Fact]
    public void ActualMinimum_OverridesConfiguredMinimumRatio()
    {
        var statement = Assert.Single(_calculator.Project(
            ActualCard(
                15_000m,
                6_000m,
                CurrentStatementPaymentMode.Minimum) with
            {
                MinimumPaymentRate = 0.10m
            },
            1));

        Assert.Equal(6_000m, statement.MinimumPayment);
        Assert.Equal(6_000m, statement.Payment);
    }

    [Fact]
    public void ActualStatement_DoesNotDoubleCountLegacySeedOrHistoricalCharges()
    {
        var statement = Assert.Single(_calculator.Project(
            ActualCard(
                15_000m,
                6_000m,
                CurrentStatementPaymentMode.Minimum) with
            {
                CarriedBalance = 50_000m,
                UnbilledSpending = 20_000m,
                Charges =
                [
                    Charge(new DateOnly(2026, 8, 20), 7_000m)
                ]
            },
            1));

        Assert.Equal(15_000m, statement.StatementBalance);
        Assert.Equal(0m, statement.NewCharges);
        Assert.Equal(9_000m, statement.CarriedAfterPayment);
    }

    [Fact]
    public void ActualStatement_UsesExactNextDatesForImmediateNextCycle()
    {
        var statements = _calculator.Project(
            ActualCard(
                15_000m,
                6_000m,
                CurrentStatementPaymentMode.Minimum) with
            {
                Charges =
                [
                    Charge(new DateOnly(2026, 9, 25), 1_250m)
                ]
            },
            2,
            carryInterestRate: 0m);

        Assert.Equal(new DateOnly(2026, 9, 28),
            statements[1].StatementCloseDate);
        Assert.Equal(new DateOnly(2026, 10, 8),
            statements[1].PaymentDueDate);
        Assert.Equal(1_250m, statements[1].NewCharges);
        Assert.Equal(10_250m, statements[1].StatementBalance);
    }

    [Fact]
    public void GarantiActualAugust24_DerivesNominalSeptember25()
    {
        var next = CreditCardStatementCalculator.ResolveNextStatementDate(
            new DateOnly(2026, 8, 24),
            statementClosingDay: 25);

        Assert.Equal(new DateOnly(2026, 9, 25), next);
    }

    [Fact]
    public void AxessActualAugust28_DerivesSeptember28()
    {
        var next = CreditCardStatementCalculator.ResolveNextStatementDate(
            new DateOnly(2026, 8, 28),
            statementClosingDay: 28);

        Assert.Equal(new DateOnly(2026, 9, 28), next);
    }

    [Fact]
    public void ImportedExactNextDates_AreAuthoritative()
    {
        var importedStatement = new DateOnly(2026, 9, 27);
        var importedDue = new DateOnly(2026, 10, 9);
        var next = CreditCardStatementCalculator.ResolveNextStatementDate(
            new DateOnly(2026, 8, 24),
            statementClosingDay: 25,
            importedExactDate: importedStatement);
        var due = CreditCardStatementCalculator.ResolveNextDueDate(
            next,
            paymentDueDay: 5,
            importedExactDate: importedDue);

        Assert.Equal(importedStatement, next);
        Assert.Equal(importedDue, due);
    }

    [Fact]
    public void NextDates_AreNotRequiredForValidActualStatement()
    {
        var card = ActualCard(
            15_000m,
            6_000m,
            CurrentStatementPaymentMode.Minimum) with
        {
            StatementClosingDay = 25,
            PaymentDueDay = 5,
            CurrentStatement = new CreditCardStatement
            {
                StatementDate = new DateOnly(2026, 8, 24),
                DueDate = new DateOnly(2026, 9, 3),
                StatementAmount = 15_000m,
                MinimumPaymentAmount = 6_000m,
                NextStatementDate = null,
                NextDueDate = null
            }
        };

        var statements = _calculator.Project(card, 2);

        Assert.Equal(new DateOnly(2026, 8, 24),
            statements[0].StatementCloseDate);
        Assert.Equal(new DateOnly(2026, 9, 25),
            statements[1].StatementCloseDate);
        Assert.Equal(new DateOnly(2026, 10, 5),
            statements[1].PaymentDueDate);
    }

    [Fact]
    public void NewActualStatement_ReanchorsInsteadOfAddingPredictionDelta()
    {
        var predicted = _calculator.Project(
            ActualCard(
                79_000m,
                31_600m,
                CurrentStatementPaymentMode.Minimum),
            2)[1];

        var reanchored = _calculator.Project(
            ActualCard(
                81_200m,
                32_480m,
                CurrentStatementPaymentMode.Minimum) with
            {
                CurrentStatement = new CreditCardStatement
                {
                    StatementDate = new DateOnly(2026, 9, 28),
                    DueDate = new DateOnly(2026, 10, 8),
                    StatementAmount = 81_200m,
                    MinimumPaymentAmount = 32_480m,
                    NextStatementDate = new DateOnly(2026, 10, 28),
                    NextDueDate = new DateOnly(2026, 11, 8)
                }
            },
            1);

        Assert.NotEqual(predicted.StatementBalance,
            reanchored[0].StatementBalance);
        Assert.Equal(81_200m, reanchored[0].StatementBalance);
        Assert.Equal(48_720m, reanchored[0].CarriedAfterPayment);
    }

    private static void AssertStatement(
        CreditCardStatementProjection statement,
        DateOnly closeDate,
        DateOnly dueDate,
        decimal balance,
        decimal payment,
        decimal carriedPrincipal,
        decimal interest,
        decimal nextCarry)
    {
        Assert.Equal(closeDate, statement.StatementCloseDate);
        Assert.Equal(dueDate, statement.PaymentDueDate);
        Assert.Equal(balance, statement.StatementBalance);
        Assert.Equal(payment, statement.Payment);
        Assert.Equal(carriedPrincipal, statement.CarriedAfterPayment);
        Assert.Equal(interest, statement.CarryInterest);
        Assert.Equal(nextCarry, statement.NextCarriedBalance);
    }

    private static CreditCard Card() => new()
    {
        Name = "Test",
        BalanceAsOfDate = new DateOnly(2026, 8, 1),
        StatementClosingDay = 25,
        PaymentDueDay = 5,
        MinimumPaymentRate = 0.40m
    };

    private static CardCharge Charge(
        DateOnly postingDate,
        decimal amount) => new()
    {
        PostingDate = postingDate,
        Amount = amount
    };

    private static CreditCard ActualCard(
        decimal statementAmount,
        decimal minimumPayment,
        CurrentStatementPaymentMode mode,
        decimal? customAmount = null)
    {
        var id = Guid.NewGuid();
        return new CreditCard
        {
            Id = id,
            Name = "Actual",
            BalanceAsOfDate = new DateOnly(2026, 8, 28),
            StatementClosingDay = 25,
            PaymentDueDay = 5,
            MinimumPaymentRate = 0.40m,
            PaymentStrategy = CreditCardPaymentStrategy.AskEachStatement,
            ProjectionFallbackStrategy = ProjectionFallbackStrategy.Minimum,
            CurrentStatement = new CreditCardStatement
            {
                CreditCardId = id,
                StatementDate = new DateOnly(2026, 8, 28),
                DueDate = new DateOnly(2026, 9, 7),
                StatementAmount = statementAmount,
                MinimumPaymentAmount = minimumPayment,
                NextStatementDate = new DateOnly(2026, 9, 28),
                NextDueDate = new DateOnly(2026, 10, 8)
            },
            CurrentStatementPaymentPlan = new CurrentStatementPaymentPlan
            {
                Mode = mode,
                CustomAmount = customAmount
            }
        };
    }
}
