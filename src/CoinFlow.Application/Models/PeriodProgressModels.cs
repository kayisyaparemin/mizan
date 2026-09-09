using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Models;

/// <summary>
/// Ana Sayfa'nın kendi verisi. Mevcut dönemin donmuş planı ile o döneme ait
/// gözlem defterinin birleşimi.
/// </summary>
/// <remarks>
/// Burada projeksiyon motoru çalışmaz (I16). Gelecek 12 dönem bilgisi de
/// taşınmaz — o 12 Dönem ekranının verisidir. Bu modeldeki her rakam ya
/// donmuş plandan ya gözlemden gelir.
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
    decimal PlannedEndingSavings,
    // GİDİŞAT bloğu — gözlem yoksa üçü de null.
    PeriodObservation? Observation,
    decimal? ObservedBalance,
    decimal? ProjectedEndingSavings,
    decimal? Deviation,
    // KALAN bloğu — plandaki, henüz ödenmiş işaretlenmemiş satırlar.
    IReadOnlyList<PeriodPlanPaymentLine> RemainingLines,
    decimal RemainingPlannedTotal,
    bool IsClosable)
{
    public bool HasObservation => Observation is not null;
    public bool HasProjection => ProjectedEndingSavings is not null;
    public bool HasRemainingLines => RemainingLines.Count > 0;
    public bool WasRevised => RevisionCount > 0;

    /// <summary>Dönemin ne kadarı geçti; ilerleme çubuğu için 0–1.</summary>
    public double ElapsedRatio => TotalDays <= 0
        ? 0d
        : Math.Clamp((double)ElapsedDays / TotalDays, 0d, 1d);
}
