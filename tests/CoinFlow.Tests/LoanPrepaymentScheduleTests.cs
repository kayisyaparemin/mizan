using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Tests;

/// <summary>
/// Erken ödeme olaylarının kredi üstünde oynatılması. Beklenen değerler
/// aynı kurallarla bağımsız hesaplandı: 100.000 TL, aylık %3, 12 × 10.046,21.
/// </summary>
public sealed class LoanPrepaymentScheduleTests
{
    private static readonly LoanPaymentScheduleBuilder Builder =
        TestFactory.LoanScheduleBuilder();

    private static readonly DateOnly SixthInstallment = new(2027, 2, 15);

    private static Loan ReferenceLoan() => new()
    {
        Name = "Referans",
        Bank = "Test",
        MonthlyPayment = 10_046.21m,
        PaymentDay = 15,
        NextPaymentDate = new DateOnly(2026, 9, 15),
        RemainingInstallmentCount = 12,
        RemainingDebt = 100_000m
    };

    private static LoanPrepayment Event(
        Loan loan,
        LoanPrepaymentMode mode,
        DateOnly date,
        decimal? principal = null) => new()
    {
        LoanId = loan.Id,
        Date = date,
        Mode = mode,
        PrincipalAmount = principal
    };

    [Fact]
    public void WithoutEvents_TheScheduleIsThePlainInstallments()
    {
        var replay = Builder.Replay(ReferenceLoan(), []);

        Assert.Equal(12, replay.Payments.Count);
        Assert.All(replay.Payments, x =>
            Assert.Equal(LoanPaymentKind.Installment, x.Kind));
        Assert.Equal(120_554.52m, replay.Total);
        Assert.True(replay.Payments[^1].IsFinal);
    }

    /// <summary>
    /// Taksit gününde kapatma: o günün taksiti önce ödenir, sonra kalan
    /// anapara kapatılır; sonraki taksitlerin hiçbiri kalmaz.
    /// </summary>
    [Fact]
    public void EarlyClosureOnAnInstallmentDay_RemovesEveryLaterInstallment()
    {
        var loan = ReferenceLoan();
        var closure = Event(loan, LoanPrepaymentMode.FullClosure, SixthInstallment);

        var replay = Builder.Replay(loan, [closure]);

        Assert.Equal(7, replay.Payments.Count);
        Assert.Equal(6, replay.Payments.Count(x =>
            x.Kind == LoanPaymentKind.Installment));
        var payoff = replay.Payments[^1];
        Assert.Equal(LoanPaymentKind.EarlyClosure, payoff.Kind);
        Assert.Equal(closure.Id, payoff.SourceId);
        Assert.True(payoff.IsFinal);
        Assert.Equal(54_422.24m, payoff.Amount);
        Assert.False(replay.StateAfterEvent[closure.Id].IsActive);
        Assert.Equal(0, replay.StateAfterEvent[closure.Id].RemainingInstallmentCount);
    }

    [Fact]
    public void EarlyClosureBetweenInstallments_PaysTheAccruedInterest()
    {
        var loan = ReferenceLoan();
        var replay = Builder.Replay(loan,
        [
            Event(loan, LoanPrepaymentMode.FullClosure, new DateOnly(2027, 2, 25))
        ]);

        Assert.Equal(54_966.46m, replay.Payments[^1].Amount);
    }

    /// <summary>
    /// Vade kısaltma: taksit aynı, 6 yerine 4 taksit kalır, son taksit küçülür.
    /// </summary>
    [Fact]
    public void ReduceTerm_KeepsTheInstallmentAndShortensTheLoan()
    {
        var loan = ReferenceLoan();
        var prepayment = Event(
            loan,
            LoanPrepaymentMode.ReduceTerm,
            SixthInstallment,
            20_000m);

        var replay = Builder.Replay(loan, [prepayment]);

        var after = replay.Payments
            .SkipWhile(x => x.Kind != LoanPaymentKind.PartialPrepayment)
            .Skip(1)
            .ToArray();
        Assert.Equal(20_000m, replay.Payments.Single(x =>
            x.Kind == LoanPaymentKind.PartialPrepayment).Amount);
        Assert.Equal(4, after.Length);
        Assert.All(after[..^1], x => Assert.Equal(10_046.21m, x.Amount));
        Assert.Equal(6_759.15m, after[^1].Amount);
        Assert.Equal(new DateOnly(2027, 6, 15), after[^1].Date);

        var state = replay.StateAfterEvent[prepayment.Id];
        Assert.Equal(4, state.RemainingInstallmentCount);
        Assert.Equal(6_759.15m, state.FinalPaymentAmount);
        Assert.Equal(34_422.24m, state.RemainingDebt);
        Assert.Equal(new DateOnly(2027, 3, 15), state.NextPaymentDate);
    }

