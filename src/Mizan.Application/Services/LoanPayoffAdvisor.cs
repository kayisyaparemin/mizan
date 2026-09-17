using Mizan.Domain.Calculations;
using Mizan.Domain.Models;

namespace Mizan.Application.Services;

public enum LoanPayoffAdviceStatus
{
    /// <summary>Açık oluşturmadan ve kazançlı kapatılabilecek bir ay var.</summary>
    Recommended,
    /// <summary>
    /// Kapatmak kazançlı olurdu ama her seferinde bir dönemde açık oluşuyor ya
    /// da büyüyor.
    /// </summary>
    NoSafeMonth,
    /// <summary>Hiçbir ayda kapatmak kazandırmıyor.</summary>
    NotWorthIt,
    /// <summary>Faiz türetilemiyor; kalan anapara ya da banka tutarı gerekli.</summary>
    NeedsPrincipal,
    /// <summary>Bu kredi için zaten bir erken kapama planlanmış.</summary>
    AlreadyClosing
}

/// <param name="Date">Önerilen (en erken uygun) kapatma günü.</param>
/// <param name="InterestSaving">
/// Kredinin ömrü boyunca ödenmeyecek faiz: olaysız toplam − olaylı toplam.
/// </param>
/// <param name="NetGain">
/// 12. dönem sonu farkı + ufuk sonrası ödenmeyecek taksitler. Kapatma için
/// kullanılan paranın açık faizi maliyeti bunun içindedir.
/// </param>
/// <param name="BestDate">En kârlı uygun gün; en erkenden farklıysa.</param>
public sealed record LoanPayoffAdvice(
    Guid LoanId,
    string LoanName,
    LoanPayoffAdviceStatus Status,
    DateOnly? Date = null,
    decimal? PayoffAmount = null,
    decimal? InterestSaving = null,
    decimal? NetGain = null,
    DateOnly? BestDate = null,
    decimal? BestNetGain = null);

