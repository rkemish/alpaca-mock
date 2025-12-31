namespace AlpacaMock.Domain.Sessions;

/// <summary>
/// Represents a backtest session with its own isolated state and simulation clock.
/// </summary>
public class Session
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public required string ApiKeyId { get; init; }

    /// <summary>
    /// The start of the historical period for this backtest.
    /// </summary>
    public required DateTimeOffset SimulationStart { get; init; }

    /// <summary>
    /// The end of the historical period for this backtest.
    /// </summary>
    public required DateTimeOffset SimulationEnd { get; init; }

    /// <summary>
    /// The current simulation time (advances during playback).
    /// </summary>
    public DateTimeOffset CurrentSimulationTime { get; set; }

    /// <summary>
    /// Current playback state.
    /// </summary>
    public PlaybackState PlaybackState { get; set; } = PlaybackState.Paused;

    /// <summary>
    /// Playback speed multiplier (1x, 10x, 100x, etc.)
    /// </summary>
    public double PlaybackSpeed { get; set; } = 1.0;

    /// <summary>
    /// Initial cash balance for accounts in this session.
    /// </summary>
    public decimal InitialCash { get; set; } = 100_000m;

    public SessionStatus Status { get; set; } = SessionStatus.Active;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Total realized P&L across all accounts in this session.
    /// </summary>
    public decimal TotalRealizedPnL { get; set; }

    /// <summary>
    /// Total unrealized P&L across all accounts in this session.
    /// </summary>
    public decimal TotalUnrealizedPnL { get; set; }
}

public enum PlaybackState
{
    Paused,
    Playing,
    StepPending
}

public enum SessionStatus
{
    Active,
    Completed,
    Cancelled
}
