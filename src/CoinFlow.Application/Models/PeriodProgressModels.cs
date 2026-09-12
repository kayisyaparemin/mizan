using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Models;

/// <summary>
/// Ana Sayfa'nın kendi verisi. Mevcut dönemin donmuş planı ile o döneme ait
/// gözlem defterinin birleşimi.
/// </summary>
/// <remarks>
/// Gelecek 12 dönem bilgisi taşımaz — o 12 Dönem ekranının verisidir (I16).
/// Ama mevcut dönemin parametrelerini **yeniden hesaplar**: gözlenen bakiye
/// girildiğinde açık faizinin ne olacağı bu ekranın sorusudur.
/// </remarks>
public sealed record PeriodProgress(
    Guid PeriodPlanSnapshotId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly Today,
    int ElapsedDays,
    int TotalDays,
    // PLAN bloğu — en güncel revizyon uygulanmış hâli (karar 7).
    DateOnly PlanFrozenOn,
    int RevisionCount,
    decimal PlannedIncome,
    decimal PlannedMandatoryPayments,
    decimal PlannedLivingBudget,
    decimal PlannedCardInterest,
    decimal PlannedDeficitInterest,
    decimal PlannedEndingSavings,
    /// <summary>Planın bugün beklediği bakiye; "şu an" satırının plan sütunu.</summary>
    decimal ExpectedBalanceToday,
    // GİDİŞAT bloğu — gözlem yoksa hepsi null.
    PeriodObservation? Observation,
    decimal? ObservedBalance,
    decimal? ProjectedDeficitInterest,
    decimal? ProjectedEndingSavings,
    decimal? Deviation,
    // KALAN bloğu — plandaki, henüz ödenmiş işaretlenmemiş satırlar.
    IReadOnlyList<PeriodPlanPaymentLine> RemainingLines,
    decimal RemainingPlannedTotal,
    decimal RemainingLivingBudget,
    bool IsClosable)
{
    public bool HasObservation => Observation is not null;
    public bool HasProjection => ProjectedEndingSavings is not null;
    public bool HasRemainingLines => RemainingLines.Count > 0;
    public bool WasRevised => RevisionCount > 0;

    /// <summary>Bugüne kadarki sapma: gözlenen − planın bugün beklediği.</summary>
    public decimal? BalanceDeviation =>
        ObservedBalance is { } balance ? balance - ExpectedBalanceToday : null;

    /// <summary>Açık faizindeki değişim; plandan sapma arttıkça büyür.</summary>
    public decimal? DeficitInterestDeviation =>
        ProjectedDeficitInterest is { } projected
            ? projected - PlannedDeficitInterest
            : null;

    /// <summary>Dönemin ne kadarı geçti; ilerleme çubuğu için 0–1.</summary>
    public double ElapsedRatio => TotalDays <= 0
        ? 0d
        : Math.Clamp((double)ElapsedDays / TotalDays, 0d, 1d);
}
