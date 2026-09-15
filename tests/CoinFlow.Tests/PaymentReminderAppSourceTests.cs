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
    public void CardPreview_IsGroupedByDueDay_AndDoesNotEchoTheNotificationTitle()
    {
        var card = Read("ViewModels", "PaymentReminderCardViewModel.cs");
        var view = Read("Controls", "PaymentReminderCardView.xaml");

        // Bildirim başlığı ("Bugün ödeme günü") çaldığı ana göre yazılır;
        // kart onu bugünkü önizlemede göstermemeli.
        // Önizleme servisin kurduğu gün satırlarından gelir (PaymentReminderPlanner.Preview).
        Assert.Contains("board.Upcoming", card);
        Assert.DoesNotContain(".Title", card);
        Assert.DoesNotContain("{Binding Title}", view);
    }

    [Fact]
    public void SnoozedPayments_AreTranslucentRedInTheCard_AndAskIfPaidWhenTapped()
    {
        var card = Read("ViewModels", "PaymentReminderCardViewModel.cs");
        var view = Read("Controls", "PaymentReminderCardView.xaml");
        var colors = Read("Resources", "Styles", "Colors.xaml");

        Assert.Contains("BindableLayout.ItemsSource=\"{Binding Snoozed}\"", view);
        Assert.Contains("BackgroundColor=\"{StaticResource SnoozedSurface}\"", view);
        Assert.Contains("BindingContext.ResolveSnoozedCommand", view);
        Assert.Contains("\"Ertelendi\"", card);
        Assert.Contains("\"Bu ödeme yapıldı mı?\"", card);
        Assert.Contains("\"Tebrikler! 🎉\"", card);
        Assert.Contains("PaymentReminderAnswerKind.Paid", card);
        // Saydam: #AARRGGBB, alfa FF değil.
        Assert.Matches("<Color x:Key=\"SnoozedSurface\">#[0-9A-E][0-9A-F]E0", colors);
        Assert.Matches("<Color x:Key=\"PaidSurface\">#[0-9A-E][0-9A-F]40A6", colors);
    }

    [Fact]
    public void PaidPayments_AreTranslucentGreen_OutsideTheReminderCard_OnHomeAndCurrentPeriodDetail()
    {
        var paid = Read("Controls", "PaymentReminderPaidView.xaml");
        var card = Read("Controls", "PaymentReminderCardView.xaml");
        var home = Read("Pages", "MainPage.xaml");
        var detail = Read("Pages", "SalaryPeriodDetailPage.xaml");

        Assert.Contains("BackgroundColor=\"{StaticResource PaidSurface}\"", paid);
        Assert.Contains("BindingContext.UndoPaidCommand", paid);
        // Hatırlatıcı kartının listesiyle karışmaz.
        Assert.DoesNotContain("Binding Paid}", card);
        const string paidView = "<controls:PaymentReminderPaidView BindingContext=\"{Binding Reminders}\" />";
        Assert.Contains(paidView, home);
        Assert.True(
            home.IndexOf(paidView, StringComparison.Ordinal) <
            home.IndexOf("<controls:PaymentReminderCardView", StringComparison.Ordinal));
        Assert.Contains($"<ContentView IsVisible=\"{{Binding ShowReminders}}\">\r\n                    {paidView}", detail.Replace("\r\n", "\n").Replace("\n", "\r\n"));
    }

    [Fact]
    public void Answers_RecalculateHome_KeepSnoozedInRemaining_AndOpenAsUnpaidInTheReviewWizard()
    {
        var dashboard = Read("ViewModels", "DashboardViewModel.cs");
        var wizard = Read("ViewModels", "PeriodReviewWizardViewModel.cs");

        Assert.Contains("Reminders.AnswersChanged += async (_, _) => await LoadAsync();", dashboard);
        Assert.Contains("progress.IsSnoozed(line.Id)", dashboard);
        Assert.Contains("GetPaymentReminderResponsesAsync()", wizard);
        Assert.Contains("\"Hatırlatıcıda ertelendi\"", wizard);
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
