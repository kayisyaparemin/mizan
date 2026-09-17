using Mizan.Domain.Models;

namespace Mizan.Domain.Calculations;

public sealed partial class SimulationCalculator
{
    private FinancialPlan AddCardPurchase(
        FinancialPlan plan,
        SimulationRequest request)
    {
        if (request.CreditCardId is null)
        {
            throw new InvalidOperationException(
                "Bu plan için bir kredi kartı seçmelisin.");
        }

        var card = plan.CreditCards
            .SingleOrDefault(x => x.Id == request.CreditCardId.Value)
            ?? throw new InvalidOperationException("Seçilen kredi kartı bulunamadı.");
        var availableLimit = card.Limit - card.KnownTotalDebt;
        if (card.Limit > 0m && request.Amount > availableLimit)
        {
            throw new InvalidOperationException(
                "Kartın bilinen kullanılabilir limiti bu plan için yetersiz.");
        }

        var charges = installmentScheduleCalculator
            .Split(request.Amount, request.PaymentCount, request.StartDate)
            .Select((x, index) => new CardCharge
            {
                Id = index == 0
                    ? request.ScenarioId
                    : ChildId(request.ScenarioId, index),
                CreditCardId = card.Id,
                Description = request.PaymentCount == 1
                    ? request.Name.Trim()
                    : $"{request.Name.Trim()} ({index + 1}/{request.PaymentCount})",
                PostingDate = x.Date,
                Amount = x.Amount
            })
            .ToArray();
        var updatedCard = card with
        {
            Charges = card.Charges.Concat(charges).ToArray()
        };

        return plan with
        {
            CreditCards = plan.CreditCards
                .Select(x => x.Id == card.Id ? updatedCard : x)
                .ToArray()
        };
    }

    // Kartı asgari yerine tamamen ödemek (veya tersi) faizi doğrudan
    // değiştirir, ama etkisi tek yönlü değildir: erken kapatmak kart faizini
    // düşürürken parayı erken çıkardığı için açık faizini yükseltebilir.
    // Kapsam iki türlüdür — tek ekstre için vade tarihine override, sürekli
    // için kartın genel ödeme şekli.
    private static FinancialPlan AddCardPaymentMode(
        FinancialPlan plan,
        SimulationRequest request)
    {
        if (request.CreditCardId is null)
        {
            throw new InvalidOperationException(
                "Ödeme şekli planı için bir kredi kartı seçmelisin.");
        }

        var card = plan.CreditCards.SingleOrDefault(x =>
                       x.Id == request.CreditCardId.Value)
                   ?? throw new InvalidOperationException(
                       "Seçilen kredi kartı bulunamadı.");
        var paymentType = request.CardPaymentType ??
                          CreditCardPaymentType.FullStatement;
        var updated = request.AppliesToAllStatements
            ? card with { PaymentStrategy = ToStrategy(paymentType) }
            : card with
            {
                PaymentPlans = card.PaymentPlans
                    .Where(x => x.DueDate != request.StartDate)
                    .Append(new CreditCardPaymentPlan
                    {
                        Id = request.ScenarioId,
                        CreditCardId = card.Id,
                        DueDate = request.StartDate,
                        PaymentType = paymentType
                    })
                    .OrderBy(x => x.DueDate)
                    .ToArray()
            };
        return plan with
        {
            CreditCards = plan.CreditCards
                .Select(x => x.Id == card.Id ? updated : x)
                .ToArray()
        };
    }

    // Erken ödeme krediyi değiştirmez, ona bir olay ekler; ödeme listesi
    // olayları oynatarak üretilir. Kimlik ScenarioId'dir ki uygulama idempotent
    // olsun.
    private FinancialPlan AddLoanPrepayment(
        FinancialPlan plan,
        SimulationRequest request)
    {
        var loan = plan.Loans.SingleOrDefault(x => x.Id == request.LoanId);
        var prepayment = new LoanPrepayment
        {
            Id = request.ScenarioId,
            LoanId = request.LoanId.GetValueOrDefault(),
            Date = request.StartDate,
            Mode = request.Type == SimulationScenarioType.LoanEarlyClosure
                ? LoanPrepaymentMode.FullClosure
                : request.PrepaymentMode ?? LoanPrepaymentMode.ReduceTerm,
            PrincipalAmount =
                request.Type == SimulationScenarioType.LoanEarlyClosure
                    ? null
                    : request.Amount
        };
        _loanScheduleBuilder.Validate(loan, plan.LoanPrepayments, prepayment);
        return plan with
        {
            LoanPrepayments = plan.LoanPrepayments
                .Append(prepayment)
                .ToArray()
        };
    }

    private IReadOnlyList<LoanPrepaymentImpact> BuildLoanImpacts(
        FinancialPlan baselinePlan,
        FinancialPlan scenarioPlan)
    {
        var existing = baselinePlan.LoanPrepayments
            .Select(x => x.Id)
            .ToHashSet();
        var added = scenarioPlan.LoanPrepayments
            .Where(x => !existing.Contains(x.Id))
            .ToArray();
        return scenarioPlan.Loans
            .Where(loan => added.Any(x => x.LoanId == loan.Id))
            .Select(loan =>
            {
                var baseline = _loanScheduleBuilder.Replay(
                    loan,
                    baselinePlan.LoanPrepayments);
                var scenario = _loanScheduleBuilder.Replay(
                    loan,
                    scenarioPlan.LoanPrepayments);
                var addedIds = added
                    .Where(x => x.LoanId == loan.Id)
                    .Select(x => x.Id)
                    .ToHashSet();
                var lastAdded = added
                    .Where(x => x.LoanId == loan.Id)
                    .OrderBy(x => x.Date)
                    .Last();
                decimal? newPayment =
                    scenario.StateAfterEvent.TryGetValue(
                        lastAdded.Id,
                        out var state) && state.IsActive
                        ? state.MonthlyPayment
                        : null;
                return new LoanPrepaymentImpact(
                    loan.Id,
                    $"{loan.Bank} {loan.Name}".Trim(),
                    scenario.Payments
                        .Where(x => addedIds.Contains(x.SourceId))
                        .Sum(x => x.Amount),
                    baseline.Total - scenario.Total,
                    baseline.LastPaymentDate,
                    scenario.LastPaymentDate,
                    loan.MonthlyPayment,
                    newPayment);
            })
            .ToArray();
    }

    private static CreditCardPaymentStrategy ToStrategy(
        CreditCardPaymentType paymentType) => paymentType switch
    {
        CreditCardPaymentType.Minimum => CreditCardPaymentStrategy.Minimum,
        CreditCardPaymentType.FullStatement =>
            CreditCardPaymentStrategy.FullStatement,
        _ => throw new InvalidOperationException(
            "Kart ödeme şekli yalnızca asgari veya tamamı olabilir.")
    };
}
