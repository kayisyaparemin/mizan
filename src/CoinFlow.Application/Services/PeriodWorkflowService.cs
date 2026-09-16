using CoinFlow.Application.Abstractions;
using CoinFlow.Application.Models;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Services;

public sealed class PeriodWorkflowService(
    ICoinFlowStore store,
    IClock clock,
    PeriodReviewService reviewService,
    PeriodProgressService periodProgressService,
    IFinancialPlanQueryService queryService) : IPeriodWorkflowService
{
    public async Task<PeriodReviewAvailability> GetPeriodReviewAvailabilityAsync(
        CancellationToken cancellationToken = default)
    {
        await queryService.GetFinancialPlanAsync(cancellationToken);
        return await reviewService.GetAvailabilityAsync(cancellationToken);
    }

    public Task<PeriodReviewContext> GetPeriodReviewContextAsync(
        Guid? planId = null,
        CancellationToken cancellationToken = default) =>
        reviewService.GetContextAsync(planId, cancellationToken);

    public async Task<PeriodReviewDraft?> GetObservedReviewDraftAsync(
        Guid periodPlanSnapshotId,
        CancellationToken cancellationToken = default)
    {
        var observation = await store.GetPeriodObservationAsync(
            periodPlanSnapshotId,
            cancellationToken);
        if (observation is null)
        {
            return null;
        }

        return new PeriodReviewDraft(
            periodPlanSnapshotId,
            observation.Payments
                .Select(x => new ActualPaymentDraft(
                    x.PeriodPlanPaymentLineId,
                    x.Status,
                    x.ActualAmount,
                    x.ActualPaymentDate,
                    x.Note))
                .ToArray(),
            observation.ObservedLivingSpend,
            0m,
            observation.Flows
                .Select(x => new ActualFlowDraft(
                    x.Type,
                    x.Name,
                    x.Category,
                    x.Date,
                    x.Amount))
                .ToArray(),
            [],
            observation.ObservedBalance,
            observation.Note);
    }

    public Task<PeriodReviewPreview> PreviewPeriodReviewAsync(
        PeriodReviewDraft draft,
        CancellationToken cancellationToken = default) =>
        reviewService.PreviewAsync(draft, cancellationToken);

    public async Task<FinancialReviewResult> FinalizePeriodReviewAsync(
        PeriodReviewDraft draft,
        CancellationToken cancellationToken = default)
    {
        var plan = await queryService.GetFinancialPlanAsync(cancellationToken);
        var result = await reviewService.FinalizeAsync(
            plan,
            draft,
            cancellationToken);
        await store.DeletePeriodObservationAsync(
            draft.PeriodPlanSnapshotId,
            cancellationToken);
        return result;
    }

    public Task<PeriodProgress?> GetPeriodProgressAsync(
        CancellationToken cancellationToken = default) =>
        periodProgressService.GetAsync(cancellationToken);

    public Task<PaymentReminderMode> GetPaymentReminderModeAsync(
        CancellationToken cancellationToken = default) =>
        store.GetPaymentReminderModeAsync(cancellationToken);

    public Task SavePaymentReminderModeAsync(
        PaymentReminderMode mode,
        CancellationToken cancellationToken = default) =>
        store.SavePaymentReminderModeAsync(mode, cancellationToken);

    public async Task<IReadOnlyList<PaymentReminder>> GetPaymentRemindersAsync(
        DateTime now,
        CancellationToken cancellationToken = default) =>
        (await GetPaymentReminderBoardAsync(now, cancellationToken)).Reminders;

    public async Task<PaymentReminderBoard> GetPaymentReminderBoardAsync(
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var mode = await store.GetPaymentReminderModeAsync(cancellationToken);
        var history = await store.GetFinancialHistoryAsync(cancellationToken);
        var openPlan = PeriodProgressService.ResolveOpenPlan(history);
        var responses = (await store.GetPaymentReminderResponsesAsync(cancellationToken))
            .Where(x => openPlan is null || x.DueDate > openPlan.PeriodStart)
            .ToArray();
        var snoozed = responses
            .Where(x => x.Kind == PaymentReminderAnswerKind.Snoozed)
            .ToArray();
        var paid = responses
            .Where(x => x.Kind == PaymentReminderAnswerKind.Paid)
            .ToArray();
        if (mode == PaymentReminderMode.Off)
        {
            return new PaymentReminderBoard(mode, [], [], snoozed, paid, null);
        }

        var dues = await GetUpcomingPaymentDuesAsync(now, cancellationToken);
        var scheduled = PaymentReminderPlanner.Plan(mode, dues, now);
        var reminders = scheduled
            .Concat(PaymentReminderPlanner.FollowUps(snoozed, now))
            .OrderBy(x => x.NotifyAt)
            .ThenBy(x => x.Key, StringComparer.Ordinal)
            .ToArray();
        var snoozedKeys = snoozed.Select(x => x.DueKey).ToHashSet();
        var upcoming = PaymentReminderPlanner.Preview(
            PaymentReminderPlanner.Plan(
                mode,
                dues.Where(x => !snoozedKeys.Contains(x.Key)),
                now),
            now);
        return new PaymentReminderBoard(
            mode,
            reminders,
            upcoming,
            snoozed,
            paid,
            PaymentReminderPlanner.Sample(dues, now));
    }

    public async Task RecordPaymentReminderAnswerAsync(
        PaymentReminderAnswer answer,
        CancellationToken cancellationToken = default)
    {
        if (answer.Payments.Count == 0)
        {
            return;
        }

        var existing = (await store.GetPaymentReminderResponsesAsync(cancellationToken))
            .ToDictionary(x => x.DueKey, StringComparer.Ordinal);
        var responses = answer.Payments
            .GroupBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => x.First())
            .Where(x => answer.Kind == PaymentReminderAnswerKind.Paid ||
                        !existing.TryGetValue(x.Key, out var previous) ||
                        previous.Kind != PaymentReminderAnswerKind.Paid)
            .Select(x => new PaymentReminderResponse(
                x.Key,
                x.Name,
                x.DueDate,
                x.Amount,
                answer.Kind,
                answer.AnsweredAt,
                answer.Kind == PaymentReminderAnswerKind.Snoozed
                    ? answer.SnoozedUntil
                    : null))
            .ToArray();
        if (responses.Length > 0)
        {
            await store.UpsertPaymentReminderResponsesAsync(
                responses,
                cancellationToken);
        }
    }

    public Task UndoPaymentReminderAnswerAsync(
        string dueKey,
        CancellationToken cancellationToken = default) =>
        store.DeletePaymentReminderResponseAsync(dueKey, cancellationToken);

    public Task<IReadOnlyList<PaymentReminderResponse>> GetPaymentReminderResponsesAsync(
        CancellationToken cancellationToken = default) =>
        store.GetPaymentReminderResponsesAsync(cancellationToken);

    public async Task<IReadOnlyList<PaymentDue>> GetUpcomingPaymentDuesAsync(
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(now);
        var last = today.AddDays(PaymentReminderPlanner.HorizonDays);
        var history = await store.GetFinancialHistoryAsync(cancellationToken);
        var openPlan = PeriodProgressService.ResolveOpenPlan(history);
        if (openPlan is null)
        {
            return [];
        }

        var revision = history.Revisions
            .Where(x => x.PeriodPlanSnapshotId == openPlan.Id)
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.RevisionNumber)
            .LastOrDefault();
        var observation = await store.GetPeriodObservationAsync(
            openPlan.Id,
            cancellationToken);
        var settled = observation?.Payments
            .Where(x => x.Status != ActualPaymentStatus.Unpaid)
            .Select(x => x.PeriodPlanPaymentLineId)
            .ToHashSet() ?? [];
        var dues = (revision?.PaymentLines ?? openPlan.PaymentLines)
            .Where(x => !settled.Contains(x.Id) &&
                        x.PlannedDate >= today &&
                        x.PlannedDate <= last)
            .Select(x => new PaymentDue(
                DueKey(x.SourceEntityId, x.Name, x.PlannedDate),
                x.Name,
                x.PlannedDate,
                x.PlannedAmount))
            .ToList();

        if (last > openPlan.PeriodEnd)
        {
            var periods = await queryService.GetFuturePeriodsAsync(
                periodCount: 3,
                cancellationToken: cancellationToken);
            dues.AddRange(periods
                .SelectMany(x => x.MandatoryItems)
                .Where(x => x.DueDate > openPlan.PeriodEnd &&
                            x.DueDate >= today &&
                            x.DueDate <= last)
                .Select(x => new PaymentDue(
                    DueKey(x.PaymentId, x.Name, x.DueDate),
                    x.Name,
                    x.DueDate,
                    x.Amount)));
            var plan = await queryService.GetFinancialPlanAsync(cancellationToken);
            dues.AddRange(plan.PlannedLargeExpenses
                .Where(x => x.Status == PlannedExpenseStatus.Planned &&
                            x.ExactDate > openPlan.PeriodEnd &&
                            x.ExactDate >= today &&
                            x.ExactDate <= last)
                .Select(x => new PaymentDue(
                    DueKey(x.Id, x.Name, x.ExactDate),
                    x.Name,
                    x.ExactDate,
                    x.Amount)));
        }

        var answeredPaid = (await store.GetPaymentReminderResponsesAsync(cancellationToken))
            .Where(x => x.Kind == PaymentReminderAnswerKind.Paid)
            .Select(x => x.DueKey)
            .ToHashSet(StringComparer.Ordinal);
        return dues
            .Where(x => !answeredPaid.Contains(x.Key))
            .GroupBy(x => x.Key)
            .Select(x => x.First())
            .OrderBy(x => x.DueDate)
            .ThenBy(x => x.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<PeriodObservation> ObserveCurrentBalanceAsync(
        decimal balance,
        CancellationToken cancellationToken = default)
    {
        var openPlan = await ResolveOpenPlanAsync(cancellationToken);
        var existing = await store.GetPeriodObservationAsync(
            openPlan.Id,
            cancellationToken);
        var now = clock.UtcNow;
        var observation = (existing ?? new PeriodObservation
            {
                PeriodPlanSnapshotId = openPlan.Id,
                CreatedAtUtc = now
            }) with
            {
                ObservedOn = clock.Today,
                ObservedBalance = balance,
                UpdatedAtUtc = now
            };
        await store.UpsertPeriodObservationAsync(
            observation,
            cancellationToken);
        return observation;
    }

    public async Task<PeriodObservation> ObservePaymentAsync(
        Guid periodPlanPaymentLineId,
        ActualPaymentStatus status,
        decimal actualAmount,
        DateOnly? actualPaymentDate = null,
        string note = "",
        CancellationToken cancellationToken = default)
    {
        var openPlan = await ResolveOpenPlanAsync(cancellationToken);
        if (openPlan.PaymentLines.All(x => x.Id != periodPlanPaymentLineId))
        {
            throw new InvalidOperationException(
                "Gözlenen ödeme satırı açık dönem planında bulunamadı.");
        }

        var now = clock.UtcNow;
        var existing = await store.GetPeriodObservationAsync(
            openPlan.Id,
            cancellationToken);
        var observation = existing ?? new PeriodObservation
        {
            PeriodPlanSnapshotId = openPlan.Id,
            ObservedOn = clock.Today,
            CreatedAtUtc = now
        };
        var payments = observation.Payments
            .Where(x => x.PeriodPlanPaymentLineId != periodPlanPaymentLineId)
            .Append(new PeriodObservationPayment
            {
                PeriodObservationId = observation.Id,
                PeriodPlanPaymentLineId = periodPlanPaymentLineId,
                Status = status,
                ActualAmount = actualAmount,
                ActualPaymentDate = actualPaymentDate ?? clock.Today,
                Note = note.Trim()
            })
            .ToArray();
        observation = observation with
        {
            Payments = payments,
            ObservedOn = clock.Today,
            UpdatedAtUtc = now
        };
        await store.UpsertPeriodObservationAsync(
            observation,
            cancellationToken);
        return observation;
    }

    private async Task<PeriodPlanSnapshot> ResolveOpenPlanAsync(
        CancellationToken cancellationToken)
    {
        var history = await store.GetFinancialHistoryAsync(cancellationToken);
        return PeriodProgressService.ResolveOpenPlan(history) ??
               throw new InvalidOperationException(
                   "Gözlem kaydedebilmek için önce güncel bir dönem planı gerekir.");
    }

    private static string DueKey(Guid sourceId, string name, DateOnly date) =>
        PaymentReminderPlanner.DueKey(sourceId, name, date);
}
