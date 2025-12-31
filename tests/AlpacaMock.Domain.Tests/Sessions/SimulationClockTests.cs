using AlpacaMock.Domain.Sessions;
using FluentAssertions;

namespace AlpacaMock.Domain.Tests.Sessions;

public class SimulationClockTests
{
    private static Session CreateSession(
        DateTimeOffset? start = null,
        DateTimeOffset? end = null,
        DateTimeOffset? current = null)
    {
        var defaultStart = new DateTimeOffset(2024, 1, 15, 9, 30, 0, TimeSpan.FromHours(-5)); // Monday 9:30 AM ET
        var defaultEnd = new DateTimeOffset(2024, 1, 31, 16, 0, 0, TimeSpan.FromHours(-5));

        return new Session
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Test Session",
            ApiKeyId = "test-key",
            SimulationStart = start ?? defaultStart,
            SimulationEnd = end ?? defaultEnd,
            CurrentSimulationTime = current ?? start ?? defaultStart
        };
    }

    #region Market Hours Tests

    [Theory]
    [InlineData(2024, 1, 15, 9, 30, 0, true)]   // Monday 9:30 AM ET - market open
    [InlineData(2024, 1, 15, 9, 29, 0, false)]  // Monday 9:29 AM ET - pre-market
    [InlineData(2024, 1, 15, 16, 0, 0, false)]  // Monday 4:00 PM ET - market closed
    [InlineData(2024, 1, 15, 15, 59, 0, true)]  // Monday 3:59 PM ET - market open
    [InlineData(2024, 1, 15, 12, 0, 0, true)]   // Monday noon ET - market open
    public void IsMarketOpen_Weekdays_ReturnsCorrectResult(
        int year, int month, int day, int hour, int minute, int second, bool expected)
    {
        // Arrange
        var time = new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.FromHours(-5)); // ET

        // Act
        var result = SimulationClock.IsMarketOpen(time);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void IsMarketOpen_Saturday_ReturnsFalse()
    {
        // Arrange - Saturday at noon
        var saturday = new DateTimeOffset(2024, 1, 13, 12, 0, 0, TimeSpan.FromHours(-5));

        // Act
        var result = SimulationClock.IsMarketOpen(saturday);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsMarketOpen_Sunday_ReturnsFalse()
    {
        // Arrange - Sunday at noon
        var sunday = new DateTimeOffset(2024, 1, 14, 12, 0, 0, TimeSpan.FromHours(-5));

        // Act
        var result = SimulationClock.IsMarketOpen(sunday);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region AdvanceBy Tests

    [Fact]
    public void AdvanceBy_MovesTimeForward()
    {
        // Arrange
        var session = CreateSession();
        var clock = new SimulationClock(session);
        var initialTime = clock.CurrentTime;

        // Act
        var result = clock.AdvanceBy(TimeSpan.FromMinutes(5));

        // Assert
        result.Success.Should().BeTrue();
        result.PreviousTime.Should().Be(initialTime);
        result.NewTime.Should().Be(initialTime.AddMinutes(5));
        clock.CurrentTime.Should().Be(initialTime.AddMinutes(5));
    }

    [Fact]
    public void AdvanceBy_ClampsToSimulationEnd()
    {
        // Arrange
        var start = new DateTimeOffset(2024, 1, 15, 9, 30, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var session = CreateSession(start: start, end: end);
        var clock = new SimulationClock(session);

        // Act - try to advance past end
        var result = clock.AdvanceBy(TimeSpan.FromHours(2));

        // Assert
        result.Success.Should().BeTrue();
        result.NewTime.Should().Be(end);
        clock.CurrentTime.Should().Be(end);
    }

    [Fact]
    public void AdvanceBy_WhenCompleted_ReturnsFalse()
    {
        // Arrange
        var start = new DateTimeOffset(2024, 1, 15, 9, 30, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var session = CreateSession(start: start, end: end, current: end);
        var clock = new SimulationClock(session);

        // Act
        var result = clock.AdvanceBy(TimeSpan.FromMinutes(1));

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("completed");
    }

    #endregion

    #region AdvanceTo Tests

    [Fact]
    public void AdvanceTo_MovesToTargetTime()
    {
        // Arrange
        var session = CreateSession();
        var clock = new SimulationClock(session);
        var target = session.SimulationStart.AddHours(1);

        // Act
        var result = clock.AdvanceTo(target);

        // Assert
        result.Success.Should().BeTrue();
        clock.CurrentTime.Should().Be(target);
    }

    [Fact]
    public void AdvanceTo_RejectsBackwardsMovement()
    {
        // Arrange
        var session = CreateSession();
        var clock = new SimulationClock(session);
        clock.AdvanceBy(TimeSpan.FromHours(1)); // Move forward first
        var pastTime = session.SimulationStart.AddMinutes(-10);

        // Act
        var result = clock.AdvanceTo(pastTime);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("backwards");
    }

    [Fact]
    public void AdvanceTo_RejectsTimeBeforeStart()
    {
        // Arrange
        var session = CreateSession();
        var clock = new SimulationClock(session);
        var beforeStart = session.SimulationStart.AddDays(-1);

        // Act
        var result = clock.AdvanceTo(beforeStart);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("backwards");
    }

    [Fact]
    public void AdvanceTo_ClampsToSimulationEnd()
    {
        // Arrange
        var session = CreateSession();
        var clock = new SimulationClock(session);
        var pastEnd = session.SimulationEnd.AddDays(1);

        // Act
        var result = clock.AdvanceTo(pastEnd);

        // Assert
        result.Success.Should().BeTrue();
        clock.CurrentTime.Should().Be(session.SimulationEnd);
    }

    #endregion

    #region Playback Tests

    [Fact]
    public void Play_SetsPlaybackStatePlaying()
    {
        // Arrange
        var session = CreateSession();
        var clock = new SimulationClock(session);

        // Act
        clock.Play();

        // Assert
        clock.IsPlaying.Should().BeTrue();
        clock.IsPaused.Should().BeFalse();
        session.PlaybackState.Should().Be(PlaybackState.Playing);
    }

    [Fact]
    public void Pause_SetsPlaybackStatePaused()
    {
        // Arrange
        var session = CreateSession();
        var clock = new SimulationClock(session);
        clock.Play();

        // Act
        clock.Pause();

        // Assert
        clock.IsPaused.Should().BeTrue();
        clock.IsPlaying.Should().BeFalse();
        session.PlaybackState.Should().Be(PlaybackState.Paused);
    }

    [Fact]
    public void SetSpeed_SetsPlaybackSpeed()
    {
        // Arrange
        var session = CreateSession();
        var clock = new SimulationClock(session);

        // Act
        clock.SetSpeed(10.0);

        // Assert
        session.PlaybackSpeed.Should().Be(10.0);
    }

    [Fact]
    public void SetSpeed_ThrowsOnNegativeSpeed()
    {
        // Arrange
        var session = CreateSession();
        var clock = new SimulationClock(session);

        // Act & Assert
        var act = () => clock.SetSpeed(-1.0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SetSpeed_ThrowsOnZeroSpeed()
    {
        // Arrange
        var session = CreateSession();
        var clock = new SimulationClock(session);

        // Act & Assert
        var act = () => clock.SetSpeed(0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    #endregion

    #region Tick Tests

    [Fact]
    public void Tick_WhenNotPlaying_ReturnsFalse()
    {
        // Arrange
        var session = CreateSession();
        var clock = new SimulationClock(session);
        // Not playing (paused by default)

        // Act
        var result = clock.Tick();

        // Assert
        result.Success.Should().BeFalse();
    }

    [Fact]
    public void Tick_WhenCompleted_ReturnsFalse()
    {
        // Arrange
        var start = new DateTimeOffset(2024, 1, 15, 9, 30, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var session = CreateSession(start: start, end: end, current: end);
        var clock = new SimulationClock(session);
        clock.Play();

        // Act
        var result = clock.Tick();

        // Assert
        result.Success.Should().BeFalse();
    }

    #endregion

    #region IsCompleted Tests

    [Fact]
    public void IsCompleted_AtEnd_ReturnsTrue()
    {
        // Arrange
        var session = CreateSession();
        session.CurrentSimulationTime = session.SimulationEnd;
        var clock = new SimulationClock(session);

        // Assert
        clock.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public void IsCompleted_BeforeEnd_ReturnsFalse()
    {
        // Arrange
        var session = CreateSession();
        var clock = new SimulationClock(session);

        // Assert
        clock.IsCompleted.Should().BeFalse();
    }

    #endregion

    #region GetNextMarketOpen Tests

    [Fact]
    public void GetNextMarketOpen_FromFriday_ReturnsMonday()
    {
        // Arrange - Friday 5 PM ET
        var friday = new DateTimeOffset(2024, 1, 12, 17, 0, 0, TimeSpan.FromHours(-5));

        // Act
        var nextOpen = SimulationClock.GetNextMarketOpen(friday);

        // Assert - should be Monday 9:30 AM
        nextOpen.DayOfWeek.Should().Be(DayOfWeek.Monday);
        nextOpen.Hour.Should().Be(9);
        nextOpen.Minute.Should().Be(30);
    }

    [Fact]
    public void GetNextMarketOpen_FromSaturday_ReturnsMonday()
    {
        // Arrange - Saturday noon
        var saturday = new DateTimeOffset(2024, 1, 13, 12, 0, 0, TimeSpan.FromHours(-5));

        // Act
        var nextOpen = SimulationClock.GetNextMarketOpen(saturday);

        // Assert
        nextOpen.DayOfWeek.Should().Be(DayOfWeek.Monday);
    }

    [Fact]
    public void GetNextMarketOpen_BeforeOpenSameDay_ReturnsSameDayOpen()
    {
        // Arrange - Tuesday 8 AM ET (before 9:30 open)
        var tuesday = new DateTimeOffset(2024, 1, 16, 8, 0, 0, TimeSpan.FromHours(-5));

        // Act
        var nextOpen = SimulationClock.GetNextMarketOpen(tuesday);

        // Assert - should be same day 9:30 AM
        nextOpen.Date.Should().Be(tuesday.Date);
        nextOpen.Hour.Should().Be(9);
        nextOpen.Minute.Should().Be(30);
    }

    #endregion
}
