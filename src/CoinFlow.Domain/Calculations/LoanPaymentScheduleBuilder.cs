using System.Globalization;
using CoinFlow.Domain.Models;

namespace CoinFlow.Domain.Calculations;

/// <summary>
/// Kredinin ödeme listesini, üstüne planlanmış erken ödemeleri oynatarak
/// üretir.
/// </summary>
/// <remarks>
/// <para>
/// Erken ödeme günü: o güne kadar (vade günü dahil) vadesi gelen taksitler
/// önce ödenir. Sonra anaparadan <c>X</c> düşer ve ödenen tutar
/// <c>X × (1 + faiz × gün ÷ 30) + ücret</c> olur; <c>gün</c> son taksitten
/// bu yana geçen süredir. Böylece <c>X</c>'in o günlere düşen faizi tahsil
/// edilir, kalan anaparanın faizi bir sonraki taksitte her zamanki gibi
/// ödenir — aynı faiz iki kez sayılmaz. Tam kapama <c>X = anapara</c>
/// hâlidir.
/// </para>
/// <para>
/// Vade kısaltmada taksit aynı kalır, son taksit küçülür. Taksit azaltmada
/// kalan sayı aynı kalır, yeni taksit annüiteden hesaplanır; kuruş farkı son
/// taksite biner.
/// </para>
/// </remarks>
public sealed class LoanPaymentScheduleBuilder(
    LoanScheduleCalculator scheduleCalculator,
    LoanAmortizationCalculator amortizationCalculator)
{
    private static readonly CultureInfo TurkishCulture =
        CultureInfo.GetCultureInfo("tr-TR");

    public LoanReplay Replay(
        Loan loan,
        IEnumerable<LoanPrepayment> prepayments)
    {
        var events = prepayments
            .Where(x => x.LoanId == loan.Id)
            .OrderBy(x => x.Date)
            .ThenBy(x => x.Mode == LoanPrepaymentMode.FullClosure)
            .ThenBy(x => x.Id)
            .ToArray();
        if (!loan.IsActive || loan.RemainingInstallmentCount < 1)
        {
            return Empty(loan);
        }

        if (events.Length == 0)
        {
            return Plain(loan, ignored: false);
        }

        if (amortizationCalculator.Analyze(loan).Amortization is not { } a)
        {
            return Plain(loan, ignored: true);
        }

        var dates = scheduleCalculator.GetPaymentDates(loan);
        var rate = a.MonthlyRate;
        var principal = a.Principal;
        var payment = a.MonthlyPayment;
        var final = a.FinalPayment;
        var count = a.RemainingInstallments;
        var index = 0;
        var lastPaid = a.PreviousDueDate;
        var payments = new List<LoanScheduledPayment>();
        var states = new Dictionary<Guid, Loan>();
        var principals = new Dictionary<Guid, decimal>();

        foreach (var prepayment in events)
        {
            if (prepayment.Date < a.PreviousDueDate)
            {
                continue;
            }

            while (index < count && dates[index] <= prepayment.Date)
            {
                var isLast = index == count - 1;
                payments.Add(new LoanScheduledPayment(
                    dates[index],
                    isLast ? final : payment,
                    LoanPaymentKind.Installment,
                    loan.Id,
                    isLast));
                principal = isLast
                    ? 0m
                    : RoundMoney(principal * (1m + rate) - payment);
                lastPaid = dates[index];
                index++;
            }

            if (index >= count || principal <= 0m)
            {
                break;
            }

            var remaining = count - index;
            var days = Math.Max(
                0,
                prepayment.Date.DayNumber - lastPaid.DayNumber);
            var closes = prepayment.Mode == LoanPrepaymentMode.FullClosure ||
                         prepayment.PrincipalAmount is null ||
                         prepayment.PrincipalAmount >= principal;
            var reduction = closes
                ? principal
                : Math.Max(0m, prepayment.PrincipalAmount!.Value);
            var fee = RoundMoney(
                reduction *
                LoanAmortizationCalculator.PrepaymentFeeRate(
                    loan.Kind,
                    remaining));
            var amount = RoundMoney(reduction * (1m + rate * days / 30m)) + fee;
            principals[prepayment.Id] = principal;

            if (closes)
            {
                payments.Add(new LoanScheduledPayment(
                    prepayment.Date,
                    amount,
                    LoanPaymentKind.EarlyClosure,
                    prepayment.Id,
                    true));
                states[prepayment.Id] = loan with
                {
                    RemainingInstallmentCount = 0,
                    RemainingDebt = loan.RemainingDebt is null ? null : 0m,
                    FinalPaymentAmount = null,
                    EarlyClosureAmount = null,
                    EarlyClosureAmountAsOf = null,
                    IsActive = false
                };
                return new LoanReplay(loan, payments, states, principals, false);
            }

            payments.Add(new LoanScheduledPayment(
                prepayment.Date,
                amount,
                LoanPaymentKind.PartialPrepayment,
                prepayment.Id,
                false));
            principal -= reduction;

            if (prepayment.Mode == LoanPrepaymentMode.ReduceTerm)
            {
                (remaining, final) = ShortenTerm(principal, rate, payment);
            }
            else
            {
                payment = RoundMoney(
                    principal * rate /
                    (1m - (decimal)Math.Pow(1d + (double)rate, -remaining)));
                final = LastInstallment(principal, rate, payment, remaining);
            }

            count = index + remaining;
            states[prepayment.Id] = loan with
            {
                NextPaymentDate = dates[index],
                RemainingInstallmentCount = remaining,
                MonthlyPayment = payment,
                FinalPaymentAmount = final == payment ? null : final,
                RemainingDebt = principal,
                EarlyClosureAmount = null,
                EarlyClosureAmountAsOf = null
            };
        }

        for (; index < count; index++)
        {
            var isLast = index == count - 1;
            payments.Add(new LoanScheduledPayment(
                dates[index],
                isLast ? final : payment,
                LoanPaymentKind.Installment,
                loan.Id,
                isLast));
        }

        return new LoanReplay(loan, payments, states, principals, false);
    }

    /// <summary>
    /// Yeni bir erken ödemenin anlamlı olup olmadığını, kredinin üstündeki
    /// diğer olaylarla birlikte denetler.
    /// </summary>
    public void Validate(
        Loan? loan,
        IReadOnlyList<LoanPrepayment> existing,
        LoanPrepayment prepayment)
    {
        if (loan is null || !loan.IsActive || loan.RemainingInstallmentCount < 1)
        {
            throw new InvalidOperationException(
                "Erken ödeme için aktif bir kredi seçmelisin.");
        }

        if (amortizationCalculator.Analyze(loan).Amortization is not { } a)
        {
            throw new InvalidOperationException(
                "Bu kredinin erken ödeme hesabı için kalan anaparası ya da " +
                "bankanın tarihli kapatma tutarı gerekli. Finansal Yapı'dan " +
                "krediyi düzenle.");
        }

        if (prepayment.Date < a.PreviousDueDate)
        {
            throw new InvalidOperationException(
                "Erken ödeme tarihi kredinin son ödenen taksitinden " +
                $"({Date(a.PreviousDueDate)}) önce olamaz.");
        }

        var earlierClosure = existing
            .Where(x => x.LoanId == loan.Id && x.Id != prepayment.Id)
            .Where(x => x.Mode == LoanPrepaymentMode.FullClosure)
            .OrderBy(x => x.Date)
            .FirstOrDefault();
        if (earlierClosure is not null &&
            (earlierClosure.Date <= prepayment.Date ||
             prepayment.Mode == LoanPrepaymentMode.FullClosure))
        {
            throw new InvalidOperationException(
                $"Bu kredi {Date(earlierClosure.Date)} tarihinde zaten " +
                "kapatılıyor.");
        }

        var earlier = existing
            .Where(x => x.LoanId == loan.Id &&
                        x.Id != prepayment.Id &&
                        x.Date <= prepayment.Date)
            .ToArray();
        var before = Replay(loan, earlier);
        var lastDate = before.LastPaymentDate;
        if (lastDate is null || prepayment.Date >= lastDate)
        {
            throw new InvalidOperationException(
                "Bu tarihte kredinin kapatılacak taksiti kalmıyor. Daha " +
                "erken bir tarih seç.");
        }

        if (prepayment.Mode == LoanPrepaymentMode.FullClosure)
        {
            return;
        }

        if (prepayment.PrincipalAmount is not decimal amount || amount <= 0m)
        {
            throw new InvalidOperationException(
                "Ara ödeme tutarı 0'dan büyük olmalı.");
        }

        // O günkü anaparayı bulmanın en kısa yolu: aynı gün tam kapamayı
        // oynatıp kapatılan anaparaya bakmak.
        var probe = Replay(
            loan,
            earlier.Append(prepayment with
            {
                Mode = LoanPrepaymentMode.FullClosure
            }));
        if (probe.PrincipalBeforeEvent.TryGetValue(
                prepayment.Id,
                out var principal) &&
            amount >= principal)
        {
            throw new InvalidOperationException(
                "Ara ödeme, o günkü kalan anaparadan " +
                $"({principal.ToString("N0", TurkishCulture)} TL) küçük " +
                "olmalı. Tamamını ödemek için erken kapamayı seç.");
        }
    }

    private LoanReplay Plain(Loan loan, bool ignored)
    {
        var dates = scheduleCalculator.GetPaymentDates(loan);
        var payments = dates
            .Select((date, index) =>
            {
                var isLast = index == dates.Count - 1;
                return new LoanScheduledPayment(
                    date,
                    isLast ? loan.LastInstallmentAmount : loan.MonthlyPayment,
                    LoanPaymentKind.Installment,
                    loan.Id,
                    isLast);
            })
            .ToArray();
        return new LoanReplay(
            loan,
            payments,
            new Dictionary<Guid, Loan>(),
            new Dictionary<Guid, decimal>(),
            ignored);
    }

    private static LoanReplay Empty(Loan loan) => new(
        loan,
        [],
        new Dictionary<Guid, Loan>(),
        new Dictionary<Guid, decimal>(),
        false);

    /// <summary>
    /// Taksit sabitken anaparayı bitiren taksit sayısı ve son taksit.
    /// </summary>
    private static (int Count, decimal Final) ShortenTerm(
        decimal principal,
        decimal rate,
        decimal payment)
    {
        var balance = principal;
        for (var count = 1; count <= 1_200; count++)
        {
            var due = balance * (1m + rate);
            if (due <= payment)
            {
                return (count, RoundMoney(due));
            }

            balance = due - payment;
        }

        throw new InvalidOperationException(
            "Taksit, kalan anaparanın faizini karşılamıyor.");
    }

    private static decimal LastInstallment(
        decimal principal,
        decimal rate,
        decimal payment,
        int count)
    {
        var balance = principal;
        for (var index = 1; index < count; index++)
        {
            balance = balance * (1m + rate) - payment;
        }

        return RoundMoney(Math.Max(0m, balance * (1m + rate)));
    }

    private static string Date(DateOnly date) =>
        date.ToString("dd.MM.yyyy", TurkishCulture);

    private static decimal RoundMoney(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
}
