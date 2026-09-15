using CoinFlow.Application.Models;
using CoinFlow.Application.Services;

namespace CoinFlow.App.Services;

/// <summary>Telefonda bildirim kurma; Android'de alarm + bildirim kanalı.</summary>
public interface IPaymentReminderScheduler
{
    bool AreNotificationsAllowed { get; }

    /// <summary>Android 13+ bildirim iznini ister; izin varsa true.</summary>
    Task<bool> RequestPermissionAsync();

    /// <summary>
    /// Profilin kurulu bildirimlerini verilen listeyle değiştirir. Diğer
    /// profillerin bildirimlerine dokunmaz.
    /// </summary>
    void Replace(Guid profileId, IReadOnlyList<PaymentReminder> reminders);

    void OpenNotificationSettings();
}

/// <summary>
/// Açık profilin hatırlatıcılarını telefondaki alarmlarla eşitler. Ana Sayfa
/// her açıldığında ve hatırlatıcı davranışı değişince çağrılır; böylece
/// eklenen, silinen ya da ödendi işaretlenen ödeme bir sonraki açılışta
/// takvime yansır.
/// </summary>
public sealed class PaymentReminderCoordinator(
    CoinFlowService service,
    ProfileService profiles,
    IPaymentReminderScheduler scheduler)
{
    public async Task<IReadOnlyList<PaymentReminder>> RefreshAsync()
    {
        if (profiles.ActiveProfile is not { } profile)
        {
            return [];
        }

        var reminders = await service.GetPaymentRemindersAsync(DateTime.Now);
        scheduler.Replace(profile.Id, reminders);
        return reminders;
    }

    /// <summary>Silinen profilin bildirimleri de silinir.</summary>
    public void Forget(Guid profileId) => scheduler.Replace(profileId, []);
}