/// <summary>
/// "Bu krediyi şu ayda kapatabilirsin" önerisi.
/// </summary>
/// <remarks>
/// <para>
/// Bakiyeye bakarak cevaplanamaz: açık faizi krediden yüksekse krediyi açık
/// parasıyla kapatmak zarardır. Her kredi için ufuktaki her taksit günü
/// denenir (o günün taksiti ödenir, işleyen faiz sıfırdır) ve projeksiyon
/// motoru kapatmalı ve kapatmasız çalıştırılır.
/// </para>
/// <para>
/// Bir gün yalnız ikisi birden sağlanırsa önerilir (kullanıcı kararı):
/// <b>açık yok</b> — hiçbir dönemde açık baz çizgiden büyük değil; <b>net
/// kazanç</b> — 12. dönem sonu farkı + ufuk sonrası ödenmeyecek taksitler
/// pozitif. İkinci terim şart: uzun bir kredinin kazancının çoğu 12 dönemin
/// dışındadır. Ufuk sonrası açık faizi sayılmaz (bilinen sadeleştirme).
/// </para>
/// </remarks>
public sealed class LoanPayoffAdvisor(
    FinancialProjectionCalculator projectionCalculator,
    SimulationCalculator simulationCalculator,
    LoanAmortizationCalculator amortizationCalculator,
    LoanPaymentScheduleBuilder scheduleBuilder)
{
    private const decimal Tolerance = 0.01m;

    public IReadOnlyList<LoanPayoffAdvice> Advise(
        FinancialPlan plan,
        DateOnly asOf,
        DateOnly? firstSalaryDate = null,
        int periodCount = 12,
        CancellationToken cancellationToken = default)
    {
        var baseline = projectionCalculator.Calculate(
            plan,
            asOf,
            periodCount,
            firstSalaryDate);
        if (baseline.Count == 0)
        {
            return [];
        }

        var advice = new List<LoanPayoffAdvice>();
        foreach (var loan in plan.Loans.Where(x =>
                     x.IsActive && x.RemainingInstallmentCount > 0))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = AdviseLoan(
                plan,
                loan,
                baseline,
                asOf,
                firstSalaryDate,
                periodCount,
                cancellationToken);
            if (result is not null)
            {
                advice.Add(result);
            }
        }

        return advice;
    }

    private LoanPayoffAdvice? AdviseLoan(
        FinancialPlan plan,
        Loan loan,
        IReadOnlyList<CashFlowPeriodProjection> baseline,
        DateOnly asOf,
        DateOnly? firstSalaryDate,
        int periodCount,
        CancellationToken cancellationToken)
    {
        var name = $"{loan.Bank} {loan.Name}".Trim();
        var events = plan.LoanPrepayments
            .Where(x => x.LoanId == loan.Id)
            .ToArray();
        if (events.FirstOrDefault(x =>
                x.Mode == LoanPrepaymentMode.FullClosure) is { } closing)
        {
            return new LoanPayoffAdvice(
                loan.Id,
                name,
                LoanPayoffAdviceStatus.AlreadyClosing,
                closing.Date);
        }

        if (amortizationCalculator.Analyze(loan).Amortization is null)
        {
            return new LoanPayoffAdvice(
                loan.Id,
                name,
                LoanPayoffAdviceStatus.NeedsPrincipal);
        }

        var replay = scheduleBuilder.Replay(loan, plan.LoanPrepayments);
        var horizonStart = baseline[0].PeriodStart;
        var horizonEnd = baseline[^1].PeriodEnd;
        var candidates = replay.Payments
            .Where(x => x.Kind == LoanPaymentKind.Installment && !x.IsFinal)
            .Select(x => x.Date)
            .Where(date => date >= asOf &&
                           date >= horizonStart &&
                           date < horizonEnd)
            .Distinct()
            .ToArray();
        if (candidates.Length == 0)
        {
            return null;
        }

        var loanIds = events.Select(x => x.Id).Append(loan.Id).ToHashSet();
        var baselineOutsideHorizon =
            replay.Total - LoanPaymentsInPeriods(baseline, loanIds);
        LoanPayoffAdvice? earliest = null;
        var bestGain = decimal.MinValue;
        DateOnly? bestDate = null;
        var sawGainWithDeficit = false;

        foreach (var date in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = new SimulationRequest(
                SimulationScenarioType.LoanEarlyClosure,
                $"{name} erken kapama",
                0m,
                date,
                ScenarioId: Guid.NewGuid(),
                LoanId: loan.Id);
            FinancialPlan scenarioPlan;
            try
            {
                scenarioPlan = simulationCalculator.BuildScenarioPlan(
                    plan,
                    request);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            var scenario = projectionCalculator.Calculate(
                scenarioPlan,
                asOf,
                periodCount,
                firstSalaryDate);
            var scenarioReplay = scheduleBuilder.Replay(
                loan,
                scenarioPlan.LoanPrepayments);
            var scenarioIds = loanIds.Append(request.ScenarioId).ToHashSet();
            var scenarioOutsideHorizon = scenarioReplay.Total -
                                         LoanPaymentsInPeriods(scenario, scenarioIds);
            var netGain =
                scenario[^1].EndingProjectedBalance -
                baseline[^1].EndingProjectedBalance +
                (baselineOutsideHorizon - scenarioOutsideHorizon);
            var noNewDeficit = baseline
                .Zip(scenario)
                .All(pair => Deficit(pair.Second) <= Deficit(pair.First) + Tolerance);

            if (netGain <= 0m)
            {
                continue;
            }

            if (!noNewDeficit)
            {
                sawGainWithDeficit = true;
                continue;
            }

            if (netGain > bestGain)
            {
                bestGain = netGain;
                bestDate = date;
            }

            earliest ??= new LoanPayoffAdvice(
                loan.Id,
                name,
                LoanPayoffAdviceStatus.Recommended,
                date,
                scenarioReplay.Payments
                    .Where(x => x.SourceId == request.ScenarioId)
                    .Sum(x => x.Amount),
                replay.Total - scenarioReplay.Total,
                netGain);
        }

        if (earliest is not null)
        {
            return bestDate is { } best && best != earliest.Date
                ? earliest with { BestDate = best, BestNetGain = bestGain }
                : earliest;
        }

        return new LoanPayoffAdvice(
            loan.Id,
            name,
            sawGainWithDeficit
                ? LoanPayoffAdviceStatus.NoSafeMonth
                : LoanPayoffAdviceStatus.NotWorthIt);
    }

    private static decimal Deficit(CashFlowPeriodProjection period) =>
        Math.Max(0m, -period.EndingProjectedBalance);

    private static decimal LoanPaymentsInPeriods(
        IReadOnlyList<CashFlowPeriodProjection> periods,
        IReadOnlySet<Guid> ids) =>
        periods
            .SelectMany(x => x.MandatoryItems)
            .Where(x => x.Type == ObligationType.Loan && ids.Contains(x.PaymentId))
            .Sum(x => x.Amount);
}
