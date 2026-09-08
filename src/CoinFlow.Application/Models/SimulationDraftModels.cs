using CoinFlow.Domain.Calculations;

namespace CoinFlow.Application.Models;

/// <summary>
/// Kullanıcının simülatörde kurduğu ve adlandırarak sakladığı koşul listesi.
/// Kalıcıdır: uygulama kapansa da kaybolmaz.
///
/// Bu bir plan **denemesidir**, finansal kayıt değil. Hiçbir projeksiyona
/// girmez; yalnız simülatöre geri yüklenir. Adı bilinçli olarak
/// <c>TemporaryPaymentPlan</c>'dan ayrı: o gerçek bir borç aracı, bu bir
/// taslak.
/// </summary>
public sealed record SimulationDraft(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<SimulationDraftCondition> Conditions)
{
    public int EnabledConditionCount =>
        Conditions.Count(x => x.IsEnabled);
}

/// <summary>
/// Kaydedilmiş bir koşul. Açık/kapalı durumu da saklanır: kullanıcı bir
/// koşulu bilerek kapattıysa, geri yüklendiğinde de kapalı gelmeli.
/// </summary>
/// <remarks>
/// <see cref="SimulationRequest.ScenarioId"/> olduğu gibi korunur. Apply
/// yolu entity kimliklerini bu değerden deterministik üretiyor; kaydedip
/// geri yüklenen bir plan ikinci kez uygulandığında mükerrer yükümlülük
/// oluşmasın diye kimlik değişmemeli.
/// </remarks>
public sealed record SimulationDraftCondition(
    SimulationRequest Request,
    bool IsEnabled);
