using System.Globalization;
using Mizan.Application.Abstractions;
using Mizan.Application.Models;
using Mizan.Domain.Models;

namespace Mizan.Infrastructure.Persistence;

public sealed partial class SqliteMizanStore
{
    public async Task<PeriodObservation?> GetPeriodObservationAsync(
        Guid periodPlanSnapshotId,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var key = Key(periodPlanSnapshotId);
        var row = await _database
            .Table<PeriodObservationRow>()
            .FirstOrDefaultAsync(x => x.PeriodPlanSnapshotId == key);
        if (row is null)
        {
            return null;
        }

        var observationKey = row.Id;
        var payments = await _database
            .Table<PeriodObservationPaymentRow>()
            .Where(x => x.PeriodObservationId == observationKey)
            .ToListAsync();
        var flows = await _database
            .Table<PeriodObservationFlowRow>()
            .Where(x => x.PeriodObservationId == observationKey)
            .ToListAsync();
        return new PeriodObservation
        {
            Id = ParseKey(row.Id),
            PeriodPlanSnapshotId = ParseKey(row.PeriodPlanSnapshotId),
            ObservedOn = ParseDate(row.ObservedOn),
            ObservedBalance = row.ObservedBalance,
            ObservedLivingSpend = row.ObservedLivingSpend,
            Note = row.Note,
            CreatedAtUtc = ParseTimestamp(row.CreatedAtUtc),
            UpdatedAtUtc = ParseTimestamp(row.UpdatedAtUtc),
            Payments = payments.Select(x => new PeriodObservationPayment
            {
                Id = ParseKey(x.Id),
                PeriodObservationId = ParseKey(x.PeriodObservationId),
                PeriodPlanPaymentLineId = ParseKey(x.PeriodPlanPaymentLineId),
                Status = (ActualPaymentStatus)x.Status,
                ActualAmount = x.ActualAmount,
                ActualPaymentDate = ParseNullableDate(x.ActualPaymentDate),
                Note = x.Note
            }).ToArray(),
            Flows = flows.Select(x => new PeriodObservationFlow
            {
                Id = ParseKey(x.Id),
                PeriodObservationId = ParseKey(x.PeriodObservationId),
                Type = (ActualFlowType)x.Type,
                Name = x.Name,
                Category = x.Category,
                Date = ParseDate(x.Date),
                Amount = x.Amount
            }).ToArray()
        };
    }

    public async Task UpsertPeriodObservationAsync(
        PeriodObservation observation,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await _database.RunInTransactionAsync(connection =>
        {
            // Açık plan başına tek defter. Aynı plana ikinci bir gözlem satırı
            // yazılırsa hangisinin geçerli olduğu belirsizleşir.
            // Anahtarlar ifade ağacının dışında hesaplanır; sqlite-net
            // Key(...) çağrısını SQL fonksiyonu sanıp sorguyu reddediyor.
            var planKey = Key(observation.PeriodPlanSnapshotId);
            var observationKey = Key(observation.Id);
            var existing = connection
                .Table<PeriodObservationRow>()
                .FirstOrDefault(x => x.PeriodPlanSnapshotId == planKey);
            if (existing is not null && existing.Id != observationKey)
            {
                connection.Execute(
                    "DELETE FROM period_observation_payments WHERE PeriodObservationId = ?",
                    existing.Id);
                connection.Execute(
                    "DELETE FROM period_observation_flows WHERE PeriodObservationId = ?",
                    existing.Id);
                connection.Execute(
                    "DELETE FROM period_observations WHERE Id = ?",
                    existing.Id);
            }

            connection.InsertOrReplace(new PeriodObservationRow
            {
                Id = observationKey,
                PeriodPlanSnapshotId = planKey,
                ObservedOn = FormatDate(observation.ObservedOn),
                ObservedBalance = observation.ObservedBalance,
                ObservedLivingSpend = observation.ObservedLivingSpend,
                Note = observation.Note,
                CreatedAtUtc = Timestamp(observation.CreatedAtUtc),
                UpdatedAtUtc = Timestamp(observation.UpdatedAtUtc)
            });
            connection.Execute(
                "DELETE FROM period_observation_payments WHERE PeriodObservationId = ?",
                observationKey);
            connection.Execute(
                "DELETE FROM period_observation_flows WHERE PeriodObservationId = ?",
                observationKey);
            foreach (var payment in observation.Payments)
            {
                connection.Insert(new PeriodObservationPaymentRow
                {
                    Id = Key(payment.Id),
                    PeriodObservationId = observationKey,
                    PeriodPlanPaymentLineId = Key(payment.PeriodPlanPaymentLineId),
                    Status = (int)payment.Status,
                    ActualAmount = payment.ActualAmount,
                    ActualPaymentDate = FormatNullableDate(payment.ActualPaymentDate),
                    Note = payment.Note
                });
            }

            foreach (var flow in observation.Flows)
            {
                connection.Insert(new PeriodObservationFlowRow
                {
                    Id = Key(flow.Id),
                    PeriodObservationId = observationKey,
                    Type = (int)flow.Type,
                    Name = flow.Name,
                    Category = flow.Category,
                    Date = FormatDate(flow.Date),
                    Amount = flow.Amount
                });
            }
        });
    }

