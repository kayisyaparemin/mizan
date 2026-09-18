using Mizan.Application.Models;
using Mizan.Application.Services;
using Mizan.Domain.Models;
using Mizan.Infrastructure.Persistence;
using SQLite;

namespace Mizan.Tests;

/// <summary>
/// Bildirimdeki "Ödedim" / "Ertele" cevapları: hatırlatıcı defteri, Ana
/// Sayfa'nın kalan ödemeleri, yeniden hatırlatma ve bildirimle taşınan veri.
/// Kanonik açık dönem (20.08, 10.09]: kart 07.09 40.321,97 · kredi 07.09
/// 14.501,23. Dönem sonrası: Burgan 18.09, Eminevim 20.09.
/// </summary>
public sealed class PaymentReminderAnswerTests
{
    private static readonly DateOnly Anchor = new(2026, 8, 20);
    private static readonly DateOnly LoanDue = new(2026, 9, 7);

    // ---------------------------------------------------------------
    // Saf kurallar
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("2026-09-15T14:00", "2026-09-15T17:00")]
    [InlineData("2026-09-15T18:59", "2026-09-15T21:59")]
    // 22:00 ve sonrası gece; ertesi sabah 09:00.
    [InlineData("2026-09-15T19:00", "2026-09-16T09:00")]
    [InlineData("2026-09-15T23:30", "2026-09-16T09:00")]
    // Gece yarısından sonra basılırsa aynı sabah 09:00.
    [InlineData("2026-09-16T03:00", "2026-09-16T09:00")]
    [InlineData("2026-09-16T04:59", "2026-09-16T09:00")]
    [InlineData("2026-09-16T05:00", "2026-09-16T08:00")]
    // Ay ve yıl sonu.
    [InlineData("2026-12-31T21:00", "2027-01-01T09:00")]
    public void Snooze_WaitsThreeHours_ButNeverRingsAtNight(string now, string expected) =>
        Assert.Equal(Time(expected), PaymentReminderPlanner.SnoozeUntil(Time(now)));

    [Fact]
    public void SnoozeText_SaysWhenItComesBack()
    {
        Assert.Equal(
            "Yeniden hatırlatma: bugün 17:00",
            PaymentReminderPlanner.SnoozeText(Time("2026-09-15T17:00"), Time("2026-09-15T14:00")));
        Assert.Equal(
            "Yeniden hatırlatma: yarın 09:00",
            PaymentReminderPlanner.SnoozeText(Time("2026-09-16T09:00"), Time("2026-09-15T20:00")));
    }

    [Fact]
    public void FollowUps_OnePerDueDay_AtTheLatestSnooze_PastOnesDropped()
    {
        var now = Time("2026-09-18T10:00");
        var followUps = PaymentReminderPlanner.FollowUps(
            [
                Snoozed("a", "Burgan", new DateOnly(2026, 9, 18), 7_375m, "2026-09-18T12:00"),
                Snoozed("b", "Eminevim", new DateOnly(2026, 9, 18), 28_167m, "2026-09-18T13:00"),
                Snoozed("c", "Eski", new DateOnly(2026, 9, 17), 100m, "2026-09-18T09:00"),
                Snoozed("d", "Süresiz", new DateOnly(2026, 9, 19), 100m, null)
            ],
            now);

        var reminder = Assert.Single(followUps);
        Assert.Equal("20260918-ertele", reminder.Key);
        Assert.Equal(Time("2026-09-18T13:00"), reminder.NotifyAt);
        Assert.Equal("Ertelediğin ödeme", reminder.Title);
        Assert.Equal(
            "2 ödeme · toplam 35.542,00 TL: Eminevim, Burgan · 18 Eylül Cuma",
            reminder.Message);
        Assert.Equal(new[] { "b", "a" }, reminder.Payments.Select(x => x.Key));
    }

    [Fact]
    public void Sample_IsTheNextDueDay_NowAndWithRealPayments()
    {
        var now = Time("2026-09-15T14:37");
        var sample = PaymentReminderPlanner.Sample(
            [
                new PaymentDue("dun", "Dün", new DateOnly(2026, 9, 14), 1m),
                new PaymentDue("e", "Eminevim", new DateOnly(2026, 9, 20), 28_167m),
                new PaymentDue("b", "Burgan Ameliyat", new DateOnly(2026, 9, 18), 7_375m)
            ],
            now);

        Assert.NotNull(sample);
        Assert.Equal(now, sample.NotifyAt);
        Assert.Equal("20260918-deneme", sample.Key);
        Assert.Equal("Deneme bildirimi", sample.Title);
        Assert.Equal("Burgan Ameliyat · 7.375,00 TL · 18 Eylül Cuma (3 gün sonra)", sample.Message);
        Assert.Equal("b", Assert.Single(sample.Payments).Key);
        Assert.Null(PaymentReminderPlanner.Sample([], now));
    }

    [Fact]
    public void DueKey_UsesTheSourceAndDate_OrTheNameWhenThereIsNoSource()
    {
        var source = Guid.Parse("11111111-2222-3333-4444-555555555555");
        Assert.Equal(
            "11111111222233334444555555555555-20260907",
            PaymentReminderPlanner.DueKey(source, "Kredi", LoanDue));
        Assert.Equal("Kira-20260907", PaymentReminderPlanner.DueKey(Guid.Empty, "Kira", LoanDue));
    }

    [Fact]
    public void Payload_RoundTripsTurkishNamesSeparatorsAndUnknownAmounts()
    {
        PaymentDue[] payments =
        [
            new("k1", "İhtiyaç kredisi · Şubat/Ğ\tsekme\nsatır", LoanDue, 14_501.23m),
            new("k2", "Kart", new DateOnly(2027, 1, 5), null)
        ];

        Assert.Equal(payments, PaymentReminderPayload.DecodePayments(
            PaymentReminderPayload.EncodePayments(payments)));
        Assert.Empty(PaymentReminderPayload.DecodePayments(null));
        Assert.Empty(PaymentReminderPayload.DecodePayments("bozuk!!"));
    }

    [Fact]
    public void AnswerLine_RoundTrips_AndGarbageIsRejected()
    {
        var profile = Guid.NewGuid();
        var snooze = new PaymentReminderAnswer(
            PaymentReminderAnswerKind.Snoozed,
            Time("2026-09-18T09:02"),
            Time("2026-09-18T12:02"),
            [new PaymentDue("k1", "Burgan", new DateOnly(2026, 9, 18), 7_374.59m)]);
        var paid = snooze with { Kind = PaymentReminderAnswerKind.Paid, SnoozedUntil = null };

        foreach (var answer in new[] { snooze, paid })
        {
            var line = PaymentReminderPayload.EncodeAnswer(profile, answer);
            Assert.DoesNotContain('\n', line);
            Assert.True(PaymentReminderPayload.TryDecodeAnswer(line, out var decodedProfile, out var decoded));
            Assert.Equal(profile, decodedProfile);
            Assert.Equal(answer.Kind, decoded.Kind);
            Assert.Equal(answer.AnsweredAt, decoded.AnsweredAt);
            Assert.Equal(answer.SnoozedUntil, decoded.SnoozedUntil);
            Assert.Equal(answer.Payments, decoded.Payments);
        }

        Assert.False(PaymentReminderPayload.TryDecodeAnswer("", out _, out _));
        Assert.False(PaymentReminderPayload.TryDecodeAnswer(
            $"{profile:N}\tbilinmeyen\t202609180902\t\t{PaymentReminderPayload.EncodePayments(paid.Payments)}",
            out _,
            out _));
        // Ödemesi olmayan cevap işe yaramaz.
        Assert.False(PaymentReminderPayload.TryDecodeAnswer(
            $"{profile:N}\todendi\t202609180902\t\t",
            out _,
            out _));
    }

    // ---------------------------------------------------------------
    // Defter ve Ana Sayfa
    // ---------------------------------------------------------------

    [Fact]
    public async Task Paid_LeavesTheRemainingPayments_AndIsNotRemindedAgain()
    {
        await WithCanonical(async store =>
        {
            var service = TestFactory.Service(store, new DateOnly(2026, 9, 1));
            var now = Time("2026-09-01T12:00");
            await service.SavePaymentReminderModeAsync(PaymentReminderMode.Relaxed);
            var before = (await service.GetPeriodProgressAsync())!;
            var loan = await LoanLineAsync(store);
            Assert.Contains(before.RemainingLines, x => x.Id == loan.Id);

            await service.RecordPaymentReminderAnswerAsync(new PaymentReminderAnswer(
                PaymentReminderAnswerKind.Paid,
                now,
                null,
                [Due(loan)]));

            var after = (await service.GetPeriodProgressAsync())!;
            Assert.DoesNotContain(after.RemainingLines, x => x.Id == loan.Id);
            Assert.Equal(before.RemainingPlannedTotal - 14_501.23m, after.RemainingPlannedTotal);

            var dues = await service.GetUpcomingPaymentDuesAsync(now);
            Assert.DoesNotContain(dues, x => x.Key == Due(loan).Key);
            Assert.Contains(dues, x => x.DueDate == LoanDue && x.Amount == 40_321.97m);

            var board = await service.GetPaymentReminderBoardAsync(now);
            Assert.Equal(Due(loan).Key, Assert.Single(board.Paid).DueKey);
            Assert.Empty(board.Snoozed);
            var morning = board.Reminders.Single(x => x.DueDate == LoanDue);
            Assert.Equal(40_321.97m, Assert.Single(morning.Payments).Amount);
        });
    }

    [Fact]
    public async Task Snoozed_StaysUnpaidAfterItsDueDate_AndComesBackAsAFollowUp()
    {
        await WithCanonical(async store =>
        {
            var loan = await LoanLineAsync(store);
            var dueMorning = Time("2026-09-07T09:02");
            var dayOf = TestFactory.Service(store, LoanDue);
            await dayOf.SavePaymentReminderModeAsync(PaymentReminderMode.Relaxed);
            var until = PaymentReminderPlanner.SnoozeUntil(dueMorning);

            await dayOf.RecordPaymentReminderAnswerAsync(new PaymentReminderAnswer(
                PaymentReminderAnswerKind.Snoozed,
                dueMorning,
                until,
                [Due(loan)]));

            var board = await dayOf.GetPaymentReminderBoardAsync(dueMorning);
            var snoozed = Assert.Single(board.Snoozed);
            Assert.Equal(until, snoozed.SnoozedUntil);
            var followUp = Assert.Single(board.Reminders, x => x.Key == "20260907-ertele");
            Assert.Equal(Time("2026-09-07T12:02"), followUp.NotifyAt);
            // Ertelenen, "Sıradaki ödemeler"de ikinci kez görünmez.
            Assert.DoesNotContain(board.Upcoming.SelectMany(x => x.Payments), x => x.Key == snoozed.DueKey);

            // Ertesi gün: vadesi geçen satır normalde ödenmiş sayılır;
            // ertelenmiş olan kalan ödemelerde durur.
            var nextDay = TestFactory.Service(store, new DateOnly(2026, 9, 8));
            var progress = (await nextDay.GetPeriodProgressAsync())!;
            var remaining = Assert.Single(progress.RemainingLines);
            Assert.Equal(loan.Id, remaining.Id);
            Assert.True(progress.IsSnoozed(loan.Id));
            Assert.Equal(14_501.23m, progress.RemainingPlannedTotal);
        });
    }

    [Fact]
    public async Task Snoozed_CanBeConfirmedPaid_ALateSnoozeDoesNotUndoPaid_AndUndoRestoresThePlan()
    {
        await WithCanonical(async store =>
        {
            var loan = await LoanLineAsync(store);
            var service = TestFactory.Service(store, new DateOnly(2026, 9, 8));
            var now = Time("2026-09-08T10:00");
            Task Answer(PaymentReminderAnswerKind kind) =>
                service.RecordPaymentReminderAnswerAsync(new PaymentReminderAnswer(
                    kind,
                    now,
                    kind == PaymentReminderAnswerKind.Snoozed ? now.AddHours(3) : null,
                    [Due(loan)]));

            await Answer(PaymentReminderAnswerKind.Snoozed);
            await Answer(PaymentReminderAnswerKind.Paid);
            var paid = Assert.Single(await service.GetPaymentReminderResponsesAsync());
            Assert.Equal(PaymentReminderAnswerKind.Paid, paid.Kind);
            Assert.Null(paid.SnoozedUntil);

            // Aynı bildirimin eski bir kopyasındaki "Ertele".
            await Answer(PaymentReminderAnswerKind.Snoozed);
            Assert.Equal(
                PaymentReminderAnswerKind.Paid,
                Assert.Single(await service.GetPaymentReminderResponsesAsync()).Kind);
            Assert.Empty((await service.GetPeriodProgressAsync())!.RemainingLines);

            await service.UndoPaymentReminderAnswerAsync(paid.DueKey);
            Assert.Empty(await service.GetPaymentReminderResponsesAsync());
            // Planın kendi varsayımı: vadesi geçen ödenmiş sayılır.
            Assert.Empty((await service.GetPeriodProgressAsync())!.RemainingLines);
        });
    }

    [Fact]
    public async Task Paid_SurvivesAPlanRevision_BecauseItIsKeyedBySourceAndDate()
    {
        await WithCanonical(async store =>
        {
            var loan = await LoanLineAsync(store);
            var service = TestFactory.Service(store, new DateOnly(2026, 9, 1));
            await service.RecordPaymentReminderAnswerAsync(new PaymentReminderAnswer(
                PaymentReminderAnswerKind.Paid,
                Time("2026-09-01T12:00"),
                null,
                [Due(loan)]));

            // Düzen değişikliği açık planı revize eder; satırlar yeni kimlik alır.
            var plan = await service.GetFinancialPlanAsync();
            await service.SaveCashFlowAllocationStrategyAsync(
                plan.PaymentAssignmentStrategies.Single() with
                {
                    Mode = CashFlowAllocationMode.PreviousPeriod
                });
            var revision = Assert.Single((await store.GetFinancialHistoryAsync()).Revisions);
            var revisedLoan = Assert.Single(revision.PaymentLines, x =>
                x.SourceEntityId == loan.SourceEntityId && x.PlannedDate == LoanDue);
            Assert.NotEqual(loan.Id, revisedLoan.Id);

            var progress = (await service.GetPeriodProgressAsync())!;
            Assert.DoesNotContain(progress.RemainingLines, x => x.SourceEntityId == loan.SourceEntityId);
        });
    }

    [Fact]
    public async Task Board_HidesAnswersOfClosedPeriods_ShowsEarlyPaidFuturePayments_EvenWhenOff()
    {
        await WithCanonical(async store =>
        {
            var service = TestFactory.Service(store, new DateOnly(2026, 9, 1));
            var now = Time("2026-09-01T12:00");
            var dues = await service.GetUpcomingPaymentDuesAsync(now);
            var burgan = Assert.Single(dues, x => x.DueDate == new DateOnly(2026, 9, 18));
            await service.RecordPaymentReminderAnswerAsync(new PaymentReminderAnswer(
                PaymentReminderAnswerKind.Paid,
                now,
                null,
                [
                    burgan,
                    // Açık dönemin başlangıç günü önceki döneme aittir.
                    new PaymentDue("eski", "Kapanmış dönem", Anchor, 500m)
                ]));

            var off = await service.GetPaymentReminderBoardAsync(now);
            Assert.Equal(PaymentReminderMode.Off, off.Mode);
            Assert.Empty(off.Reminders);
            Assert.Null(off.Sample);
            Assert.Equal(burgan.Key, Assert.Single(off.Paid).DueKey);

            await service.SavePaymentReminderModeAsync(PaymentReminderMode.Aggressive);
            var on = await service.GetPaymentReminderBoardAsync(now);
            Assert.DoesNotContain(on.Reminders, x => x.Payments.Any(p => p.Key == burgan.Key));
            Assert.DoesNotContain(await service.GetUpcomingPaymentDuesAsync(now), x => x.Key == burgan.Key);
            Assert.NotNull(on.Sample);
            Assert.Equal(LoanDue, on.Sample.DueDate);
        });
    }

    [Fact]
    public async Task Answers_PersistAcrossRestart_AreClearedWithData_AndUpgradeFromSchema16AddsTheTable()
    {
        var path = TempPath();
        try
        {
            var answer = new PaymentReminderResponse(
                "k1",
                "Burgan",
                new DateOnly(2026, 9, 18),
                7_374.59m,
                PaymentReminderAnswerKind.Snoozed,
                Time("2026-09-18T09:02"),
                Time("2026-09-18T12:02"));
            await using (var store = new SqliteMizanStore(path, true, Anchor))
            {
                await TestFactory.Service(store).LoadCanonicalDevelopmentDataAsync();
                await store.UpsertPaymentReminderResponsesAsync([answer]);
            }

            await using (var reopened = new SqliteMizanStore(path, true, Anchor))
            {
                Assert.Equal(answer, Assert.Single(await reopened.GetPaymentReminderResponsesAsync()));
                await reopened.UpsertPaymentReminderResponsesAsync(
                    [answer with { Kind = PaymentReminderAnswerKind.Paid, SnoozedUntil = null }]);
                Assert.Equal(
                    PaymentReminderAnswerKind.Paid,
                    Assert.Single(await reopened.GetPaymentReminderResponsesAsync()).Kind);
            }

            var raw = new SQLiteAsyncConnection(path);
            await raw.ExecuteAsync("DROP TABLE payment_reminder_responses");
            await raw.ExecuteAsync("UPDATE settings SET SchemaVersion = 16");
            await raw.CloseAsync();

            await using (var upgraded = new SqliteMizanStore(path, true, Anchor))
            {
                Assert.Empty(await upgraded.GetPaymentReminderResponsesAsync());
                await upgraded.UpsertPaymentReminderResponsesAsync([answer]);
                Assert.Single(await upgraded.GetPaymentReminderResponsesAsync());
                await upgraded.ClearAllFinancialDataAsync();
                Assert.Empty(await upgraded.GetPaymentReminderResponsesAsync());
            }

            var check = new SQLiteAsyncConnection(path);
            Assert.Equal(17, await check.ExecuteScalarAsync<int>("SELECT SchemaVersion FROM settings"));
            await check.CloseAsync();
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task UserBugReproduction_ObserveBalance_Snooze_Paid_Undo_PreservesLivingExpenseCalculation()
    {
        await WithCanonical(async store =>
        {
            var testDate = new DateOnly(2026, 9, 8);
            var service = TestFactory.Service(store, testDate);
            var plan = Assert.Single((await store.GetFinancialHistoryAsync()).Plans);
            var startingPosition = plan.OpeningBalance + plan.PlannedIncome;

            // Ödemeler ödeme gününde (2026-09-07) ertelenmişti.
            var loan = await LoanLineAsync(store);
            var card = plan.PaymentLines.Single(x => x.SourceType == PlanPaymentSourceType.CreditCard && x.PlannedDate == LoanDue);
            var snoozeTime = Time("2026-09-07T15:30");
            await service.RecordPaymentReminderAnswerAsync(new PaymentReminderAnswer(
                PaymentReminderAnswerKind.Snoozed,
                snoozeTime,
                snoozeTime.AddHours(3),
                [Due(loan), Due(card)]));

            // 1. Ertesi gün (08.09) kullanıcı banka bakiyesini girer (15.000 TL yaşam gideri harcamış, henüz borç ödememiş).
            var observation = await service.ObserveCurrentBalanceAsync(startingPosition - 15_000m);
            var progress1 = (await service.GetPeriodProgressAsync())!;
            Assert.Equal(15_000m, progress1.ObservedLivingSpend);

            // 2. Ödemelerin hâlâ ertelenmiş ve yaşam giderinin korunduğu doğrulanır.
            var progress2 = (await service.GetPeriodProgressAsync())!;
            Assert.Equal(15_000m, progress2.ObservedLivingSpend);

            // 3. Ertelenen kırmızı karta tıklayıp 16:00'da (gözlemden sonra) Ödedim der.
            var paidTime = Time("2026-09-08T16:00");
            await service.RecordPaymentReminderAnswerAsync(new PaymentReminderAnswer(
                PaymentReminderAnswerKind.Paid,
                paidTime,
                null,
                [Due(loan)]));

            var progress3 = (await service.GetPeriodProgressAsync())!;
            // Bakiye kaydı (15:00 TRT) Ödedim cevabından (16:00 TRT) ÖNCE alındığı için yaşam gideri 15.000 TL olarak korunur.
            Assert.Equal(15_000m, progress3.ObservedLivingSpend);

            // 4. Yeşil ödenen karta tıklayıp 16:05'te Geri Al der.
            // Karttan geri alınınca vadesi gelen/geçen ödeme Kalan Ödemeler'e dönebilmek için ertelenir.
            await service.RecordPaymentReminderAnswerAsync(new PaymentReminderAnswer(
                PaymentReminderAnswerKind.Snoozed,
                Time("2026-09-08T16:05"),
                Time("2026-09-08T19:05"),
                [Due(loan)]));

            var progress4 = (await service.GetPeriodProgressAsync())!;
            // Yaşam gideri yine 15.000 TL olarak kalır, bozulmaz.
            Assert.Equal(15_000m, progress4.ObservedLivingSpend);
        });
    }

    [Fact]
    public async Task UserBugReproduction_OnPaymentDay_ObserveBalance_Snooze_Paid_Undo_PreservesLivingExpenseCalculation()
    {
        await WithCanonical(async store =>
        {
            // Ödeme gününde (LoanDue = 2026-09-07) kullanıcı akışı:
            var service = TestFactory.Service(store, LoanDue);
            var plan = Assert.Single((await store.GetFinancialHistoryAsync()).Plans);
            var startingPosition = plan.OpeningBalance + plan.PlannedIncome;

            // 1. Ödeme gününde kullanıcı mevcut bakiyeyi kaydeder (15:00 TRT / 12:00 UTC).
            await service.ObserveCurrentBalanceAsync(startingPosition - 15_000m);
            var progress1 = (await service.GetPeriodProgressAsync())!;
            Assert.Equal(15_000m, progress1.ObservedLivingSpend);

            // 2. Ertele der (15:30 TRT).
            var loan = await LoanLineAsync(store);
            var card = plan.PaymentLines.Single(x => x.SourceType == PlanPaymentSourceType.CreditCard && x.PlannedDate == LoanDue);
            var snoozeTime = Time("2026-09-07T15:30");
            await service.RecordPaymentReminderAnswerAsync(new PaymentReminderAnswer(
                PaymentReminderAnswerKind.Snoozed,
                snoozeTime,
                snoozeTime.AddHours(3),
                [Due(loan), Due(card)]));

            var progress2 = (await service.GetPeriodProgressAsync())!;
            Assert.Equal(15_000m, progress2.ObservedLivingSpend);

            // 3. Ödedim der (16:00 TRT, gözlemden sonra).
            var paidTime = Time("2026-09-07T16:00");
            await service.RecordPaymentReminderAnswerAsync(new PaymentReminderAnswer(
                PaymentReminderAnswerKind.Paid,
                paidTime,
                null,
                [Due(loan)]));

            var progress3 = (await service.GetPeriodProgressAsync())!;
            Assert.Equal(15_000m, progress3.ObservedLivingSpend);

            // 4. Geri al der.
            await service.UndoPaymentReminderAnswerAsync(Due(loan).Key);

            var progress4 = (await service.GetPeriodProgressAsync())!;
            Assert.Equal(15_000m, progress4.ObservedLivingSpend);
        });
    }

    private static async Task<PeriodPlanPaymentLine> LoanLineAsync(SqliteMizanStore store) =>
        (await store.GetFinancialHistoryAsync()).Plans.Single().PaymentLines
            .Single(x => x.SourceType == PlanPaymentSourceType.Loan && x.PlannedDate == LoanDue);

    private static PaymentDue Due(PeriodPlanPaymentLine line) =>
        new(
            PaymentReminderPlanner.DueKey(line.SourceEntityId, line.Name, line.PlannedDate),
            line.Name,
            line.PlannedDate,
            line.PlannedAmount);

    private static PaymentReminderResponse Snoozed(
        string key,
        string name,
        DateOnly due,
        decimal amount,
        string? until) =>
        new(
            key,
            name,
            due,
            amount,
            PaymentReminderAnswerKind.Snoozed,
            due.ToDateTime(new TimeOnly(9, 0)),
            until is null ? null : Time(until));

    private static DateTime Time(string value) =>
        DateTime.ParseExact(value, "yyyy-MM-ddTHH:mm", System.Globalization.CultureInfo.InvariantCulture);

    private static async Task WithCanonical(Func<SqliteMizanStore, Task> test)
    {
        var path = TempPath();
        try
        {
            await using var store = new SqliteMizanStore(path, true, Anchor);
            var service = TestFactory.Service(store, Anchor);
            await service.LoadCanonicalDevelopmentDataAsync();
            await service.GetFinancialPlanAsync();
            await test(store);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    private static string TempPath() => Path.Combine(
        Path.GetTempPath(),
        $"coinflow-reminder-answer-{Guid.NewGuid():N}.db3");

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
