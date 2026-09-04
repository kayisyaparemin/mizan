using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;
using CoinFlow.Infrastructure.Persistence;

namespace CoinFlow.Tests;

/// <summary>
/// Ekstre kesim günü + ekstre ödeme tercihi geçmişi davranış testleri.
/// UI değil, business davranışı doğrulanır.
/// </summary>
public sealed class CreditCardPaymentPreferenceTests
{
    private static readonly DateOnly Today = new(2026, 8, 20);
    private static readonly Guid CardId =
        Guid.Parse("aa000000-0000-0000-0000-000000000001");

    // 1 — Onboarding'de girilen kesim günü canonical state'e yazılır.
    [Fact]
    public async Task ConfiguredClosingDay_IsPersisted()
    {
        await WithStore(async store =>
        {
            var service = TestFactory.Service(store, Today);
            await service.SaveCreditCardAsync(Card(closingDay: 17));

            var persisted = Assert.Single(
                (await service.GetFinancialPlanAsync()).CreditCards);
            Assert.Equal(17, persisted.StatementClosingDay);
        });
    }

    // 2 — Projection, yapılandırılan kesim gününü kullanır.
    [Fact]
    public void Projection_UsesConfiguredClosingDay()
    {
        var calculator = new CreditCardStatementCalculator();
        var card = Card(closingDay: 17) with
        {
            BalanceAsOfDate = new DateOnly(2026, 8, 1),
            CarriedBalance = 1_000m
        };

        var projection = calculator.Project(card, 2, true, 0.05m);

        Assert.Equal(new DateOnly(2026, 8, 17),
            projection[0].StatementCloseDate);
        Assert.Equal(new DateOnly(2026, 9, 17),
            projection[1].StatementCloseDate);
    }

    // 9 — Ay sonu kırpma merkezi takvim kuralına bırakılır.
    [Fact]
    public void Projection_ClampsClosingDayOnShortMonths()
    {
        var calculator = new CreditCardStatementCalculator();
        var card = Card(closingDay: 31) with
        {
            BalanceAsOfDate = new DateOnly(2027, 1, 31),
            CarriedBalance = 1_000m
        };

        var projection = calculator.Project(card, 3, true, 0.05m);

        Assert.Equal(new DateOnly(2027, 1, 31),
            projection[0].StatementCloseDate);
        Assert.Equal(new DateOnly(2027, 2, 28),
            projection[1].StatementCloseDate);
        Assert.Equal(new DateOnly(2027, 3, 31),
            projection[2].StatementCloseDate);
    }

    // 3 — İlk ödeme tercihi seçimi geçmişe tek kayıt olarak düşer.
    [Fact]
    public async Task FirstPreference_CreatesSingleHistoryEntry()
    {
        await WithStore(async store =>
        {
            var service = TestFactory.Service(store, Today);
            await service.SaveCreditCardAsync(CardWithStatement(
                CurrentStatementPaymentMode.Minimum));

            var card = Assert.Single(
                (await service.GetFinancialPlanAsync()).CreditCards);
            var preference = Assert.Single(card.PaymentPreferences);
            Assert.Equal(CurrentStatementPaymentMode.Minimum,
                preference.Mode);
            Assert.Equal(new DateOnly(2026, 8, 25),
                preference.EffectiveFromStatementDate);
            Assert.Null(preference.CustomAmount);
        });
    }

    // 4 — Özel tutar geçmişte tutarıyla birlikte saklanır.
    [Fact]
    public async Task CustomPreference_PersistsAmount()
    {
        await WithStore(async store =>
        {
            var service = TestFactory.Service(store, Today);
            await service.SaveCreditCardAsync(CardWithStatement(
                CurrentStatementPaymentMode.Custom,
                customAmount: 2_500m));

            var card = Assert.Single(
                (await service.GetFinancialPlanAsync()).CreditCards);
            var preference = Assert.Single(card.PaymentPreferences);
            Assert.Equal(CurrentStatementPaymentMode.Custom,
                preference.Mode);
            Assert.Equal(2_500m, preference.CustomAmount);
        });
    }

