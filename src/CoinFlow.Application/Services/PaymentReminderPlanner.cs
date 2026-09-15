using System.Globalization;
using CoinFlow.Application.Models;

namespace CoinFlow.Application.Services;

/// <summary>
/// Ödemelerden bildirim takvimi üretir. Saatler telefonun yerel saatidir.
/// </summary>
/// <remarks>
/// Rahat: ödeme günü 09:00'da tek bildirim. Agresif: 3 gün önce 10:00,
/// bir gün önce 20:00, ödeme günü 09:00 ve 18:00. Geçmiş saatler kurulmaz;
/// aynı güne düşen ödemeler tek bildirimde toplanır ki bildirim yağmasın.
/// </remarks>
public static class PaymentReminderPlanner
{
    /// <summary>Kaç gün ilerisi kurulur. Uygulama her açıldığında yenilenir.</summary>
    public const int HorizonDays = 35;

    private const int NamesInMessage = 3;

    private static readonly CultureInfo TurkishCulture =
        CultureInfo.GetCultureInfo("tr-TR");

    private sealed record Slot(
        string Code,
        int DaysBefore,
        TimeOnly At,
        string Title);

    private static readonly Slot DayOf = new("gun", 0, new TimeOnly(9, 0), "Bugün ödeme günü");

    private static readonly IReadOnlyList<Slot> Relaxed = [DayOf];

    private static readonly IReadOnlyList<Slot> Aggressive =
    [
        new("3gun", 3, new TimeOnly(10, 0), "3 gün sonra ödeme var"),
        new("1gun", 1, new TimeOnly(20, 0), "Yarın ödeme günü"),
        DayOf,
        new("aksam", 0, new TimeOnly(18, 0), "Ödemeyi unutma, bugün son gün")
    ];

    /// <summary>Kullanıcıya gösterilen davranış açıklaması.</summary>
    public static string Describe(PaymentReminderMode mode) => mode switch
    {
        PaymentReminderMode.Relaxed =>
            "Ödeme günü sabah 09:00'da tek bildirim.",
        PaymentReminderMode.Aggressive =>
            "3 gün önce, bir gün önce akşam 20:00, ödeme günü sabah 09:00 ve akşam 18:00'de; ödeme başına dört bildirim.",
        _ => "Ödeme günlerinde bildirim gönderilmez."
    };

    public static IReadOnlyList<PaymentReminder> Plan(
        PaymentReminderMode mode,
        IEnumerable<PaymentDue> dues,
        DateTime now,
        int horizonDays = HorizonDays)
    {
        var slots = mode switch
        {
            PaymentReminderMode.Relaxed => Relaxed,
            PaymentReminderMode.Aggressive => Aggressive,
            _ => []
        };
        if (slots.Count == 0)
        {
            return [];
        }

        var today = DateOnly.FromDateTime(now);
        var last = today.AddDays(horizonDays);
        return dues
            .Where(x => x.DueDate >= today && x.DueDate <= last)
            .GroupBy(x => x.Key)
            .Select(x => x.First())
            .GroupBy(x => x.DueDate)
            .SelectMany(day =>
            {
                var payments = day
                    .OrderByDescending(x => x.Amount ?? 0m)
                    .ThenBy(x => x.Name, StringComparer.Create(TurkishCulture, false))
                    .ToArray();
                return slots.Select(slot => Build(day.Key, payments, slot));
            })
            .Where(x => x.NotifyAt > now)
            .OrderBy(x => x.NotifyAt)
            .ToArray();
    }

    private static PaymentReminder Build(
        DateOnly dueDate,
        IReadOnlyList<PaymentDue> payments,
        Slot slot)
    {
        var notifyAt = dueDate
            .AddDays(-slot.DaysBefore)
            .ToDateTime(slot.At);
        var what = payments.Count == 1
            ? $"{payments[0].Name} · {AmountText(payments[0].Amount)}"
            : $"{payments.Count} ödeme · toplam {Money(payments.Sum(x => x.Amount ?? 0m))}: {Names(payments)}";
        var message = slot.DaysBefore == 0
            ? what
            : $"{what} · {dueDate.ToString("d MMMM dddd", TurkishCulture)}";
        return new PaymentReminder(
            $"{dueDate:yyyyMMdd}-{slot.Code}",
            notifyAt,
            slot.Title,
            message,
            dueDate,
            payments);
    }

    private static string Names(IReadOnlyList<PaymentDue> payments)
    {
        var names = string.Join(", ", payments.Take(NamesInMessage).Select(x => x.Name));
        return payments.Count > NamesInMessage
            ? $"{names} ve {payments.Count - NamesInMessage} ödeme daha"
            : names;
    }

    private static string AmountText(decimal? amount) =>
        amount is decimal value
            ? Money(value)
            : "tutarı henüz belli değil";

    private static string Money(decimal value) =>
        $"{value.ToString("N2", TurkishCulture)} TL";
}
