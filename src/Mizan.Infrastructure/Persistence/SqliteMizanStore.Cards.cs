using Mizan.Application.Abstractions;
using Mizan.Domain.Calculations;
using Mizan.Domain.Models;
using SQLite;

namespace Mizan.Infrastructure.Persistence;

public sealed partial class SqliteMizanStore
{
    public async Task<IReadOnlyList<CreditCard>> GetCreditCardsAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var cards = await _database.Table<CreditCardRow>().ToListAsync();
        var charges = await _database
            .Table<CardInstallmentRow>()
            .ToListAsync();
        var payments = await _database
            .Table<CreditCardPaymentPlanRow>()
            .ToListAsync();
        var statements = await _database
            .Table<CreditCardStatementRow>()
            .ToListAsync();
        var preferences = await _database
            .Table<CreditCardPaymentPreferenceRow>()
            .ToListAsync();
        return cards.Select(row => FromRow(
            row,
            charges.Where(x => x.CreditCardId == row.Id),
            payments.Where(x => x.CreditCardId == row.Id),
            statements.Where(x => x.CreditCardId == row.Id),
            preferences.Where(x => x.CreditCardId == row.Id))).ToArray();
    }

    public async Task UpsertCreditCardAsync(
        CreditCard card,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await _database.RunInTransactionAsync(connection =>
        {
            connection.InsertOrReplace(ToRow(card));
            connection.Execute(
                "DELETE FROM card_installments WHERE CreditCardId = ?",
                Key(card.Id));
            foreach (var charge in card.Charges)
            {
                connection.Insert(
                    ToRow(charge with { CreditCardId = card.Id }));
            }

            connection.Execute(
                "DELETE FROM credit_card_payment_plans WHERE CreditCardId = ?",
                Key(card.Id));
            foreach (var payment in card.PaymentPlans)
            {
                connection.Insert(
                    ToRow(payment with { CreditCardId = card.Id }));
            }

            connection.Execute(
                "DELETE FROM credit_card_statements WHERE CreditCardId = ?",
                Key(card.Id));
            if (card.CurrentStatement is { } statement)
            {
                connection.Insert(ToRow(
                    statement with { CreditCardId = card.Id },
                    card.CurrentStatementPaymentPlan));
            }

            connection.Execute(
                "DELETE FROM credit_card_payment_preferences WHERE CreditCardId = ?",
                Key(card.Id));
            foreach (var preference in card.PaymentPreferences)
            {
                connection.Insert(
                    ToRow(preference with { CreditCardId = card.Id }));
            }
        });
    }

    public async Task DeleteCreditCardAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.ExecuteAsync(
            "DELETE FROM card_installments WHERE CreditCardId = ?",
            Key(id));
        await _database.ExecuteAsync(
            "DELETE FROM credit_card_payment_plans WHERE CreditCardId = ?",
            Key(id));
        await _database.ExecuteAsync(
            "DELETE FROM credit_card_statements WHERE CreditCardId = ?",
            Key(id));
        await _database.ExecuteAsync(
            "DELETE FROM credit_card_payment_preferences WHERE CreditCardId = ?",
            Key(id));
        await _database.ExecuteAsync(
            "DELETE FROM credit_cards WHERE Id = ?",
            Key(id));
    }

    private static void InsertCreditCard(SQLiteConnection connection, CreditCard card)
    {
        connection.InsertOrReplace(ToRow(card));
        connection.Execute(
            "DELETE FROM card_installments WHERE CreditCardId = ?",
            Key(card.Id));
        foreach (var charge in card.Charges)
        {
            connection.Insert(ToRow(charge with
            {
                CreditCardId = card.Id
            }));
        }

        connection.Execute(
            "DELETE FROM credit_card_payment_plans WHERE CreditCardId = ?",
            Key(card.Id));
        foreach (var payment in card.PaymentPlans)
        {
            connection.Insert(ToRow(payment with
            {
                CreditCardId = card.Id
            }));
        }

        connection.Execute(
            "DELETE FROM credit_card_statements WHERE CreditCardId = ?",
            Key(card.Id));
        if (card.CurrentStatement is { } statement)
        {
            connection.Insert(ToRow(
                statement with { CreditCardId = card.Id },
                card.CurrentStatementPaymentPlan));
        }
    }

    private async Task MigrateLegacyCreditCardsAsync()
    {
        var cards = await _database.Table<CreditCardRow>().ToListAsync();
        foreach (var row in cards.Where(x =>
                     x.StatementModelVersion <
                     CurrentCardStatementModelVersion))
        {
            if (row.StatementModelVersion < 2)
            {
                row.CarriedBalance = row.LastStatementRemaining > 0m
                    ? row.LastStatementRemaining
                    : row.LastStatementDebt;
                row.UnbilledSpending = row.CurrentCycleSpending;
                row.BalanceAsOfDate = FormatDate(_migrationDate);

                if (row.PaymentMode == 1 &&
                    row.ManualPaymentAmount is > 0m)
                {
                    var close = CreditCardStatementCalculator
                        .ResolveStatementCloseOnOrAfter(
                            _migrationDate,
                            row.StatementClosingDay);
                    var due = CreditCardStatementCalculator
                        .ResolvePaymentDueDate(
                            close,
                            row.PaymentDueDay);
                    await _database.InsertOrReplaceAsync(ToRow(
                        new CreditCardPaymentPlan
                        {
                            CreditCardId = ParseKey(row.Id),
                            DueDate = due,
                            PaymentType =
                                CreditCardPaymentType.FixedAmount,
                            Amount = row.ManualPaymentAmount.Value
                        }));
                }
            }

            if (row.StatementModelVersion < 3)
            {
                row.PaymentStrategy =
                    (int)CreditCardPaymentStrategy.AskEachStatement;
                row.FixedPaymentAmount = null;
                row.ProjectionFallbackStrategy =
                    (int)ProjectionFallbackStrategy.None;
                row.ProjectionFallbackFixedAmount = null;
            }

            if (string.IsNullOrWhiteSpace(row.BalanceAsOfDate))
            {
                row.BalanceAsOfDate = FormatDate(_migrationDate);
            }

            row.StatementModelVersion =
                CurrentCardStatementModelVersion;
            await _database.UpdateAsync(row);
        }
    }
}
