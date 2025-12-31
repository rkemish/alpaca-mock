using AlpacaMock.Api.Middleware;
using AlpacaMock.Domain.Accounts;
using AlpacaMock.Infrastructure.Cosmos;
using Microsoft.Azure.Cosmos;

namespace AlpacaMock.Api.Endpoints;

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/v1/accounts")
            .WithTags("Accounts");

        group.MapPost("/", CreateAccount);
        group.MapGet("/", ListAccounts);
        group.MapGet("/{accountId}", GetAccount);
        group.MapPatch("/{accountId}", UpdateAccount);
        group.MapDelete("/{accountId}", CloseAccount);
    }

    private static async Task<IResult> CreateAccount(
        HttpContext context,
        CosmosDbContext cosmos,
        CreateAccountRequest request)
    {
        var sessionId = GetSessionId(context);
        if (string.IsNullOrEmpty(sessionId))
            return Results.BadRequest(new { code = 40010000, message = "X-Session-Id header required" });

        var account = new Account
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = sessionId,
            Cash = request.InitialCash ?? 100_000m,
            Contact = request.Contact != null ? new ContactInfo
            {
                EmailAddress = request.Contact.EmailAddress,
                PhoneNumber = request.Contact.PhoneNumber,
                City = request.Contact.City,
                State = request.Contact.State,
                PostalCode = request.Contact.PostalCode,
                Country = request.Contact.Country ?? "USA"
            } : null,
            Identity = request.Identity != null ? new IdentityInfo
            {
                GivenName = request.Identity.GivenName,
                FamilyName = request.Identity.FamilyName,
                DateOfBirth = request.Identity.DateOfBirth
            } : null
        };

        account.PortfolioValue = account.Cash;
        account.BuyingPower = account.Cash;
        account.Equity = account.Cash;

        await cosmos.Accounts.CreateItemAsync(account, new PartitionKey(sessionId));

        return Results.Created($"/v1/accounts/{account.Id}", MapToResponse(account));
    }

    private static async Task<IResult> ListAccounts(
        HttpContext context,
        CosmosDbContext cosmos)
    {
        var sessionId = GetSessionId(context);
        if (string.IsNullOrEmpty(sessionId))
            return Results.BadRequest(new { code = 40010000, message = "X-Session-Id header required" });

        var query = new QueryDefinition("SELECT * FROM c WHERE c.sessionId = @sessionId")
            .WithParameter("@sessionId", sessionId);

        var accounts = new List<Account>();
        using var iterator = cosmos.Accounts.GetItemQueryIterator<Account>(query);

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            accounts.AddRange(response);
        }

        return Results.Ok(accounts.Select(MapToResponse));
    }

    private static async Task<IResult> GetAccount(
        HttpContext context,
        string accountId,
        CosmosDbContext cosmos)
    {
        var sessionId = GetSessionId(context);
        if (string.IsNullOrEmpty(sessionId))
            return Results.BadRequest(new { code = 40010000, message = "X-Session-Id header required" });

        try
        {
            var response = await cosmos.Accounts.ReadItemAsync<Account>(
                accountId,
                new PartitionKey(sessionId));

            return Results.Ok(MapToResponse(response.Resource));
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Results.NotFound(new { code = 40410000, message = "Account not found" });
        }
    }

    private static async Task<IResult> UpdateAccount(
        HttpContext context,
        string accountId,
        CosmosDbContext cosmos,
        UpdateAccountRequest request)
    {
        var sessionId = GetSessionId(context);
        if (string.IsNullOrEmpty(sessionId))
            return Results.BadRequest(new { code = 40010000, message = "X-Session-Id header required" });

        try
        {
            var response = await cosmos.Accounts.ReadItemAsync<Account>(
                accountId,
                new PartitionKey(sessionId));

            var account = response.Resource;

            if (request.Contact != null && account.Contact != null)
            {
                if (request.Contact.EmailAddress != null)
                    account.Contact.EmailAddress = request.Contact.EmailAddress;
                if (request.Contact.PhoneNumber != null)
                    account.Contact.PhoneNumber = request.Contact.PhoneNumber;
            }

            await cosmos.Accounts.UpsertItemAsync(account, new PartitionKey(sessionId));

            return Results.Ok(MapToResponse(account));
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Results.NotFound(new { code = 40410000, message = "Account not found" });
        }
    }

    private static async Task<IResult> CloseAccount(
        HttpContext context,
        string accountId,
        CosmosDbContext cosmos)
    {
        var sessionId = GetSessionId(context);
        if (string.IsNullOrEmpty(sessionId))
            return Results.BadRequest(new { code = 40010000, message = "X-Session-Id header required" });

        try
        {
            var response = await cosmos.Accounts.ReadItemAsync<Account>(
                accountId,
                new PartitionKey(sessionId));

            var account = response.Resource;
            account.Status = AccountStatus.AccountClosed;

            await cosmos.Accounts.UpsertItemAsync(account, new PartitionKey(sessionId));

            return Results.NoContent();
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Results.NotFound(new { code = 40410000, message = "Account not found" });
        }
    }

    private static string? GetSessionId(HttpContext context)
        => context.Request.Headers["X-Session-Id"].FirstOrDefault();

    private static object MapToResponse(Account account) => new
    {
        id = account.Id,
        account_number = account.AccountNumber,
        status = account.Status.ToString().ToUpperInvariant(),
        crypto_status = account.CryptoStatus.ToString().ToUpperInvariant(),
        currency = account.Currency,
        cash = account.Cash.ToString("F2"),
        portfolio_value = account.PortfolioValue.ToString("F2"),
        buying_power = account.BuyingPower.ToString("F2"),
        equity = account.Equity.ToString("F2"),
        last_equity = account.LastEquity.ToString("F2"),
        long_market_value = account.LongMarketValue.ToString("F2"),
        short_market_value = account.ShortMarketValue.ToString("F2"),
        initial_margin = account.InitialMargin.ToString("F2"),
        maintenance_margin = account.MaintenanceMargin.ToString("F2"),
        daytrade_count = account.DayTradeCount,
        pattern_day_trader = account.PatternDayTrader,
        trading_blocked = account.TradingBlocked,
        transfers_blocked = account.TransfersBlocked,
        created_at = account.CreatedAt
    };
}

public record CreateAccountRequest(
    decimal? InitialCash = null,
    ContactInfoRequest? Contact = null,
    IdentityInfoRequest? Identity = null);

public record ContactInfoRequest(
    string? EmailAddress = null,
    string? PhoneNumber = null,
    string? City = null,
    string? State = null,
    string? PostalCode = null,
    string? Country = null);

public record IdentityInfoRequest(
    string? GivenName = null,
    string? FamilyName = null,
    DateOnly? DateOfBirth = null);

public record UpdateAccountRequest(
    ContactInfoRequest? Contact = null);
