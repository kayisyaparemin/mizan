using System.Globalization;
using Mizan.Application.Abstractions;
using Mizan.Application.Models;
using Mizan.Application.Services;
using Mizan.Domain.Calculations;
using Mizan.Domain.Models;
using Mizan.Infrastructure.Persistence;

namespace Mizan.Tests;

public sealed class FinancialSnapshotReviewTests
{
    private static readonly DateOnly InitialDate = new(2026, 8, 20);
    private static readonly DateOnly FirstReviewDate = new(2026, 9, 10);
    private static readonly DateOnly SecondReviewDate = new(2026, 10, 10);

    [Fact]
    public async Task FirstUse_CreatesCurrentSnapshotAndFrozenPlan_WithoutHistory()
    {
        await WithStore(async store =>
        {
            var service = TestFactory.Service(store, InitialDate);
            await service.LoadCanonicalDevelopmentDataAsync();
            await service.GetFinancialPlanAsync();
            var history = await store.GetFinancialHistoryAsync();

            var snapshot = Assert.Single(history.Snapshots);
            var frozen = Assert.Single(history.Plans);
            Assert.True(snapshot.IsCurrent);
            Assert.Equal(InitialDate, snapshot.SnapshotDate);
            Assert.Equal(FirstReviewDate, snapshot.NextSettlementDate);
            Assert.Equal(snapshot.Id, frozen.FinancialSnapshotId);
            Assert.Equal(InitialDate, frozen.PeriodStart);
            Assert.Equal(FirstReviewDate, frozen.PeriodEnd);
            Assert.Equal(FirstReviewDate, frozen.SettlementAvailableFrom);
            Assert.Equal(20_322.58m, frozen.PlannedVariableExpenseAllowance);
            Assert.All(frozen.PaymentLines, line =>
                Assert.True(
                    line.PlannedDate > InitialDate &&
                    line.PlannedDate <= FirstReviewDate));
            Assert.Contains(frozen.PaymentLines, x =>
                x.SourceType == PlanPaymentSourceType.CreditCard &&
                x.PlannedDate == new DateOnly(2026, 9, 7));
            Assert.Contains(frozen.PaymentLines, x =>
                x.SourceType == PlanPaymentSourceType.Loan &&
                x.PlannedDate == new DateOnly(2026, 9, 7));
            Assert.DoesNotContain(frozen.PaymentLines, x =>
                x.PlannedDate == new DateOnly(2026, 9, 18));
            Assert.Empty(history.Actuals);
            Assert.Empty(await service.GetHistoryPeriodsAsync());
            Assert.Equal(115_000m, frozen.PlannedIncome);
            Assert.Equal(
                frozen.OpeningBalance + frozen.PlannedIncome -
                frozen.PlannedMandatoryPayments -
                frozen.PlannedVariableExpenseAllowance -
                frozen.PlannedLargeExpenses -
                frozen.PlannedDeficitInterest,
                frozen.PlannedEndingBalance);
        });
    }

    [Fact]
    public async Task ReviewAvailability_IsDueOnCheckpointAndRemainsDueAfterward()
    {
        await WithStore(async store =>
        {
            var initial = TestFactory.Service(store, InitialDate);
            await initial.LoadCanonicalDevelopmentDataAsync();
            await initial.GetFinancialPlanAsync();

            Assert.False((await TestFactory.Service(
                store,
                FirstReviewDate.AddDays(-1))
                .GetPeriodReviewAvailabilityAsync()).IsDue);

            var onCheckpoint = await TestFactory.Service(
                store,
                FirstReviewDate).GetPeriodReviewAvailabilityAsync();
            Assert.True(onCheckpoint.IsDue);
            Assert.Equal(InitialDate, onCheckpoint.PendingPlan!.PeriodStart);
            Assert.Equal(FirstReviewDate, onCheckpoint.PendingPlan.PeriodEnd);

            Assert.True((await TestFactory.Service(
                store,
                FirstReviewDate.AddDays(1))
                .GetPeriodReviewAvailabilityAsync()).IsDue);
        });
    }

