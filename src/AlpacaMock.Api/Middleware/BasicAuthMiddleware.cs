using System.Text;

namespace AlpacaMock.Api.Middleware;

/// <summary>
/// Basic authentication middleware matching Alpaca's auth pattern.
/// </summary>
public class BasicAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<BasicAuthMiddleware> _logger;

    // For development - in production, these would come from a database
    private static readonly Dictionary<string, (string Secret, string Name)> ApiKeys = new()
    {
        ["test-api-key"] = ("test-api-secret", "Test User"),
        ["demo-api-key"] = ("demo-api-secret", "Demo User")
    };

    public BasicAuthMiddleware(RequestDelegate next, ILogger<BasicAuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip auth for health check and OpenAPI
        var path = context.Request.Path.Value ?? "";
        if (path == "/health" || path.StartsWith("/openapi"))
        {
            await _next(context);
            return;
        }

        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();

        if (string.IsNullOrEmpty(authHeader))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { code = 40110000, message = "Authorization header required" });
            return;
        }

        if (!authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { code = 40110001, message = "Basic authentication required" });
            return;
        }

        try
        {
            var base64Credentials = authHeader["Basic ".Length..];
            var credentials = Encoding.UTF8.GetString(Convert.FromBase64String(base64Credentials));
            var parts = credentials.Split(':', 2);

            if (parts.Length != 2)
            {
                throw new FormatException("Invalid credential format");
            }

            var apiKey = parts[0];
            var apiSecret = parts[1];

            if (!ApiKeys.TryGetValue(apiKey, out var keyInfo) || keyInfo.Secret != apiSecret)
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { code = 40110002, message = "Invalid API credentials" });
                return;
            }

            // Set user info in context
            context.Items["ApiKeyId"] = apiKey;
            context.Items["UserName"] = keyInfo.Name;

            _logger.LogDebug("Authenticated request from {ApiKey}", apiKey);

            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Authentication failed");
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { code = 40110003, message = "Authentication failed" });
        }
    }
}

/// <summary>
/// Extension methods for accessing auth context.
/// </summary>
public static class AuthContextExtensions
{
    public static string GetApiKeyId(this HttpContext context)
        => context.Items["ApiKeyId"]?.ToString() ?? throw new InvalidOperationException("Not authenticated");

    public static string? TryGetApiKeyId(this HttpContext context)
        => context.Items["ApiKeyId"]?.ToString();
}
