using AlpacaMock.Domain.Trading;
using AlpacaMock.Infrastructure.Cosmos;
using AlpacaMock.Infrastructure.Postgres;
using Microsoft.Azure.Cosmos;

namespace AlpacaMock.Api.Endpoints;

/// <summary>
/// Trading API endpoints following Alpaca API specification.
///
/// Base URL: /v1/trading/accounts/{accountId}
///
/// Endpoints:
/// - POST   /orders              - Create a new order
/// - GET    /orders              - List all orders (with optional status filter)
/// - GET    /orders/{orderId}    - Get a specific order by ID
/// - DELETE /orders/{orderId}    - Cancel an order
/// - GET    /positions           - List all open positions
/// - GET    /positions/{symbol}  - Get position for a specific symbol
/// - DELETE /positions/{symbol}  - Close a position (market order to liquidate)
///
/// All endpoints require X-Session-Id header to identify the simulation session.
///
/// Reference: https://alpaca.markets/docs/api-references/trading-api/orders/
/// </summary>
public static class TradingEndpoints
{
    /// <summary>
    /// Registers all trading endpoints under /v1/trading/accounts/{accountId}.
    /// </summary>
    public static void MapTradingEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/v1/trading/accounts/{accountId}")
            .WithTags("Trading");

        // Order management endpoints
        group.MapPost("/orders", CreateOrder);
        group.MapGet("/orders", ListOrders);
        group.MapGet("/orders/{orderId}", GetOrder);
        group.MapDelete("/orders/{orderId}", CancelOrder);

