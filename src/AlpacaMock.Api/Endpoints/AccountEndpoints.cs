using AlpacaMock.Api.Middleware;
using AlpacaMock.Domain.Accounts;
using AlpacaMock.Infrastructure;

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
        ISessionRepository repo,
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

        await repo.UpsertAccountAsync(account);

        return Results.Created($"/v1/accounts/{account.Id}", MapToResponse(account));
    }

    private static async Task<IResult> ListAccounts(
        HttpContext context,
        ISessionRepository repo)
    {
        var sessionId = GetSessionId(context);
        if (string.IsNullOrEmpty(sessionId))
            return Results.BadRequest(new { code = 40010000, message = "X-Session-Id header required" });

        var accounts = await repo.GetAccountsAsync(sessionId);

        return Results.Ok(accounts.Select(MapToResponse));
    }

    private static async Task<IResult> GetAccount(
        HttpContext context,
        string accountId,
        ISessionRepository repo)
    {
        var sessionId = GetSessionId(context);
        if (string.IsNullOrEmpty(sessionId))
            return Results.BadRequest(new { code = 40010000, message = "X-Session-Id header required" });

        var account = await repo.GetAccountAsync(sessionId, accountId);
        if (account == null)
            return Results.NotFound(new { code = 40410000, message = "Account not found" });

        return Results.Ok(MapToResponse(account));
    }

    private static async Task<IResult> UpdateAccount(
        HttpContext context,
        string accountId,
        ISessionRepository repo,
        UpdateAccountRequest request)
    {
        var sessionId = GetSessionId(context);
        if (string.IsNullOrEmpty(sessionId))
            return Results.BadRequest(new { code = 40010000, message = "X-Session-Id header required" });

        var account = await repo.GetAccountAsync(sessionId, accountId);
        if (account == null)
            return Results.NotFound(new { code = 40410000, message = "Account not found" });

        if (request.Contact != null && account.Contact != null)
        {
            if (request.Contact.EmailAddress != null)
                account.Contact.EmailAddress = request.Contact.EmailAddress;
            if (request.Contact.PhoneNumber != null)
                account.Contact.PhoneNumber = request.Contact.PhoneNumber;
        }

        await repo.UpsertAccountAsync(account);

        return Results.Ok(MapToResponse(account));
    }

    private static async Task<IResult> CloseAccount(
        HttpContext context,
        string accountId,
        ISessionRepository repo)
    {
        var sessionId = GetSessionId(context);
        if (string.IsNullOrEmpty(sessionId))
            return Results.BadRequest(new { code = 40010000, message = "X-Session-Id header required" });

        var account = await repo.GetAccountAsync(sessionId, accountId);
        if (account == null)
            return Results.NotFound(new { code = 40410000, message = "Account not found" });

        account.Status = AccountStatus.AccountClosed;

        await repo.UpsertAccountAsync(account);

        return Results.NoContent();
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