    // 5 + 6 — Tercih değişince yeni effective-dated kayıt eklenir ve
    // eski kayıt aynen kalır (I6).
    [Fact]
    public async Task ChangedPreference_AppendsWithoutMutatingHistory()
    {
        await WithStore(async store =>
        {
            var service = TestFactory.Service(store, Today);
            await service.SaveCreditCardAsync(CardWithStatement(
                CurrentStatementPaymentMode.Minimum));
            var first = Assert.Single(
                Assert.Single((await service.GetFinancialPlanAsync())
                    .CreditCards).PaymentPreferences);

            var later = TestFactory.Service(store, new DateOnly(2026, 9, 26));
            await later.SaveCreditCardAsync(CardWithStatement(
                CurrentStatementPaymentMode.Full,
                statementDate: new DateOnly(2026, 9, 25)));

            var card = Assert.Single(
                (await later.GetFinancialPlanAsync()).CreditCards);
            Assert.Equal(2, card.PaymentPreferences.Count);

            var original = card.PaymentPreferences.Single(x =>
                x.Id == first.Id);
            Assert.Equal(CurrentStatementPaymentMode.Minimum, original.Mode);
            Assert.Equal(first.EffectiveFromStatementDate,
                original.EffectiveFromStatementDate);
            Assert.Equal(first.CreatedAt, original.CreatedAt);

            var appended = card.PaymentPreferences.Single(x =>
                x.Id != first.Id);
            Assert.Equal(CurrentStatementPaymentMode.Full, appended.Mode);
            Assert.Equal(new DateOnly(2026, 9, 25),
                appended.EffectiveFromStatementDate);
        });
    }

    // 7 — Yürürlükteki karar, en yeni effective kayıttır.
    [Fact]
    public void Resolver_SelectsLatestEffectiveDecision()
    {
        var resolver = new CreditCardPaymentPreferenceResolver();
        var history = new[]
        {
            Preference(new DateOnly(2026, 8, 25),
                CurrentStatementPaymentMode.Minimum),
            Preference(new DateOnly(2026, 9, 25),
                CurrentStatementPaymentMode.Full)
        };

        Assert.Equal(
            CurrentStatementPaymentMode.Minimum,
            resolver.Resolve(new DateOnly(2026, 9, 24), history)!.Mode);
        Assert.Equal(
            CurrentStatementPaymentMode.Full,
            resolver.Resolve(new DateOnly(2026, 10, 25), history)!.Mode);
        Assert.Null(
            resolver.Resolve(new DateOnly(2026, 8, 24), history));
    }

    // 8 — Aynı kararın tekrar kaydedilmesi geçmişi şişirmez.
    [Fact]
    public async Task RepeatedSameDecision_DoesNotGrowHistory()
    {
        await WithStore(async store =>
        {
            var service = TestFactory.Service(store, Today);
            await service.SaveCreditCardAsync(CardWithStatement(
                CurrentStatementPaymentMode.Minimum));
            await service.SaveCreditCardAsync(CardWithStatement(
                CurrentStatementPaymentMode.Minimum));
            await service.SaveCreditCardAsync(CardWithStatement(
                CurrentStatementPaymentMode.Minimum));

            var card = Assert.Single(
                (await service.GetFinancialPlanAsync()).CreditCards);
            Assert.Single(card.PaymentPreferences);
        });
    }

    // 8b — Aynı effective date'e farklı karar yazılırsa geçmiş korunur ve
    // en yeni karar kazanır.
    [Fact]
    public async Task SameEffectiveDateChange_KeepsBothAndResolvesLatest()
    {
        await WithStore(async store =>
        {
            var service = TestFactory.Service(store, Today);
            await service.SaveCreditCardAsync(CardWithStatement(
                CurrentStatementPaymentMode.Minimum));
            var later = TestFactory.Service(store, new DateOnly(2026, 8, 27));
            await later.SaveCreditCardAsync(CardWithStatement(
                CurrentStatementPaymentMode.Full));

            var card = Assert.Single(
                (await later.GetFinancialPlanAsync()).CreditCards);
            Assert.Equal(2, card.PaymentPreferences.Count);
            Assert.Equal(
                CurrentStatementPaymentMode.Full,
                new CreditCardPaymentPreferenceResolver()
                    .Resolve(
                        new DateOnly(2026, 8, 25),
                        card.PaymentPreferences)!.Mode);
        });
    }

    // 10 — Ekstresi olmayan kart geçmiş üretmez; mevcut davranış korunur.
    [Fact]
    public async Task CardWithoutStatement_ProducesNoHistory()
    {
        await WithStore(async store =>
        {
            var service = TestFactory.Service(store, Today);
            await service.SaveCreditCardAsync(Card(closingDay: 25));

            var card = Assert.Single(
                (await service.GetFinancialPlanAsync()).CreditCards);
            Assert.Empty(card.PaymentPreferences);
        });
    }

    // 10b — Geçmiş, kartı sıfırdan kuran bir kaydetmede kaybolmaz.
    [Fact]
    public async Task History_SurvivesCardRebuiltFromScratch()
    {
        await WithStore(async store =>
        {
            var service = TestFactory.Service(store, Today);
            await service.SaveCreditCardAsync(CardWithStatement(
                CurrentStatementPaymentMode.Minimum));

            // UI ekranları kartı yeniden kurar ve geçmişi taşımaz.
            var rebuilt = CardWithStatement(
                CurrentStatementPaymentMode.Minimum) with
            {
                PaymentPreferences = []
            };
            await service.SaveCreditCardAsync(rebuilt);

            var card = Assert.Single(
                (await service.GetFinancialPlanAsync()).CreditCards);
            Assert.Single(card.PaymentPreferences);
        });
    }