        // Position management endpoints
        group.MapGet("/positions", ListPositions);
        group.MapGet("/positions/{symbol}", GetPosition);
        group.MapDelete("/positions/{symbol}", ClosePosition);
    }

    /// <summary>
    /// Creates a new order for the specified account.
    ///
    /// Alpaca Order Creation Flow:
    /// 1. Validate session and account exist
    /// 2. Convert notional to quantity if needed (for market orders)
    /// 3. Build order object with all parameters
    /// 4. Validate against Alpaca trading rules (price precision, buying power, etc.)
    /// 5. Persist order to database
    /// 6. Attempt immediate fill for market orders with available bar data
    /// 7. Return created order with status
    ///
    /// Error codes:
    /// - 40010000: Missing X-Session-Id header
    /// - 40010001: Invalid session
    /// - 40010003: Order validation failed
    /// - 40410000: Account not found
    /// </summary>
    private static async Task<IResult> CreateOrder(
        HttpContext context,
        string accountId,
        CosmosDbContext cosmos,
        BarRepository barRepo,
        MatchingEngine matchingEngine,
        OrderValidator orderValidator,
        CreateOrderRequest request)
    {
        // Extract session ID from header - required for all trading operations
        var sessionId = GetSessionId(context);
        if (string.IsNullOrEmpty(sessionId))
            return Results.BadRequest(new { code = 40010000, message = "X-Session-Id header required" });

        // Get session for current simulation time
        // Session determines what time "now" is for the backtesting engine
        var session = await GetSessionAsync(cosmos, sessionId);
        if (session == null)
            return Results.BadRequest(new { code = 40010001, message = "Invalid session" });

        // Get account for validation (buying power, PDT status, etc.)
        Domain.Accounts.Account account;
        try
        {
            var accountResponse = await cosmos.Accounts.ReadItemAsync<Domain.Accounts.Account>(
                accountId, new PartitionKey(sessionId));
            account = accountResponse.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Results.NotFound(new { code = 40410000, message = "Account not found" });
        }

        // Handle notional orders: convert dollar amount to share quantity
        // Alpaca allows specifying order value instead of quantity for market orders
        decimal qty = request.Qty ?? 0;
        if (request.Notional.HasValue && qty == 0)
        {
            var bar = await barRepo.GetBarAsync(request.Symbol, session.CurrentSimulationTime);
            if (bar != null && bar.Close > 0)
            {
                qty = request.Notional.Value / bar.Close;
            }
        }

        // Build order object with all parameters
        // Default values match Alpaca API defaults
        var order = new Order
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = sessionId,
            AccountId = accountId,
            ClientOrderId = request.ClientOrderId ?? Guid.NewGuid().ToString(),
            Symbol = request.Symbol.ToUpperInvariant(),
            Qty = qty,
            Notional = request.Notional,
            Type = Enum.Parse<OrderType>(request.Type ?? "market", ignoreCase: true),
            Side = Enum.Parse<OrderSide>(request.Side, ignoreCase: true),
            TimeInForce = Enum.Parse<TimeInForce>(request.TimeInForce ?? "day", ignoreCase: true),
            LimitPrice = request.LimitPrice,
            StopPrice = request.StopPrice,
            ExtendedHours = request.ExtendedHours ?? false,
            SubmittedAt = session.CurrentSimulationTime,
            Status = OrderStatus.Accepted
        };

        // Validate order against Alpaca trading rules:
        // - Price decimal precision (2 for ≥$1, 4 for <$1)
        // - Buying power sufficiency
        // - Extended hours restrictions (limit orders only)
        // - Stop order direction validation
        var currentBar = await barRepo.GetBarAsync(order.Symbol, session.CurrentSimulationTime);
        var validationResult = orderValidator.Validate(order, account, currentBar?.Close);

        if (!validationResult.IsValid)
        {
            order.Status = OrderStatus.Rejected;
            order.FailedAt = session.CurrentSimulationTime;

            // Return first validation error in Alpaca error format
            var firstError = validationResult.Errors.First();
            return Results.BadRequest(new
            {
                code = 40010003,
                message = firstError.Message,
                field = firstError.Field
            });
        }

        // Persist order to database
        await cosmos.Orders.CreateItemAsync(order, new PartitionKey(sessionId));

        // For market orders with available bar data, attempt immediate fill
        // This simulates real-time execution during market hours
        if (currentBar != null && order.Type == OrderType.Market)
        {
            var fill = matchingEngine.TryFill(order, currentBar);
            if (fill.Filled)
            {
                // Update order with fill information
                order.FilledQty = fill.FillQty;
                order.FilledAvgPrice = fill.FillPrice;
                order.FilledAt = fill.Timestamp;
                order.Status = fill.IsPartial ? OrderStatus.PartiallyFilled : OrderStatus.Filled;

                await cosmos.Orders.UpsertItemAsync(order, new PartitionKey(sessionId));

                // Update position to reflect the fill
                await UpdatePositionAsync(cosmos, sessionId, accountId, order, fill);
            }
        }

        return Results.Created($"/v1/trading/accounts/{accountId}/orders/{order.Id}", MapOrderToResponse(order));
    }

    /// <summary>
    /// Lists all orders for the specified account.
    ///
    /// Query parameters:
    /// - status: Filter by order status (new, filled, cancelled, etc.)
    /// - limit: Maximum number of orders to return (default 100)
    ///
    /// Orders are returned in descending order by submission time.
    /// </summary>
    private static async Task<IResult> ListOrders(
        HttpContext context,
        string accountId,
        CosmosDbContext cosmos,
        string? status = null,
        int? limit = null)
    {
        var sessionId = GetSessionId(context);
        if (string.IsNullOrEmpty(sessionId))
            return Results.BadRequest(new { code = 40010000, message = "X-Session-Id header required" });

        // Build query with optional status filter
        var queryText = "SELECT * FROM c WHERE c.sessionId = @sessionId AND c.accountId = @accountId";
        if (!string.IsNullOrEmpty(status))
        {
            queryText += " AND c.status = @status";
        }
        queryText += " ORDER BY c.submittedAt DESC";

        var query = new QueryDefinition(queryText)
            .WithParameter("@sessionId", sessionId)
            .WithParameter("@accountId", accountId);

        if (!string.IsNullOrEmpty(status))
        {
            query.WithParameter("@status", Enum.Parse<OrderStatus>(status, ignoreCase: true));
        }

        // Execute query with pagination
        var orders = new List<Order>();
        using var iterator = cosmos.Orders.GetItemQueryIterator<Order>(query);

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            orders.AddRange(response);
            if (limit.HasValue && orders.Count >= limit.Value)
                break;
        }

        return Results.Ok(orders.Take(limit ?? 100).Select(MapOrderToResponse));
    }

    /// <summary>
    /// Gets a specific order by ID.
    ///
    /// Returns 404 if order doesn't exist or belongs to a different account.
    /// </summary>
    private static async Task<IResult> GetOrder(
        HttpContext context,
        string accountId,
        string orderId,
        CosmosDbContext cosmos)
    {
        var sessionId = GetSessionId(context);
        if (string.IsNullOrEmpty(sessionId))
            return Results.BadRequest(new { code = 40010000, message = "X-Session-Id header required" });

        try
        {
            var response = await cosmos.Orders.ReadItemAsync<Order>(
                orderId, new PartitionKey(sessionId));

            // Verify order belongs to the specified account
            if (response.Resource.AccountId != accountId)
                return Results.NotFound(new { code = 40410000, message = "Order not found" });

            return Results.Ok(MapOrderToResponse(response.Resource));
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Results.NotFound(new { code = 40410000, message = "Order not found" });
        }
    }

    /// <summary>
    /// Cancels an open order.
    ///
    /// Only orders in cancellable states (new, accepted, partially_filled) can be cancelled.
    /// Orders that are already filled, cancelled, or expired return error 40010002.
    /// </summary>
    private static async Task<IResult> CancelOrder(
        HttpContext context,
        string accountId,
        string orderId,
        CosmosDbContext cosmos)
    {
        var sessionId = GetSessionId(context);
        if (string.IsNullOrEmpty(sessionId))
            return Results.BadRequest(new { code = 40010000, message = "X-Session-Id header required" });

        try
        {
            var response = await cosmos.Orders.ReadItemAsync<Order>(
                orderId, new PartitionKey(sessionId));

            var order = response.Resource;

            // Cannot cancel orders that are already in terminal states
            if (order.Status is OrderStatus.Filled or OrderStatus.Cancelled or OrderStatus.Expired)
                return Results.BadRequest(new { code = 40010002, message = "Order cannot be cancelled" });

            // Mark order as cancelled
            order.Status = OrderStatus.Cancelled;
            order.CancelledAt = DateTimeOffset.UtcNow;

            await cosmos.Orders.UpsertItemAsync(order, new PartitionKey(sessionId));

            return Results.NoContent();
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Results.NotFound(new { code = 40410000, message = "Order not found" });
        }
    }

    /// <summary>
    /// Lists all open positions for the specified account.
    ///
    /// Only returns positions with non-zero quantity.
    /// Closed positions (qty = 0) are not included.
    /// </summary>
    private static async Task<IResult> ListPositions(
        HttpContext context,
        string accountId,
        CosmosDbContext cosmos)
    {
        var sessionId = GetSessionId(context);
        if (string.IsNullOrEmpty(sessionId))
            return Results.BadRequest(new { code = 40010000, message = "X-Session-Id header required" });

        // Query for non-zero positions only
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.sessionId = @sessionId AND c.accountId = @accountId AND c.qty != 0")
            .WithParameter("@sessionId", sessionId)
            .WithParameter("@accountId", accountId);

        var positions = new List<Position>();
        using var iterator = cosmos.Positions.GetItemQueryIterator<Position>(query);

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            positions.AddRange(response);
        }

        return Results.Ok(positions.Select(MapPositionToResponse));
    }

    /// <summary>
    /// Gets the position for a specific symbol.
    ///
    /// Returns 404 if no open position exists for the symbol.
    /// Symbol matching is case-insensitive.
    /// </summary>
    private static async Task<IResult> GetPosition(
        HttpContext context,
        string accountId,
        string symbol,
        CosmosDbContext cosmos)
    {
        var sessionId = GetSessionId(context);
        if (string.IsNullOrEmpty(sessionId))
            return Results.BadRequest(new { code = 40010000, message = "X-Session-Id header required" });

        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.sessionId = @sessionId AND c.accountId = @accountId AND c.symbol = @symbol")
            .WithParameter("@sessionId", sessionId)
            .WithParameter("@accountId", accountId)
            .WithParameter("@symbol", symbol.ToUpperInvariant());

        using var iterator = cosmos.Positions.GetItemQueryIterator<Position>(query);

        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            var position = response.FirstOrDefault();
            // Only return if position has non-zero quantity
            if (position != null && position.Qty != 0)
                return Results.Ok(MapPositionToResponse(position));
        }

        return Results.NotFound(new { code = 40410000, message = "Position not found" });
    }

    /// <summary>
    /// Closes a position by creating a market order to liquidate all shares.
    ///
    /// Not yet implemented - returns 501 Not Implemented.
    /// When implemented, this should:
    /// 1. Get current position quantity
    /// 2. Create market order for opposite side with that quantity
    /// 3. Return the created order
    /// </summary>
    private static async Task<IResult> ClosePosition(
        HttpContext context,
        string accountId,
        string symbol,
        CosmosDbContext cosmos)
    {
        // TODO: Implement position closing by creating a market order
        // This would create a market order to close the position
        return Results.StatusCode(501);
    }

    /// <summary>
    /// Retrieves a session by ID from Cosmos DB.
    /// Returns null if the session doesn't exist.
    /// </summary>
    private static async Task<Domain.Sessions.Session?> GetSessionAsync(CosmosDbContext cosmos, string sessionId)
    {
        try
        {
            var response = await cosmos.Sessions.ReadItemAsync<Domain.Sessions.Session>(
                sessionId, new PartitionKey(sessionId));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// Updates the position for a symbol after an order fill.
    ///
    /// This method:
    /// 1. Looks up existing position for the symbol (or creates new)
    /// 2. Applies the fill to update quantity and average price
    /// 3. Updates current price and P&L calculations
    /// 4. Persists the updated position to Cosmos DB
    /// </summary>
    private static async Task UpdatePositionAsync(
        CosmosDbContext cosmos,
        string sessionId,
        string accountId,
        Order order,
        FillResult fill)
    {
        // Try to find existing position for this symbol
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.sessionId = @sessionId AND c.accountId = @accountId AND c.symbol = @symbol")
            .WithParameter("@sessionId", sessionId)
            .WithParameter("@accountId", accountId)
            .WithParameter("@symbol", order.Symbol);

        Position? position = null;
        using (var iterator = cosmos.Positions.GetItemQueryIterator<Position>(query))
        {
            if (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                position = response.FirstOrDefault();
            }
        }

        // Create new position if none exists
        if (position == null)
        {
            position = new Position
            {
                Id = Guid.NewGuid().ToString(),
                SessionId = sessionId,
                AccountId = accountId,
                Symbol = order.Symbol,
                AvgEntryPrice = fill.FillPrice,
                Qty = 0
            };
        }

        // Apply the fill to update position quantity and average price
        position.ApplyFill(fill.FillQty, fill.FillPrice, order.Side);

        // Update current price and recalculate P&L metrics
        position.UpdatePrices(fill.FillPrice);

        // Persist the updated position
        await cosmos.Positions.UpsertItemAsync(position, new PartitionKey(sessionId));
    }

    /// <summary>
    /// Extracts the session ID from the X-Session-Id request header.
    /// </summary>
    private static string? GetSessionId(HttpContext context)
        => context.Request.Headers["X-Session-Id"].FirstOrDefault();

    /// <summary>
    /// Maps an Order domain object to the Alpaca API response format.
    /// Uses snake_case property names to match Alpaca API conventions.
    /// </summary>
    private static object MapOrderToResponse(Order order) => new
    {
        id = order.Id,
        client_order_id = order.ClientOrderId,
        symbol = order.Symbol,
        asset_class = order.AssetClass.ToString().ToLowerInvariant(),
        qty = order.Qty.ToString("G"),
        notional = order.Notional?.ToString("F2"),
        filled_qty = order.FilledQty.ToString("G"),
        filled_avg_price = order.FilledAvgPrice?.ToString("F4"),
        type = order.Type.ToString().ToLowerInvariant(),
        side = order.Side.ToString().ToLowerInvariant(),
        time_in_force = order.TimeInForce.ToString().ToLowerInvariant(),
        limit_price = order.LimitPrice?.ToString("F4"),
        stop_price = order.StopPrice?.ToString("F4"),
        status = order.Status.ToString().ToLowerInvariant(),
        extended_hours = order.ExtendedHours,
        submitted_at = order.SubmittedAt,
        filled_at = order.FilledAt,
        expired_at = order.ExpiredAt,
        cancelled_at = order.CancelledAt
    };

    /// <summary>
    /// Maps a Position domain object to the Alpaca API response format.
    /// Uses snake_case property names to match Alpaca API conventions.
    /// </summary>
    private static object MapPositionToResponse(Position position) => new
    {
        asset_id = position.AssetId,
        symbol = position.Symbol,
        exchange = position.Exchange,
        asset_class = position.AssetClass.ToString().ToLowerInvariant(),
        avg_entry_price = position.AvgEntryPrice.ToString("F4"),
        qty = position.Qty.ToString("G"),
        side = position.Side.ToString().ToLowerInvariant(),
        market_value = position.MarketValue.ToString("F2"),
        cost_basis = position.CostBasis.ToString("F2"),
        unrealized_pl = position.UnrealizedPnL.ToString("F2"),
        unrealized_plpc = position.UnrealizedPnLPercent.ToString("F4"),
        unrealized_intraday_pl = position.UnrealizedIntradayPnL.ToString("F2"),
        current_price = position.CurrentPrice.ToString("F4"),
        lastday_price = position.LastDayPrice.ToString("F4"),
        change_today = position.ChangeToday.ToString("F4")
    };
}

/// <summary>
/// Request body for creating a new order.
/// Matches Alpaca API order creation request structure.
/// </summary>
/// <param name="Symbol">Ticker symbol (required)</param>
/// <param name="Side">Order side: "buy" or "sell" (required)</param>
/// <param name="Qty">Number of shares (mutually exclusive with Notional for market orders)</param>
/// <param name="Notional">Dollar amount for notional orders (market orders only)</param>
/// <param name="Type">Order type: "market", "limit", "stop", "stop_limit" (default: market)</param>
/// <param name="TimeInForce">TIF: "day", "gtc", "opg", "cls", "ioc", "fok" (default: day)</param>
/// <param name="LimitPrice">Limit price for limit and stop-limit orders</param>
/// <param name="StopPrice">Stop/trigger price for stop and stop-limit orders</param>
/// <param name="ExtendedHours">Allow execution in pre/post market (default: false)</param>
/// <param name="ClientOrderId">Client-provided ID for idempotency</param>
public record CreateOrderRequest(
    string Symbol,
    string Side,
    decimal? Qty = null,
    decimal? Notional = null,
    string? Type = null,
    string? TimeInForce = null,
    decimal? LimitPrice = null,
    decimal? StopPrice = null,
    bool? ExtendedHours = null,
    string? ClientOrderId = null);
