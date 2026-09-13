using System.Globalization;
using CoinFlow.Application.Abstractions;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Services;

/// <summary>Bir kredinin bugünkü hâli: faizi, anaparası, kapatma bedeli.</summary>
public sealed record LoanPayoffOverview(
    Loan Loan,
    LoanAnalysis Analysis,
    LoanPayoffQuote? Today)
{
    public string? IssueMessage =>
        LoanPayoffService.DescribeIssue(Analysis.Issue);
}

/// <param name="Amount">
/// Ödenecek tutar; kredi o tarihe kadar bitiyorsa ya da hesaplanamıyorsa null.
/// </param>
public sealed record PlannedLoanPrepayment(
    Loan Loan,
    LoanPrepayment Prepayment,
    decimal? Amount,
    bool IsUnquotable);

/// <summary>
/// Kredi erken kapama ve ara ödemenin uygulama katmanı.
/// </summary>
/// <remarks>
/// Kredinin kalan anaparası ya da bankanın tarihli kapatma tutarı faizin
/// tek kaynağıdır (<see cref="LoanAmortizationCalculator"/>). Bu servis
/// kaydetmeden önce veriyi doğrular ki üstüne kurulan her rakam anlamlı olsun.
/// </remarks>
public sealed class LoanPayoffService(
    IClock clock,
    LoanAmortizationCalculator calculator,
    LoanPaymentScheduleBuilder scheduleBuilder)
{
    private static readonly CultureInfo TurkishCulture =
        CultureInfo.GetCultureInfo("tr-TR");

    public IReadOnlyList<LoanPayoffOverview> Describe(IEnumerable<Loan> loans) =>
        loans
            .Select(Describe)
            .ToArray();

    public LoanPayoffOverview Describe(Loan loan)
    {
        var analysis = calculator.Analyze(loan);
        var today = analysis.Amortization is { } amortization
            ? calculator.PayoffOn(loan, amortization, clock.Today)
            : null;
        return new LoanPayoffOverview(loan, analysis, today);
    }

    /// <summary>
    /// Uygulanmış erken ödemeler, her biri o günkü kredi durumundan hesaplanan
    /// tutarıyla. Tam kapamanın tutarı saklanmaz; anapara değiştikçe yeniden
    /// hesaplanır.
    /// </summary>
    public IReadOnlyList<PlannedLoanPrepayment> DescribePrepayments(
        FinancialPlan plan) =>
        plan.Loans
            .SelectMany(loan =>
            {
                var replay = scheduleBuilder.Replay(loan, plan.LoanPrepayments);
                return plan.LoanPrepayments
                    .Where(x => x.LoanId == loan.Id)
                    .Select(prepayment => new PlannedLoanPrepayment(
                        loan,
                        prepayment,
                        replay.Payments
                            .Where(x => x.SourceId == prepayment.Id)
                            .Select(x => (decimal?)x.Amount)
                            .FirstOrDefault(),
                        replay.IgnoredBecauseUnquotable));
            })
            .OrderBy(x => x.Prepayment.Date)
            .ToArray();

    /// <summary>
    /// Kaydetmeden önce kredi verisini doğrular. Banka tutarı verilmişse
    /// otoritedir: anapara ondan çözülür ve <see cref="Loan.RemainingDebt"/>'e
    /// yazılır (K2).
    /// </summary>
    public Loan PrepareForSave(Loan loan)
    {
        if (loan.EarlyClosureAmount is null or <= 0m)
        {
            loan = loan with
            {
                EarlyClosureAmount = null,
                EarlyClosureAmountAsOf = null
            };
            return ValidatePrincipal(loan);
        }

        if (loan.EarlyClosureAmountAsOf is not DateOnly asOf)
        {
            throw new InvalidOperationException(
                "Bankanın kapatma tutarını hangi gün aldığını seç.");
        }

        if (asOf > clock.Today)
        {
            throw new InvalidOperationException(
                "Kapatma tutarının tarihi bugünden sonra olamaz.");
        }

        var previousDue = LoanAmortizationCalculator.PreviousDueDate(loan);
        if (asOf < previousDue)
        {
            throw new InvalidOperationException(
                $"Kapatma tutarı son ödenen taksitten " +
                $"({previousDue.ToString("dd.MM.yyyy", TurkishCulture)}) " +
                "sonra alınmış olmalı. Güncel tutarı bankandan al.");
        }

        var analysis = calculator.Analyze(loan);
        if (analysis.Amortization is not
            {
                Source: LoanRateSource.BankQuote
            } amortization)
        {
            throw new InvalidOperationException(
                "Bu kapatma tutarı taksit tutarı ve kalan taksit sayısıyla " +
                "uyuşmuyor. Tutarı ve taksit bilgilerini kontrol et.");
        }

        return loan with { RemainingDebt = amortization.Principal };
    }

    private Loan ValidatePrincipal(Loan loan)
    {
        if (loan.RemainingDebt is null)
        {
            return loan;
        }

        var analysis = calculator.Analyze(loan);
        return analysis.Issue switch
        {
            LoanAnalysisIssue.PrincipalNotBelowInstallments =>
                throw new InvalidOperationException(
                    "Kalan anapara, kalan taksitlerin toplamından " +
                    $"({Money(loan.RemainingInstallmentTotal)}) " +
                    "küçük olmalı. Bankanın gösterdiği kalan borç taksitlerin " +
                    "toplamıysa bu alanı boş bırakıp kapatma tutarını gir."),
            LoanAnalysisIssue.ImplausibleRate =>
                throw new InvalidOperationException(
                    "Bu anaparayla kredinin aylık faizi gerçekçi olmayan " +
                    "bir değere çıkıyor. Kalan anaparayı ya da bankanın " +
                    "kapatma tutarını kontrol et."),
            _ => loan
        };
    }

    public static string? DescribeIssue(LoanAnalysisIssue issue) => issue switch
    {
        LoanAnalysisIssue.MissingPrincipal =>
            "Kapatma tutarı için kalan anaparayı ya da bankanın kapatma tutarını gir.",
        LoanAnalysisIssue.PrincipalNotBelowInstallments =>
            "Kalan anapara kalan taksitlerin toplamından küçük olmalı; bu alana toplam borç girilmiş olabilir.",
        LoanAnalysisIssue.ImplausibleRate =>
            "Bu kredinin anaparası güncel görünmüyor; bankandan kapatma tutarını gir.",
        _ => null
    };

    private static string Money(decimal value) =>
        $"{value.ToString("N0", TurkishCulture)} TL";
}
