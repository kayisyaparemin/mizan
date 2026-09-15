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
    private static readonly PaymentReminderBoard Empty =
        new(PaymentReminderMode.Off, [], [], [], [], null);

    /// <summary>
    /// Kartın verisini okur ve telefondaki bildirimleri onunla eşitler:
    /// "Ödedim" denen ödemenin kalan bildirimleri kalkar, ertelenenin
    /// yeniden hatırlatması kurulur.
    /// </summary>
    public async Task<PaymentReminderBoard> RefreshAsync()
    {
        if (profiles.ActiveProfile is not { } profile)
        {
            return Empty;
        }

        var board = await service.GetPaymentReminderBoardAsync(DateTime.Now);
        scheduler.Replace(profile.Id, board.Reminders);
        return board;
    }

    public Task SaveModeAsync(PaymentReminderMode mode) =>
        service.SavePaymentReminderModeAsync(mode);

    /// <summary>Karttan gelen cevap: ertelenen ödeme için "Evet, ödedim".</summary>
    public Task AnswerAsync(
        PaymentReminderAnswerKind kind,
        IReadOnlyList<PaymentDue> payments) =>
        service.RecordPaymentReminderAnswerAsync(new PaymentReminderAnswer(
            kind,
            DateTime.Now,
            kind == PaymentReminderAnswerKind.Snoozed
                ? PaymentReminderPlanner.SnoozeUntil(DateTime.Now)
                : null,
            payments));

    public Task UndoAsync(string dueKey) =>
        service.UndoPaymentReminderAnswerAsync(dueKey);

    /// <summary>Silinen profilin bildirimleri de silinir.</summary>
    public void Forget(Guid profileId) => scheduler.Replace(profileId, []);
}
