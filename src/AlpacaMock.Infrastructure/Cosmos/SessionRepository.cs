using AlpacaMock.Domain.Sessions;
using Microsoft.Azure.Cosmos;

namespace AlpacaMock.Infrastructure.Cosmos;

/// <summary>
/// Repository for managing session state in Cosmos DB.
/// </summary>
public class SessionRepository
{
    private readonly CosmosDbContext _context;

    public SessionRepository(CosmosDbContext context)
    {
        _context = context;
    }

    public async Task<Session> CreateAsync(Session session, CancellationToken cancellationToken = default)
    {
        var response = await _context.Sessions.CreateItemAsync(
            session,
            new PartitionKey(session.Id),
            cancellationToken: cancellationToken);

        return response.Resource;
    }

    public async Task<Session?> GetByIdAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _context.Sessions.ReadItemAsync<Session>(
                sessionId,
                new PartitionKey(sessionId),
                cancellationToken: cancellationToken);

            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<Session>> GetByApiKeyAsync(string apiKeyId, CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.apiKeyId = @apiKeyId")
            .WithParameter("@apiKeyId", apiKeyId);

        var sessions = new List<Session>();
        using var iterator = _context.Sessions.GetItemQueryIterator<Session>(query);

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            sessions.AddRange(response);
        }

        return sessions;
    }

    public async Task<Session> UpdateAsync(Session session, CancellationToken cancellationToken = default)
    {
        var response = await _context.Sessions.UpsertItemAsync(
            session,
            new PartitionKey(session.Id),
            cancellationToken: cancellationToken);

        return response.Resource;
    }

    public async Task DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.Sessions.DeleteItemAsync<Session>(
                sessionId,
                new PartitionKey(sessionId),
                cancellationToken: cancellationToken);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Already deleted
        }
    }
}
