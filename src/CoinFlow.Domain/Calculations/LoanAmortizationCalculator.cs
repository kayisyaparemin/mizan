using CoinFlow.Domain.Models;

namespace CoinFlow.Domain.Calculations;

public enum LoanRateSource
{
    /// <summary>Faiz, kullanıcının girdiği kalan anaparadan türetildi.</summary>
    RemainingPrincipal,
    /// <summary>Faiz ve anapara, bankanın tarihli kapatma tutarından çözüldü.</summary>
    BankQuote
}

public enum LoanAnalysisIssue
{
    None,
    /// <summary>Kalan taksit yok ya da kredi pasif.</summary>
    Finished,
    /// <summary>Ne kalan anapara ne de kullanılabilir banka tutarı var.</summary>
    MissingPrincipal,
    /// <summary>
    /// Kalan anapara kalan taksitlerin toplamına eşit ya da büyük; faiz ≤ 0
    /// çıkar. Alana büyük olasılıkla toplam kalan borç girilmiş.
    /// </summary>
    PrincipalNotBelowInstallments,
    /// <summary>
    /// Türetilen faiz makul sınırın üstünde; anapara güncel değil. v1.9.0
    /// öncesi reconciliation taksitin tamamını düştüğü için bu durum gerçek
    /// veritabanlarında beklenir.
    /// </summary>
    ImplausibleRate
}

/// <summary>
/// Bir kredinin annüite hâli: <see cref="PreviousDueDate"/>'te son taksit
/// ödenmiş, geriye <see cref="RemainingInstallments"/> eşit taksit kalmış.
/// </summary>
/// <remarks>
/// <see cref="MonthlyRate"/> gerçek taksitten türediği için BSMV ve KKDF'yi
/// zaten içerir; ayrıca modellenmez.
/// </remarks>
public sealed record LoanAmortization(
    decimal MonthlyRate,
    decimal Principal,
    int RemainingInstallments,
    decimal MonthlyPayment,
    decimal FinalPayment,
    DateOnly PreviousDueDate,
    LoanRateSource Source)
{
    public decimal RemainingInstallmentTotal => RemainingInstallments < 1
        ? 0m
        : MonthlyPayment * (RemainingInstallments - 1) + FinalPayment;

    public decimal RemainingInterest =>
        Math.Max(0m, RemainingInstallmentTotal - Principal);
}

public sealed record LoanAnalysis(
    Loan Loan,
    LoanAmortization? Amortization,
    LoanAnalysisIssue Issue);

/// <summary>
/// Belirli bir günde krediyi kapatmanın bedeli. O güne kadar vadesi gelen
/// taksitlerin ödendiği varsayılır (vade günü dahil).
/// </summary>
public sealed record LoanPayoffQuote(
    DateOnly Date,
    int InstallmentsPaidBefore,
    decimal Principal,
    decimal AccruedInterest,
    decimal Fee,
    int InstallmentsRemoved,
    decimal RemovedInstallmentTotal)
{
    public decimal Amount => Principal + AccruedInterest + Fee;

    /// <summary>Ödenmeyecek taksitler eksi kapatma tutarı.</summary>
    public decimal InterestSaving => RemovedInstallmentTotal - Amount;

    public bool HasAnythingToClose => InstallmentsRemoved > 0;
}

