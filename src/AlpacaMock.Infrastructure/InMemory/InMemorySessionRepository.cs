using System.Collections.Concurrent;
using AlpacaMock.Domain.Accounts;
using AlpacaMock.Domain.Sessions;
using AlpacaMock.Domain.Trading;

namespace AlpacaMock.Infrastructure.InMemory;

/// <summary>
/// In-memory implementation of session storage for local development.
/// Data is not persisted between restarts.
/// </summary>
public class InMemorySessionRepository : ISessionRepository
{
    private readonly ConcurrentDictionary<string, Session> _sessions = new();
    private readonly ConcurrentDictionary<string, Account> _accounts = new();
    private readonly ConcurrentDictionary<string, Order> _orders = new();
    private readonly ConcurrentDictionary<string, Position> _positions = new();

    // Sessions
    public Task<Session?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        _sessions.TryGetValue(id, out var session);
        return Task.FromResult(session);
    }

    public Task<IReadOnlyList<Session>> GetByApiKeyAsync(string apiKeyId, CancellationToken ct = default)
    {
        var sessions = _sessions.Values
            .Where(s => s.ApiKeyId == apiKeyId)
            .ToList();
        return Task.FromResult<IReadOnlyList<Session>>(sessions);
    }

    public Task CreateAsync(Session session, CancellationToken ct = default)
    {
        _sessions[session.Id] = session;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Session session, CancellationToken ct = default)
    {
        _sessions[session.Id] = session;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id, CancellationToken ct = default)
    {
        _sessions.TryRemove(id, out _);
        // Also delete related data
        var accountsToRemove = _accounts.Where(kvp => kvp.Value.SessionId == id).Select(kvp => kvp.Key).ToList();
        foreach (var key in accountsToRemove) _accounts.TryRemove(key, out _);

        var ordersToRemove = _orders.Where(kvp => kvp.Value.SessionId == id).Select(kvp => kvp.Key).ToList();
        foreach (var key in ordersToRemove) _orders.TryRemove(key, out _);

        var positionsToRemove = _positions.Where(kvp => kvp.Value.SessionId == id).Select(kvp => kvp.Key).ToList();
        foreach (var key in positionsToRemove) _positions.TryRemove(key, out _);

        return Task.CompletedTask;
    }

    // Accounts
    public Task<Account?> GetAccountAsync(string sessionId, string accountId, CancellationToken ct = default)
    {
        var key = $"{sessionId}:{accountId}";
        _accounts.TryGetValue(key, out var account);
        return Task.FromResult(account);
    }

    public Task<IReadOnlyList<Account>> GetAccountsAsync(string sessionId, CancellationToken ct = default)
    {
        var accounts = _accounts.Values
            .Where(a => a.SessionId == sessionId)
            .ToList();
        return Task.FromResult<IReadOnlyList<Account>>(accounts);
    }

    public Task UpsertAccountAsync(Account account, CancellationToken ct = default)
    {
        var key = $"{account.SessionId}:{account.Id}";
        _accounts[key] = account;
        return Task.CompletedTask;
    }

    // Orders
    public Task<Order?> GetOrderAsync(string sessionId, string orderId, CancellationToken ct = default)
    {
        var key = $"{sessionId}:{orderId}";
        _orders.TryGetValue(key, out var order);
        return Task.FromResult(order);
    }

    public Task<IReadOnlyList<Order>> GetOrdersAsync(string sessionId, string? accountId = null, CancellationToken ct = default)
    {
        var query = _orders.Values.Where(o => o.SessionId == sessionId);
        if (accountId != null)
            query = query.Where(o => o.AccountId == accountId);
        return Task.FromResult<IReadOnlyList<Order>>(query.ToList());
    }

    public Task<IReadOnlyList<Order>> GetActiveOrdersAsync(string sessionId, CancellationToken ct = default)
    {
        var orders = _orders.Values
            .Where(o => o.SessionId == sessionId && o.IsActive)
            .ToList();
        return Task.FromResult<IReadOnlyList<Order>>(orders);
    }

    public Task UpsertOrderAsync(Order order, CancellationToken ct = default)
    {
        var key = $"{order.SessionId}:{order.Id}";
        _orders[key] = order;
        return Task.CompletedTask;
    }

    public Task UpsertOrdersAsync(IEnumerable<Order> orders, CancellationToken ct = default)
    {
        foreach (var order in orders)
        {
            var key = $"{order.SessionId}:{order.Id}";
            _orders[key] = order;
        }
        return Task.CompletedTask;
    }

    // Positions
    public Task<Position?> GetPositionAsync(string sessionId, string accountId, string symbol, CancellationToken ct = default)
    {
        var key = $"{sessionId}:{accountId}:{symbol}";
        _positions.TryGetValue(key, out var position);
        return Task.FromResult(position);
    }

    public Task<IReadOnlyList<Position>> GetPositionsAsync(string sessionId, string accountId, CancellationToken ct = default)
    {
        var positions = _positions.Values
            .Where(p => p.SessionId == sessionId && p.AccountId == accountId)
            .ToList();
        return Task.FromResult<IReadOnlyList<Position>>(positions);
    }

    public Task UpsertPositionAsync(Position position, CancellationToken ct = default)
    {
        var key = $"{position.SessionId}:{position.AccountId}:{position.Symbol}";
        _positions[key] = position;
        return Task.CompletedTask;
    }

    public Task DeletePositionAsync(string sessionId, string accountId, string symbol, CancellationToken ct = default)
    {
        var key = $"{sessionId}:{accountId}:{symbol}";
        _positions.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
