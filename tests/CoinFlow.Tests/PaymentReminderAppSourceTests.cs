namespace CoinFlow.Tests;

/// <summary>
/// Ödeme hatırlatıcısının Android tarafı ve ekran yerleşimi. App projesi
/// Android'e bağlı olduğu için sözleşme kaynaktan okunur.
/// </summary>
public sealed class PaymentReminderAppSourceTests
{
    [Fact]
    public void Manifest_AsksForNotifications_AndSurvivesReboot_WithoutExactAlarms()
    {
        var manifest = Read("Platforms", "Android", "AndroidManifest.xml");

        Assert.Contains("android.permission.POST_NOTIFICATIONS", manifest);
        Assert.Contains("android.permission.RECEIVE_BOOT_COMPLETED", manifest);
        // Android 14'te kesin alarm ayrı kullanıcı izni ister; gerekmiyor.
        Assert.DoesNotContain("SCHEDULE_EXACT_ALARM", manifest);
        Assert.DoesNotContain("USE_EXACT_ALARM", manifest);
    }

    [Fact]
    public void Receivers_HaveStableJavaNames_AndTheBootReceiverListensForRebootAndUpdate()
    {
        var source = Read("Platforms", "Android", "PaymentReminders.cs");

        Assert.Contains("Name = \"com.coinflow.mobile.PaymentReminderReceiver\"", source);
        Assert.Contains("Name = \"com.coinflow.mobile.PaymentReminderBootReceiver\"", source);
        Assert.Contains("Intent.ActionBootCompleted, Intent.ActionMyPackageReplaced", source);
        // Kesin alarm izni yoksa (Android 12+) on dakikalık pencere; bir saatlik
        // pencere alan SetAndAllowWhileIdle kullanılmaz.
        Assert.Contains("CanScheduleExactAlarms()", source);
        Assert.Contains("alarms.SetWindow(", source);
        Assert.DoesNotContain("alarms.SetAndAllowWhileIdle(", source);
        // İstek kodu yeniden başlatmadan sonra aynı olmalı; GetHashCode değil.
        Assert.DoesNotContain("GetHashCode", source);
        Assert.Contains("FNV-1a", source);
    }

    [Fact]
    public void HomeAndCurrentPeriodDetail_HostTheSameReminderCard()
    {
        var home = Read("Pages", "MainPage.xaml");
        var detail = Read("Pages", "SalaryPeriodDetailPage.xaml");
        var dashboard = Read("ViewModels", "DashboardViewModel.cs");
        var detailViewModel = Read("ViewModels", "SalaryPeriodDetailViewModel.cs");

        Assert.Contains("<controls:PaymentReminderCardView BindingContext=\"{Binding Reminders}\" />", home);
        Assert.Contains("<controls:PaymentReminderCardView BindingContext=\"{Binding Reminders}\" />", detail);
        // Kart yalnız Ana Sayfa'dan açılan mevcut dönemde; görünürlük dıştaki
        // elemanda, yoksa IsVisible Reminders'a bakardı.
        Assert.Contains("<ContentView IsVisible=\"{Binding ShowReminders}\">", detail);
        Assert.Contains("IsCurrentPeriod: true", dashboard);
        Assert.Contains("ShowReminders = request.IsCurrentPeriod;", detailViewModel);
        // Ana Sayfa her açıldığında telefondaki bildirimler planla eşitlenir.
        Assert.Contains("await Reminders.LoadAsync();", dashboard);
    }

    [Fact]
    public void Card_OffersThreeModes_AsksPermissionOnlyWhenTurnedOn()
    {
        var card = Read("ViewModels", "PaymentReminderCardViewModel.cs");
        var view = Read("Controls", "PaymentReminderCardView.xaml");

        Assert.Contains("new(PaymentReminderMode.Off, \"Kapalı\")", card);
        Assert.Contains("new(PaymentReminderMode.Relaxed, \"Rahat\")", card);
        Assert.Contains("new(PaymentReminderMode.Aggressive, \"Agresif\")", card);
        Assert.Contains("option.Mode != PaymentReminderMode.Off", card);
        Assert.Contains("RequestPermissionAsync()", card);
        Assert.Contains("OpenNotificationSettingsCommand", view);
        Assert.Contains("BindableLayout.ItemsSource=\"{Binding Modes}\"", view);
    }

    [Fact]
    public void DeletedProfile_ForgetsItsReminders()
    {
        var profiles = Read("ViewModels", "ProfileSelectionViewModel.cs");

        Assert.Contains("reminders.Forget(profile.Id);", profiles);
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(
            [RepositoryRoot(), "src", "CoinFlow.App", .. parts]));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "CoinFlow.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
               throw new DirectoryNotFoundException(
                   "CoinFlow repository root was not found.");
    }
}
