using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
/// Ödeme günü hatırlatıcısı kartı. Ana Sayfa'da ve oradan açılan "Bu dönem
/// nasıl oluşuyor" ekranında aynı kart durur; ikisi aynı profil ayarını
/// okuyup yazar.
/// </summary>
public sealed partial class PaymentReminderCardViewModel(
    CoinFlowService service,
    PaymentReminderCoordinator coordinator,
    IPaymentReminderScheduler scheduler) : ViewModelBase
{
    private const int PreviewCount = 3;

    public IReadOnlyList<ReminderModeOption> Modes { get; } =
    [
        new(PaymentReminderMode.Off, "Kapalı"),
        new(PaymentReminderMode.Relaxed, "Rahat"),
        new(PaymentReminderMode.Aggressive, "Agresif")
    ];

    public ObservableCollection<ReminderPreviewLine> Upcoming { get; } = [];

    [ObservableProperty] private string modeDescription =
        PaymentReminderPlanner.Describe(PaymentReminderMode.Off);
    [ObservableProperty] private string summaryText = string.Empty;
    [ObservableProperty] private bool hasSummary;
    [ObservableProperty] private bool hasUpcoming;
    [ObservableProperty] private bool showPermissionWarning;

    private PaymentReminderMode _mode;

    public async Task LoadAsync()
    {
        try
        {
            Apply(await service.GetPaymentReminderModeAsync());
            Present(await coordinator.RefreshAsync());
            SetStatus(string.Empty);
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
            await service.SavePaymentReminderModeAsync(option.Mode);
            Apply(option.Mode);
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

    [RelayCommand]
    private void OpenNotificationSettings() =>
        scheduler.OpenNotificationSettings();

    private void Apply(PaymentReminderMode mode)
    {
        _mode = mode;
        foreach (var option in Modes)
        {
            option.IsSelected = option.Mode == mode;
        }

        ModeDescription = PaymentReminderPlanner.Describe(mode);
    }

    private void Present(IReadOnlyList<PaymentReminder> reminders)
    {
        Upcoming.Clear();
        // Bildirimin başlığı çaldığı ana göre yazılır; kart bugüne göre konuşur.
        foreach (var day in PaymentReminderPlanner.Preview(reminders, DateTime.Now).Take(PreviewCount))
        {
            Upcoming.Add(new ReminderPreviewLine(day.When, day.What, day.Schedule));
        }

        HasUpcoming = Upcoming.Count > 0;
        ShowPermissionWarning = _mode != PaymentReminderMode.Off &&
                                !scheduler.AreNotificationsAllowed;
        SummaryText = _mode == PaymentReminderMode.Off
            ? string.Empty
            : reminders.Count == 0
                ? $"Önümüzdeki {PaymentReminderPlanner.HorizonDays} günde hatırlatılacak ödeme yok."
                : $"Önümüzdeki {PaymentReminderPlanner.HorizonDays} gün için {reminders.Count} bildirim kuruldu. Sıradaki ödemeler:";
        HasSummary = SummaryText.Length > 0;
    }
}
