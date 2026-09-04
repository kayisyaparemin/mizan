using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Tests;

public sealed class CreditCardStatementSettlementTests
{
    private readonly CreditCardStatementCalculator _calculator = new();
    private readonly CreditCardActualPaymentReconciler _reconciler = new(new());

    [Fact]
    public void FullPayment_RetiresCurrentStatementAndPlan()
    {
        var settled = _reconciler.Apply(
            ActualCard(15_000m, 6_000m, CurrentStatementPaymentMode.Full),
            new DateOnly(2026, 9, 7),
            15_000m,
            0.05m);

        Assert.Null(settled.CurrentStatement);
        Assert.Null(settled.CurrentStatementPaymentPlan);
        Assert.Equal(0m, settled.CarriedBalance);
    }

    [Fact]
    public void FullPayment_NextProjectionStartsFromZeroCarry()
    {
        var settled = _reconciler.Apply(
            ActualCard(15_000m, 6_000m, CurrentStatementPaymentMode.Full),
            new DateOnly(2026, 9, 7),
            15_000m,
            0.05m);

        var next = Assert.Single(_calculator.Project(settled, 1));

        Assert.Equal(0m, next.StatementBalance);
        Assert.Equal(0m, next.CarryInterest);
    }

    [Fact]
    public void PartialPayment_RetiresCurrentStatementAndCarriesOnlyRemainder()
    {
        var settled = _reconciler.Apply(
            ActualCard(15_000m, 6_000m, CurrentStatementPaymentMode.Custom, 10_000m),
            new DateOnly(2026, 9, 7),
            10_000m,
            0.05m);

        Assert.Null(settled.CurrentStatement);
        Assert.Null(settled.CurrentStatementPaymentPlan);
        // Remaining principal 5,000 + 5% carry interest = 5,250.
        Assert.Equal(5_250m, settled.CarriedBalance);

        var next = Assert.Single(_calculator.Project(settled, 1));
        Assert.Equal(5_250m, next.StatementBalance);
    }

    [Fact]
    public void FullAndPartialPayment_ProduceDifferentNextProjections()
    {
        var fullyPaid = _reconciler.Apply(
            ActualCard(15_000m, 6_000m, CurrentStatementPaymentMode.Full),
            new DateOnly(2026, 9, 7),
            15_000m,
            0.05m);
        var partiallyPaid = _reconciler.Apply(
            ActualCard(15_000m, 6_000m, CurrentStatementPaymentMode.Custom, 5_000m),
            new DateOnly(2026, 9, 7),
            5_000m,
            0.05m);

        var fullNext = Assert.Single(_calculator.Project(fullyPaid, 1));
        var partialNext = Assert.Single(_calculator.Project(partiallyPaid, 1));

        Assert.NotEqual(fullNext.StatementBalance, partialNext.StatementBalance);
        Assert.Equal(0m, fullNext.StatementBalance);
        Assert.Equal(10_500m, partialNext.StatementBalance);
    }

    [Fact]
    public void Settlement_PreservesBankKnownNextStatementAndDueDate()
    {
        var settled = _reconciler.Apply(
            ActualCard(15_000m, 6_000m, CurrentStatementPaymentMode.Minimum),
            new DateOnly(2026, 9, 7),
            6_000m,
            0.05m);

        Assert.Equal(new DateOnly(2026, 9, 28), settled.KnownNextStatementDate);
        Assert.Equal(new DateOnly(2026, 10, 8), settled.KnownNextDueDate);

        var next = Assert.Single(_calculator.Project(settled, 1));
        Assert.Equal(new DateOnly(2026, 9, 28), next.StatementCloseDate);
        Assert.Equal(new DateOnly(2026, 10, 8), next.PaymentDueDate);
    }

    [Fact]
    public void Settlement_DoesNotLeakCurrentStatementPaymentPlanIntoNextProjection()
    {
        // CurrentStatementPaymentPlan asks for Full payment of the settled
        // statement. Post-settlement, the card's own PaymentStrategy
        // (Minimum) must drive the next statement instead.
        var card = ActualCard(15_000m, 6_000m, CurrentStatementPaymentMode.Full) with
        {
            PaymentStrategy = CreditCardPaymentStrategy.Minimum
        };
        var settled = _reconciler.Apply(
            card,
            new DateOnly(2026, 9, 7),
            15_000m,
            0.05m);

        var next = Assert.Single(_calculator.Project(settled, 1));

        Assert.Equal(
            CreditCardPaymentResolution.GeneralStrategy,
            next.PaymentResolution);
        Assert.Equal(CreditCardPaymentType.Minimum, next.AppliedPaymentType);
    }

    [Fact]
    public void Settlement_DropsChargesAlreadyReflectedInActualStatement()
    {
        // Bank shifted the real close to Aug28 instead of the naive
        // calendar day (25). A charge posted Aug26 falls before the real
        // close and is therefore already included in StatementAmount - it
        // must not survive settlement, or a later projection would count
        // it a second time.
        var card = ActualCard(15_000m, 6_000m, CurrentStatementPaymentMode.Full) with
        {
            CurrentStatement = new CreditCardStatement
            {
                StatementDate = new DateOnly(2026, 8, 28),
                DueDate = new DateOnly(2026, 9, 7),
                StatementAmount = 15_000m,
                MinimumPaymentAmount = 6_000m,
                NextStatementDate = new DateOnly(2026, 9, 28),
                NextDueDate = new DateOnly(2026, 10, 8)
            },
            Charges =
            [
                new CardCharge
                {
                    PostingDate = new DateOnly(2026, 8, 26),
                    Amount = 1_000m
                }
            ]
        };

        var settled = _reconciler.Apply(
            card,
            new DateOnly(2026, 9, 7),
            15_000m,
            0.05m);

        Assert.Empty(settled.Charges);
    }

    [Fact]
    public void Settlement_RetainsChargesPostedAfterActualStatement()
    {
        var card = ActualCard(15_000m, 6_000m, CurrentStatementPaymentMode.Full) with
        {
            Charges =
            [
                new CardCharge
                {
                    PostingDate = new DateOnly(2026, 9, 10),
                    Amount = 1_000m
                }
            ]
        };

        var settled = _reconciler.Apply(
            card,
            new DateOnly(2026, 9, 7),
            15_000m,
            0.05m);

        var remaining = Assert.Single(settled.Charges);
        Assert.Equal(new DateOnly(2026, 9, 10), remaining.PostingDate);

        var next = Assert.Single(_calculator.Project(settled, 1));
        Assert.Equal(1_000m, next.NewCharges);
    }

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
