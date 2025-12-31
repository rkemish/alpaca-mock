using AlpacaMock.Api.Endpoints;
using AlpacaMock.Api.Middleware;
using AlpacaMock.Infrastructure.Cosmos;
using AlpacaMock.Infrastructure.InMemory;
using AlpacaMock.Infrastructure.Postgres;

var builder = WebApplication.CreateBuilder(args);

// Configuration
var useInMemoryCosmos = bool.TryParse(
    builder.Configuration["USE_INMEMORY_COSMOS"]
    ?? Environment.GetEnvironmentVariable("USE_INMEMORY_COSMOS"),
    out var inMemory) && inMemory;

var cosmosConnectionString = builder.Configuration["Cosmos:ConnectionString"]
    ?? Environment.GetEnvironmentVariable("COSMOS_CONNECTION_STRING");

// Environment variable takes precedence for Docker deployments
var postgresConnectionString = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
    ?? builder.Configuration["Postgres:ConnectionString"]
    ?? throw new InvalidOperationException("Postgres connection string not configured");

// Add services
builder.Services.AddOpenApi();

// Session storage - use in-memory for local dev or Cosmos for production
if (useInMemoryCosmos || string.IsNullOrEmpty(cosmosConnectionString))
{
    Console.WriteLine("Using in-memory session storage (data will not persist)");
    builder.Services.AddSingleton<AlpacaMock.Infrastructure.ISessionRepository, InMemorySessionRepository>();
}
else
{
    Console.WriteLine("Using Cosmos DB for session storage");
    builder.Services.AddSingleton(new CosmosDbContext(cosmosConnectionString));
    builder.Services.AddSingleton<AlpacaMock.Infrastructure.ISessionRepository, SessionRepository>();
}

// PostgreSQL
builder.Services.AddSingleton(new BarRepository(postgresConnectionString));

// Domain services
builder.Services.AddSingleton<AlpacaMock.Domain.Trading.MatchingEngine>();
builder.Services.AddSingleton<AlpacaMock.Domain.Trading.OrderValidator>();
builder.Services.AddSingleton<AlpacaMock.Domain.Trading.DayTradeTracker>();

var app = builder.Build();

// Initialize Cosmos DB if using it
if (!useInMemoryCosmos && !string.IsNullOrEmpty(cosmosConnectionString))
{
    using var scope = app.Services.CreateScope();
    var cosmos = scope.ServiceProvider.GetRequiredService<CosmosDbContext>();
    await cosmos.InitializeAsync();
}

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Basic auth middleware
app.UseMiddleware<BasicAuthMiddleware>();

// Map endpoints
app.MapSessionEndpoints();
app.MapAccountEndpoints();
app.MapTradingEndpoints();
app.MapMarketDataEndpoints();

// Health check
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTimeOffset.UtcNow }));

app.Run();
