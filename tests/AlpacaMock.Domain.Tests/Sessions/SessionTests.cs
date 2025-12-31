using AlpacaMock.Domain.Sessions;
using FluentAssertions;

namespace AlpacaMock.Domain.Tests.Sessions;

public class SessionTests
{
    [Fact]
    public void Session_DefaultValues_AreCorrect()
    {
        // Act
        var session = new Session
        {
            Id = "test-id",
            Name = "Test Session",
            ApiKeyId = "api-key-123",
            SimulationStart = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            SimulationEnd = new DateTimeOffset(2024, 12, 31, 0, 0, 0, TimeSpan.Zero)
        };

        // Assert
        session.PlaybackState.Should().Be(PlaybackState.Paused);
        session.PlaybackSpeed.Should().Be(1.0);
        session.InitialCash.Should().Be(100_000m);
        session.Status.Should().Be(SessionStatus.Active);
        session.CompletedAt.Should().BeNull();
        session.TotalRealizedPnL.Should().Be(0m);
        session.TotalUnrealizedPnL.Should().Be(0m);
    }

    [Fact]
    public void Session_CompletedAt_CanBeSet()
    {
        // Arrange
        var session = new Session
        {
            Id = "test-id",
            Name = "Test Session",
            ApiKeyId = "api-key-123",
            SimulationStart = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            SimulationEnd = new DateTimeOffset(2024, 12, 31, 0, 0, 0, TimeSpan.Zero)
        };
        var completedTime = DateTimeOffset.UtcNow;

        // Act
        session.CompletedAt = completedTime;
        session.Status = SessionStatus.Completed;

        // Assert
        session.CompletedAt.Should().Be(completedTime);
        session.Status.Should().Be(SessionStatus.Completed);
    }

    [Fact]
    public void Session_CurrentSimulationTime_CanBeAdvanced()
    {
        // Arrange
        var startTime = new DateTimeOffset(2024, 1, 1, 9, 30, 0, TimeSpan.Zero);
        var session = new Session
        {
            Id = "test-id",
            Name = "Test Session",
            ApiKeyId = "api-key-123",
            SimulationStart = startTime,
            SimulationEnd = new DateTimeOffset(2024, 12, 31, 16, 0, 0, TimeSpan.Zero),
            CurrentSimulationTime = startTime
        };

        // Act
        session.CurrentSimulationTime = startTime.AddHours(1);

        // Assert
        session.CurrentSimulationTime.Should().Be(startTime.AddHours(1));
    }

    [Fact]
    public void Session_PlaybackState_CanBeChanged()
    {
        // Arrange
        var session = new Session
        {
            Id = "test-id",
            Name = "Test Session",
            ApiKeyId = "api-key-123",
            SimulationStart = DateTimeOffset.UtcNow,
            SimulationEnd = DateTimeOffset.UtcNow.AddDays(30)
        };

        // Act & Assert - Paused -> Playing
        session.PlaybackState = PlaybackState.Playing;
        session.PlaybackState.Should().Be(PlaybackState.Playing);

        // Playing -> StepPending
        session.PlaybackState = PlaybackState.StepPending;
        session.PlaybackState.Should().Be(PlaybackState.StepPending);

        // StepPending -> Paused
        session.PlaybackState = PlaybackState.Paused;
        session.PlaybackState.Should().Be(PlaybackState.Paused);
    }

    [Fact]
    public void Session_PlaybackSpeed_CanBeChanged()
    {
        // Arrange
        var session = new Session
        {
            Id = "test-id",
            Name = "Test Session",
            ApiKeyId = "api-key-123",
            SimulationStart = DateTimeOffset.UtcNow,
            SimulationEnd = DateTimeOffset.UtcNow.AddDays(30)
        };

        // Act
        session.PlaybackSpeed = 10.0;

        // Assert
        session.PlaybackSpeed.Should().Be(10.0);
    }

    [Fact]
    public void Session_PnL_CanBeUpdated()
    {
        // Arrange
        var session = new Session
        {
            Id = "test-id",
            Name = "Test Session",
            ApiKeyId = "api-key-123",
            SimulationStart = DateTimeOffset.UtcNow,
            SimulationEnd = DateTimeOffset.UtcNow.AddDays(30)
        };

        // Act
        session.TotalRealizedPnL = 5000m;
        session.TotalUnrealizedPnL = 2500m;

        // Assert
        session.TotalRealizedPnL.Should().Be(5000m);
        session.TotalUnrealizedPnL.Should().Be(2500m);
    }

    [Fact]
    public void Session_InitialCash_CanBeCustomized()
    {
        // Arrange & Act
        var session = new Session
        {
            Id = "test-id",
            Name = "Test Session",
            ApiKeyId = "api-key-123",
            SimulationStart = DateTimeOffset.UtcNow,
            SimulationEnd = DateTimeOffset.UtcNow.AddDays(30),
            InitialCash = 500_000m
        };

        // Assert
        session.InitialCash.Should().Be(500_000m);
    }

    [Fact]
    public void Session_Name_CanBeUpdated()
    {
        // Arrange
        var session = new Session
        {
            Id = "test-id",
            Name = "Original Name",
            ApiKeyId = "api-key-123",
            SimulationStart = DateTimeOffset.UtcNow,
            SimulationEnd = DateTimeOffset.UtcNow.AddDays(30)
        };

        // Act
        session.Name = "Updated Name";

        // Assert
        session.Name.Should().Be("Updated Name");
    }

    [Fact]
    public void Session_CancelledStatus_CanBeSet()
    {
        // Arrange
        var session = new Session
        {
            Id = "test-id",
            Name = "Test Session",
            ApiKeyId = "api-key-123",
            SimulationStart = DateTimeOffset.UtcNow,
            SimulationEnd = DateTimeOffset.UtcNow.AddDays(30)
        };

        // Act
        session.Status = SessionStatus.Cancelled;

        // Assert
        session.Status.Should().Be(SessionStatus.Cancelled);
    }

    [Fact]
    public void PlaybackState_AllValuesAreDefined()
    {
        // Assert - verify all enum values are available
        Enum.GetValues<PlaybackState>().Should().Contain(PlaybackState.Paused);
        Enum.GetValues<PlaybackState>().Should().Contain(PlaybackState.Playing);
        Enum.GetValues<PlaybackState>().Should().Contain(PlaybackState.StepPending);
        Enum.GetValues<PlaybackState>().Should().HaveCount(3);
    }

    [Fact]
    public void SessionStatus_AllValuesAreDefined()
    {
        // Assert - verify all enum values are available
        Enum.GetValues<SessionStatus>().Should().Contain(SessionStatus.Active);
        Enum.GetValues<SessionStatus>().Should().Contain(SessionStatus.Completed);
        Enum.GetValues<SessionStatus>().Should().Contain(SessionStatus.Cancelled);
        Enum.GetValues<SessionStatus>().Should().HaveCount(3);
    }
}
