using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Models;

/// <summary>
/// Ana Sayfa'nın kendi verisi: mevcut dönemin donmuş planı ile gözlem
/// defterinin birleşimi, parametre parametre.
/// </summary>
/// <remarks>
/// Gelecek 12 dönem bilgisi taşımaz — o 12 Dönem ekranının verisidir (I16).
/// Ama mevcut dönemin değişebilen parametrelerini yeniden hesaplar: elindeki
/// tutarı girdiğinde yaşam gideri havuzundan ne kaldığı, KMH faizinin ne
/// olacağı ve dönem sonunun nereye gittiği bu ekranın sorusudur.
/// </remarks>
public sealed record PeriodProgress(
    Guid PeriodPlanSnapshotId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly Today,
    int ElapsedDays,
    int TotalDays,
    DateOnly PlanFrozenOn,
    int RevisionCount,
    decimal PlannedIncome,
    decimal PlannedMandatoryPayments,
    decimal PlannedEndingSavings,
    // YAŞAM GİDERİ — havuz. Günlere bölünmez: 20.000 planlandıysa ve 15.000
    // harcandıysa 5.000 kalmıştır. Kart harcaması buraya girmez; o kartın
    // içinde ekstre tarihiyle yönetilir.
    decimal PlannedLivingBudget,
    decimal? ObservedLivingSpend,
    decimal? RemainingLivingBudget,
    // KREDİ KARTLARI — planlanan ödeme ile şu anki ödemenin karşılaştırması.
    IReadOnlyList<PeriodCardComparison> Cards,
    // KMH — açık finansman faizi; pozisyonla değişir.
    decimal PlannedDeficitInterest,
    decimal? ProjectedDeficitInterest,
    // DÖNEM SONU
    decimal? ObservedBalance,
    decimal? ProjectedEndingSavings,
    PeriodObservation? Observation,
    // KALAN — plandaki, henüz ödenmiş işaretlenmemiş satırlar.
    IReadOnlyList<PeriodPlanPaymentLine> RemainingLines,
    decimal RemainingPlannedTotal,
    bool IsClosable)
{
    public bool HasObservation => Observation is not null;
    public bool HasRemainingLines => RemainingLines.Count > 0;
    public bool WasRevised => RevisionCount > 0;
    public bool HasCards => Cards.Count > 0;

    /// <summary>
    /// KMH satırı yalnız gerçekten açık varsa gösterilir; sıfır yazmak için
    /// alan açılmaz.
    /// </summary>
    public bool HasDeficitFinancing =>
        PlannedDeficitInterest > 0m || ProjectedDeficitInterest > 0m;

    /// <summary>Yaşam gideri havuzu aşıldıysa fark; aşılmadıysa null.</summary>
    public decimal? LivingOverspend =>
        ObservedLivingSpend is { } spent && spent > PlannedLivingBudget
            ? spent - PlannedLivingBudget
            : null;

    public decimal? EndingDeviation =>
        ProjectedEndingSavings is { } projected
            ? projected - PlannedEndingSavings
            : null;

    public decimal? DeficitInterestDeviation =>
        ProjectedDeficitInterest is { } projected
            ? projected - PlannedDeficitInterest
            : null;

    public double ElapsedRatio => TotalDays <= 0
        ? 0d
        : Math.Clamp((double)ElapsedDays / TotalDays, 0d, 1d);
}

/// <summary>
/// Bir kartın bu dönemdeki ödemesi: donmuş planın dediği ile kartın şu anki
/// durumunun dediği.
/// </summary>
public sealed record PeriodCardComparison(
    Guid CardId,
    string Name,
    DateOnly DueDate,
    decimal Planned,
    decimal? Current)
{
    public decimal? Difference => Current is { } current
        ? current - Planned
        : null;

    public bool HasCurrent => Current is not null;
}
