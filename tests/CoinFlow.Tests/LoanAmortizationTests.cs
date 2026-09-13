using CoinFlow.Application.Services;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;
using CoinFlow.Infrastructure.Persistence;
using SQLite;

namespace CoinFlow.Tests;

/// <summary>
/// Kredinin faizi, anaparası ve kapatma bedeli. Erken kapama ve ara ödeme
/// bu hesabın üstüne kurulur; buradaki her rakam yanlışsa üstündeki öneri de
/// yanlış olur.
/// </summary>
public sealed class LoanAmortizationTests
{
    private static readonly LoanAmortizationCalculator Calculator =
        new(new LoanScheduleCalculator());

    /// <summary>
    /// Ders kitabı kredisi: 100.000 TL, aylık %3, 12 taksit → taksit
    /// 10.046,21. Beklenen değerler bağımsız annüite formülüyle hesaplandı.
    /// </summary>
    private static Loan ReferenceLoan(LoanKind kind = LoanKind.Consumer) => new()
    {
        Name = "Referans",
        Bank = "Test",
        MonthlyPayment = 10_046.21m,
        PaymentDay = 15,
        NextPaymentDate = new DateOnly(2026, 9, 15),
        RemainingInstallmentCount = 12,
        RemainingDebt = 100_000m,
        Kind = kind
    };

    [Fact]
    public void RateIsDerivedFromTheInstallment()
    {
        var analysis = Calculator.Analyze(ReferenceLoan());

        var amortization = Assert.IsType<LoanAmortization>(
            analysis.Amortization);
        Assert.Equal(LoanAnalysisIssue.None, analysis.Issue);
        Assert.Equal(0.03m, amortization.MonthlyRate, 5);
        Assert.Equal(LoanRateSource.RemainingPrincipal, amortization.Source);
        Assert.Equal(new DateOnly(2026, 8, 15), amortization.PreviousDueDate);
        Assert.Equal(20_554.52m, amortization.RemainingInterest);
    }

    [Theory]
    [InlineData(190_188, 14_501.23, 22, 0.0504)]
    [InlineData(55_777, 7_374.59, 9, 0.0363)]
    public void CanonicalLoans_HaveRealisticRates(
        decimal principal,
        decimal payment,
        int count,
        decimal expected)
    {
        Assert.Equal(
            expected,
            LoanAmortizationCalculator.SolveMonthlyRate(
                principal,
                payment,
                count),
            4);
    }

    [Fact]
    public void PrincipalFollowsTheAmortizationTable()
    {
        var amortization = Calculator.Analyze(ReferenceLoan()).Amortization!;

        Assert.Equal(78_220.88m,
            LoanAmortizationCalculator.PrincipalAfter(amortization, 3), 0);
        Assert.Equal(54_422.24m,
            LoanAmortizationCalculator.PrincipalAfter(amortization, 6), 0);
        Assert.Equal(0m,
            LoanAmortizationCalculator.PrincipalAfter(amortization, 12));
    }

    /// <summary>
    /// v1.9.0 öncesi regresyon: reconciliation taksitin tamamını düşüyordu.
    /// Garanti 12 taksit sonra 111.758 olmalı; eski kod 16.173 yazıyordu.
    /// </summary>
    [Fact]
    public void PayingInstallments_ReducesOnlyThePrincipalPortion()
    {
        var loan = new Loan
        {
            Name = "borç kapama",
            Bank = "Garanti BBVA",
            MonthlyPayment = 14_501.23m,
            PaymentDay = 7,
            NextPaymentDate = new DateOnly(2026, 9, 7),
            RemainingInstallmentCount = 22,
            RemainingDebt = 190_188m
        };

        for (var paid = 0; paid < 12; paid++)
        {
            var amortization = Calculator.Analyze(loan).Amortization!;
            loan = loan with
            {
                RemainingDebt = LoanAmortizationCalculator
                    .PrincipalAfterPayment(amortization, loan.MonthlyPayment),
                RemainingInstallmentCount = loan.RemainingInstallmentCount - 1,
                NextPaymentDate = loan.NextPaymentDate.AddMonths(1)
            };
        }

        Assert.Equal(111_758m, loan.RemainingDebt!.Value, 0);
        // Faiz yol boyunca sabit kalmalı; kayma birikmiş yuvarlama demektir.
        Assert.Equal(
            0.0504m,
            Calculator.Analyze(loan).Amortization!.MonthlyRate,
            4);
    }

