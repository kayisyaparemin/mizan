using System.Globalization;
using CoinFlow.Application.Models;
using CoinFlow.Application.Services;
using CoinFlow.Domain.Models;
using CoinFlow.Infrastructure.Persistence;
using SQLite;

namespace CoinFlow.Tests;

/// <summary>
/// Ödeme günü hatırlatıcısı: hangi ödeme için, hangi saatte bildirim
/// kurulduğu. Rahat tek bildirim; agresif 3 gün önce, bir gün önce akşam,
/// ödeme günü sabah ve akşam. Saatler telefonun yerel saatidir.
/// </summary>
public sealed class PaymentReminderTests
{
    // 15.09.2026 Salı; 18.09.2026 Cuma.
    private static readonly DateOnly Due = new(2026, 9, 18);

    [Fact]
    public void Off_PlansNothing()
    {
        Assert.Empty(PaymentReminderPlanner.Plan(
            PaymentReminderMode.Off,
            [Payment("Kredi", Due, 1_000m)],
            At(2026, 9, 15, 12, 0)));
    }

    [Fact]
    public void Relaxed_OneNotificationOnTheDueDayMorning()
    {
        var reminder = Assert.Single(PaymentReminderPlanner.Plan(
            PaymentReminderMode.Relaxed,
            [Payment("Taşıt kredisi", Due, 7_374.59m)],
            At(2026, 9, 15, 12, 0)));

        Assert.Equal(At(2026, 9, 18, 9, 0), reminder.NotifyAt);
        Assert.Equal("20260918-gun", reminder.Key);
        Assert.Equal("Bugün ödeme günü", reminder.Title);
        Assert.Equal("Taşıt kredisi · 7.374,59 TL", reminder.Message);
        Assert.Equal(Due, reminder.DueDate);
    }

    [Fact]
    public void Aggressive_FourNotifications_EarlyOnesSayTheDate()
    {
        var reminders = PaymentReminderPlanner.Plan(
            PaymentReminderMode.Aggressive,
            [Payment("Taşıt kredisi", Due, 7_374.59m)],
            At(2026, 9, 15, 9, 30));

        Assert.Equal(
            new[]
            {
                At(2026, 9, 15, 10, 0),
                At(2026, 9, 17, 20, 0),
                At(2026, 9, 18, 9, 0),
                At(2026, 9, 18, 18, 0)
            },
            reminders.Select(x => x.NotifyAt));
        Assert.Equal(
            new[] { "20260918-3gun", "20260918-1gun", "20260918-gun", "20260918-aksam" },
            reminders.Select(x => x.Key));
        Assert.Equal("3 gün sonra ödeme var", reminders[0].Title);
        Assert.Equal("Taşıt kredisi · 7.374,59 TL · 18 Eylül Cuma", reminders[0].Message);
        Assert.Equal("Yarın ödeme günü", reminders[1].Title);
        Assert.Equal("Taşıt kredisi · 7.374,59 TL · 18 Eylül Cuma", reminders[1].Message);
        Assert.Equal("Taşıt kredisi · 7.374,59 TL", reminders[2].Message);
        Assert.Equal("Ödemeyi unutma, bugün son gün", reminders[3].Title);
    }

