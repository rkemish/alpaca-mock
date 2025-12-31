using System.Net.Http;
using Microsoft.Azure.Cosmos;

namespace AlpacaMock.Infrastructure.Cosmos;

/// <summary>
/// Cosmos DB context for managing session state.
/// </summary>
public class CosmosDbContext : IAsyncDisposable
{
    private readonly CosmosClient _client;
    private readonly string _databaseName;
    private Database? _database;

    public const string SessionsContainer = "sessions";
    public const string AccountsContainer = "accounts";
    public const string OrdersContainer = "orders";
    public const string PositionsContainer = "positions";
    public const string EventsContainer = "events";

    public CosmosDbContext(string connectionString, string databaseName = "alpacamock")
    {
        var options = new CosmosClientOptions
        {
            SerializerOptions = new CosmosSerializationOptions
            {
                PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
            }
        };

        // Detect emulator by checking for well-known emulator key or endpoint
        var isEmulator = connectionString.Contains("C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==")
            || connectionString.Contains("localhost:8081")
            || connectionString.Contains("cosmosdb:8081");

        if (isEmulator)
        {
            // Disable SSL verification for emulator's self-signed certificate
            options.HttpClientFactory = () =>
            {
                var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                };
                return new HttpClient(handler);
            };
            options.ConnectionMode = ConnectionMode.Gateway;
        }

        _client = new CosmosClient(connectionString, options);
        _databaseName = databaseName;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var response = await _client.CreateDatabaseIfNotExistsAsync(_databaseName, cancellationToken: cancellationToken);
        _database = response.Database;

        // Create containers with appropriate partition keys
        await CreateContainerAsync(SessionsContainer, "/id", cancellationToken);
        await CreateContainerAsync(AccountsContainer, "/sessionId", cancellationToken);
        await CreateContainerAsync(OrdersContainer, "/sessionId", cancellationToken);
        await CreateContainerAsync(PositionsContainer, "/sessionId", cancellationToken);
        await CreateContainerAsync(EventsContainer, "/sessionId", cancellationToken);
    }

    private async Task CreateContainerAsync(string containerName, string partitionKeyPath, CancellationToken cancellationToken)
    {
        if (_database == null) throw new InvalidOperationException("Database not initialized");

        await _database.CreateContainerIfNotExistsAsync(
            new ContainerProperties(containerName, partitionKeyPath),
            cancellationToken: cancellationToken);
    }

    public Container GetContainer(string containerName)
    {
        if (_database == null) throw new InvalidOperationException("Database not initialized");
        return _database.GetContainer(containerName);
    }

    public Container Sessions => GetContainer(SessionsContainer);
    public Container Accounts => GetContainer(AccountsContainer);
    public Container Orders => GetContainer(OrdersContainer);
    public Container Positions => GetContainer(PositionsContainer);
    public Container Events => GetContainer(EventsContainer);

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await Task.CompletedTask;
    }
}
