using AlpacaMock.Domain.Accounts;
using AlpacaMock.Domain.Sessions;
using AlpacaMock.Domain.Trading;

namespace AlpacaMock.Infrastructure;

/// <summary>
/// Interface for session storage operations.
/// </summary>
public interface ISessionRepository
{
    // Sessions
    Task<Session?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<Session>> GetByApiKeyAsync(string apiKeyId, CancellationToken ct = default);
    Task CreateAsync(Session session, CancellationToken ct = default);
    Task UpdateAsync(Session session, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);

    // Accounts
    Task<Account?> GetAccountAsync(string sessionId, string accountId, CancellationToken ct = default);
    Task<IReadOnlyList<Account>> GetAccountsAsync(string sessionId, CancellationToken ct = default);
    Task UpsertAccountAsync(Account account, CancellationToken ct = default);

    // Orders
    Task<Order?> GetOrderAsync(string sessionId, string orderId, CancellationToken ct = default);
    Task<IReadOnlyList<Order>> GetOrdersAsync(string sessionId, string? accountId = null, CancellationToken ct = default);
    Task<IReadOnlyList<Order>> GetActiveOrdersAsync(string sessionId, CancellationToken ct = default);
    Task UpsertOrderAsync(Order order, CancellationToken ct = default);
    Task UpsertOrdersAsync(IEnumerable<Order> orders, CancellationToken ct = default);

    // Positions
    Task<Position?> GetPositionAsync(string sessionId, string accountId, string symbol, CancellationToken ct = default);
    Task<IReadOnlyList<Position>> GetPositionsAsync(string sessionId, string accountId, CancellationToken ct = default);
    Task UpsertPositionAsync(Position position, CancellationToken ct = default);
    Task DeletePositionAsync(string sessionId, string accountId, string symbol, CancellationToken ct = default);
}
