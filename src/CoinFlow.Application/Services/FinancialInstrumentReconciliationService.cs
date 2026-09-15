using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Services;

public sealed record ReconciledFinancialInstruments(
    IReadOnlyList<Loan> Loans,
    IReadOnlyList<TemporaryPaymentPlan> PaymentPlans,
    IReadOnlyList<CreditCard> CreditCards,
    IReadOnlyList<PlannedLargeExpense> LargeExpenses,
    IReadOnlyList<Guid> RemovedLoanPrepaymentIds);

public sealed class FinancialInstrumentReconciliationService(
    CreditCardActualPaymentReconciler cardReconciler,
    LoanAmortizationCalculator loanCalculator,
    LoanPaymentScheduleBuilder loanScheduleBuilder)
{
    public ReconciledFinancialInstruments Apply(
        FinancialPlan data,
        IReadOnlyList<PeriodPlanPaymentLine> paymentLines,
        IReadOnlyList<ActualPayment> actualPayments,
        DateOnly newAnchor)
    {
        var lines = paymentLines.ToDictionary(x => x.Id);
        var loans = data.Loans.ToDictionary(x => x.Id);
        var paymentPlans = data.PaymentPlans.ToDictionary(x => x.Id);
        var cards = data.CreditCards.ToDictionary(x => x.Id);
        var largeExpenses = data.PlannedLargeExpenses.ToDictionary(x => x.Id);
        // Ödenmemiş yükümlülük yeni dönemin ilk gününe taşınır, checkpoint
        // gününe değil: yeni dönemin donmuş planı (checkpoint, sonraki
        // checkpoint] penceresini okur. Checkpoint gününe taşınan borç
        // projeksiyonda görünüp Ana Sayfa planına hiç girmiyordu; geçici ödeme
        // ve büyük gider bu yüzden hiçbir review'da kapatılamıyordu (I4).
        var carryDate = newAnchor.AddDays(1);
        var unpaidLoanIds = new HashSet<Guid>();
        var prepayments = data.LoanPrepayments.ToDictionary(x => x.Id);
        var removedPrepaymentIds = new HashSet<Guid>();

        // Aynı gün önce taksit, sonra erken ödeme: erken ödeme o günün
        // taksiti ödenmiş kredinin üstüne işlenir.
        foreach (var actual in actualPayments
                     .OrderBy(x => x.PlannedDate)
                     .ThenBy(x => lines.TryGetValue(
                                      x.PeriodPlanPaymentLineId,
                                      out var candidate) &&
                                  prepayments.ContainsKey(
                                      candidate.SourceEntityId)))
        {
            if (!lines.TryGetValue(
                    actual.PeriodPlanPaymentLineId,
                    out var line))
            {
                throw new InvalidOperationException(
                    "Planlanan ödeme satırı bulunamadı.");
            }

            var paid = actual.Status != ActualPaymentStatus.Unpaid &&
                       actual.ActualAmount > 0m;
            switch (actual.SourceType)
            {
                case PlanPaymentSourceType.Loan:
                    {
                        if (prepayments.TryGetValue(
                                line.SourceEntityId,
                                out var prepayment))
                        {
                            // Ödendiyse kanonik krediye işlenir; ödenmediyse
                            // gönüllü bir karardı ve iptal olur (K6). İkisinde
                            // de olay tüketilir.
                            removedPrepaymentIds.Add(prepayment.Id);
                            if (paid &&
                                loans.TryGetValue(prepayment.LoanId, out var owner) &&
                                loanScheduleBuilder
                                    .Replay(owner, [prepayment])
                                    .StateAfterEvent
                                    .TryGetValue(prepayment.Id, out var closed))
                            {
                                loans[owner.Id] = closed;
                            }

                            break;
                        }

                        if (!loans.TryGetValue(line.SourceEntityId, out var loan))
                        {
                            // Plan donduktan sonra geri alınmış bir erken ödeme:
                            // kanonik kayda işlenecek bir şey kalmadı.
                            break;
                        }
                        if (paid)
                        {
                            var remaining = Math.Max(
                                0,
                                loan.RemainingInstallmentCount - 1);
                            var paidLoan = loan with
                            {
                                NextPaymentDate = ResolveOutstandingDate(
                                    CalendarRules.AddMonthsKeepingDay(
                                        line.PlannedDate,
                                        1,
                                        loan.PaymentDay),
                                    newAnchor,
                                    carryDate),
                                RemainingInstallmentCount = remaining,
                                RemainingDebt = RemainingPrincipalAfter(
                                    loan,
                                    actual.ActualAmount,
                                    remaining),
                                IsActive = remaining > 0
                            };
                            loans[loan.Id] = DropStaleClosureQuote(paidLoan);
                        }
                        else if (loan.NextPaymentDate <= newAnchor)
                        {
                            // Ödenmeyen yükümlülük gelecek plandan kaybolmaz.
                            loans[loan.Id] = DropStaleClosureQuote(loan with
                            {
                                NextPaymentDate = carryDate
                            });
                        }
                        if (!paid)
                        {
                            unpaidLoanIds.Add(loan.Id);
                        }

                        break;
                    }

                case PlanPaymentSourceType.TemporaryPayment:
                case PlanPaymentSourceType.InstallmentPayment:
                case PlanPaymentSourceType.OtherScheduledPayment:
                    {
                        var parent = paymentPlans.Values.SingleOrDefault(x =>
                            x.Installments.Any(i => i.Id == line.SourceEntityId))
                            ?? throw new InvalidOperationException(
                                "Planlı ödeme kaydı bulunamadı.");
                        paymentPlans[parent.Id] = parent with
                        {
                            Installments = parent.Installments.Select(item =>
                                item.Id != line.SourceEntityId
                                    ? item
                                    : paid
                                        ? item with { IsPaid = true }
                                        : item.DueDate <= newAnchor
                                            ? item with { DueDate = carryDate }
                                            : item).ToArray()
                        };
                        break;
                    }

                case PlanPaymentSourceType.CreditCard:
                    {
                        var card = cards.GetValueOrDefault(line.SourceEntityId)
                            ?? throw new InvalidOperationException(
                                "Kredi kartı bulunamadı.");
                        cards[card.Id] = cardReconciler.Apply(
                            card,
                            line.PlannedDate,
                            paid ? actual.ActualAmount : 0m,
                            data.Settings.CreditCardCarryInterestRate);
                        break;
                    }

                case PlanPaymentSourceType.PlannedLargeExpense:
                    {
                        var expense = largeExpenses.GetValueOrDefault(
                            line.SourceEntityId)
                            ?? throw new InvalidOperationException(
                                "Planlı büyük ödeme bulunamadı.");
                        largeExpenses[expense.Id] = paid
                            ? expense with
                            {
                                Status = PlannedExpenseStatus.Completed
                            }
                            : expense.ExactDate <= newAnchor
                                ? expense with { ExactDate = carryDate }
                                : expense;
                        break;
                    }
            }
        }

        foreach (var loanId in unpaidLoanIds)
        {
            loans[loanId] = loans[loanId] with
            {
                NextPaymentDate = carryDate
            };
        }

        paymentPlans = paymentPlans.ToDictionary(
            x => x.Key,
            x => x.Value with
            {
                Installments = x.Value.Installments.Select(item =>
                    !item.IsPaid && item.DueDate <= newAnchor
                        ? item with { DueDate = carryDate }
                        : item).ToArray()
            });
        largeExpenses = largeExpenses.ToDictionary(
            x => x.Key,
            x => x.Value.Status == PlannedExpenseStatus.Planned &&
                 x.Value.ExactDate <= newAnchor
                ? x.Value with { ExactDate = carryDate }
                : x.Value);

        // Checkpoint'i geçmiş ama plan satırına hiç düşmemiş erken ödeme de
        // gerçekleşmemiş bir karardır.
        foreach (var stale in data.LoanPrepayments.Where(x =>
                     x.Date <= newAnchor))
        {
            removedPrepaymentIds.Add(stale.Id);
        }

        return new ReconciledFinancialInstruments(
            loans.Values.OrderBy(x => x.NextPaymentDate).ToArray(),
            paymentPlans.Values.OrderBy(x => x.Name).ToArray(),
            cards.Values.OrderBy(x => x.Name).ToArray(),
            largeExpenses.Values.OrderBy(x => x.ExactDate).ToArray(),
            removedPrepaymentIds.ToArray());
    }

    private static DateOnly ResolveOutstandingDate(
        DateOnly date,
        DateOnly newAnchor,
        DateOnly carryDate) => date <= newAnchor ? carryDate : date;

    /// <summary>
    /// I17 — taksit ödenince kalan anaparadan yalnız anapara payı düşer.
    /// v1.9.0 öncesi taksitin tamamı düşülüyordu; 22 taksitli bir kredide
    /// 12 taksit sonra anapara 111.758 yerine 16.173 görünüyordu.
    /// </summary>
    /// <remarks>
    /// Faiz türetilemiyorsa (anapara yok ya da güncel değil) anaparaya
    /// dokunulmaz: yanlış düşmektense bırakmak, kullanıcıya uyarı olarak
    /// görünür ve bankadan düzeltilir.
    /// </remarks>
    private decimal? RemainingPrincipalAfter(
        Loan loan,
        decimal paidAmount,
        int remainingInstallments)
    {
        if (remainingInstallments == 0)
        {
            return loan.RemainingDebt is null ? null : 0m;
        }

        var analysis = loanCalculator.Analyze(loan);
        return analysis.Amortization is { } amortization
            ? LoanAmortizationCalculator.PrincipalAfterPayment(
                amortization,
                paidAmount)
            : loan.RemainingDebt;
    }

    /// <summary>
    /// Banka kapatma tutarı yalnız alındığı gün için geçerlidir. Son ödenen
    /// taksit o günü geçtiyse tutar artık hiçbir şeyi kalibre etmez; kanonik
    /// kayıtta bayat bir rakam bırakılmaz.
    /// </summary>
    private static Loan DropStaleClosureQuote(Loan loan) =>
        loan.EarlyClosureAmountAsOf is DateOnly asOf &&
        asOf < LoanAmortizationCalculator.PreviousDueDate(loan)
            ? loan with
            {
                EarlyClosureAmount = null,
                EarlyClosureAmountAsOf = null
            }
            : loan;
}
