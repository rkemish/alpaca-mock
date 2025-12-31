using System.Security.Cryptography;
using System.Text;

namespace AlpacaMock.Api.Middleware;

/// <summary>
/// Basic authentication middleware matching Alpaca's auth pattern.
/// API keys are loaded from configuration (appsettings.json or environment variables).
/// </summary>
public class BasicAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<BasicAuthMiddleware> _logger;
    private readonly Dictionary<string, (string Secret, string Name)> _apiKeys;

    public BasicAuthMiddleware(RequestDelegate next, ILogger<BasicAuthMiddleware> logger, IConfiguration configuration)
    {
        _next = next;
        _logger = logger;
        _apiKeys = LoadApiKeys(configuration);
    }

    /// <summary>
    /// Loads API keys from configuration.
    /// Keys can be set via appsettings.json or environment variables (e.g., ApiKeys__0__Key).
    /// </summary>
    private static Dictionary<string, (string Secret, string Name)> LoadApiKeys(IConfiguration configuration)
    {
        var keys = new Dictionary<string, (string Secret, string Name)>();
        var section = configuration.GetSection("ApiKeys");

        foreach (var child in section.GetChildren())
        {
            var key = child["Key"];
            var secret = child["Secret"];
            var name = child["Name"] ?? "API User";

            if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(secret))
            {
                keys[key] = (secret, name);
            }
        }

        if (keys.Count == 0)
        {
            // Log warning if no keys configured
            Console.WriteLine("WARNING: No API keys configured. Add ApiKeys section to configuration.");
        }

        return keys;
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

            if (!_apiKeys.TryGetValue(apiKey, out var keyInfo) ||
                !ConstantTimeEquals(keyInfo.Secret, apiSecret))
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

    /// <summary>
    /// Performs constant-time string comparison to prevent timing attacks.
    /// Uses CryptographicOperations.FixedTimeEquals to ensure comparison time
    /// doesn't vary based on how many characters match.
    /// </summary>
    private static bool ConstantTimeEquals(string a, string b)
    {
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
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
