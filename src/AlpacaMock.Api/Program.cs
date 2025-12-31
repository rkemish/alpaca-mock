using AlpacaMock.Api.Endpoints;
using AlpacaMock.Api.Middleware;
using AlpacaMock.Infrastructure.Cosmos;
using AlpacaMock.Infrastructure.Postgres;

var builder = WebApplication.CreateBuilder(args);

// Configuration
var cosmosConnectionString = builder.Configuration["Cosmos:ConnectionString"]
    ?? Environment.GetEnvironmentVariable("COSMOS_CONNECTION_STRING")
    ?? throw new InvalidOperationException("Cosmos connection string not configured");

var postgresConnectionString = builder.Configuration["Postgres:ConnectionString"]
    ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
    ?? throw new InvalidOperationException("Postgres connection string not configured");

// Add services
builder.Services.AddOpenApi();

// Cosmos DB
builder.Services.AddSingleton(new CosmosDbContext(cosmosConnectionString));
builder.Services.AddSingleton<SessionRepository>();

// PostgreSQL
builder.Services.AddSingleton(new BarRepository(postgresConnectionString));

// Domain services
builder.Services.AddSingleton<AlpacaMock.Domain.Trading.MatchingEngine>();
builder.Services.AddSingleton<AlpacaMock.Domain.Trading.OrderValidator>();
builder.Services.AddSingleton<AlpacaMock.Domain.Trading.DayTradeTracker>();

var app = builder.Build();

// Initialize databases
using (var scope = app.Services.CreateScope())
{
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
