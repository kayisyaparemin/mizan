using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Abstractions;

public interface ICreditCardRepository
{
    Task<IReadOnlyList<CreditCard>> GetCreditCardsAsync(
        CancellationToken cancellationToken = default);
    Task UpsertCreditCardAsync(
        CreditCard card,
        CancellationToken cancellationToken = default);
    Task DeleteCreditCardAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
