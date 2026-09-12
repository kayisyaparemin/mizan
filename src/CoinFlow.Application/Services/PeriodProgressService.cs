using CoinFlow.Application.Abstractions;
using CoinFlow.Application.Models;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Services;

/// <summary>
/// Mevcut dönemin motoru — Ana Sayfa'nın sahibi olduğu tek zaman dilimi (I16).
/// </summary>
/// <remarks>
/// Donmuş planı ve gözlem defterini toplar, sonra bu dönemin değişebilen
/// parametrelerini gözlenen pozisyondan yeniden hesaplar: yaşam gideri
/// havuzundan ne kaldı, KMH faizi ne olacak, dönem sonu nereye gidiyor.
///
/// I16 ana sayfanın **başka zaman dilimlerinin** rakamını göstermesini
/// yasaklar; mevcut dönemi hesaplamasını değil.
/// <c>FinancialProjectionCalculator</c> yine çağrılmaz.
/// </remarks>
public sealed class PeriodProgressService(
    ICoinFlowStore store,
    IClock clock,
    CreditCardStatementCalculator cardStatementCalculator)
{
    public async Task<PeriodProgress?> GetAsync(
        CancellationToken cancellationToken = default)
    {
        var history = await store.GetFinancialHistoryAsync(cancellationToken);
        var openPlan = ResolveOpenPlan(history);
        if (openPlan is null)
        {
            return null;
        }

        var observation = await store.GetPeriodObservationAsync(
            openPlan.Id,
            cancellationToken);
        var settings = await store.GetSettingsAsync(cancellationToken);
        var cards = await store.GetCreditCardsAsync(cancellationToken);
        return Build(
            history,
            openPlan,
            observation,
            settings,
            CurrentCardPayments(openPlan, cards, settings),
            clock.Today);
    }

    /// <summary>
    /// Açık dönem planı: güncel snapshot'a ait, henüz actual'ı olmayan plan.
    /// Emekliye ayrılmış snapshot'lara bağlı yetim planlar buraya düşmez.
    /// </summary>
    public static PeriodPlanSnapshot? ResolveOpenPlan(
        FinancialHistoryData history)
    {
        var currentSnapshot = FinancialSnapshotService.LatestCurrent(history);
        return currentSnapshot is null
            ? null
            : history.Plans
                .Where(x => x.FinancialSnapshotId == currentSnapshot.Id)
                .Where(x => history.Actuals.All(actual =>
                    actual.PeriodPlanSnapshotId != x.Id))
                .OrderByDescending(x => x.CreatedAtUtc)
                .FirstOrDefault();
    }

    /// <summary>
    /// Kartların şu anki durumuna göre bu dönemde düşen ödeme. Donmuş plan
    /// o günkü tahmini saklar; bu, aradan geçen ekstre girişleri ve ödeme
    /// kararlarından sonraki hâlidir.
    /// </summary>
    private IReadOnlyDictionary<Guid, decimal> CurrentCardPayments(
        PeriodPlanSnapshot openPlan,
        IReadOnlyList<CreditCard> cards,
        UserSettings settings)
    {
        var result = new Dictionary<Guid, decimal>();
        foreach (var card in cards)
        {
            var payment = cardStatementCalculator
                .Project(card, 6, true, settings.CreditCardCarryInterestRate)
                .Where(x => x.PaymentDueDate > openPlan.PeriodStart &&
                            x.PaymentDueDate <= openPlan.PeriodEnd)
                .Sum(x => x.Payment ?? 0m);
            result[card.Id] = payment;
        }

        return result;
    }

    public static PeriodProgress Build(
        FinancialHistoryData history,
        PeriodPlanSnapshot openPlan,
        PeriodObservation? observation,
        UserSettings settings,
        IReadOnlyDictionary<Guid, decimal> currentCardPayments,
        DateOnly today)
    {
        var revisions = history.Revisions
            .Where(x => x.PeriodPlanSnapshotId == openPlan.Id)
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.RevisionNumber)
            .ToArray();
        // Karar 7 — dönem içinde "planım ne" sorusunun cevabı yaşayan
        // taahhüttür. Orijinal plan Geçmiş ekranında korunuyor.
        var latest = revisions.LastOrDefault();
        var plannedIncome = latest?.PlannedIncome ?? openPlan.PlannedIncome;
        var plannedMandatory = latest?.PlannedMandatoryPayments ??
                               openPlan.PlannedMandatoryPayments;
        var plannedLiving = latest?.PlannedLivingBudget ??
                            openPlan.PlannedLivingBudget;
        var plannedDeficitInterest = latest?.PlannedDeficitInterest ??
                                     openPlan.PlannedDeficitInterest;
        var plannedEnding = latest?.PlannedEndingSavings ??
                            openPlan.PlannedEndingSavings;
        var planLines = latest?.PaymentLines ?? openPlan.PaymentLines;

        var totalDays = Math.Max(
            0,
            openPlan.PeriodEnd.DayNumber - openPlan.PeriodStart.DayNumber);
        var elapsedDays = Math.Clamp(
            today.DayNumber - openPlan.PeriodStart.DayNumber,
            0,
            totalDays);

        // Bir plan satırı iki yoldan "yapılmış" sayılır:
        //  1. Gözlem defterinde açıkça işaretlenmişse (review akışından gelir),
        //  2. Vadesi geçmişse — karta ekstre kesilmeden ödeme yapılmaz ve
        //     vade günü gelen ödeme yapılır. Kullanıcıdan ayrıca işaretlemesini
        //     istemek gereksiz; planın kendi varsayımı zaten budur.
        var explicitEntries = observation?.Payments
            .ToDictionary(x => x.PeriodPlanPaymentLineId) ??
            new Dictionary<Guid, PeriodObservationPayment>();
        var settledTotal = 0m;
        var remaining = new List<PeriodPlanPaymentLine>();
        foreach (var line in planLines)
        {
            if (explicitEntries.TryGetValue(line.Id, out var entry))
            {
                if (entry.Status == ActualPaymentStatus.Unpaid)
                {
                    remaining.Add(line);
                }
                else
                {
                    settledTotal += entry.ActualAmount;
                }

                continue;
            }

            if (line.PlannedDate <= today)
            {
                settledTotal += line.PlannedAmount ?? 0m;
            }
            else
            {
                remaining.Add(line);
            }
        }

        var remainingLines = remaining
            .OrderBy(x => x.PlannedDate)
            .ThenBy(x => x.Name)
            .ToArray();
        var remainingPlannedTotal = remainingLines
            .Sum(x => x.PlannedAmount ?? 0m);

        // YAŞAM GİDERİ HAVUZU. Bakiyedeki düşüş, işaretlenen ödemeler
        // çıkarıldıktan sonra yaşam giderine sayılır. Günlere bölünmez:
        // planlanan bir havuzdur, harcanan ondan düşer, kalan geriye kalandır.
        decimal? observedLivingSpend = null;
        decimal? remainingLiving = null;
        if (observation?.ObservedBalance is { } balance)
        {
            var spent = openPlan.OpeningSavings
                        + plannedIncome
                        - settledTotal
                        - balance;
            // Bakiye beklenenden yüksekse harcama negatife düşemez.
            observedLivingSpend = Math.Max(0m, spent);
            remainingLiving =
                Math.Max(0m, plannedLiving - observedLivingSpend.Value);
        }

        decimal? projectedDeficitInterest = null;
        decimal? projectedEnding = null;
        if (observation?.ObservedBalance is { } current &&
            remainingLiving is { } living)
        {
            // Motorla birebir aynı kural (PeriodPlanSnapshotService.Freeze).
            // Kart faizi bu hesaba girmez: karta kapitalize olur, nakit dönem
            // sonunu değiştirmez (I9).
            var endingBeforeDeficitInterest =
                current - remainingPlannedTotal - living;
            projectedDeficitInterest = endingBeforeDeficitInterest < 0m
                ? RoundMoney(
                    Math.Abs(endingBeforeDeficitInterest) *
                    settings.DeficitFinancingInterestRate)
                : 0m;
            projectedEnding =
                endingBeforeDeficitInterest - projectedDeficitInterest.Value;
        }

        var cards = planLines
            .Where(x => x.SourceType == PlanPaymentSourceType.CreditCard)
            .OrderBy(x => x.PlannedDate)
            .ThenBy(x => x.Name)
            .Select(line => new PeriodCardComparison(
                line.SourceEntityId,
                line.Name,
                line.PlannedDate,
                line.PlannedAmount ?? 0m,
                currentCardPayments.TryGetValue(line.SourceEntityId, out var now)
                    ? now
                    : null))
            .ToArray();

        return new PeriodProgress(
            openPlan.Id,
            openPlan.PeriodStart,
            openPlan.PeriodEnd,
            today,
            elapsedDays,
            totalDays,
            openPlan.PeriodStart,
            revisions.Length,
            plannedIncome,
            plannedMandatory,
            plannedEnding,
            plannedLiving,
            observedLivingSpend,
            remainingLiving,
            cards,
            plannedDeficitInterest,
            projectedDeficitInterest,
            observation?.ObservedBalance,
            projectedEnding,
            observation,
            remainingLines,
            remainingPlannedTotal,
            today >= openPlan.ReviewAvailableFrom);
    }

    private static decimal RoundMoney(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
}