    [Fact]
    public void TotalRemainingDebtInsteadOfPrincipal_IsRejected()
    {
        var loan = ReferenceLoan() with
        {
            RemainingDebt = 10_046.21m * 12
        };

        var analysis = Calculator.Analyze(loan);

        Assert.Null(analysis.Amortization);
        Assert.Equal(
            LoanAnalysisIssue.PrincipalNotBelowInstallments,
            analysis.Issue);
    }

    /// <summary>
    /// Eski reconciliation'ın bıraktığı anapara: 10 taksit kalmışken 16.173.
    /// Buradan aylık %89 faiz çıkar — bir kredi değil, bayat veridir.
    /// </summary>
    [Fact]
    public void StalePrincipal_IsFlaggedInsteadOfQuoted()
    {
        var loan = new Loan
        {
            Name = "borç kapama",
            Bank = "Garanti BBVA",
            MonthlyPayment = 14_501.23m,
            PaymentDay = 7,
            NextPaymentDate = new DateOnly(2027, 9, 7),
            RemainingInstallmentCount = 10,
            RemainingDebt = 16_173m
        };

        var analysis = Calculator.Analyze(loan);

        Assert.Null(analysis.Amortization);
        Assert.Equal(LoanAnalysisIssue.ImplausibleRate, analysis.Issue);
    }

    [Fact]
    public void MissingPrincipal_IsReported()
    {
        var analysis = Calculator.Analyze(
            ReferenceLoan() with { RemainingDebt = null });

        Assert.Equal(LoanAnalysisIssue.MissingPrincipal, analysis.Issue);
    }

    /// <summary>
    /// Taksit gününde kapatınca işleyen faiz yoktur: kapatma tutarı o günkü
    /// anaparadır, tasarruf ödenmeyecek taksitlerle farkıdır.
    /// </summary>
    [Fact]
    public void PayoffOnAnInstallmentDay_HasNoAccruedInterest()
    {
        var loan = ReferenceLoan();
        var amortization = Calculator.Analyze(loan).Amortization!;

        var quote = Calculator.PayoffOn(
            loan,
            amortization,
            new DateOnly(2027, 2, 15));

        Assert.Equal(6, quote.InstallmentsPaidBefore);
        Assert.Equal(6, quote.InstallmentsRemoved);
        Assert.Equal(0m, quote.AccruedInterest);
        Assert.Equal(0m, quote.Fee);
        Assert.Equal(54_422.24m, quote.Amount, 0);
        Assert.Equal(60_277.26m, quote.RemovedInstallmentTotal);
        Assert.Equal(5_855.02m, quote.InterestSaving, 0);
    }

    [Fact]
    public void PayoffBetweenInstallments_AddsAccruedInterest()
    {
        var loan = ReferenceLoan();
        var amortization = Calculator.Analyze(loan).Amortization!;

        var quote = Calculator.PayoffOn(
            loan,
            amortization,
            new DateOnly(2027, 2, 25));

        // 54.422,24 × %3 × 10 ÷ 30
        Assert.Equal(544.22m, quote.AccruedInterest, 0);
        Assert.Equal(6, quote.InstallmentsRemoved);
    }

    [Fact]
    public void PayoffAfterTheLastInstallment_HasNothingToClose()
    {
        var loan = ReferenceLoan();
        var amortization = Calculator.Analyze(loan).Amortization!;

        var quote = Calculator.PayoffOn(
            loan,
            amortization,
            new DateOnly(2027, 9, 1));

        Assert.False(quote.HasAnythingToClose);
        Assert.Equal(0m, quote.Amount);
    }