    [Fact]
    public async Task ReviewTexts_UseTurkishFormats_OnEnglishDevice()
    {
        // Emülatör İngilizce çalışıyor; ekrana giden metinler yine de Türkçe
        // ay adı ve Türkçe ondalık ayırıcı kullanmalı.
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            await WithStore(async store =>
            {
                var initial = TestFactory.Service(store, InitialDate);
                await initial.LoadCanonicalDevelopmentDataAsync();
                await initial.GetFinancialPlanAsync();

                Assert.Equal(
                    "Son güncelleme: 20 Ağustos 2026",
                    (await TestFactory.Service(
                        store,
                        FirstReviewDate.AddDays(-1))
                        .GetPeriodReviewAvailabilityAsync()).Message);

                var review = TestFactory.Service(store, FirstReviewDate);
                Assert.Equal(
                    "20 Ağustos dönemi güncellenmeye hazır.",
                    (await review.GetPeriodReviewAvailabilityAsync()).Message);

                var context = await review.GetPeriodReviewContextAsync();
                var preview = await review.PreviewPeriodReviewAsync(
                    DefaultDraft(
                        context,
                        context.OriginalPlan.PlannedVariableExpenseAllowance + 1_234.50m));
                Assert.Matches(
                    @"planın \d{1,3}(\.\d{3})*,\d{2} TL (altında|üzerinde)",
                    preview.Comparison.Summary);
            });
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void InitialReviewDate_DoesNotDependOnCashFlowAllocationMode()
    {
        var basePlan = TestFactory.CanonicalPlan();
        var snapshot = Snapshot(InitialDate);

        foreach (var mode in new[]
                 {
                     CashFlowAllocationMode.PreviousPeriod,
                     CashFlowAllocationMode.UpcomingPeriod
                 })
        {
            var plan = basePlan with
            {
                PaymentAssignmentStrategies =
                [basePlan.PaymentAssignmentStrategies[0] with { Mode = mode }]
            };
            var frozen = Freeze(plan, snapshot);

            Assert.Equal(InitialDate, frozen.PeriodStart);
            Assert.Equal(FirstReviewDate, frozen.PeriodEnd);
            Assert.Equal(FirstReviewDate, frozen.SettlementAvailableFrom);
        }
    }

    [Fact]
    public void InitialReviewPaymentWindow_IsSnapshotExclusiveAndReviewInclusive()
    {
        var planId = Guid.NewGuid();
        var dates = new[]
        {
            new DateOnly(2026, 8, 15),
            new DateOnly(2026, 8, 25),
            new DateOnly(2026, 9, 5),
            new DateOnly(2026, 9, 10),
            new DateOnly(2026, 9, 18)
        };
        var plan = TestFactory.CanonicalPlan() with
        {
            Loans = [],
            CreditCards = [],
            PaymentPlans =
            [
                new TemporaryPaymentPlan
                {
                    Id = planId,
                    Name = "Sınır testi",
                    Installments = dates.Select((date, index) =>
                        new TemporaryPaymentInstallment
                        {
                            PlanId = planId,
                            DueDate = date,
                            Amount = 1_000m + index
                        }).ToArray()
                }
            ]
        };

        var frozen = Freeze(plan, Snapshot(InitialDate));

        Assert.Equal(
            new[]
            {
                new DateOnly(2026, 8, 25),
                new DateOnly(2026, 9, 5),
                new DateOnly(2026, 9, 10)
            },
            frozen.PaymentLines.Select(x => x.PlannedDate));
    }

    [Fact]
    public void VariableExpenseAllowance_IsProratedOnlyForInitialPartialPeriod()
    {
        var plan = TestFactory.CanonicalPlan();

        var partial = Freeze(plan, Snapshot(InitialDate));
        var fullDate = FirstReviewDate;
        var fullPlan = plan with
        {
            Settings = plan.Settings with
            {
                ProjectionAnchorDate = fullDate
            }
        };
        var full = Freeze(fullPlan, Snapshot(fullDate));

        Assert.Equal(20_322.58m, partial.PlannedVariableExpenseAllowance);
        Assert.Equal(30_000m, full.PlannedVariableExpenseAllowance);
    }

    [Fact]
    public async Task FirstInstallOnSalaryDate_StartsWithNextFullReviewOnly()
    {
        await WithStore(async store =>
        {
            var plan = TestFactory.CanonicalPlan();
            await CopyCanonicalStateAsync(
                plan with
                {
                    Settings = plan.Settings with
                    {
                        ProjectionAnchorDate = FirstReviewDate
                    }
                },
                store);

            var service = TestFactory.Service(store, FirstReviewDate);
            await service.GetFinancialPlanAsync();
            var history = await store.GetFinancialHistoryAsync();
            var snapshot = Assert.Single(history.Snapshots);
            var frozen = Assert.Single(history.Plans);

            Assert.Equal(FirstReviewDate, snapshot.SnapshotDate);
            Assert.Equal(SecondReviewDate, snapshot.NextSettlementDate);
            Assert.Equal(FirstReviewDate, frozen.PeriodStart);
            Assert.Equal(SecondReviewDate, frozen.PeriodEnd);
            Assert.Equal(30_000m, frozen.PlannedVariableExpenseAllowance);
            Assert.Empty(history.Actuals);
            Assert.Empty(await service.GetHistoryPeriodsAsync());
        });
    }

    [Fact]
    public async Task ReviewCadence_PreservesInitialPartialThenContinuesMonthly()
    {
        await WithStore(async store =>
        {
            var initial = TestFactory.Service(store, InitialDate);
            await initial.LoadCanonicalDevelopmentDataAsync();
            await initial.GetFinancialPlanAsync();

            Assert.False((await TestFactory.Service(
                store,
                FirstReviewDate.AddDays(-1))
                .GetPeriodReviewAvailabilityAsync()).IsDue);

            var september = TestFactory.Service(store, FirstReviewDate);
            var septemberAvailability = await september
                .GetPeriodReviewAvailabilityAsync();
            Assert.True(septemberAvailability.IsDue);
            var septemberContext = await september
                .GetPeriodReviewContextAsync();
            var firstResult = await september.FinalizePeriodReviewAsync(
                DefaultDraft(
                    septemberContext,
                    septemberContext.OriginalPlan.PlannedVariableExpenseAllowance));

            Assert.Equal(FirstReviewDate, firstResult.NewSnapshot.SnapshotDate);
            Assert.Equal(SecondReviewDate, firstResult.NewSnapshot.NextSettlementDate);
            var firstHistory = Assert.Single(
                await september.GetHistoryPeriodsAsync());
            Assert.Equal(InitialDate, firstHistory.OriginalPlan.PeriodStart);
            Assert.Equal(FirstReviewDate, firstHistory.OriginalPlan.PeriodEnd);

            var october = TestFactory.Service(store, SecondReviewDate);
            var octoberAvailability = await october
                .GetPeriodReviewAvailabilityAsync();
            Assert.True(octoberAvailability.IsDue);
            Assert.Equal(FirstReviewDate,
                octoberAvailability.PendingPlan!.PeriodStart);
            Assert.Equal(SecondReviewDate,
                octoberAvailability.PendingPlan.PeriodEnd);
            Assert.Equal(30_000m,
                octoberAvailability.PendingPlan.PlannedVariableExpenseAllowance);
        });
    }

    [Fact]
    public async Task OverdueReview_UsesScheduledCheckpointAsNewSnapshotDate()
    {
        await WithStore(async store =>
        {
            var initial = TestFactory.Service(store, InitialDate);
            await initial.LoadCanonicalDevelopmentDataAsync();
            await initial.GetFinancialPlanAsync();

            var late = TestFactory.Service(
                store,
                FirstReviewDate.AddDays(1));
            var context = await late.GetPeriodReviewContextAsync();
            var result = await late.FinalizePeriodReviewAsync(
                DefaultDraft(context, context.OriginalPlan.PlannedVariableExpenseAllowance));

            Assert.Equal(FirstReviewDate, result.NewSnapshot.SnapshotDate);
            Assert.Equal(SecondReviewDate, result.NewSnapshot.NextSettlementDate);
            Assert.Equal(FirstReviewDate.AddDays(1),
                DateOnly.FromDateTime(result.Actual.FinalizedAtUtc.Date));
        });
    }

    [Fact]
    public async Task JumpingPastMultipleCheckpoints_DoesNotInventActualHistory()
    {
        await WithStore(async store =>
        {
            var initial = TestFactory.Service(store, InitialDate);
            await initial.LoadCanonicalDevelopmentDataAsync();
            await initial.GetFinancialPlanAsync();

            var jumped = TestFactory.Service(
                store,
                SecondReviewDate.AddDays(1));
            var availability = await jumped
                .GetPeriodReviewAvailabilityAsync();
            var history = await store.GetFinancialHistoryAsync();

            Assert.True(availability.IsDue);
            Assert.Equal(InitialDate, availability.PendingPlan!.PeriodStart);
            Assert.Equal(FirstReviewDate, availability.PendingPlan.PeriodEnd);
            Assert.Empty(history.Actuals);
            Assert.Empty(await jumped.GetHistoryPeriodsAsync());
            Assert.Equal(InitialDate,
                Assert.Single(history.Snapshots, x => x.IsCurrent)
                    .SnapshotDate);
        });
    }

    [Fact]
    public async Task PendingLegacyPlan_IsRepairedWithoutDeletingUserData()
    {
        await WithStore(async store =>
        {
            var service = TestFactory.Service(store, InitialDate);
            await service.LoadCanonicalDevelopmentDataAsync();
            await service.GetFinancialPlanAsync();
            var original = await store.GetFinancialHistoryAsync();
            var snapshot = Assert.Single(original.Snapshots);
            var plan = Assert.Single(original.Plans);
            var legacySnapshot = snapshot with
            {
                NextSettlementDate = SecondReviewDate
            };
            var legacyPlan = plan with
            {
                Id = Guid.NewGuid(),
                PeriodStart = FirstReviewDate,
                PeriodEnd = SecondReviewDate,
                SettlementAvailableFrom = SecondReviewDate,
                PaymentLines = plan.PaymentLines.Select(x => x with
                {
                    Id = Guid.NewGuid()
                }).ToArray()
            };
            await store.ReplacePendingFinancialSnapshotPlanAsync(
                legacySnapshot,
                legacyPlan);

            await service.GetFinancialPlanAsync();
            var repaired = await store.GetFinancialHistoryAsync();
            var repairedSnapshot = Assert.Single(repaired.Snapshots);
            var repairedPlan = Assert.Single(repaired.Plans);

            Assert.Equal(FirstReviewDate,
                repairedSnapshot.NextSettlementDate);
            Assert.Equal(InitialDate, repairedPlan.PeriodStart);
            Assert.Equal(FirstReviewDate, repairedPlan.PeriodEnd);
            Assert.Equal(20_322.58m,
                repairedPlan.PlannedVariableExpenseAllowance);
            Assert.Empty(repaired.Actuals);
        });
    }

    [Fact]
    public async Task ActualActivityDates_MustStayInsideSnapshotReviewWindow()
    {
        await WithStore(async store =>
        {
            var initial = TestFactory.Service(store, InitialDate);
            await initial.LoadCanonicalDevelopmentDataAsync();
            await initial.GetFinancialPlanAsync();
            var review = TestFactory.Service(store, FirstReviewDate);
            var context = await review.GetPeriodReviewContextAsync();
            var valid = DefaultDraft(
                context,
                context.OriginalPlan.PlannedVariableExpenseAllowance);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                review.FinalizePeriodReviewAsync(valid with
                {
                    Flows =
                    [
                        new ActualFlowDraft(
                            ActualFlowType.UnplannedIncome,
                            "Snapshot günündeki gelir",
                            "Diğer",
                            InitialDate,
                            1_000m)
                    ]
                }));

            var firstPayment = valid.Payments[0];
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                review.FinalizePeriodReviewAsync(valid with
                {
                    Payments = valid.Payments.Select(x =>
                        x.PeriodPlanPaymentLineId ==
                        firstPayment.PeriodPlanPaymentLineId
                            ? x with
                            {
                                ActualPaymentDate =
                                    FirstReviewDate.AddDays(1)
                            }
                            : x).ToArray()
                }));
        });
    }

    [Fact]
    public async Task Review_FinalizesAtomically_PersistsAcrossRestart_AndRefreshesBaseline()
    {
        var path = TempPath();
        try
        {
            Guid oldSnapshotId;
            decimal confirmed;
            await using (var first = NewStore(path))
            {
                var initial = TestFactory.Service(first, InitialDate);
                await initial.LoadCanonicalDevelopmentDataAsync();
                await initial.GetFinancialPlanAsync();
                oldSnapshotId = Assert.Single(
                    (await first.GetFinancialHistoryAsync()).Snapshots).Id;

                var review = TestFactory.Service(first, FirstReviewDate);
                var context = await review.GetPeriodReviewContextAsync();
                var draft = DefaultDraft(context, 37_500m);
                var preview = await review.PreviewPeriodReviewAsync(draft);
                confirmed = preview.SuggestedStartingSavings;
                var result = await review.FinalizePeriodReviewAsync(
                    draft with { ConfirmedStartingSavings = confirmed });

                Assert.Equal(
                    FirstReviewDate,
                    result.NewSnapshot.SnapshotDate);
                Assert.Equal(
                    SecondReviewDate,
                    result.NewSnapshot.NextSettlementDate);
                Assert.Equal(oldSnapshotId,
                    result.NewSnapshot.PreviousSnapshotId);
                Assert.Equal(confirmed,
                    result.NewSnapshot.ProjectionOpeningBalance);
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    review.FinalizePeriodReviewAsync(draft));
            }

            await using (var restartedStore = NewStore(path))
            {
                var restarted = TestFactory.Service(
                    restartedStore,
                    FirstReviewDate);
                var plan = await restarted.GetFinancialPlanAsync();
                var history = await restartedStore
                    .GetFinancialHistoryAsync();
                var latest = Assert.Single(history.Snapshots, x =>
                    x.IsCurrent);

                Assert.Equal(FirstReviewDate, latest.SnapshotDate);
                Assert.Equal(confirmed,
                    plan.Settings.ProjectionOpeningBalance);
                Assert.Equal(FirstReviewDate,
                    plan.Settings.ProjectionAnchorDate);
                Assert.Single(history.Actuals);
                Assert.Single(await restarted.GetHistoryPeriodsAsync());
                Assert.False((await restarted
                    .GetPeriodReviewAvailabilityAsync()).IsDue);
                Assert.Equal(
                    confirmed,
                    (await restarted.GetFuturePeriodsAsync(
                        periodCount: 1))[0].OpeningProjectedBalance);
            }
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task PlanRevisionAndFutureSettings_DoNotRewriteFrozenHistory()
    {
        await WithStore(async store =>
        {
            var initial = TestFactory.Service(store, InitialDate);
            await initial.LoadCanonicalDevelopmentDataAsync();
            await initial.GetFinancialPlanAsync();

            var beforeCheckpoint = TestFactory.Service(
                store,
                new DateOnly(2026, 9, 2));
            var settings = (await beforeCheckpoint.GetFinancialPlanAsync())
                .Settings;
            await beforeCheckpoint.SaveSettingsAsync(settings with
            {
                MonthlyVariableExpenseAllowance = 35_000m
            });

            var review = TestFactory.Service(store, FirstReviewDate);
            var context = await review.GetPeriodReviewContextAsync();
            var originalEnding = context.OriginalPlan.PlannedEndingBalance;
            var result = await review.FinalizePeriodReviewAsync(
                DefaultDraft(context, 38_000m));

            var currentSettings = (await review.GetFinancialPlanAsync())
                .Settings;
            await review.SaveSettingsAsync(currentSettings with
            {
                MonthlyVariableExpenseAllowance = 40_000m,
                CreditCardCarryInterestRate = 0.04m
            });

            var period = Assert.Single(
                await review.GetHistoryPeriodsAsync());
            Assert.Equal(20_322.58m,
                period.OriginalPlan.PlannedVariableExpenseAllowance);
            Assert.Equal(23_709.68m,
                Assert.IsType<PeriodPlanRevision>(period.Revision)
                    .PlannedVariableExpenseAllowance);
            Assert.Equal(38_000m, period.Actual.ActualLivingSpend);
            Assert.Equal(originalEnding,
                period.OriginalPlan.PlannedEndingBalance);
            Assert.Equal(40_000m,
                (await review.GetFuturePeriodsAsync(
                    periodCount: 1))[0].VariableExpenseAllowance);
            Assert.Equal(result.NewSnapshot.ProjectionOpeningBalance,
                (await review.GetFinancialPlanAsync())
                    .Settings.ProjectionOpeningBalance);
        });
    }

    [Fact]
    public async Task PlanningChangesBeforeCheckpoint_CreateFinalPlanRevisionFromSharedEngine()
    {
        await WithStore(async store =>
        {
            var initial = TestFactory.Service(store, InitialDate);
            await initial.LoadCanonicalDevelopmentDataAsync();
            await initial.GetFinancialPlanAsync();
            var initialPlan = Assert.Single(
                (await store.GetFinancialHistoryAsync()).Plans);
            Assert.Equal(40_321.97m, initialPlan.PlannedCardPayments);

            var cardChange = TestFactory.Service(
                store,
                new DateOnly(2026, 9, 2));
            var cardPlan = await cardChange.GetFinancialPlanAsync();
            var card = cardPlan.CreditCards.Single();
            await cardChange.SaveCreditCardStatementAsync(
                card.Id,
                card.CurrentStatement!,
                new CurrentStatementPaymentPlan
                {
                    Mode = CurrentStatementPaymentMode.Full
                });

            var strategyChange = TestFactory.Service(
                store,
                new DateOnly(2026, 9, 7));
            var strategyPlan = await strategyChange.GetFinancialPlanAsync();
            var strategy = strategyPlan.PaymentAssignmentStrategies.Single();
            await strategyChange.SaveCashFlowAllocationStrategyAsync(
                strategy with
                {
                    Mode = CashFlowAllocationMode.PreviousPeriod
                });

            var current = Assert.Single(
                await strategyChange.GetFuturePeriodsAsync(periodCount: 1));
            Assert.Equal(100_804.94m, current.CreditCardPayments);

            var review = TestFactory.Service(store, FirstReviewDate);
            var context = await review.GetPeriodReviewContextAsync();
            var revision = Assert.IsType<PeriodPlanRevision>(
                context.Revision);

            Assert.Equal(2, context.RevisionCount);
            Assert.Equal(40_321.97m,
                context.OriginalPlan.PlannedCardPayments);
            Assert.Equal(100_804.94m, revision.PlannedCardPayments);
            Assert.Equal(0m, revision.PlannedCardInterest);
            Assert.Equal(115_306.17m,
                revision.PlannedMandatoryPayments);
            Assert.Equal(20_322.58m, revision.PlannedVariableExpenseAllowance);
            Assert.Equal(1_031.44m, revision.PlannedDeficitInterest);
            Assert.Equal(-21_660.19m, revision.PlannedEndingBalance);

            await review.FinalizePeriodReviewAsync(
                DefaultDraft(context, revision.PlannedVariableExpenseAllowance));
            var history = Assert.Single(await review.GetHistoryPeriodsAsync());
            var cardLine = history.Comparison.Lines.Single(x =>
                x.Category == "Kredi kartları");

            Assert.Equal(100_804.94m, cardLine.Planned);
            Assert.Equal(100_804.94m,
                Assert.IsType<PeriodPlanRevision>(history.Revision)
                    .PlannedCardPayments);
            Assert.Equal(40_321.97m,
                history.OriginalPlan.PlannedCardPayments);

            var afterCheckpoint = TestFactory.Service(
                store,
                FirstReviewDate.AddDays(1));
            var afterPlan = await afterCheckpoint.GetFinancialPlanAsync();
            await afterCheckpoint.SaveCreditCardAsync(
                afterPlan.CreditCards.Single() with
                {
                    PaymentStrategy = CreditCardPaymentStrategy.Minimum
                });
            var persisted = Assert.Single(
                await afterCheckpoint.GetHistoryPeriodsAsync());
            Assert.Equal(100_804.94m,
                Assert.IsType<PeriodPlanRevision>(persisted.Revision)
                    .PlannedCardPayments);
        });
    }

    [Fact]
    public async Task FutureEffectiveStrategy_DoesNotReviseOpenHistoricalPeriod()
    {
        await WithStore(async store =>
        {
            var initial = TestFactory.Service(store, InitialDate);
            await initial.LoadCanonicalDevelopmentDataAsync();
            await initial.GetFinancialPlanAsync();

            var september = TestFactory.Service(
                store,
                new DateOnly(2026, 9, 5));
            await september.SaveCashFlowAllocationStrategyAsync(
                new CashFlowAllocationStrategy
                {
                    Mode = CashFlowAllocationMode.PreviousPeriod,
                    EffectiveFromPeriodDate = SecondReviewDate,
                    Note = "Gelecek dönem değişikliği"
                });

            var history = await store.GetFinancialHistoryAsync();
            Assert.Empty(history.Revisions);

            var context = await TestFactory.Service(store, FirstReviewDate)
                .GetPeriodReviewContextAsync();
            Assert.Null(context.Revision);
            Assert.Equal(0, context.RevisionCount);
        });
    }

    [Fact]
    public async Task PlanningChangeOrder_DoesNotChangeCurrentOrFinalPlan()
    {
        var cardThenStrategy = await BuildFullPreviousScenarioAsync(
            cardFirst: true);
        var strategyThenCard = await BuildFullPreviousScenarioAsync(
            cardFirst: false);

        Assert.Equal(cardThenStrategy, strategyThenCard);
    }

    [Fact]
    public async Task ActualFinalization_DerivesFutureBoundaryAfterClosedCheckpoint()
    {
        var path = TempPath();
        try
        {
            await using (var store = NewStore(path))
            {
                var initial = TestFactory.Service(store, InitialDate);
                await initial.LoadCanonicalDevelopmentDataAsync();
                await initial.GetFinancialPlanAsync();
                await ApplyFullCardAsync(
                    store,
                    new DateOnly(2026, 9, 2));
                await ApplyPreviousStrategyAsync(
                    store,
                    new DateOnly(2026, 9, 7));

                var review = TestFactory.Service(store, FirstReviewDate);
                var context = await review.GetPeriodReviewContextAsync();
                var revision = Assert.IsType<PeriodPlanRevision>(
                    context.Revision);
                Assert.Equal(115_000m, revision.PlannedIncome);
                Assert.Equal(14_501.23m, revision.PlannedLoanPayments);
                Assert.Equal(100_804.94m, revision.PlannedCardPayments);

                var result = await review.FinalizePeriodReviewAsync(
                    PromptActualDraft(context));

                Assert.Equal(FirstReviewDate, result.NewSnapshot.SnapshotDate);
                Assert.Equal(-306.17m,
                    result.NewSnapshot.ProjectionOpeningBalance);
                Assert.Equal(-306.17m,
                    result.Actual.ConfirmedEndingBalance);

                var future = await review.GetFuturePeriodsAsync(
                    periodCount: 12);
                AssertFutureStartsAfterFinalization(
                    future,
                    result.NewSnapshot.ProjectionOpeningBalance);
                Assert.True(future[0].EndingProjectedBalance > 0m);

                var dashboard = await review.GetDashboardAsync();
                Assert.NotNull(dashboard);
                Assert.Equal(SecondReviewDate,
                    dashboard!.CurrentPeriod.PeriodStart);
                Assert.Equal(-306.17m,
                    dashboard.CurrentPeriod.OpeningProjectedBalance);

                var simulation = await review.SimulateAsync(
                    new SimulationRequest(
                        SimulationScenarioType.FutureIncome,
                        "Ek gelir",
                        1_000m,
                        SecondReviewDate));
                Assert.Equal(SecondReviewDate,
                    simulation.Baseline[0].PeriodStart);
                Assert.Equal(-306.17m,
                    simulation.Baseline[0].OpeningProjectedBalance);
                Assert.Equal(SecondReviewDate,
                    simulation.Scenario[0].PeriodStart);

                var target = await review.FindTargetPeriodAsync(
                    future[0].EndingProjectedBalance);
                Assert.Equal(SecondReviewDate, target!.PeriodStart);

                var overview = await review
                    .GetCashFlowAllocationStrategyOverviewAsync();
                Assert.Equal(SecondReviewDate,
                    overview.AvailableEffectivePeriodDates[0]);
                var preview = await review.PreviewCashFlowAllocationStrategyAsync(
                    CashFlowAllocationMode.UpcomingPeriod,
                    SecondReviewDate);
                Assert.Equal(SecondReviewDate, preview.Baseline.PeriodStart);
                Assert.Equal(SecondReviewDate, preview.Scenario.PeriodStart);

                foreach (var today in new[]
                         {
                             FirstReviewDate,
                             FirstReviewDate.AddDays(1),
                             new DateOnly(2026, 9, 20)
                         })
                {
                    var dated = TestFactory.Service(store, today);
                    AssertFutureStartsAfterFinalization(
                        await dated.GetFuturePeriodsAsync(periodCount: 12),
                        result.NewSnapshot.ProjectionOpeningBalance);
                }
            }

            await using (var restartedStore = NewStore(path))
            {
                var restarted = TestFactory.Service(
                    restartedStore,
                    FirstReviewDate.AddDays(1));
                var restartedFuture = await restarted.GetFuturePeriodsAsync(
                    periodCount: 12);
                AssertFutureStartsAfterFinalization(
                    restartedFuture,
                    -306.17m);
            }
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task ActualCardPayment_UpdatesCanonicalCarryExactlyOnce()
    {
        await WithStore(async store =>
        {
            var initial = TestFactory.Service(store, InitialDate);
            await initial.LoadCanonicalDevelopmentDataAsync();
            var originalPlan = await initial.GetFinancialPlanAsync();
            var originalCard = Assert.Single(originalPlan.CreditCards);
            var review = TestFactory.Service(store, FirstReviewDate);
            var context = await review.GetPeriodReviewContextAsync();
            var cardLines = context.OriginalPlan.PaymentLines
                .Where(x =>
                    x.SourceType == PlanPaymentSourceType.CreditCard)
                .OrderBy(x => x.PlannedDate)
                .ToArray();
            var cardLine = cardLines[0];
            var actualAmount = cardLine.PlannedAmount.GetValueOrDefault() +
                               10_000m;
            var reconciler = new CreditCardActualPaymentReconciler(
                new CreditCardStatementCalculator());
            var expectedCard = originalCard;
            foreach (var line in cardLines)
            {
                expectedCard = reconciler.Apply(
                    expectedCard,
                    line.PlannedDate,
                    line.Id == cardLine.Id
                        ? actualAmount
                        : line.PlannedAmount.GetValueOrDefault(),
                    originalPlan.Settings.CreditCardCarryInterestRate);
            }

            var draft = DefaultDraft(context, 30_000m);
            draft = draft with
            {
                Payments = draft.Payments.Select(x =>
                    x.PeriodPlanPaymentLineId == cardLine.Id
                        ? x with
                        {
                            Status = ActualPaymentStatus.DifferentAmount,
                            ActualAmount = actualAmount
                        }
                        : x).ToArray()
            };
            await review.FinalizePeriodReviewAsync(draft);

            var updated = Assert.Single(
                (await review.GetFinancialPlanAsync()).CreditCards);
            Assert.Equal(expectedCard.CarriedBalance,
                updated.CarriedBalance);
            Assert.DoesNotContain(updated.PaymentPlans, x =>
                x.DueDate == cardLine.PlannedDate);
            var history = Assert.Single(
                await review.GetHistoryPeriodsAsync());
            Assert.Equal(actualAmount,
                Assert.Single(history.Actual.Payments, x =>
                    x.PeriodPlanPaymentLineId == cardLine.Id)
                    .ActualAmount);
        });
    }

    [Fact]
    public async Task UnpaidLoan_RemainsOutstandingInFuturePlan()
    {
        await WithStore(async store =>
        {
            var initial = TestFactory.Service(store, InitialDate);
            await initial.LoadCanonicalDevelopmentDataAsync();
            await initial.GetFinancialPlanAsync();
            var review = TestFactory.Service(store, FirstReviewDate);
            var context = await review.GetPeriodReviewContextAsync();
            var loanLine = context.OriginalPlan.PaymentLines.First(x =>
                x.SourceType == PlanPaymentSourceType.Loan);
            var before = (await review.GetFinancialPlanAsync()).Loans
                .Single(x => x.Id == loanLine.SourceEntityId);
            var draft = DefaultDraft(context, 30_000m);
            draft = draft with
            {
                Payments = draft.Payments.Select(x =>
                    x.PeriodPlanPaymentLineId == loanLine.Id
                        ? x with
                        {
                            Status = ActualPaymentStatus.Unpaid,
                            ActualAmount = 0m,
                            ActualPaymentDate = null
                        }
                        : x).ToArray()
            };
            await review.FinalizePeriodReviewAsync(draft);

            var after = (await review.GetFinancialPlanAsync()).Loans
                .Single(x => x.Id == loanLine.SourceEntityId);
            Assert.Equal(before.RemainingInstallmentCount,
                after.RemainingInstallmentCount);
            Assert.True(after.IsActive);
            // Yeni dönemin ilk günü: donmuş planın penceresi checkpoint'i
            // dışarıda bırakır.
            Assert.Equal(FirstReviewDate.AddDays(1), after.NextPaymentDate);
            Assert.Contains(
                (await store.GetFinancialHistoryAsync()).Plans
                    .Single(x => x.PeriodStart == FirstReviewDate)
                    .PaymentLines,
                x => x.SourceEntityId == after.Id &&
                     x.PlannedDate == FirstReviewDate.AddDays(1));
            Assert.Contains(
                (await review.GetFuturePeriodsAsync(periodCount: 1))[0]
                .MandatoryItems,
                x => x.PaymentId == after.Id);
        });
    }

    [Fact]
    public async Task PaidLoansAndScheduledPayments_AdvanceCanonicalState()
    {
        await WithStore(async store =>
        {
            var initial = TestFactory.Service(store, InitialDate);
            await initial.LoadCanonicalDevelopmentDataAsync();
            await initial.GetFinancialPlanAsync();
            var review = TestFactory.Service(store, FirstReviewDate);
            var context = await review.GetPeriodReviewContextAsync();
            var before = await review.GetFinancialPlanAsync();

            await review.FinalizePeriodReviewAsync(
                DefaultDraft(context, 30_000m));
            var after = await review.GetFinancialPlanAsync();

            foreach (var loan in before.Loans)
            {
                var paidCount = context.OriginalPlan.PaymentLines.Count(x =>
                    x.SourceType == PlanPaymentSourceType.Loan &&
                    x.SourceEntityId == loan.Id);
                var updated = after.Loans.Single(x => x.Id == loan.Id);
                Assert.Equal(
                    loan.RemainingInstallmentCount - paidCount,
                    updated.RemainingInstallmentCount);
            }

            var scheduledLineIds = context.OriginalPlan.PaymentLines
                .Where(x => x.SourceType is
                    PlanPaymentSourceType.TemporaryPayment or
                    PlanPaymentSourceType.InstallmentPayment or
                    PlanPaymentSourceType.OtherScheduledPayment)
                .Select(x => x.SourceEntityId)
                .ToHashSet();
            Assert.All(
                after.PaymentPlans
                    .SelectMany(x => x.Installments)
                    .Where(x => scheduledLineIds.Contains(x.Id)),
                x => Assert.True(x.IsPaid));
        });
    }

    /// <summary>
    /// I17 — ödenen taksitin yalnız anapara payı düşer. Garanti: 190.188
    /// anapara, aylık %5,04 → 9.585,21 faiz, 4.916,02 anapara. Eski kod
    /// taksitin tamamını düşüp 175.686,77 yazıyordu.
    /// </summary>
    [Fact]
    public async Task PaidLoanInstallment_ReducesOnlyItsPrincipalPortion()
    {
        await WithStore(async store =>
        {
            var initial = TestFactory.Service(store, InitialDate);
            await initial.LoadCanonicalDevelopmentDataAsync();
            await initial.GetFinancialPlanAsync();
            var review = TestFactory.Service(store, FirstReviewDate);
            var context = await review.GetPeriodReviewContextAsync();
            var garanti = (await review.GetFinancialPlanAsync()).Loans
                .Single(x => x.Bank == "Garanti BBVA");
            Assert.Contains(context.OriginalPlan.PaymentLines, x =>
                x.SourceType == PlanPaymentSourceType.Loan &&
                x.SourceEntityId == garanti.Id);

            await review.FinalizePeriodReviewAsync(
                DefaultDraft(context, 30_000m));

            var after = (await review.GetFinancialPlanAsync()).Loans
                .Single(x => x.Id == garanti.Id);
            Assert.Equal(185_271.98m, after.RemainingDebt!.Value, 0);
        });
    }

    [Fact]
    public async Task SalaryDay31_SnapshotUsesCalendarResolvedReviewDate()
    {
        await WithStore(async store =>
        {
            var service = TestFactory.Service(
                store,
                new DateOnly(2027, 1, 31));
            await service.SaveSettingsAsync(new UserSettings
            {
                IncomeDay = 31,
                MonthlyVariableExpenseAllowance = 10_000m,
                ProjectionOpeningBalance = 5_000m,
                ProjectionAnchorDate = new DateOnly(2027, 1, 31)
            });
            await service.SaveSalaryAsync(new SalaryScheduleEntry
            {
                Amount = 50_000m,
                EffectiveDate = new DateOnly(2027, 1, 1),
                Description = "Maaş"
            });
            await service.CompleteInitialPaymentStrategySetupAsync(
                CashFlowAllocationMode.UpcomingPeriod);

            var snapshot = Assert.Single(
                (await store.GetFinancialHistoryAsync()).Snapshots);
            Assert.Equal(new DateOnly(2027, 2, 28),
                snapshot.NextSettlementDate);
        });
    }

    [Fact]
    public async Task ActualFinalizationHistory_AdvancesBoundaryBeyondFreshInstall()
    {
        var pathA = TempPath();
        var pathB = TempPath();
        try
        {
            FinancialPlan octoberState;
            IReadOnlyList<CashFlowPeriodProjection> projectionA;
            await using (var storeA = NewStore(pathA))
            {
                var initial = TestFactory.Service(storeA, InitialDate);
                await initial.LoadCanonicalDevelopmentDataAsync();
                await initial.GetFinancialPlanAsync();
                var review = TestFactory.Service(storeA, FirstReviewDate);
                var context = await review.GetPeriodReviewContextAsync();
                await review.FinalizePeriodReviewAsync(
                    DefaultDraft(context, 37_500m));
                octoberState = await review.GetFinancialPlanAsync();
                projectionA = await review.GetFuturePeriodsAsync(
                    periodCount: 3);
                Assert.Equal(SecondReviewDate,
                    projectionA[0].PeriodStart);
                Assert.Single(await review.GetHistoryPeriodsAsync());
            }

            await using (var storeB = NewStore(pathB))
            {
                await CopyCanonicalStateAsync(octoberState, storeB);
                var fresh = TestFactory.Service(storeB, FirstReviewDate);
                var projectionB = await fresh.GetFuturePeriodsAsync(
                    periodCount: 3);

                Assert.Equal(FirstReviewDate,
                    projectionB[0].PeriodStart);
                Assert.NotEqual(
                    projectionA.Select(ProjectionSignature),
                    projectionB.Select(ProjectionSignature));
                Assert.Empty(await fresh.GetHistoryPeriodsAsync());
                Assert.Single(
                    (await storeB.GetFinancialHistoryAsync()).Snapshots);
            }
        }
        finally
        {
            DeleteDatabase(pathA);
            DeleteDatabase(pathB);
        }
    }

    [Fact]
    public async Task ClearData_RemovesSnapshotsAndHistory()
    {
        await WithStore(async store =>
        {
            var service = TestFactory.Service(store, InitialDate);
            await service.LoadCanonicalDevelopmentDataAsync();
            await service.GetFinancialPlanAsync();
            Assert.NotEmpty(
                (await store.GetFinancialHistoryAsync()).Snapshots);

            await service.ClearDevelopmentDataAsync();
            var history = await store.GetFinancialHistoryAsync();
            Assert.Empty(history.Snapshots);
            Assert.Empty(history.Plans);
            Assert.Empty(history.Revisions);
            Assert.Empty(history.Actuals);
        });
    }

    private static FinancialSnapshot Snapshot(DateOnly date) => new()
    {
        SnapshotDate = date,
        ProjectionAnchorDate = date,
        ProjectionOpeningBalance = 0m,
        IncomeDay = 10,
        IsCurrent = true,
        Source = FinancialSnapshotSource.Initial,
        CreatedAtUtc = new DateTimeOffset(
            date.Year,
            date.Month,
            date.Day,
            12,
            0,
            0,
            TimeSpan.Zero)
    };

    private static async Task<RevisionScenarioSignature>
        BuildFullPreviousScenarioAsync(bool cardFirst)
    {
        var path = TempPath();
        try
        {
            await using var store = NewStore(path);
            var initial = TestFactory.Service(store, InitialDate);
            await initial.LoadCanonicalDevelopmentDataAsync();
            await initial.GetFinancialPlanAsync();

            if (cardFirst)
            {
                await ApplyFullCardAsync(
                    store,
                    new DateOnly(2026, 9, 2));
                await ApplyPreviousStrategyAsync(
                    store,
                    new DateOnly(2026, 9, 7));
            }
            else
            {
                await ApplyPreviousStrategyAsync(
                    store,
                    new DateOnly(2026, 9, 2));
                await ApplyFullCardAsync(
                    store,
                    new DateOnly(2026, 9, 7));
            }

            var beforeReview = TestFactory.Service(
                store,
                FirstReviewDate.AddDays(-1));
            var current = Assert.Single(
                await beforeReview.GetFuturePeriodsAsync(periodCount: 1));
            var context = await TestFactory
                .Service(store, FirstReviewDate)
                .GetPeriodReviewContextAsync();
            var revision = Assert.IsType<PeriodPlanRevision>(
                context.Revision);
            return new RevisionScenarioSignature(
                current.CreditCardPayments,
                current.MandatoryOutflow,
                revision.PlannedCardPayments,
                revision.PlannedMandatoryPayments,
                revision.PlannedDeficitInterest,
                revision.PlannedEndingBalance,
                context.RevisionCount);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    private static async Task ApplyFullCardAsync(
        SqliteMizanStore store,
        DateOnly today)
    {
        var service = TestFactory.Service(store, today);
        var plan = await service.GetFinancialPlanAsync();
        var card = plan.CreditCards.Single();
        await service.SaveCreditCardStatementAsync(
            card.Id,
            card.CurrentStatement!,
            new CurrentStatementPaymentPlan
            {
                Mode = CurrentStatementPaymentMode.Full
            });
    }

    private static async Task ApplyPreviousStrategyAsync(
        SqliteMizanStore store,
        DateOnly today)
    {
        var service = TestFactory.Service(store, today);
        var plan = await service.GetFinancialPlanAsync();
        var strategy = plan.PaymentAssignmentStrategies.Single(x =>
            x.EffectiveFromPeriodDate == FirstReviewDate);
        await service.SaveCashFlowAllocationStrategyAsync(
            strategy with
            {
                Mode = CashFlowAllocationMode.PreviousPeriod
            });
    }

    private sealed record RevisionScenarioSignature(
        decimal CurrentCardPayments,
        decimal CurrentMandatoryPayments,
        decimal FinalCardPayments,
        decimal FinalMandatoryPayments,
        decimal FinalDeficitInterest,
        decimal FinalEndingSavings,
        int RevisionCount);

    private static PeriodPlanSnapshot Freeze(
        FinancialPlan source,
        FinancialSnapshot snapshot)
    {
        var plan = source with
        {
            Settings = source.Settings with
            {
                ProjectionAnchorDate = snapshot.SnapshotDate,
                ProjectionOpeningBalance =
                    snapshot.ProjectionOpeningBalance,
                IncomeDay = snapshot.IncomeDay
            }
        };
        return new PeriodPlanSnapshotService(
            TestFactory.ProjectionCalculator(),
            new CashFlowPeriodCalculator(),
            new IncomeResolver()).Freeze(
            plan,
            snapshot,
            snapshot.CreatedAtUtc);
    }

    private static PeriodReviewDraft DefaultDraft(
        PeriodReviewContext context,
        decimal living) => new(
        context.OriginalPlan.Id,
        FinalPaymentLines(context).Select(line =>
            new ActualPaymentDraft(
                line.Id,
                line.PlannedAmount is null
                    ? ActualPaymentStatus.Unpaid
                    : ActualPaymentStatus.Paid,
                line.PlannedAmount.GetValueOrDefault(),
                line.PlannedAmount is null ? null : line.PlannedDate))
            .ToArray(),
        living,
        context.Revision?.PlannedDeficitInterest ??
        context.OriginalPlan.PlannedDeficitInterest,
        [],
        [],
        null);

    private static PeriodReviewDraft PromptActualDraft(
        PeriodReviewContext context) => new(
        context.OriginalPlan.Id,
        FinalPaymentLines(context).Select(line =>
            new ActualPaymentDraft(
                line.Id,
                line.PlannedAmount is null
                    ? ActualPaymentStatus.Unpaid
                    : ActualPaymentStatus.Paid,
                line.PlannedAmount.GetValueOrDefault(),
                line.PlannedAmount is null ? null : line.PlannedDate))
            .ToArray(),
        0m,
        0m,
        [],
        [],
        null);

    private static void AssertFutureStartsAfterFinalization(
        IReadOnlyList<CashFlowPeriodProjection> periods,
        decimal OpeningBalance)
    {
        Assert.Equal(12, periods.Count);
        Assert.Equal(
            Enumerable.Range(0, 12)
                .Select(index => CalendarRules.AddMonthsKeepingDay(
                    SecondReviewDate,
                    index,
                    10)),
            periods.Select(x => x.PeriodStart));
        Assert.Equal(OpeningBalance, periods[0].OpeningProjectedBalance);
        Assert.DoesNotContain(periods, x =>
            x.PeriodStart == FirstReviewDate);
        Assert.DoesNotContain(periods, x =>
            x.PeriodStart == FirstReviewDate &&
            x.PrimaryIncome > 0m);
        Assert.All(periods, x => Assert.True(
            x.PeriodStart < x.PeriodEnd,
            $"{x.PeriodStart:yyyy-MM-dd} -> {x.PeriodEnd:yyyy-MM-dd}"));
    }

    private static IReadOnlyList<PeriodPlanPaymentLine> FinalPaymentLines(
        PeriodReviewContext context) =>
        context.Revision?.PaymentLines.Count > 0
            ? context.Revision.PaymentLines
            : context.OriginalPlan.PaymentLines;

    private static object ProjectionSignature(
        CashFlowPeriodProjection row) => new
        {
            row.PeriodStart,
            row.PeriodEnd,
            row.OpeningProjectedBalance,
            row.TotalIncome,
            row.MandatoryOutflow,
            row.VariableExpenseAllowance,
            row.PlannedLargeCashExpenses,
            row.CardInterestGenerated,
            row.DeficitFinancingInterest,
            row.EndingProjectedBalance
        };

    private static async Task CopyCanonicalStateAsync(
        FinancialPlan source,
        IMizanStore target)
    {
        await target.InitializeAsync();
        await target.SaveSettingsAsync(source.Settings);
        foreach (var item in source.Salaries)
            await target.UpsertSalaryAsync(item);
        foreach (var item in source.OtherIncomes)
            await target.UpsertOtherIncomeAsync(item);
        foreach (var item in source.Loans)
            await target.UpsertLoanAsync(item);
        foreach (var item in source.PaymentPlans)
            await target.UpsertPaymentPlanAsync(item);
        foreach (var item in source.CreditCards)
            await target.UpsertCreditCardAsync(item);
        foreach (var item in source.PlannedLargeExpenses)
            await target.UpsertPlannedLargeExpenseAsync(item);
        foreach (var item in source.PaymentAssignmentStrategies)
            await target.UpsertCashFlowAllocationStrategyAsync(item);
    }

    private static async Task WithStore(
        Func<SqliteMizanStore, Task> test)
    {
        var path = TempPath();
        try
        {
            await using var store = NewStore(path);
            await test(store);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    private static SqliteMizanStore NewStore(string path) =>
        new(path, true, InitialDate);

    private static string TempPath() => Path.Combine(
        Path.GetTempPath(),
        $"coinflow-snapshot-{Guid.NewGuid():N}.db3");

    private static void DeleteDatabase(string path)
    {
        foreach (var candidate in new[]
                 {
                     path,
                     path + "-shm",
                     path + "-wal"
                 })
        {
            if (File.Exists(candidate))
            {
                File.Delete(candidate);
            }
        }
    }
}