    [Fact]
    public void ReduceInstallment_KeepsTheTermAndLowersTheInstallment()
    {
        var loan = ReferenceLoan();
        var prepayment = Event(
            loan,
            LoanPrepaymentMode.ReduceInstallment,
            SixthInstallment,
            20_000m);

        var replay = Builder.Replay(loan, [prepayment]);

        var after = replay.Payments
            .SkipWhile(x => x.Kind != LoanPaymentKind.PartialPrepayment)
            .Skip(1)
            .ToArray();
        Assert.Equal(6, after.Length);
        Assert.All(after, x => Assert.Equal(6_354.26m, x.Amount, 0));
        Assert.Equal(6_354.26m, replay.StateAfterEvent[prepayment.Id].MonthlyPayment);
    }

    /// <summary>
    /// Vade kısaltmadan sonra kanonik kredi küçük son taksiti taşır; faiz
    /// ondan yine doğru türetilmeli, yoksa bir sonraki erken ödeme yanlış olur.
    /// </summary>
    [Fact]
    public void LoanStateAfterReduceTerm_StillYieldsTheSameRate()
    {
        var loan = ReferenceLoan();
        var prepayment = Event(
            loan,
            LoanPrepaymentMode.ReduceTerm,
            SixthInstallment,
            20_000m);
        var state = Builder.Replay(loan, [prepayment])
            .StateAfterEvent[prepayment.Id];

        var amortization = new LoanAmortizationCalculator(
                new LoanScheduleCalculator())
            .Analyze(state)
            .Amortization;

        Assert.NotNull(amortization);
        Assert.Equal(0.03m, amortization!.MonthlyRate, 4);
    }

    [Fact]
    public void EveryPrepayment_LowersTheTotalPaid()
    {
        var loan = ReferenceLoan();
        var baseline = Builder.Replay(loan, []).Total;

        foreach (var mode in Enum.GetValues<LoanPrepaymentMode>())
        {
            var replay = Builder.Replay(loan,
            [
                Event(loan, mode, SixthInstallment, 20_000m)
            ]);
            Assert.True(replay.Total < baseline, $"{mode} toplamı düşürmedi.");
        }
    }

    /// <summary>
    /// Faizi türetilemeyen kredinin olayları oynatılamaz; taksitler olduğu
    /// gibi kalır ve bu durum işaretlenir — sessizce yanlış hesaplanmaz.
    /// </summary>
    [Fact]
    public void UnquotableLoan_KeepsItsInstallmentsAndIsFlagged()
    {
        var loan = ReferenceLoan() with { RemainingDebt = null };

        var replay = Builder.Replay(loan,
        [
            Event(loan, LoanPrepaymentMode.FullClosure, SixthInstallment)
        ]);

        Assert.True(replay.IgnoredBecauseUnquotable);
        Assert.Equal(12, replay.Payments.Count);
    }

    [Fact]
    public void PartialAtOrAbovePrincipal_IsRejected()
    {
        var loan = ReferenceLoan();

        var error = Assert.Throws<InvalidOperationException>(() =>
            Builder.Validate(loan, [],
                Event(loan, LoanPrepaymentMode.ReduceTerm, SixthInstallment, 60_000m)));

        Assert.Contains("erken kapamayı seç", error.Message);
    }

    [Fact]
    public void PrepaymentAfterTheLastInstallment_IsRejected()
    {
        var loan = ReferenceLoan();

        Assert.Throws<InvalidOperationException>(() =>
            Builder.Validate(loan, [],
                Event(loan, LoanPrepaymentMode.FullClosure, new DateOnly(2027, 8, 15))));
    }

    [Fact]
    public void SecondClosureOfTheSameLoan_IsRejected()
    {
        var loan = ReferenceLoan();
        var first = Event(loan, LoanPrepaymentMode.FullClosure, SixthInstallment);

        Assert.Throws<InvalidOperationException>(() =>
            Builder.Validate(loan, [first],
                Event(loan, LoanPrepaymentMode.FullClosure, new DateOnly(2027, 3, 15))));
    }

    [Fact]
    public void PrepaymentOnALoanWithoutPrincipal_IsRejectedWithGuidance()
    {
        var loan = ReferenceLoan() with { RemainingDebt = null };

        var error = Assert.Throws<InvalidOperationException>(() =>
            Builder.Validate(loan, [],
                Event(loan, LoanPrepaymentMode.FullClosure, SixthInstallment)));

        Assert.Contains("kalan anaparası", error.Message);
    }

    [Fact]
    public void Obligations_CarryThePrepaymentIdentity()
    {
        var loan = ReferenceLoan();
        var closure = Event(loan, LoanPrepaymentMode.FullClosure, SixthInstallment);
        var calculator = new MandatoryPaymentCalculator(
            Builder,
            new ScheduledPaymentCalculator());

        var items = calculator.BuildObligations([loan], [closure], [], []);

        var payoff = Assert.Single(items, x => x.PaymentId == closure.Id);
        Assert.Equal(ObligationType.Loan, payoff.Type);
        Assert.EndsWith("erken kapama", payoff.Name);
        Assert.DoesNotContain(items, x =>
            x.PaymentId == loan.Id && x.DueDate > SixthInstallment);
    }
}
