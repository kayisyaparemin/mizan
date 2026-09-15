using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CoinFlow.App.Services;
using CoinFlow.Application.Models;
using CoinFlow.Application.Services;

namespace CoinFlow.App.ViewModels;

/// <summary>Hatırlatıcı davranışı çipi: Kapalı · Rahat · Agresif.</summary>
public sealed partial class ReminderModeOption(PaymentReminderMode mode, string label)
    : ObservableObject
{
    public PaymentReminderMode Mode { get; } = mode;
    public string Label { get; } = label;

    [ObservableProperty]
    private bool isSelected;
}

/// <summary>
/// Sıradaki ödeme günlerinden biri: bugüne göre ne zaman, hangi ödeme ve
/// hangi saatlerde bildirim gelecek.
/// </summary>
public sealed record ReminderPreviewLine(string When, string What, string Schedule);

/// <summary>
/// Hatırlatıcıya cevap verilmiş bir ödeme: ertelenen (kartta saydam kırmızı)
/// ya da ödenen ("Ödediklerin" listesinde saydam yeşil).
/// </summary>
public sealed record ReminderAnswerLine(
    PaymentReminderResponse Response,
    string When,
    string What,
    string Detail,
    string Tag);

/// <summary>
/// Ödeme günü hatırlatıcısı kartı. Ana Sayfa'da ve oradan açılan "Bu dönem
/// nasıl oluşuyor" ekranında aynı kart durur; ikisi aynı profil ayarını
/// okuyup yazar.
/// </summary>
public sealed partial class PaymentReminderCardViewModel(
    PaymentReminderCoordinator coordinator,
    IPaymentReminderScheduler scheduler,
    IUserFeedbackService feedback) : ViewModelBase
{
    private const int PreviewCount = 3;

    /// <summary>
    /// Ödemelerin ödenmiş sayılıp sayılmadığı değişti. Ana Sayfa kalan
    /// ödemelerini ve dönem sonunu yeniden hesaplar.
    /// </summary>
    public event EventHandler? AnswersChanged;

    // Ertelenen bir ödeme "Evet, ödedim" denince. Ödemeyi zamanında yapmak
    // küçük bir kutlamayı hak ediyor.
    private static readonly string[] Congratulations =
    [
        "Ödemeleri yapmak böyledir: zamanında, dert etmeden. Listeden kaldırdım.",
        "Bir ödeme daha tamam! Cüzdanın biraz hafifledi ama içi rahatladı.",
        "Borç beklemez, sen de bekletmedin. Listeden kaldırdım.",
        "Zamanında yapılan ödeme, faizsiz uyunan gecedir. Aferin!",
        "Harika! Planın bir adım daha gerçeğe döndü."
    ];

    public IReadOnlyList<ReminderModeOption> Modes { get; } =
    [
        new(PaymentReminderMode.Off, "Kapalı"),
        new(PaymentReminderMode.Relaxed, "Rahat"),
        new(PaymentReminderMode.Aggressive, "Agresif")
    ];

    public ObservableCollection<ReminderPreviewLine> Upcoming { get; } = [];

    /// <summary>Bildirimde "Ertele" denenler; dokununca "ödedin mi?" sorulur.</summary>
    public ObservableCollection<ReminderAnswerLine> Snoozed { get; } = [];

    /// <summary>
    /// Hatırlatıcıdan "Ödedim" denenler. Kartın içinde değil, ayrı
    /// "Ödediklerin" listesinde gösterilir; dokununca geri alınabilir.
    /// </summary>
    public ObservableCollection<ReminderAnswerLine> Paid { get; } = [];

    [ObservableProperty] private string modeDescription =
        PaymentReminderPlanner.Describe(PaymentReminderMode.Off);
    [ObservableProperty] private string summaryText = string.Empty;
    [ObservableProperty] private bool hasSummary;
    [ObservableProperty] private bool hasUpcoming;
    [ObservableProperty] private bool hasSnoozed;
    [ObservableProperty] private bool hasPaid;
    [ObservableProperty] private bool showPermissionWarning;
    [ObservableProperty] private bool canSendSample;
    [ObservableProperty] private string infoText = string.Empty;
    [ObservableProperty] private bool hasInfo;

    private PaymentReminderMode _mode;
    private bool _listensToNotificationAnswers;

    /// <summary>
    /// Bildirim düğmeleriyle gelen cevapları deftere işler. Ana Sayfa kalan
    /// ödemelerini hesaplamadan önce çağırır.
    /// </summary>
    public async Task ApplyPendingAnswersAsync()
    {
        try
        {
            await coordinator.ApplyPendingAnswersAsync();
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(
                exception,
                "Bildirimden gelen cevap kaydedilemedi."));
        }
    }

    public async Task LoadAsync()
    {
        if (!_listensToNotificationAnswers)
        {
            // Uygulama açıkken bildirimde düğmeye basılırsa ekran hemen
            // güncellenir. Zayıf referans: kapanan dönem detayı sızmaz.
            _listensToNotificationAnswers = true;
            WeakReferenceMessenger.Default.Register<PaymentReminderCardViewModel, PaymentReminderAnsweredMessage>(
                this,
                static (card, _) => MainThread.BeginInvokeOnMainThread(card.OnNotificationAnswered));
        }

        try
        {
            Present(await coordinator.RefreshAsync());
            SetStatus(string.Empty);
            // "Deneme bildirimi gönderildi" bir sonraki yenilemede eskir.
            SetInfo(string.Empty);
        }
        catch (Exception exception)
        {
            // Hatırlatıcı kurulamadı diye ekran bozulmaz; kartta söylenir.
            SetStatus(UserFacingMessages.FromException(
                exception,
                "Hatırlatıcılar güncellenemedi."));
        }
    }

    [RelayCommand]
    private async Task SelectModeAsync(ReminderModeOption? option)
    {
        if (option is null || option.Mode == _mode || IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await coordinator.SaveModeAsync(option.Mode);
            if (option.Mode != PaymentReminderMode.Off &&
                !scheduler.AreNotificationsAllowed)
            {
                await scheduler.RequestPermissionAsync();
            }

            Present(await coordinator.RefreshAsync());
            SetStatus(string.Empty);
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(
                exception,
                "Hatırlatıcı kaydedilemedi."));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Ertelenen satıra dokunuldu: "Bu ödeme yapıldı mı?"</summary>
    [RelayCommand]
    private async Task ResolveSnoozedAsync(ReminderAnswerLine? line)
    {
        if (line is null || IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var paid = await feedback.ConfirmAsync(
                "Bu ödeme yapıldı mı?",
                $"{line.What}\n{line.When}",
                "Evet, ödedim",
                "Henüz değil");
            if (!paid)
            {
                return;
            }

            var response = line.Response;
            await coordinator.AnswerAsync(
                PaymentReminderAnswerKind.Paid,
                [new PaymentDue(response.DueKey, response.Name, response.DueDate, response.Amount)]);
            await feedback.ShowSuccessAsync(
                Congratulations[Random.Shared.Next(Congratulations.Length)],
                "Tebrikler! 🎉",
                "Süper");
            Present(await coordinator.RefreshAsync());
            SetStatus(string.Empty);
            AnswersChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(
                exception,
                "Ödeme kaydedilemedi."));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>"Ödediklerin" satırına dokunuldu: yanlışlıkla basıldıysa geri al.</summary>
    [RelayCommand]
    private async Task UndoPaidAsync(ReminderAnswerLine? line)
    {
        if (line is null || IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var undo = await feedback.ConfirmAsync(
                "Ödendi işareti kaldırılsın mı?",
                $"{line.What}\nÖdeme yeniden kalan ödemelerine döner ve vadesinde hatırlatılır.",
                "Geri al",
                "Vazgeç");
            if (!undo)
            {
                return;
            }

            await coordinator.UndoAsync(line.Response.DueKey);
            Present(await coordinator.RefreshAsync());
            SetStatus(string.Empty);
            AnswersChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(
                exception,
                "Ödendi işareti kaldırılamadı."));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// "Deneme bildirimi gönder": sıradaki ödeme gününün bildirimi hemen düşer,
    /// düğmeleri denenebilir. Ödeme gününü beklemeden akışı görmek için.
    /// </summary>
    [RelayCommand]
    private async Task SendSampleAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            SetInfo(string.Empty);
            if (!scheduler.AreNotificationsAllowed &&
                !await scheduler.RequestPermissionAsync())
            {
                ShowPermissionWarning = true;
                return;
            }

            var sample = await coordinator.SendSampleAsync();
            SetInfo(sample is null
                ? "Hatırlatılacak ödeme yok; deneme bildirimi gönderilmedi."
                : "Deneme bildirimi gönderildi; bildirim çubuğuna bak. \"Ödedim\" gerçekten ödendi işaretler, Ödediklerin listesinden geri alabilirsin.");
            SetStatus(string.Empty);
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(
                exception,
                "Deneme bildirimi gönderilemedi."));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenNotificationSettings() =>
        scheduler.OpenNotificationSettings();

    private async void OnNotificationAnswered()
    {
        await LoadAsync();
        AnswersChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetInfo(string message)
    {
        InfoText = message;
        HasInfo = message.Length > 0;
    }

    private void Present(PaymentReminderBoard board)
    {
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        _mode = board.Mode;
        foreach (var option in Modes)
        {
            option.IsSelected = option.Mode == board.Mode;
        }

        ModeDescription = PaymentReminderPlanner.Describe(board.Mode);

        Upcoming.Clear();
        // Bildirimin başlığı çaldığı ana göre yazılır; kart bugüne göre konuşur.
        foreach (var day in board.Upcoming.Take(PreviewCount))
        {
            Upcoming.Add(new ReminderPreviewLine(day.When, day.What, day.Schedule));
        }

        Snoozed.Clear();
        foreach (var response in board.Snoozed)
        {
            Snoozed.Add(new ReminderAnswerLine(
                response,
                DueText(response, today),
                WhatText(response),
                response.SnoozedUntil is { } until && until > now && board.Mode != PaymentReminderMode.Off
                    ? $"{PaymentReminderPlanner.SnoozeText(until, now)} · ödediysen dokun"
                    : "Ödediysen dokun",
                "Ertelendi"));
        }

        Paid.Clear();
        foreach (var response in board.Paid)
        {
            Paid.Add(new ReminderAnswerLine(
                response,
                DueText(response, today),
                WhatText(response),
                $"{response.AnsweredAt.ToString("d MMMM HH:mm", TurkishCulture)} işaretlendi · geri almak için dokun",
                "Ödendi"));
        }

        HasUpcoming = Upcoming.Count > 0;
        CanSendSample = board.Mode != PaymentReminderMode.Off && board.Sample is not null;
        HasSnoozed = Snoozed.Count > 0;
        HasPaid = Paid.Count > 0;
        ShowPermissionWarning = _mode != PaymentReminderMode.Off &&
                                !scheduler.AreNotificationsAllowed;
        SummaryText = _mode == PaymentReminderMode.Off
            ? string.Empty
            : board.Reminders.Count == 0
                ? $"Önümüzdeki {PaymentReminderPlanner.HorizonDays} günde hatırlatılacak ödeme yok."
                : $"Önümüzdeki {PaymentReminderPlanner.HorizonDays} gün için {board.Reminders.Count} bildirim kuruldu. Sıradaki ödemeler:";
        HasSummary = SummaryText.Length > 0;
    }

    private static string DueText(PaymentReminderResponse response, DateOnly today) =>
        $"{response.DueDate.ToString("d MMMM dddd", TurkishCulture)} · {PaymentReminderPlanner.RelativeDay(response.DueDate, today)}";

    private static string WhatText(PaymentReminderResponse response) =>
        response.Amount is { } amount
            ? $"{response.Name} · {Money(amount, 2)}"
            : response.Name;
}