    public async Task DeletePeriodObservationAsync(
        Guid periodPlanSnapshotId,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var key = Key(periodPlanSnapshotId);
        await _database.ExecuteAsync(
            "DELETE FROM period_observation_payments WHERE PeriodObservationId IN " +
            "(SELECT Id FROM period_observations WHERE PeriodPlanSnapshotId = ?)",
            key);
        await _database.ExecuteAsync(
            "DELETE FROM period_observation_flows WHERE PeriodObservationId IN " +
            "(SELECT Id FROM period_observations WHERE PeriodPlanSnapshotId = ?)",
            key);
        await _database.ExecuteAsync(
            "DELETE FROM period_observations WHERE PeriodPlanSnapshotId = ?",
            key);
    }

    public async Task<IReadOnlyList<PaymentReminderResponse>> GetPaymentReminderResponsesAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var rows = await _database.Table<PaymentReminderResponseRow>().ToListAsync();
        return rows
            .Select(x => new PaymentReminderResponse(
                x.DueKey,
                x.Name,
                ParseDate(x.DueDate),
                x.Amount,
                x.Kind == (int)PaymentReminderAnswerKind.Paid
                    ? PaymentReminderAnswerKind.Paid
                    : PaymentReminderAnswerKind.Snoozed,
                ParseLocalTime(x.AnsweredAt),
                string.IsNullOrWhiteSpace(x.SnoozedUntil)
                    ? null
                    : ParseLocalTime(x.SnoozedUntil)))
            .OrderBy(x => x.DueDate)
            .ThenBy(x => x.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task UpsertPaymentReminderResponsesAsync(
        IReadOnlyList<PaymentReminderResponse> responses,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await _database.RunInTransactionAsync(connection =>
        {
            foreach (var response in responses)
            {
                connection.InsertOrReplace(new PaymentReminderResponseRow
                {
                    DueKey = response.DueKey,
                    Name = response.Name,
                    DueDate = FormatDate(response.DueDate),
                    Amount = response.Amount,
                    Kind = (int)response.Kind,
                    AnsweredAt = LocalTime(response.AnsweredAt),
                    SnoozedUntil = response.SnoozedUntil is { } until
                        ? LocalTime(until)
                        : null
                });
            }
        });
    }

    public async Task DeletePaymentReminderResponseAsync(
        string dueKey,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.ExecuteAsync(
            "DELETE FROM payment_reminder_responses WHERE DueKey = ?",
            dueKey);
    }

    // Hatırlatıcı saatleri telefonun yerel saatidir; bölge bilgisi taşımaz.
    private static string LocalTime(DateTime value) =>
        value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

    private static DateTime ParseLocalTime(string value) =>
        DateTime.TryParseExact(
            value,
            "yyyy-MM-ddTHH:mm:ss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : DateTime.MinValue;
}
