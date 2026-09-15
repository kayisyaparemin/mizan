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

/// <summary>Sıradaki bildirimlerden biri: ne zaman, ne yazacak.</summary>
public sealed record ReminderPreviewLine(string When, string Title, string Message);

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
        foreach (var reminder in reminders.Take(PreviewCount))
        {
            Upcoming.Add(new ReminderPreviewLine(
                reminder.NotifyAt.ToString("d MMMM dddd · HH:mm", TurkishCulture),
                reminder.Title,
                reminder.Message));
        }

        HasUpcoming = Upcoming.Count > 0;
        ShowPermissionWarning = _mode != PaymentReminderMode.Off &&
                                !scheduler.AreNotificationsAllowed;
        SummaryText = _mode == PaymentReminderMode.Off
            ? string.Empty
            : reminders.Count == 0
                ? $"Önümüzdeki {PaymentReminderPlanner.HorizonDays} günde hatırlatılacak ödeme yok."
                : $"Önümüzdeki {PaymentReminderPlanner.HorizonDays} gün için {reminders.Count} bildirim kuruldu. Sıradakiler:";
        HasSummary = SummaryText.Length > 0;
    }
}
