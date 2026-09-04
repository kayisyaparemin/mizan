using CoinFlow.Domain.Models;

namespace CoinFlow.Domain.Calculations;

/// <summary>
/// Kartın ekstre ödeme tercihi geçmişini çözer. PaymentAssignmentStrategyResolver
/// ile aynı effective-dated seçim kuralını kullanır: verilen tarihte yürürlükte
/// olan en yeni kayıt kazanır. Geçmiş kayıtlar hiçbir zaman değiştirilmez.
/// </summary>
public sealed class CreditCardPaymentPreferenceResolver
{
    public CreditCardPaymentPreference? Resolve(
        DateOnly statementDate,
        IEnumerable<CreditCardPaymentPreference> history) =>
        history
            .Where(x => x.EffectiveFromStatementDate <= statementDate)
            .OrderByDescending(x => x.EffectiveFromStatementDate)
            .ThenByDescending(x => x.CreatedAt)
            .FirstOrDefault();

    /// <summary>
    /// Geçmişi eskiden yeniye sıralar. UI ve raporlama için tek sıralama kaynağı.
    /// </summary>
    public IReadOnlyList<CreditCardPaymentPreference> Ordered(
        IEnumerable<CreditCardPaymentPreference> history) =>
        history
            .OrderBy(x => x.EffectiveFromStatementDate)
            .ThenBy(x => x.CreatedAt)
            .ToArray();

    /// <summary>
    /// Yürürlükteki tercih ile verilen ekstre planı aynı kararı mı ifade ediyor?
    /// Aynıysa yeni bir geçmiş kaydı üretilmez; böylece ilgisiz kart
    /// güncellemeleri geçmişi şişirmez.
    /// </summary>
    public static bool RepresentsSameDecision(
        CreditCardPaymentPreference? preference,
        CurrentStatementPaymentPlan? plan)
    {
        if (preference is null || plan is null)
        {
            return preference is null && plan is null;
        }

        return preference.Mode == plan.Mode &&
               NormalizeAmount(preference.Mode, preference.CustomAmount) ==
               NormalizeAmount(plan.Mode, plan.CustomAmount);
    }

    public static void Validate(
        IEnumerable<CreditCardPaymentPreference> history)
    {
        foreach (var preference in history)
        {
            if (!Enum.IsDefined(preference.Mode))
            {
                throw new InvalidOperationException(
                    "Ödeme tercihi geçmişinde geçersiz ödeme şekli var.");
            }

            if (preference.EffectiveFromStatementDate == default)
            {
                throw new InvalidOperationException(
                    "Ödeme tercihi geçmişi için geçerli bir kesim tarihi gereklidir.");
            }

            if (preference.Mode == CurrentStatementPaymentMode.Custom &&
                preference.CustomAmount is null or <= 0m)
            {
                throw new InvalidOperationException(
                    "Özel ödeme tercihi için sıfırdan büyük bir tutar gereklidir.");
            }
        }
    }

    private static decimal? NormalizeAmount(
        CurrentStatementPaymentMode mode,
        decimal? amount) =>
        mode == CurrentStatementPaymentMode.Custom ? amount : null;
}
