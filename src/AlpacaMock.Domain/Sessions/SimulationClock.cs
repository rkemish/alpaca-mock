namespace AlpacaMock.Domain.Sessions;

/// <summary>
/// Manages simulation time for a backtest session.
/// Supports step mode, real-time replay, and accelerated playback.
/// </summary>
public class SimulationClock
{
    private readonly Session _session;
    private DateTimeOffset _lastRealTime;
    private readonly object _lock = new();

    public SimulationClock(Session session)
    {
        _session = session;
        _lastRealTime = DateTimeOffset.UtcNow;
    }

    public DateTimeOffset CurrentTime => _session.CurrentSimulationTime;
    public bool IsPlaying => _session.PlaybackState == PlaybackState.Playing;
    public bool IsPaused => _session.PlaybackState == PlaybackState.Paused;
    public bool IsCompleted => _session.CurrentSimulationTime >= _session.SimulationEnd;

    /// <summary>
    /// Advances time by a specific duration (step mode).
    /// </summary>
    public TimeAdvanceResult AdvanceBy(TimeSpan duration)
    {
        lock (_lock)
        {
            if (IsCompleted)
                return new TimeAdvanceResult(false, "Simulation has completed");

            var newTime = _session.CurrentSimulationTime.Add(duration);

            // Clamp to simulation end
            if (newTime > _session.SimulationEnd)
                newTime = _session.SimulationEnd;

            var previousTime = _session.CurrentSimulationTime;
            _session.CurrentSimulationTime = newTime;

            return new TimeAdvanceResult(true, null, previousTime, newTime);
        }
    }

    /// <summary>
    /// Advances time to a specific timestamp.
    /// </summary>
    public TimeAdvanceResult AdvanceTo(DateTimeOffset targetTime)
    {
        lock (_lock)
        {
            if (targetTime < _session.CurrentSimulationTime)
                return new TimeAdvanceResult(false, "Cannot move time backwards");

            if (targetTime < _session.SimulationStart)
                return new TimeAdvanceResult(false, "Target time is before simulation start");

            if (targetTime > _session.SimulationEnd)
                targetTime = _session.SimulationEnd;

            var previousTime = _session.CurrentSimulationTime;
            _session.CurrentSimulationTime = targetTime;

            return new TimeAdvanceResult(true, null, previousTime, targetTime);
        }
    }

    /// <summary>
    /// Updates time based on real-time elapsed and playback speed (for play mode).
    /// </summary>
    public TimeAdvanceResult Tick()
    {
        lock (_lock)
        {
            if (!IsPlaying || IsCompleted)
                return new TimeAdvanceResult(false, "Not playing or completed");

            var now = DateTimeOffset.UtcNow;
            var realElapsed = now - _lastRealTime;
            _lastRealTime = now;

            // Apply playback speed multiplier
            var simElapsed = TimeSpan.FromTicks((long)(realElapsed.Ticks * _session.PlaybackSpeed));

            return AdvanceBy(simElapsed);
        }
    }

    /// <summary>
    /// Starts playback mode.
    /// </summary>
    public void Play()
    {
        lock (_lock)
        {
            _lastRealTime = DateTimeOffset.UtcNow;
            _session.PlaybackState = PlaybackState.Playing;
        }
    }

    /// <summary>
    /// Pauses playback.
    /// </summary>
    public void Pause()
    {
        lock (_lock)
        {
            _session.PlaybackState = PlaybackState.Paused;
        }
    }

    /// <summary>
    /// Sets the playback speed multiplier.
    /// </summary>
    public void SetSpeed(double speed)
    {
        if (speed <= 0)
            throw new ArgumentOutOfRangeException(nameof(speed), "Speed must be positive");

        _session.PlaybackSpeed = speed;
    }

    /// <summary>
    /// Checks if a given timestamp falls within market hours.
    /// </summary>
    public static bool IsMarketOpen(DateTimeOffset time)
    {
        // Convert to Eastern Time
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var easternTime = TimeZoneInfo.ConvertTime(time, eastern);

        // Skip weekends
        if (easternTime.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return false;

        // Market hours: 9:30 AM - 4:00 PM ET
        var marketOpen = new TimeSpan(9, 30, 0);
        var marketClose = new TimeSpan(16, 0, 0);
        var timeOfDay = easternTime.TimeOfDay;

        return timeOfDay >= marketOpen && timeOfDay < marketClose;
    }

    /// <summary>
    /// Gets the next market open time from the given time.
    /// </summary>
    public static DateTimeOffset GetNextMarketOpen(DateTimeOffset from)
    {
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var easternTime = TimeZoneInfo.ConvertTime(from, eastern);
        var marketOpen = new TimeSpan(9, 30, 0);

        // If before market open today and it's a weekday
        if (easternTime.TimeOfDay < marketOpen &&
            easternTime.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
        {
            return new DateTimeOffset(easternTime.Date + marketOpen, eastern.GetUtcOffset(easternTime));
        }

        // Find next weekday
        var nextDay = easternTime.Date.AddDays(1);
        while (nextDay.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            nextDay = nextDay.AddDays(1);
        }

        var nextOpen = nextDay + marketOpen;
        return new DateTimeOffset(nextOpen, eastern.GetUtcOffset(nextOpen));
    }
}

public record TimeAdvanceResult(
    bool Success,
    string? Error = null,
    DateTimeOffset? PreviousTime = null,
    DateTimeOffset? NewTime = null);