    /// <summary>6502 md. 27 ve md. 37.</summary>
    [Theory]
    [InlineData(LoanKind.Consumer, 60, 0)]
    [InlineData(LoanKind.HousingVariable, 60, 0)]
    [InlineData(LoanKind.HousingFixed, 36, 0.01)]
    [InlineData(LoanKind.HousingFixed, 37, 0.02)]
    public void PrepaymentFee_FollowsTheLaw(
        LoanKind kind,
        int remaining,
        decimal expected)
    {
        Assert.Equal(
            expected,
            LoanAmortizationCalculator.PrepaymentFeeRate(kind, remaining));
    }

    [Fact]
    public void FixedRateHousingPayoff_IncludesTheFee()
    {
        var loan = ReferenceLoan(LoanKind.HousingFixed);
        var amortization = Calculator.Analyze(loan).Amortization!;

        var quote = Calculator.PayoffOn(
            loan,
            amortization,
            new DateOnly(2027, 2, 15));

        Assert.Equal(544.22m, quote.Fee, 0);
        Assert.Equal(quote.Principal + quote.Fee, quote.Amount);
    }

    /// <summary>
    /// Bankanın tarihli kapatma tutarı faizi ve anaparayı birlikte çözer.
    /// 25.09'da alınan 93.883,33 TL: 15.09 taksiti ödenmiş, 10 gün faiz işlemiş.
    /// </summary>
    [Fact]
    public void BankQuote_CalibratesRateAndPrincipal()
    {
        var loan = ReferenceLoan() with
        {
            RemainingDebt = null,
            EarlyClosureAmount = 93_883.33m,
            EarlyClosureAmountAsOf = new DateOnly(2026, 9, 25)
        };

        var amortization = Calculator.Analyze(loan).Amortization!;

        Assert.Equal(LoanRateSource.BankQuote, amortization.Source);
        Assert.Equal(0.03m, amortization.MonthlyRate, 4);
        Assert.Equal(100_000m, amortization.Principal, 0);
    }

    [Fact]
    public void BankQuote_WinsOverTheEnteredPrincipal()
    {
        var loan = ReferenceLoan() with
        {
            RemainingDebt = 90_000m,
            EarlyClosureAmount = 93_883.33m,
            EarlyClosureAmountAsOf = new DateOnly(2026, 9, 25)
        };

        var amortization = Calculator.Analyze(loan).Amortization!;

        Assert.Equal(LoanRateSource.BankQuote, amortization.Source);
        Assert.Equal(100_000m, amortization.Principal, 0);
    }

    [Fact]
    public void QuoteOlderThanTheLastPaidInstallment_IsIgnored()
    {
        var loan = ReferenceLoan() with
        {
            EarlyClosureAmount = 93_883.33m,
            EarlyClosureAmountAsOf = new DateOnly(2026, 8, 1)
        };

        var amortization = Calculator.Analyze(loan).Amortization!;

        Assert.Equal(LoanRateSource.RemainingPrincipal, amortization.Source);
    }

    [Fact]
    public void Saving_TotalDebtAsPrincipal_IsRejectedWithGuidance()
    {
        var service = PayoffService(new DateOnly(2026, 9, 1));

        var error = Assert.Throws<InvalidOperationException>(() =>
            service.PrepareForSave(ReferenceLoan() with
            {
                RemainingDebt = 130_000m
            }));

        Assert.Contains("kalan taksitlerin toplamından", error.Message);
    }

    [Fact]
    public void Saving_WithABankQuote_WritesTheSolvedPrincipal()
    {
        var service = PayoffService(new DateOnly(2026, 9, 25));

        var saved = service.PrepareForSave(ReferenceLoan() with
        {
            RemainingDebt = null,
            EarlyClosureAmount = 93_883.33m,
            EarlyClosureAmountAsOf = new DateOnly(2026, 9, 25)
        });

        Assert.Equal(100_000m, saved.RemainingDebt!.Value, 0);
    }