    // Geçmiş SQLite round-trip'inde bozulmadan geri okunur.
    [Fact]
    public async Task History_RoundTripsAcrossRestart()
    {
        var path = TempPath();
        try
        {
            var store = new SqliteCoinFlowStore(path, false, Today);
            await TestFactory.Service(store, Today).SaveCreditCardAsync(
                CardWithStatement(
                    CurrentStatementPaymentMode.Custom,
                    customAmount: 1_250m));
            await store.DisposeAsync();

            var reopened = new SqliteCoinFlowStore(path, false, Today);
            try
            {
                var card = Assert.Single((await TestFactory
                    .Service(reopened, Today)
                    .GetFinancialPlanAsync()).CreditCards);
                var preference = Assert.Single(card.PaymentPreferences);
                Assert.Equal(CurrentStatementPaymentMode.Custom,
                    preference.Mode);
                Assert.Equal(1_250m, preference.CustomAmount);
                Assert.Equal(new DateOnly(2026, 8, 25),
                    preference.EffectiveFromStatementDate);
                Assert.Equal(CardId, preference.CreditCardId);
            }
            finally
            {
                await reopened.DisposeAsync();
            }
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    // Geçmiş projection'ı beslemez: canonical karar hâlâ
    // CurrentStatementPaymentPlan'dır (mevcut precedence korunur).
    [Fact]
    public void History_DoesNotOverrideProjectionPrecedence()
    {
        var calculator = new CreditCardStatementCalculator();
        var card = CardWithStatement(CurrentStatementPaymentMode.Full) with
        {
            PaymentPreferences =
            [
                Preference(new DateOnly(2026, 8, 25),
                    CurrentStatementPaymentMode.Minimum)
            ]
        };

        var projection = calculator.Project(card, 1, true, 0.05m);

        // Geçmişte "Asgari" yazsa da yürürlükteki plan "Tamamı" olduğu için
        // projection tam ödemeyi kullanır.
        Assert.Equal(10_000m, projection[0].Payment);
    }

    private static CreditCardPaymentPreference Preference(
        DateOnly effectiveFrom,
        CurrentStatementPaymentMode mode) => new()
        {
            CreditCardId = CardId,
            Mode = mode,
            EffectiveFromStatementDate = effectiveFrom,
            CreatedAt = new DateTimeOffset(
                effectiveFrom.Year,
                effectiveFrom.Month,
                effectiveFrom.Day,
                12,
                0,
                0,
                TimeSpan.Zero)
        };

    private static CreditCard Card(int closingDay) => new()
    {
        Id = CardId,
        Name = "Test Kart",
        Bank = "Test Bank",
        Limit = 100_000m,
        CarriedBalance = 0m,
        UnbilledSpending = 0m,
        BalanceAsOfDate = Today,
        StatementClosingDay = closingDay,
        PaymentDueDay = 5,
        MinimumPaymentRate = 0.40m,
        PaymentStrategy = CreditCardPaymentStrategy.AskEachStatement,
        ProjectionFallbackStrategy = ProjectionFallbackStrategy.Minimum
    };

    private static CreditCard CardWithStatement(
        CurrentStatementPaymentMode mode,
        decimal? customAmount = null,
        DateOnly? statementDate = null)
    {
        var close = statementDate ?? new DateOnly(2026, 8, 25);
        return Card(closingDay: 25) with
        {
            CurrentStatement = new CreditCardStatement
            {
                CreditCardId = CardId,
                StatementDate = close,
                DueDate = close.AddDays(11),
                StatementAmount = 10_000m,
                MinimumPaymentAmount = 4_000m
            },
            CurrentStatementPaymentPlan = new CurrentStatementPaymentPlan
            {
                Mode = mode,
                CustomAmount = customAmount
            }
        };
    }

    private static async Task WithStore(
        Func<SqliteCoinFlowStore, Task> test)
    {
        var path = TempPath();
        var store = new SqliteCoinFlowStore(path, false, Today);
        try
        {
            await test(store);
        }
        finally
        {
            await store.DisposeAsync();
            DeleteDatabase(path);
        }
    }

    private static string TempPath() => Path.Combine(
        Path.GetTempPath(),
        $"coinflow-preference-{Guid.NewGuid():N}.db3");

    private static void DeleteDatabase(string path)
    {
        foreach (var suffix in new[] { string.Empty, "-shm", "-wal" })
        {
            var file = path + suffix;
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }
}
