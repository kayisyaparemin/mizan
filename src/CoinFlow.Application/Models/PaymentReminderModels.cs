namespace CoinFlow.Application.Models;

/// <summary>Ödeme günü hatırlatıcısının davranışı. Profil başına saklanır.</summary>
public enum PaymentReminderMode
{
    Off = 0,
    /// <summary>Ödeme günü sabahı tek bildirim.</summary>
    Relaxed = 1,
    /// <summary>Üç gün önce, bir gün önce akşam, ödeme günü sabah ve akşam.</summary>
    Aggressive = 2
}

/// <summary>Hatırlatılacak bir ödeme: kaynağı ve vadesiyle tekil.</summary>
public sealed record PaymentDue(
    string Key,
    string Name,
    DateOnly DueDate,
    decimal? Amount);

/// <summary>
/// Telefonda kurulacak tek bildirim. Aynı güne düşen ödemeler tek bildirimde
/// toplanır; <see cref="Key"/> gün ve saat diliminden türer, profil içinde
/// tekildir.
/// </summary>
public sealed record PaymentReminder(
    string Key,
    DateTime NotifyAt,
    string Title,
    string Message,
    DateOnly DueDate,
    IReadOnlyList<PaymentDue> Payments);

/// <summary>
/// Kartta gösterilen bir ödeme günü. Bildirimin başlığı çaldığı ana göre
/// yazılır ("Bugün ödeme günü"); kart ise bugüne göre konuşur ("3 gün sonra").
/// </summary>
public sealed record PaymentReminderDay(
    DateOnly DueDate,
    string When,
    string What,
    string Schedule,
    IReadOnlyList<PaymentDue> Payments);

/// <summary>Kullanıcının bir hatırlatmaya cevabı: bildirimdeki "Ödedim" / "Ertele".</summary>
public enum PaymentReminderAnswerKind
{
    Snoozed = 1,
    Paid = 2
}

/// <summary>
/// Hatırlatıcı defterinin bir satırı. <see cref="DueKey"/> ödemenin kaynağı ve
/// vadesinden türer (<c>PaymentReminderPlanner.DueKey</c>); plan satırının
/// kimliğine bağlı değildir. Plan revizyonu satır kimliklerini yeniler, dönem
/// kapanışı yeni plan kurar — anahtar ikisinden de etkilenmez.
/// </summary>
public sealed record PaymentReminderResponse(
    string DueKey,
    string Name,
    DateOnly DueDate,
    decimal? Amount,
    PaymentReminderAnswerKind Kind,
    DateTime AnsweredAt,
    DateTime? SnoozedUntil);

/// <summary>
/// Bildirimden ya da karttan gelen tek cevap. Aynı güne düşen ödemeler tek
/// bildirimde toplandığı için cevap o bildirimdeki bütün ödemeler içindir.
/// </summary>
public sealed record PaymentReminderAnswer(
    PaymentReminderAnswerKind Kind,
    DateTime AnsweredAt,
    DateTime? SnoozedUntil,
    IReadOnlyList<PaymentDue> Payments);

/// <summary>Hatırlatıcı kartının ve "Ödediklerin" listesinin verisi.</summary>
public sealed record PaymentReminderBoard(
    PaymentReminderMode Mode,
    // Telefonda kurulacak bildirimler (ertelenenlerin yeniden hatırlatması dahil).
    IReadOnlyList<PaymentReminder> Reminders,
    // Kartın "Sıradaki ödemeler" satırları; ertelenenler burada değil.
    IReadOnlyList<PaymentReminderDay> Upcoming,
    IReadOnlyList<PaymentReminderResponse> Snoozed,
    IReadOnlyList<PaymentReminderResponse> Paid,
    // "Deneme bildirimi gönder" için sıradaki ödeme günü; yoksa null.
    PaymentReminder? Sample);
