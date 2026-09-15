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