/// <summary>
/// Kredinin faizini, anaparasını ve herhangi bir günkü kapatma tutarını
/// hesaplar.
/// </summary>
/// <remarks>
/// Kapatma tutarı = kalan anapara + son taksitten bu yana işleyen faiz
/// (<c>anapara × aylık faiz ÷ 30 × gün</c>) + varsa erken ödeme ücreti.
/// Ücret 6502 sayılı Kanun'a göre yalnız sabit faizli konut kredisinde
/// alınabilir (md. 37); tüketici kredisinde alınamaz (md. 27).
/// </remarks>
public sealed class LoanAmortizationCalculator(
    LoanScheduleCalculator scheduleCalculator)
{
    /// <summary>
    /// Bunun üstündeki aylık efektif faiz gerçek bir kredi değil, güncel
    /// olmayan anaparadır. 2026'da tüketici kredisinin vergili efektif maliyeti
    /// aylık ~%6,5'i geçmiyor.
    /// </summary>
    public const decimal MaxPlausibleMonthlyRate = 0.08m;

    public LoanAnalysis Analyze(Loan loan)
    {
        if (!loan.IsActive || loan.RemainingInstallmentCount < 1)
        {
            return new LoanAnalysis(loan, null, LoanAnalysisIssue.Finished);
        }

        var previousDue = PreviousDueDate(loan);
        if (FromBankQuote(loan, previousDue) is { } quoted)
        {
            return new LoanAnalysis(loan, quoted, LoanAnalysisIssue.None);
        }

        if (loan.RemainingDebt is not decimal principal || principal <= 0m)
        {
            return new LoanAnalysis(
                loan,
                null,
                LoanAnalysisIssue.MissingPrincipal);
        }

        if (principal >= loan.RemainingInstallmentTotal)
        {
            return new LoanAnalysis(
                loan,
                null,
                LoanAnalysisIssue.PrincipalNotBelowInstallments);
        }

        var rate = SolveMonthlyRate(
            principal,
            loan.MonthlyPayment,
            loan.RemainingInstallmentCount,
            loan.FinalPaymentAmount);
        if (rate > MaxPlausibleMonthlyRate)
        {
            return new LoanAnalysis(
                loan,
                null,
                LoanAnalysisIssue.ImplausibleRate);
        }

        return new LoanAnalysis(
            loan,
            new LoanAmortization(
                rate,
                principal,
                loan.RemainingInstallmentCount,
                loan.MonthlyPayment,
                loan.LastInstallmentAmount,
                previousDue,
                LoanRateSource.RemainingPrincipal),
            LoanAnalysisIssue.None);
    }

    /// <summary>
    /// <paramref name="installmentsPaid"/> taksit daha ödendikten sonra kalan
    /// anapara.
    /// </summary>
    public static decimal PrincipalAfter(
        LoanAmortization amortization,
        int installmentsPaid)
    {
        if (installmentsPaid <= 0)
        {
            return amortization.Principal;
        }

        if (installmentsPaid >= amortization.RemainingInstallments)
        {
            return 0m;
        }

        var rate = amortization.MonthlyRate;
        var growth = Pow(1m + rate, installmentsPaid);
        var balance = amortization.Principal * growth -
                      amortization.MonthlyPayment * (growth - 1m) / rate;
        return RoundMoney(Math.Max(0m, balance));
    }

    /// <summary>
    /// Bir taksit ödendikten sonraki anapara: yalnız anapara payı düşer (I17).
    /// Ödenen tutar o ayın faizinden azsa anapara büyür.
    /// </summary>
    public static decimal PrincipalAfterPayment(
        LoanAmortization amortization,
        decimal paidAmount)
    {
        var interest = amortization.Principal * amortization.MonthlyRate;
        return RoundMoney(Math.Max(
            0m,
            amortization.Principal - (paidAmount - interest)));
    }

    public LoanPayoffQuote PayoffOn(
        Loan loan,
        LoanAmortization amortization,
        DateOnly date)
    {
        var dates = scheduleCalculator.GetPaymentDates(loan);
        var paidBefore = dates.Count(x => x <= date);
        var removed = Math.Max(
            0,
            amortization.RemainingInstallments - paidBefore);
        if (removed == 0)
        {
            return new LoanPayoffQuote(date, paidBefore, 0m, 0m, 0m, 0, 0m);
        }

        var principal = PrincipalAfter(amortization, paidBefore);
        var lastPaid = paidBefore == 0
            ? amortization.PreviousDueDate
            : dates[paidBefore - 1];
        var days = Math.Max(0, date.DayNumber - lastPaid.DayNumber);
        var accrued = RoundMoney(
            principal * amortization.MonthlyRate * days / 30m);
        var fee = RoundMoney(principal * PrepaymentFeeRate(loan.Kind, removed));
        return new LoanPayoffQuote(
            date,
            paidBefore,
            principal,
            accrued,
            fee,
            removed,
            amortization.MonthlyPayment * (removed - 1) +
            amortization.FinalPayment);
    }

    /// <summary>6502 md. 27 ve md. 37'deki tavanlar.</summary>
    public static decimal PrepaymentFeeRate(
        LoanKind kind,
        int remainingInstallments) => kind switch
    {
        LoanKind.HousingFixed => remainingInstallments > 36 ? 0.02m : 0.01m,
        _ => 0m
    };

    public static DateOnly PreviousDueDate(Loan loan) =>
        CalendarRules.AddMonthsKeepingDay(
            loan.NextPaymentDate,
            -1,
            loan.PaymentDay);

    /// <summary>
    /// Banka tutarı o günkü taksit ödendikten sonra alınmış kabul edilir;
    /// <c>tutar = kalan anapara × (1 + faiz × gün ÷ 30)</c> ve
    /// <c>kalan anapara = taksit × annüite(faiz, kalan)</c> birlikte çözülür.
    /// </summary>
    private LoanAmortization? FromBankQuote(Loan loan, DateOnly previousDue)
    {
        if (loan.EarlyClosureAmount is not decimal amount ||
            amount <= 0m ||
            loan.EarlyClosureAmountAsOf is not DateOnly asOf ||
            asOf < previousDue)
        {
            return null;
        }

        var dates = scheduleCalculator.GetPaymentDates(loan);
        var paidBefore = dates.Count(x => x <= asOf);
        var remaining = loan.RemainingInstallmentCount - paidBefore;
        var final = (double)loan.LastInstallmentAmount;
        if (remaining < 1 ||
            amount >= loan.MonthlyPayment * (remaining - 1) +
                      loan.LastInstallmentAmount)
        {
            return null;
        }

        var lastPaid = paidBefore == 0 ? previousDue : dates[paidBefore - 1];
        var days = Math.Max(0, asOf.DayNumber - lastPaid.DayNumber);
        var payment = (double)loan.MonthlyPayment;
        var rate = Bisect(
            (double)amount,
            r => Valuation(r, remaining, payment, final) *
                 (1d + r * days / 30d));
        if (rate > MaxPlausibleMonthlyRate)
        {
            return null;
        }

        var principal = RoundMoney((decimal)Valuation(
            (double)rate,
            loan.RemainingInstallmentCount,
            payment,
            final));
        return new LoanAmortization(
            rate,
            principal,
            loan.RemainingInstallmentCount,
            loan.MonthlyPayment,
            loan.LastInstallmentAmount,
            previousDue,
            LoanRateSource.BankQuote);
    }

    public static decimal SolveMonthlyRate(
        decimal principal,
        decimal payment,
        int count,
        decimal? finalPayment = null)
    {
        var monthly = (double)payment;
        var final = (double)(finalPayment ?? payment);
        return Bisect(
            (double)principal,
            r => Valuation(r, count, monthly, final));
    }

    /// <summary>
    /// Kalan taksitlerin bugünkü değeri: <paramref name="count"/> − 1 eşit
    /// taksit ve farklı olabilecek son taksit.
    /// </summary>
    public static double Valuation(
        double rate,
        int count,
        double payment,
        double finalPayment) =>
        count < 1
            ? 0d
            : payment * Annuity(rate, count - 1) +
              finalPayment * Math.Pow(1d + rate, -count);

    /// <summary>
    /// <paramref name="value"/>(r) = <paramref name="target"/> denkleminin
    /// (0, 1] aralığındaki kökü. Fonksiyon bu aralıkta azalandır; kök 1'in
    /// üstündeyse 1 döner ve çağıran makul sınırda eler.
    /// </summary>
    private static decimal Bisect(double target, Func<double, double> value)
    {
        const double upper = 1d;
        if (value(upper) > target)
        {
            return (decimal)upper;
        }

        var low = 0d;
        var high = upper;
        for (var iteration = 0; iteration < 200; iteration++)
        {
            var middle = (low + high) / 2d;
            if (value(middle) > target)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return decimal.Round((decimal)((low + high) / 2d), 10);
    }

    private static double Annuity(double rate, int count) =>
        rate <= 0d
            ? count
            : (1d - Math.Pow(1d + rate, -count)) / rate;

    private static decimal Pow(decimal value, int exponent)
    {
        var result = 1m;
        for (var index = 0; index < exponent; index++)
        {
            result *= value;
        }

        return result;
    }

    private static decimal RoundMoney(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
}
