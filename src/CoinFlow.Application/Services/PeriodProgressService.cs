using CoinFlow.Application.Abstractions;
using CoinFlow.Application.Models;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Services;

/// <summary>
/// Mevcut dönemin motoru — Ana Sayfa'nın sahibi olduğu tek zaman dilimi (I16).
/// </summary>
/// <remarks>
/// Donmuş planı ve gözlem defterini toplar, sonra **bu dönemin** değişebilen
/// parametrelerini gözlenen pozisyondan yeniden hesaplar. Sorulan soru şu:
/// "elimdeki bu tutarla kalan ödemeleri yapınca dönem sonu ve faiz ne olur?"
///
/// I16'nın yasakladığı şey ana sayfanın **başka zaman dilimlerinin** rakamını
/// göstermesidir; mevcut dönemi hesaplamak tam olarak bu servisin işidir.
/// <c>FinancialProjectionCalculator</c> yine çağrılmaz — gereken tek formül
/// açık faizidir ve o <see cref="PeriodPlanSnapshotService"/> ile birebir
/// aynı kuralı kullanır.
/// </remarks>
public sealed class PeriodProgressService(
    ICoinFlowStore store,
    IClock clock)
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
        return Build(
            history,
            openPlan,
            observation,
            settings.DeficitFinancingInterestRate,
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

    public static PeriodProgress Build(
        FinancialHistoryData history,
        PeriodPlanSnapshot openPlan,
        PeriodObservation? observation,
        decimal deficitInterestRate,
        DateOnly today)
    {
        var revisions = history.Revisions
            .Where(x => x.PeriodPlanSnapshotId == openPlan.Id)
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.RevisionNumber)
            .ToArray();
        // Karar 7 — dönem içinde "planım ne" sorusunun cevabı yaşayan
        // taahhüttür. Orijinal plan kaybolmuyor; Geçmiş ekranı Orijinal Plan /
        // Son Plan / Gerçek üçlüsünü ayrı sütunlarda tutuyor.
        var latest = revisions.LastOrDefault();
        var plannedIncome = latest?.PlannedIncome ?? openPlan.PlannedIncome;
        var plannedMandatory = latest?.PlannedMandatoryPayments ??
                               openPlan.PlannedMandatoryPayments;
        var plannedLiving = latest?.PlannedLivingBudget ??
                            openPlan.PlannedLivingBudget;
        var plannedCardInterest = latest?.PlannedCardInterest ??
                                  openPlan.PlannedCardInterest;
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

        var settledLineIds = observation is null
            ? new HashSet<Guid>()
            : observation.Payments
                .Where(x => x.Status != ActualPaymentStatus.Unpaid)
                .Select(x => x.PeriodPlanPaymentLineId)
                .ToHashSet();
        var remainingLines = planLines
            .Where(x => !settledLineIds.Contains(x.Id))
            .OrderBy(x => x.PlannedDate)
            .ThenBy(x => x.Name)
            .ToArray();
        var remainingPlannedTotal = remainingLines
            .Sum(x => x.PlannedAmount ?? 0m);
        var remainingLiving = RemainingLiving(
            observation,
            plannedLiving,
            elapsedDays,
            totalDays);

        // Planın bugün beklediği bakiye — "şu an" satırının plan sütunu.
        // Gelir dönem başında alınmış sayılır (dönem maaş gününde başlar),
        // bugüne kadar vadesi gelen satırlar ödenmiş, yaşam gideri günü
        // gününe harcanmış kabul edilir.
        var duePlannedSoFar = planLines
            .Where(x => x.PlannedDate <= today)
            .Sum(x => x.PlannedAmount ?? 0m);
        var expectedToday = openPlan.OpeningSavings
                            + plannedIncome
                            - duePlannedSoFar
                            - (plannedLiving - remainingLiving);

        decimal? projectedDeficitInterest = null;
        decimal? projectedEnding = null;
        if (observation?.ObservedBalance is { } balance)
        {
            // Motorla birebir aynı kural (PeriodPlanSnapshotService.Freeze):
            // açık faizi, faiz öncesi dönem sonu negatifse onun mutlak değeri
            // üzerinden işler. Kart faizi bu hesaba girmez ve nakit dönem
            // sonunu değiştirmez — karta kapitalize olur (I9).
            var endingBeforeDeficitInterest =
                balance - remainingPlannedTotal - remainingLiving;
            projectedDeficitInterest = endingBeforeDeficitInterest < 0m
                ? RoundMoney(
                    Math.Abs(endingBeforeDeficitInterest) * deficitInterestRate)
                : 0m;
            projectedEnding =
                endingBeforeDeficitInterest - projectedDeficitInterest.Value;
        }

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
            plannedLiving,
            plannedCardInterest,
            plannedDeficitInterest,
            plannedEnding,
            expectedToday,
            observation,
            observation?.ObservedBalance,
            projectedDeficitInterest,
            projectedEnding,
            projectedEnding is null ? null : projectedEnding - plannedEnding,
            remainingLines,
            remainingPlannedTotal,
            remainingLiving,
            today >= openPlan.ReviewAvailableFrom);
    }

    /// <summary>
    /// Dönemin kalanına düşen yaşam gideri. Gözlemde gerçek harcama
    /// girildiyse onun günlük hızı, girilmediyse planın günlük hızı esas alınır.
    /// </summary>
    private static decimal RemainingLiving(
        PeriodObservation? observation,
        decimal plannedLiving,
        int elapsedDays,
        int totalDays)
    {
        if (totalDays <= 0)
        {
            return 0m;
        }

        if (observation is { ObservedLivingSpend: > 0m } && elapsedDays > 0)
        {
            var dailyObserved = observation.ObservedLivingSpend / elapsedDays;
            return RoundMoney(dailyObserved * (totalDays - elapsedDays));
        }

        return RoundMoney(
            plannedLiving * (totalDays - elapsedDays) / totalDays);
    }

    private static decimal RoundMoney(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
}