    [Theory]
    // Saatinden sonra kurulan bildirim çalmaz; tam saatinde de kurulmaz.
    [InlineData("2026-09-15T10:00", 3)]
    [InlineData("2026-09-17T19:59", 3)]
    [InlineData("2026-09-17T20:00", 2)]
    [InlineData("2026-09-18T09:00", 1)]
    [InlineData("2026-09-18T12:00", 1)]
    [InlineData("2026-09-18T18:00", 0)]
    public void PastSlots_AreNotScheduled(string now, int expected)
    {
        var reminders = PaymentReminderPlanner.Plan(
            PaymentReminderMode.Aggressive,
            [Payment("Kredi", Due, 1_000m)],
            DateTime.Parse(now, CultureInfo.InvariantCulture));

        Assert.Equal(expected, reminders.Count);
        Assert.All(reminders, x => Assert.True(
            x.NotifyAt > DateTime.Parse(now, CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void SameDayPayments_ShareOneNotification_LargestFirst()
    {
        var reminder = Assert.Single(PaymentReminderPlanner.Plan(
            PaymentReminderMode.Relaxed,
            [
                Payment("Taşıt kredisi", Due, 7_374.59m),
                Payment("İhtiyaç kredisi", Due, 14_501.23m)
            ],
            At(2026, 9, 15, 12, 0)));

        Assert.Equal(
            "2 ödeme · toplam 21.875,82 TL: İhtiyaç kredisi, Taşıt kredisi",
            reminder.Message);
        Assert.Equal(2, reminder.Payments.Count);
    }

    [Fact]
    public void ManyPayments_NameThreeAndCountTheRest_UnknownAmountsDoNotBreakTheTotal()
    {
        var reminder = Assert.Single(PaymentReminderPlanner.Plan(
            PaymentReminderMode.Relaxed,
            [
                Payment("A", Due, 500m),
                Payment("B", Due, 400m),
                Payment("C", Due, 300m),
                Payment("D", Due, 200m),
                Payment("Kart", Due, null)
            ],
            At(2026, 9, 15, 12, 0)));

        Assert.Equal(
            "5 ödeme · toplam 1.400,00 TL: A, B, C ve 2 ödeme daha",
            reminder.Message);
    }

    [Fact]
    public void UnknownAmount_IsSaidPlainly()
    {
        var reminder = Assert.Single(PaymentReminderPlanner.Plan(
            PaymentReminderMode.Relaxed,
            [Payment("Kart", Due, null)],
            At(2026, 9, 15, 12, 0)));

        Assert.Equal("Kart · tutarı henüz belli değil", reminder.Message);
    }

    [Fact]
    public void Horizon_CoversTodayThroughThirtyFiveDays_AndDropsDuplicates()
    {
        var today = new DateOnly(2026, 9, 15);
        var reminders = PaymentReminderPlanner.Plan(
            PaymentReminderMode.Relaxed,
            [
                Payment("Dün", today.AddDays(-1), 1m),
                Payment("Bugün", today, 1m),
                Payment("Bugün", today, 1m),
                Payment("Sınır", today.AddDays(35), 1m),
                Payment("Ufuk dışı", today.AddDays(36), 1m)
            ],
            At(2026, 9, 15, 8, 0));

        Assert.Equal(
            new[] { today, today.AddDays(35) },
            reminders.Select(x => x.DueDate));
        Assert.Single(reminders[0].Payments);
    }

    [Fact]
    public void Aggressive_EarlyNotificationsCrossMonthAndYearBoundaries()
    {
        var reminders = PaymentReminderPlanner.Plan(
            PaymentReminderMode.Aggressive,
            [Payment("Kira", new DateOnly(2027, 1, 1), 20_000m)],
            At(2026, 12, 1, 0, 0));

        Assert.Equal(
            new[]
            {
                At(2026, 12, 29, 10, 0),
                At(2026, 12, 31, 20, 0),
                At(2027, 1, 1, 9, 0),
                At(2027, 1, 1, 18, 0)
            },
            reminders.Select(x => x.NotifyAt));
        Assert.EndsWith("1 Ocak Cuma", reminders[0].Message);
    }

    [Fact]
    public void Texts_AreTurkish_OnAnEnglishDevice()
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var reminder = PaymentReminderPlanner.Plan(
                PaymentReminderMode.Aggressive,
                [Payment("Kredi", Due, 1_234.5m)],
                At(2026, 9, 15, 0, 0))[0];

            Assert.Equal("Kredi · 1.234,50 TL · 18 Eylül Cuma", reminder.Message);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // ---------------------------------------------------------------
    // Kart önizlemesi: bugüne göre konuşur (15.09'da 18.09 "bugün" değil)
    // ---------------------------------------------------------------

    [Fact]
    public void Preview_SpeaksRelativeToToday_NotToTheNotificationMoment()
    {
        // Kullanıcının ekranı: 15 Eylül'de 18 Eylül satırı "Bugün ödeme günü"
        // diyordu. Bildirimin kendi başlığı doğru (o gün çalınca), kart yanlıştı.
        var now = At(2026, 9, 15, 14, 37);
        var reminders = PaymentReminderPlanner.Plan(
            PaymentReminderMode.Relaxed,
            [
                Payment("Burgan Ameliyat", Due, 7_375m),
                Payment("Eminevim", new DateOnly(2026, 9, 20), 28_167m),
                Payment("Akbank Axess", new DateOnly(2026, 10, 5), 14_000m),
                Payment("Garanti Bonus", new DateOnly(2026, 10, 5), 10_233m)
            ],
            now);

        var days = PaymentReminderPlanner.Preview(reminders, now);

        Assert.Equal(
            new[]
            {
                "18 Eylül Cuma · 3 gün sonra",
                "20 Eylül Pazar · 5 gün sonra",
                "5 Ekim Pazartesi · 20 gün sonra"
            },
            days.Select(x => x.When));
        Assert.All(days, x => Assert.DoesNotContain("Bugün", x.When + x.What + x.Schedule));
        Assert.Equal("Burgan Ameliyat · 7.375,00 TL", days[0].What);
        Assert.Equal("Bildirim: ödeme günü 09:00", days[0].Schedule);
        Assert.Equal("2 ödeme · toplam 24.233,00 TL: Akbank Axess, Garanti Bonus", days[2].What);
    }

    [Fact]
    public void Preview_OnTheDueDay_SaysToday_AndAggressiveListsEveryNotification()
    {
        var reminders = PaymentReminderPlanner.Plan(
            PaymentReminderMode.Aggressive,
            [Payment("Kredi", Due, 1_000m)],
            At(2026, 9, 14, 8, 0));

        var early = Assert.Single(PaymentReminderPlanner.Preview(reminders, At(2026, 9, 14, 8, 0)));
        Assert.Equal("18 Eylül Cuma · 4 gün sonra", early.When);
        Assert.Equal(
            "Bildirimler: 3 gün önce 10:00 · bir gün önce 20:00 · ödeme günü 09:00 ve 18:00",
            early.Schedule);

        var onTheDay = Assert.Single(PaymentReminderPlanner.Preview(
            reminders.Where(x => x.NotifyAt > At(2026, 9, 18, 8, 0)),
            At(2026, 9, 18, 8, 0)));
        Assert.Equal("18 Eylül Cuma · bugün", onTheDay.When);
        Assert.Equal("Bildirimler: ödeme günü 09:00 ve 18:00", onTheDay.Schedule);
    }

    [Theory]
    [InlineData(0, "bugün")]
    [InlineData(1, "yarın")]
    [InlineData(2, "2 gün sonra")]
    [InlineData(-1, "dün")]
    [InlineData(-3, "3 gün önce")]
    public void RelativeDay_NamesTheDayFromToday(int offset, string expected)
    {
        var today = new DateOnly(2026, 9, 15);
        Assert.Equal(expected, PaymentReminderPlanner.RelativeDay(today.AddDays(offset), today));
    }

    [Theory]
    [InlineData(PaymentReminderMode.Off, "gönderilmez")]
    [InlineData(PaymentReminderMode.Relaxed, "tek bildirim")]
    [InlineData(PaymentReminderMode.Aggressive, "dört bildirim")]
    public void EveryMode_HasADescription(PaymentReminderMode mode, string fragment) =>
        Assert.Contains(fragment, PaymentReminderPlanner.Describe(mode));

    // ---------------------------------------------------------------
    // Kalıcılık ve kanonik veriden ödemeler
    // ---------------------------------------------------------------

    [Fact]
    public async Task Mode_DefaultsToOff_PersistsAcrossRestart_AndIsNotPartOfFinanceSettings()
    {
        var path = TempPath();
        try
        {
            await using (var store = new SqliteCoinFlowStore(path, true, new DateOnly(2026, 8, 20)))
            {
                Assert.Equal(PaymentReminderMode.Off, await store.GetPaymentReminderModeAsync());
                var service = TestFactory.Service(store);
                await service.LoadCanonicalDevelopmentDataAsync();
                var settings = await store.GetSettingsAsync();

                await service.SavePaymentReminderModeAsync(PaymentReminderMode.Aggressive);
                // Finans ayarlarını kaydetmek hatırlatıcıyı sıfırlamaz.
                await store.SaveSettingsAsync(settings with { MonthlyLivingBudget = 31_000m });

                Assert.Equal(PaymentReminderMode.Aggressive, await service.GetPaymentReminderModeAsync());
                Assert.Equal(settings with { MonthlyLivingBudget = 31_000m }, await store.GetSettingsAsync());
            }

            await using (var reopened = new SqliteCoinFlowStore(path, true, new DateOnly(2026, 8, 20)))
            {
                Assert.Equal(PaymentReminderMode.Aggressive, await reopened.GetPaymentReminderModeAsync());
                await reopened.ClearAllFinancialDataAsync();
                Assert.Equal(PaymentReminderMode.Off, await reopened.GetPaymentReminderModeAsync());
            }
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task UpgradeFromSchema15_AddsTheColumnAsOff()
    {
        var path = TempPath();
        try
        {
            await using (var store = new SqliteCoinFlowStore(path, true, new DateOnly(2026, 8, 20)))
            {
                await TestFactory.Service(store).LoadCanonicalDevelopmentDataAsync();
            }

            var raw = new SQLiteAsyncConnection(path);
            await raw.ExecuteAsync("ALTER TABLE settings DROP COLUMN PaymentReminderMode");
            await raw.ExecuteAsync("UPDATE settings SET SchemaVersion = 15");
            await raw.CloseAsync();

            await using var upgraded = new SqliteCoinFlowStore(path, true, new DateOnly(2026, 8, 20));
            Assert.Equal(PaymentReminderMode.Off, await upgraded.GetPaymentReminderModeAsync());
            await upgraded.SavePaymentReminderModeAsync(PaymentReminderMode.Relaxed);
            Assert.Equal(PaymentReminderMode.Relaxed, await upgraded.GetPaymentReminderModeAsync());
            var check = new SQLiteAsyncConnection(path);
            Assert.Equal(
                SqliteCoinFlowStore.CurrentSchemaVersion,
                await check.ExecuteScalarAsync<int>("SELECT SchemaVersion FROM settings"));
            await check.CloseAsync();
            Assert.Equal(16, SqliteCoinFlowStore.CurrentSchemaVersion);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task Dues_ComeFromTheOpenPlanAndThenTheProjection_WithinTheHorizon()
    {
        await WithCanonical(new DateOnly(2026, 8, 20), async (store, service) =>
        {
            var dues = await service.GetUpcomingPaymentDuesAsync(At(2026, 8, 20, 12, 0));

            // Açık dönem (20.08, 10.09]: kart ve kredi 07.09. Dönem sonrası,
            // 24.09'a kadar: Burgan 18.09, Eminevim 20.09 (projeksiyon).
            Assert.Equal(
                new[]
                {
                    (new DateOnly(2026, 9, 7), 40_321.97m),
                    (new DateOnly(2026, 9, 7), 14_501.23m),
                    (new DateOnly(2026, 9, 18), 7_374.59m),
                    (new DateOnly(2026, 9, 20), 28_167.40m)
                },
                dues.OrderBy(x => x.DueDate)
                    .ThenByDescending(x => x.Amount)
                    .Select(x => (x.DueDate, x.Amount.GetValueOrDefault())));
            Assert.Equal(dues.Count, dues.Select(x => x.Key).Distinct().Count());
            Assert.All(dues, x => Assert.InRange(
                x.DueDate,
                new DateOnly(2026, 8, 20),
                new DateOnly(2026, 8, 20).AddDays(PaymentReminderPlanner.HorizonDays)));

            // Kapalıyken bildirim yok; açınca aynı ödemelerden kurulur.
            Assert.Empty(await service.GetPaymentRemindersAsync(At(2026, 8, 20, 12, 0)));
            await service.SavePaymentReminderModeAsync(PaymentReminderMode.Relaxed);
            var reminders = await service.GetPaymentRemindersAsync(At(2026, 8, 20, 12, 0));
            Assert.Equal(
                new[]
                {
                    At(2026, 9, 7, 9, 0),
                    At(2026, 9, 18, 9, 0),
                    At(2026, 9, 20, 9, 0)
                },
                reminders.Select(x => x.NotifyAt));
            Assert.Equal(2, reminders[0].Payments.Count);
        });
    }

    [Fact]
    public async Task Dues_SkipPaymentsMarkedPaid_ButKeepTodaysUnmarkedPayment()
    {
        var today = new DateOnly(2026, 9, 7);
        await WithCanonical(new DateOnly(2026, 8, 20), async (store, _) =>
        {
            var service = TestFactory.Service(store, today);
            var progress = await service.GetPeriodProgressAsync();
            var plan = (await store.GetFinancialHistoryAsync()).Plans.Single();
            var loan = plan.PaymentLines.Single(x => x.SourceType == PlanPaymentSourceType.Loan);
            var card = plan.PaymentLines.Single(x => x.SourceType == PlanPaymentSourceType.CreditCard);
            // Ana Sayfa vadesi gelen satırı ödenmiş sayar; hatırlatıcı saymaz.
            Assert.DoesNotContain(progress!.RemainingLines, x => x.Id == card.Id);

            await service.ObservePaymentAsync(loan.Id, ActualPaymentStatus.Paid, 14_501.23m);
            var dues = await service.GetUpcomingPaymentDuesAsync(At(2026, 9, 7, 8, 0));

            Assert.Contains(dues, x => x.Name == card.Name && x.DueDate == today);
            Assert.DoesNotContain(dues, x => x.Name == loan.Name && x.DueDate == today);

            // "Ödenmedi" işaretlenen satır hâlâ hatırlatılır.
            await service.ObservePaymentAsync(loan.Id, ActualPaymentStatus.Unpaid, 0m);
            Assert.Contains(
                await service.GetUpcomingPaymentDuesAsync(At(2026, 9, 7, 8, 0)),
                x => x.Name == loan.Name && x.DueDate == today);
        });
    }

    [Fact]
    public async Task Dues_AreEmptyBeforeSetup()
    {
        var path = TempPath();
        try
        {
            await using var store = new SqliteCoinFlowStore(path, true, new DateOnly(2026, 8, 20));
            var service = TestFactory.Service(store);
            await service.SavePaymentReminderModeAsync(PaymentReminderMode.Aggressive);

            Assert.Empty(await service.GetUpcomingPaymentDuesAsync(At(2026, 8, 20, 12, 0)));
            Assert.Empty(await service.GetPaymentRemindersAsync(At(2026, 8, 20, 12, 0)));
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    private static PaymentDue Payment(string name, DateOnly date, decimal? amount) =>
        new($"{name}-{date:yyyyMMdd}", name, date, amount);

    private static DateTime At(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0);

    private static async Task WithCanonical(
        DateOnly today,
        Func<SqliteCoinFlowStore, CoinFlowService, Task> test)
    {
        var path = TempPath();
        try
        {
            await using var store = new SqliteCoinFlowStore(path, true, today);
            var service = TestFactory.Service(store, today);
            await service.LoadCanonicalDevelopmentDataAsync();
            await service.GetFinancialPlanAsync();
            await test(store, service);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    private static string TempPath() => Path.Combine(
        Path.GetTempPath(),
        $"coinflow-reminder-{Guid.NewGuid():N}.db3");

    private static void DeleteDatabase(string path)
    {
        foreach (var candidate in new[] { path, path + "-shm", path + "-wal" })
        {
            if (File.Exists(candidate))
            {
                File.Delete(candidate);
            }
        }
    }
}
