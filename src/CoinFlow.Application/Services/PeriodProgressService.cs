using CoinFlow.Application.Abstractions;
using CoinFlow.Application.Models;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Services;

/// <summary>
/// Mevcut dönemin motoru — Ana Sayfa'nın sahibi olduğu tek zaman dilimi (I16).
/// </summary>
/// <remarks>
/// Yeni bir hesap motoru değil: donmuş planı ve gözlem defterini toplar.
/// <c>FinancialProjectionCalculator</c> burada çağrılmaz; ana sayfanın
/// rakamları gelecek motorundan gelmeyi bırakır. Hiçbir metodu snapshot
/// zincirine yazmaz (I14).
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
        return Build(history, openPlan, observation, clock.Today);
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
        var plannedEnding = latest?.PlannedEndingSavings ??
                            openPlan.PlannedEndingSavings;
        // Faiz de planın bir kalemi ve dönem sonuna kadar önümüzde: kart
        // faizi ekstre gününde, açık faizi dönem sonunda işler. İkisi de
        // gözlenen nakit bakiyenin içinde DEĞİL (I9 — kart faizi karta
        // kapitalize olur, açık faizi bir planlama kalemidir), bu yüzden
        // gidişattan ayrıca düşülmeleri gerekir. Düşülmezse plana tam uygun
        // giden kullanıcı bile faiz kadar kârda görünür.
        var plannedInterest =
            (latest?.PlannedCardInterest ?? openPlan.PlannedCardInterest) +
            (latest?.PlannedDeficitInterest ?? openPlan.PlannedDeficitInterest);
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

        var projected = ProjectEnding(
            observation,
            plannedLiving,
            remainingPlannedTotal,
            plannedInterest,
            elapsedDays,
            totalDays);

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
            plannedInterest,
            plannedEnding,
            observation,
            observation?.ObservedBalance,
            projected,
            projected is null ? null : projected - plannedEnding,
            remainingLines,
            remainingPlannedTotal,
            today >= openPlan.ReviewAvailableFrom);
    }

    /// <summary>
    /// Gidişat: gözlenen bakiyeden dönemin kalanı düşülür.
    /// Yalnız donmuş plan ve gözlem kullanılır — projeksiyon motoru değil.
    /// </summary>
    /// <remarks>
    /// Gözlenen bakiye yoksa <c>null</c> döner. Rakam uydurmaktansa gidişat
    /// bloğunu hiç göstermemek doğrudur; ekranın kendini yalanlaması bu
    /// projede tekrar eden bir hataydı.
    /// </remarks>
    private static decimal? ProjectEnding(
        PeriodObservation? observation,
        decimal plannedLiving,
        decimal remainingPlannedTotal,
        decimal plannedInterest,
        int elapsedDays,
        int totalDays)
    {
        if (observation?.ObservedBalance is not { } balance)
        {
            return null;
        }

        // Yaşam gideri dönem boyunca eşit dağıtılır; gözlenen kısmı zaten
        // bakiyenin içinde, kalan kısmı önümüzde.
        var remainingLiving = totalDays <= 0
            ? 0m
            : RoundMoney(
                plannedLiving * (totalDays - elapsedDays) / totalDays);
        // Gözlemde ayrıca yaşam gideri girildiyse, plan yerine onun kalan
        // günlere düşen oranı esas alınır.
        if (observation.ObservedLivingSpend > 0m && elapsedDays > 0)
        {
            var dailyObserved = observation.ObservedLivingSpend / elapsedDays;
            remainingLiving = RoundMoney(
                dailyObserved * (totalDays - elapsedDays));
        }

        // Planlanan gelir toplama girmez: dönem maaş gününde başladığı için
        // dönemin geliri dönem başında alınmıştır ve gözlenen bakiyenin
        // içindedir. Dönem ortasına düşen tek seferlik gelir bu hesapta
        // görünmez — bilinen sadeleştirme, gözlem defterine akış olarak
        // girildiğinde bakiyeye zaten yansımış olur.
        return balance
               - remainingPlannedTotal
               - remainingLiving
               - plannedInterest;
    }

    private static decimal RoundMoney(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
}
