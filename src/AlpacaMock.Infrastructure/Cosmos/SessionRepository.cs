using AlpacaMock.Domain.Accounts;
using AlpacaMock.Domain.Sessions;
using AlpacaMock.Domain.Trading;
using Microsoft.Azure.Cosmos;

namespace AlpacaMock.Infrastructure.Cosmos;

/// <summary>
/// Repository for managing session state in Cosmos DB.
/// </summary>
public class SessionRepository : ISessionRepository
{
    private readonly CosmosDbContext _context;

    public SessionRepository(CosmosDbContext context)
    {
        _context = context;
    }

    // Sessions
    public async Task CreateAsync(Session session, CancellationToken ct = default)
    {
        await _context.Sessions.CreateItemAsync(
            session,
            new PartitionKey(session.Id),
            cancellationToken: ct);
    }

    public async Task<Session?> GetByIdAsync(string sessionId, CancellationToken ct = default)
    {
        try
        {
            var response = await _context.Sessions.ReadItemAsync<Session>(
                sessionId,
                new PartitionKey(sessionId),
                cancellationToken: ct);

            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<Session>> GetByApiKeyAsync(string apiKeyId, CancellationToken ct = default)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.apiKeyId = @apiKeyId")
            .WithParameter("@apiKeyId", apiKeyId);

        var sessions = new List<Session>();
        using var iterator = _context.Sessions.GetItemQueryIterator<Session>(query);

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(ct);
            sessions.AddRange(response);
        }

        return sessions;
    }

    public async Task UpdateAsync(Session session, CancellationToken ct = default)
    {
        await _context.Sessions.UpsertItemAsync(
            session,
            new PartitionKey(session.Id),
            cancellationToken: ct);
    }

    public async Task DeleteAsync(string sessionId, CancellationToken ct = default)
    {
        try
        {
            await _context.Sessions.DeleteItemAsync<Session>(
                sessionId,
                new PartitionKey(sessionId),
                cancellationToken: ct);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Already deleted
        }
    }

    // Accounts
    public async Task<Account?> GetAccountAsync(string sessionId, string accountId, CancellationToken ct = default)
    {
        try
        {
            var response = await _context.Accounts.ReadItemAsync<Account>(
                accountId,
                new PartitionKey(sessionId),
                cancellationToken: ct);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<Account>> GetAccountsAsync(string sessionId, CancellationToken ct = default)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.sessionId = @sessionId")
            .WithParameter("@sessionId", sessionId);

        var accounts = new List<Account>();
        using var iterator = _context.Accounts.GetItemQueryIterator<Account>(query);

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(ct);
            accounts.AddRange(response);
        }

        return accounts;
    }

    public async Task UpsertAccountAsync(Account account, CancellationToken ct = default)
    {
        await _context.Accounts.UpsertItemAsync(account, new PartitionKey(account.SessionId), cancellationToken: ct);
    }

    // Orders
    public async Task<Order?> GetOrderAsync(string sessionId, string orderId, CancellationToken ct = default)
    {
        try
        {
            var response = await _context.Orders.ReadItemAsync<Order>(
                orderId,
                new PartitionKey(sessionId),
                cancellationToken: ct);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<Order>> GetOrdersAsync(string sessionId, string? accountId = null, CancellationToken ct = default)
    {
        var queryText = accountId != null
            ? "SELECT * FROM c WHERE c.sessionId = @sessionId AND c.accountId = @accountId"
            : "SELECT * FROM c WHERE c.sessionId = @sessionId";

        var query = new QueryDefinition(queryText)
            .WithParameter("@sessionId", sessionId);

        if (accountId != null)
            query.WithParameter("@accountId", accountId);

        var orders = new List<Order>();
        using var iterator = _context.Orders.GetItemQueryIterator<Order>(query);

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(ct);
            orders.AddRange(response);
        }

        return orders;
    }

    public async Task<IReadOnlyList<Order>> GetActiveOrdersAsync(string sessionId, CancellationToken ct = default)
    {
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.sessionId = @sessionId AND c.status IN ('New', 'Accepted', 'PartiallyFilled', 'PendingNew')")
            .WithParameter("@sessionId", sessionId);

        var orders = new List<Order>();
        using var iterator = _context.Orders.GetItemQueryIterator<Order>(query);

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(ct);
            orders.AddRange(response);
        }

        return orders;
    }

    public async Task UpsertOrderAsync(Order order, CancellationToken ct = default)
    {
        await _context.Orders.UpsertItemAsync(order, new PartitionKey(order.SessionId), cancellationToken: ct);
    }

    public async Task UpsertOrdersAsync(IEnumerable<Order> orders, CancellationToken ct = default)
    {
        foreach (var order in orders)
        {
            await _context.Orders.UpsertItemAsync(order, new PartitionKey(order.SessionId), cancellationToken: ct);
        }
    }

    // Positions
    public async Task<Position?> GetPositionAsync(string sessionId, string accountId, string symbol, CancellationToken ct = default)
    {
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.sessionId = @sessionId AND c.accountId = @accountId AND c.symbol = @symbol")
            .WithParameter("@sessionId", sessionId)
            .WithParameter("@accountId", accountId)
            .WithParameter("@symbol", symbol);

        using var iterator = _context.Positions.GetItemQueryIterator<Position>(query);

        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(ct);
            return response.FirstOrDefault();
        }

        return null;
    }

    public async Task<IReadOnlyList<Position>> GetPositionsAsync(string sessionId, string accountId, CancellationToken ct = default)
    {
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.sessionId = @sessionId AND c.accountId = @accountId")
            .WithParameter("@sessionId", sessionId)
            .WithParameter("@accountId", accountId);

        var positions = new List<Position>();
        using var iterator = _context.Positions.GetItemQueryIterator<Position>(query);

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(ct);
            positions.AddRange(response);
        }

        return positions;
    }

    public async Task UpsertPositionAsync(Position position, CancellationToken ct = default)
    {
        await _context.Positions.UpsertItemAsync(position, new PartitionKey(position.SessionId), cancellationToken: ct);
    }

    public async Task DeletePositionAsync(string sessionId, string accountId, string symbol, CancellationToken ct = default)
    {
        var position = await GetPositionAsync(sessionId, accountId, symbol, ct);
        if (position != null)
        {
            try
            {
                await _context.Positions.DeleteItemAsync<Position>(
                    position.Id,
                    new PartitionKey(sessionId),
                    cancellationToken: ct);
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Already deleted
            }
        }
    }
}
