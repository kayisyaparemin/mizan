using Mizan.Application.Models;
using Mizan.Application.Services;

namespace Mizan.App.Services;

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

    /// <summary>Bildirimi alarm kurmadan hemen gösterir ("Deneme bildirimi gönder").</summary>
    void ShowNow(Guid profileId, PaymentReminder reminder);

    /// <summary>
    /// Bildirimdeki "Ödedim" / "Ertele" düğmeleriyle gelen, henüz veritabanına
    /// işlenmemiş cevaplar; geliş sırasıyla.
    /// </summary>
    IReadOnlyList<PaymentReminderAnswer> ReadAnswers(Guid profileId);

    /// <summary>Profilin işlenen ilk <paramref name="count"/> cevabını kuyruktan siler.</summary>
    void RemoveAnswers(Guid profileId, int count);

    void OpenNotificationSettings();
}

/// <summary>
/// Bildirimdeki bir düğmeye basıldı. Uygulama açıksa kart ve Ana Sayfa
/// kuyruğu hemen işler; kapalıysa bir sonraki açılışta işlenir.
/// </summary>
public sealed record PaymentReminderAnsweredMessage;

/// <summary>
/// Açık profilin hatırlatıcılarını telefondaki alarmlarla eşitler. Ana Sayfa
/// her açıldığında ve hatırlatıcı davranışı değişince çağrılır; böylece
/// eklenen, silinen ya da ödendi işaretlenen ödeme bir sonraki açılışta
/// takvime yansır.
/// </summary>
public sealed class PaymentReminderCoordinator(
    MizanService service,
    ProfileService profiles,
    IPaymentReminderScheduler scheduler)
{
    private static readonly PaymentReminderBoard Empty =
        new(PaymentReminderMode.Off, [], [], [], [], null);

    /// <summary>
    /// Kartın verisini okur ve telefondaki bildirimleri onunla eşitler:
    /// "Ödedim" denen ödemenin kalan bildirimleri kalkar, ertelenenin
    /// yeniden hatırlatması kurulur. Önce bildirimden gelen cevaplar işlenir.
    /// </summary>
    public async Task<PaymentReminderBoard> RefreshAsync()
    {
        if (profiles.ActiveProfile is not { } profile)
        {
            return Empty;
        }

        await ApplyPendingAnswersAsync();
        var board = await service.GetPaymentReminderBoardAsync(DateTime.Now);
        scheduler.Replace(profile.Id, board.Reminders);
        return board;
    }

    /// <summary>
    /// Bildirim düğmeleriyle kuyruğa yazılan cevapları deftere işler. Kayıt
    /// tekrarlanabilir (aynı anahtar üzerine yazılır); kuyruktan ancak
    /// yazıldıktan sonra silinir, arada uygulama kapanırsa cevap kaybolmaz.
    /// </summary>
    public async Task<int> ApplyPendingAnswersAsync()
    {
        if (profiles.ActiveProfile is not { } profile)
        {
            return 0;
        }

        var answers = scheduler.ReadAnswers(profile.Id);
        foreach (var answer in answers)
        {
            await service.RecordPaymentReminderAnswerAsync(answer);
        }

        if (answers.Count > 0)
        {
            scheduler.RemoveAnswers(profile.Id, answers.Count);
        }

        return answers.Count;
    }

    /// <summary>
    /// Sıradaki ödeme gününün bildirimini hemen gösterir. Düğmeleri gerçektir:
    /// "Ödedim" gerçekten ödendi işaretler (Ödediklerin'den geri alınır).
    /// </summary>
    public async Task<PaymentReminder?> SendSampleAsync()
    {
        if (profiles.ActiveProfile is not { } profile)
        {
            return null;
        }

        var board = await service.GetPaymentReminderBoardAsync(DateTime.Now);
        if (board.Sample is { } sample)
        {
            scheduler.ShowNow(profile.Id, sample);
        }

        return board.Sample;
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

    /// <summary>Silinen profilin bildirimleri ve işlenmemiş cevapları da silinir.</summary>
    public void Forget(Guid profileId)
    {
        scheduler.Replace(profileId, []);
        scheduler.RemoveAnswers(profileId, int.MaxValue);
    }
}
