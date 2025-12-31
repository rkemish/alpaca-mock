using AlpacaMock.Api.Middleware;
using AlpacaMock.Domain.Sessions;
using AlpacaMock.Infrastructure.Cosmos;

namespace AlpacaMock.Api.Endpoints;

public static class SessionEndpoints
{
    public static void MapSessionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/v1/sessions")
            .WithTags("Sessions");

        group.MapPost("/", CreateSession);
        group.MapGet("/", ListSessions);
        group.MapGet("/{sessionId}", GetSession);
        group.MapDelete("/{sessionId}", DeleteSession);
        group.MapPost("/{sessionId}/time/advance", AdvanceTime);
        group.MapPost("/{sessionId}/time/play", PlaySession);
        group.MapPost("/{sessionId}/time/pause", PauseSession);
        group.MapPut("/{sessionId}/time/speed", SetSpeed);
    }

    private static async Task<IResult> CreateSession(
        HttpContext context,
        SessionRepository repo,
        CreateSessionRequest request)
    {
        var apiKeyId = context.GetApiKeyId();

        var session = new Session
        {
            Id = Guid.NewGuid().ToString(),
            Name = request.Name ?? $"Backtest {DateTime.UtcNow:yyyy-MM-dd HH:mm}",
            ApiKeyId = apiKeyId,
            SimulationStart = request.StartTime,
            SimulationEnd = request.EndTime,
            CurrentSimulationTime = request.StartTime,
            InitialCash = request.InitialCash ?? 100_000m,
            PlaybackState = PlaybackState.Paused,
            PlaybackSpeed = 1.0
        };

        await repo.CreateAsync(session);

        return Results.Created($"/v1/sessions/{session.Id}", MapToResponse(session));
    }

    private static async Task<IResult> ListSessions(
        HttpContext context,
        SessionRepository repo)
    {
        var apiKeyId = context.GetApiKeyId();
        var sessions = await repo.GetByApiKeyAsync(apiKeyId);

        return Results.Ok(sessions.Select(MapToResponse));
    }

    private static async Task<IResult> GetSession(
        string sessionId,
        SessionRepository repo)
    {
        var session = await repo.GetByIdAsync(sessionId);
        if (session == null)
            return Results.NotFound(new { code = 40410000, message = "Session not found" });

        return Results.Ok(MapToResponse(session));
    }

    private static async Task<IResult> DeleteSession(
        string sessionId,
        SessionRepository repo)
    {
        await repo.DeleteAsync(sessionId);
        return Results.NoContent();
    }

    private static async Task<IResult> AdvanceTime(
        string sessionId,
        SessionRepository repo,
        AdvanceTimeRequest request)
    {
        var session = await repo.GetByIdAsync(sessionId);
        if (session == null)
            return Results.NotFound(new { code = 40410000, message = "Session not found" });

        var clock = new SimulationClock(session);

        TimeAdvanceResult result;
        if (request.Duration != null)
        {
            result = clock.AdvanceBy(TimeSpan.FromMinutes(request.Duration.Value));
        }
        else if (request.TargetTime != null)
        {
            result = clock.AdvanceTo(request.TargetTime.Value);
        }
        else
        {
            // Default: advance by 1 minute
            result = clock.AdvanceBy(TimeSpan.FromMinutes(1));
        }

        if (!result.Success)
            return Results.BadRequest(new { code = 40010000, message = result.Error });

        await repo.UpdateAsync(session);

        return Results.Ok(new
        {
            previousTime = result.PreviousTime,
            currentTime = result.NewTime,
            simulationEnd = session.SimulationEnd,
            isCompleted = session.CurrentSimulationTime >= session.SimulationEnd
        });
    }

    private static async Task<IResult> PlaySession(
        string sessionId,
        SessionRepository repo)
    {
        var session = await repo.GetByIdAsync(sessionId);
        if (session == null)
            return Results.NotFound(new { code = 40410000, message = "Session not found" });

        var clock = new SimulationClock(session);
        clock.Play();

        await repo.UpdateAsync(session);

        return Results.Ok(MapToResponse(session));
    }

    private static async Task<IResult> PauseSession(
        string sessionId,
        SessionRepository repo)
    {
        var session = await repo.GetByIdAsync(sessionId);
        if (session == null)
            return Results.NotFound(new { code = 40410000, message = "Session not found" });

        var clock = new SimulationClock(session);
        clock.Pause();

        await repo.UpdateAsync(session);

        return Results.Ok(MapToResponse(session));
    }

    private static async Task<IResult> SetSpeed(
        string sessionId,
        SessionRepository repo,
        SetSpeedRequest request)
    {
        var session = await repo.GetByIdAsync(sessionId);
        if (session == null)
            return Results.NotFound(new { code = 40410000, message = "Session not found" });

        if (request.Speed <= 0)
            return Results.BadRequest(new { code = 40010001, message = "Speed must be positive" });

        var clock = new SimulationClock(session);
        clock.SetSpeed(request.Speed);

        await repo.UpdateAsync(session);

        return Results.Ok(MapToResponse(session));
    }

    private static object MapToResponse(Session session) => new
    {
        id = session.Id,
        name = session.Name,
        status = session.Status.ToString().ToLowerInvariant(),
        simulation_start = session.SimulationStart,
        simulation_end = session.SimulationEnd,
        current_time = session.CurrentSimulationTime,
        playback_state = session.PlaybackState.ToString().ToLowerInvariant(),
        playback_speed = session.PlaybackSpeed,
        initial_cash = session.InitialCash,
        total_realized_pnl = session.TotalRealizedPnL,
        total_unrealized_pnl = session.TotalUnrealizedPnL,
        created_at = session.CreatedAt,
        completed_at = session.CompletedAt
    };
}

public record CreateSessionRequest(
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    string? Name = null,
    decimal? InitialCash = null);

public record AdvanceTimeRequest(
    double? Duration = null,  // in minutes
    DateTimeOffset? TargetTime = null);

public record SetSpeedRequest(double Speed);