    [Fact]
    public void Saving_AQuoteFromTheFuture_IsRejected()
    {
        var service = PayoffService(new DateOnly(2026, 9, 20));

        Assert.Throws<InvalidOperationException>(() =>
            service.PrepareForSave(ReferenceLoan() with
            {
                EarlyClosureAmount = 93_883.33m,
                EarlyClosureAmountAsOf = new DateOnly(2026, 9, 25)
            }));
    }

    [Fact]
    public void Saving_AQuoteOlderThanTheLastInstallment_IsRejected()
    {
        var service = PayoffService(new DateOnly(2026, 9, 25));

        var error = Assert.Throws<InvalidOperationException>(() =>
            service.PrepareForSave(ReferenceLoan() with
            {
                EarlyClosureAmount = 93_883.33m,
                EarlyClosureAmountAsOf = new DateOnly(2026, 8, 1)
            }));

        Assert.Contains("son ödenen taksitten", error.Message);
    }

    [Fact]
    public void Saving_WithoutAQuote_ClearsAStrayQuoteDate()
    {
        var service = PayoffService(new DateOnly(2026, 9, 25));

        var saved = service.PrepareForSave(ReferenceLoan() with
        {
            EarlyClosureAmountAsOf = new DateOnly(2026, 9, 25)
        });

        Assert.Null(saved.EarlyClosureAmountAsOf);
    }

    /// <summary>
    /// Şema v14 iki sütun ekler. v13 veritabanındaki kredi tüketici kredisi
    /// olarak ve tarihsiz açılmalı.
    /// </summary>
    [Fact]
    public async Task LegacyLoanRows_OpenAsConsumerLoansWithoutQuoteDate()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"coinflow-loan-legacy-{Guid.NewGuid():N}.db3");
        SQLitePCL.Batteries_V2.Init();
        var legacy = new SQLiteAsyncConnection(path);
        await legacy.ExecuteAsync(
            """
            CREATE TABLE loans (
                Id varchar PRIMARY KEY NOT NULL,
                Name varchar,
                Bank varchar,
                MonthlyInstallment decimal,
                PaymentDay integer,
                StartDate varchar,
                EndDate varchar,
                InstallmentCount integer,
                RemainingDebt decimal,
                EarlyClosureAmount decimal,
                IsActive integer
            )
            """);
        var id = Guid.NewGuid();
        await legacy.ExecuteAsync(
            "INSERT INTO loans VALUES (?, 'Eski', 'Banka', 1000, 5, '2026-10-05', NULL, 10, 8000, 8500, 1)",
            id.ToString("D"));
        await legacy.CloseAsync();

        try
        {
            await using var store = new SqliteCoinFlowStore(
                path,
                false,
                new DateOnly(2026, 9, 13));
            var loan = Assert.Single(await store.GetLoansAsync());

            Assert.Equal(id, loan.Id);
            Assert.Equal(LoanKind.Consumer, loan.Kind);
            Assert.Null(loan.EarlyClosureAmountAsOf);
            Assert.Equal(8_000m, loan.RemainingDebt);

            await store.UpsertLoanAsync(loan with
            {
                Kind = LoanKind.HousingFixed,
                EarlyClosureAmountAsOf = new DateOnly(2026, 9, 10)
            });
            var reloaded = Assert.Single(await store.GetLoansAsync());
            Assert.Equal(LoanKind.HousingFixed, reloaded.Kind);
            Assert.Equal(
                new DateOnly(2026, 9, 10),
                reloaded.EarlyClosureAmountAsOf);
        }
        finally
        {
            foreach (var suffix in new[] { string.Empty, "-shm", "-wal" })
            {
                if (File.Exists(path + suffix))
                {
                    File.Delete(path + suffix);
                }
            }
        }
    }

    private static LoanPayoffService PayoffService(DateOnly today) =>
        new(new FixedClock(today), Calculator);

    private sealed class FixedClock(DateOnly today)
        : Application.Abstractions.IClock
    {
        public DateOnly Today { get; } = today;
        public DateTimeOffset UtcNow { get; } =
            new(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    }
}
