using Mizan.Application.Abstractions;
using Mizan.Application.Models;
using Mizan.Domain.Calculations;
using Mizan.Domain.Models;

namespace Mizan.Infrastructure.Persistence;

public sealed partial class SqliteMizanStore
{
    public async Task<IReadOnlyList<SimulationDraft>> GetSimulationDraftsAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var drafts = await _database.Table<SimulationDraftRow>().ToListAsync();
        var conditions = await _database
            .Table<SimulationDraftConditionRow>()
            .ToListAsync();
        return drafts
            .Select(row => new SimulationDraft(
                ParseKey(row.Id),
                row.Name,
                ParseTimestamp(row.CreatedAt),
                ParseTimestamp(row.UpdatedAt),
                conditions
                    .Where(x => x.DraftId == row.Id)
                    .OrderBy(x => x.Position)
                    .Select(FromRow)
                    .ToArray()))
            .OrderByDescending(x => x.UpdatedAt)
            .ToArray();
    }

    public async Task UpsertSimulationDraftAsync(
        SimulationDraft draft,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await _database.RunInTransactionAsync(connection =>
        {
            connection.InsertOrReplace(new SimulationDraftRow
            {
                Id = Key(draft.Id),
                Name = draft.Name,
                CreatedAt = Timestamp(draft.CreatedAt),
                UpdatedAt = Timestamp(draft.UpdatedAt)
            });
            // Koşul listesi baştan yazılır: sıra da veri, tek tek upsert
            // etmek silinen bir koşulu geride bırakırdı.
            connection.Execute(
                "DELETE FROM simulation_draft_conditions WHERE DraftId = ?",
                Key(draft.Id));
            for (var index = 0; index < draft.Conditions.Count; index++)
            {
                connection.Insert(ToRow(
                    draft.Id,
                    index,
                    draft.Conditions[index]));
            }
        });
    }

    public async Task DeleteSimulationDraftAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.ExecuteAsync(
            "DELETE FROM simulation_draft_conditions WHERE DraftId = ?",
            Key(id));
        await _database.ExecuteAsync(
            "DELETE FROM simulation_drafts WHERE Id = ?",
            Key(id));
    }

    public async Task ApplySimulationBatchAsync(
        SimulationPersistenceBatch batch,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        await _database.RunInTransactionAsync(connection =>
        {
            foreach (var expense in batch.PlannedLargeExpenses)
            {
                connection.InsertOrReplace(ToRow(expense));
            }

            foreach (var plan in batch.PaymentPlans)
            {
                connection.InsertOrReplace(new PaymentPlanRow
                {
                    Id = Key(plan.Id),
                    Name = plan.Name,
                    Kind = (int)plan.Kind,
                    OriginalAmount = plan.OriginalAmount,
                    TotalRepaymentAmount = plan.TotalRepaymentAmount
                });
                connection.Execute(
                    "DELETE FROM payment_installments WHERE PlanId = ?",
                    Key(plan.Id));
                foreach (var installment in plan.Installments)
                {
                    connection.Insert(
                        ToRow(installment with { PlanId = plan.Id }));
                }
            }

            foreach (var card in batch.CreditCards)
            {
                InsertCreditCard(connection, card);
            }

            foreach (var income in batch.OtherIncomes)
            {
                connection.InsertOrReplace(ToRow(income));
            }

            foreach (var salary in batch.Salaries)
            {
                connection.InsertOrReplace(ToRow(salary));
            }

            foreach (var strategy in batch.PaymentAssignmentStrategies)
            {
                connection.InsertOrReplace(ToRow(strategy));
            }

            foreach (var prepayment in batch.LoanPrepayments)
            {
                connection.InsertOrReplace(ToRow(prepayment));
            }
        });
    }

    private static SimulationDraftConditionRow ToRow(
        Guid draftId,
        int position,
        SimulationDraftCondition condition)
    {
        var request = condition.Request;
        return new SimulationDraftConditionRow
        {
            Id = Key(request.ScenarioId),
            DraftId = Key(draftId),
            Position = position,
            IsEnabled = condition.IsEnabled,
            Type = (int)request.Type,
            Name = request.Name,
            Amount = request.Amount,
            StartDate = FormatDate(request.StartDate),
            PaymentCount = request.PaymentCount,
            FirstPaymentDate = FormatNullableDate(request.FirstPaymentDate),
            CreditCardId = request.CreditCardId is { } cardId
                ? Key(cardId)
                : null,
            TotalRepaymentAmount = request.TotalRepaymentAmount,
            NewCashFlowAllocationMode =
                request.NewCashFlowAllocationMode is { } mode
                    ? (int)mode
                    : null,
            EffectivePeriodDate = FormatNullableDate(request.EffectivePeriodDate),
            CardPaymentType = request.CardPaymentType is { } cardPaymentType
                ? (int)cardPaymentType
                : null,
            AppliesToAllStatements = request.AppliesToAllStatements,
            LoanId = request.LoanId is { } loanId ? Key(loanId) : null,
            PrepaymentMode = request.PrepaymentMode is { } prepaymentMode
                ? (int)prepaymentMode
                : null
        };
    }

    private static SimulationDraftCondition FromRow(
        SimulationDraftConditionRow row) =>
        new(
            new SimulationRequest(
                (SimulationScenarioType)row.Type,
                row.Name,
                row.Amount,
                ParseDate(row.StartDate),
                row.PaymentCount,
                ParseNullableDate(row.FirstPaymentDate),
                string.IsNullOrWhiteSpace(row.CreditCardId)
                    ? null
                    : ParseKey(row.CreditCardId),
                row.TotalRepaymentAmount,
                row.NewCashFlowAllocationMode is { } mode
                    ? (CashFlowAllocationMode)mode
                    : null,
                ParseNullableDate(row.EffectivePeriodDate),
                ParseKey(row.Id),
                row.CardPaymentType is { } cardPaymentType
                    ? (CreditCardPaymentType)cardPaymentType
                    : null,
                row.AppliesToAllStatements,
                string.IsNullOrWhiteSpace(row.LoanId)
                    ? null
                    : ParseKey(row.LoanId),
                row.PrepaymentMode is { } prepaymentMode
                    ? (LoanPrepaymentMode)prepaymentMode
                    : null),
            row.IsEnabled);
}
