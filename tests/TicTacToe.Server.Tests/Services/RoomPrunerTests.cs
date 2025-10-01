using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using XoHub.Server.Services;

namespace TicTacToe.Server.Tests.Services;

public class RoomPrunerTests : IDisposable
{
    private readonly Mock<IRoomManager> _roomManagerMock;
    private readonly Mock<IKeyManager> _keyManagerMock;
    private readonly Mock<ILogger<RoomPruner>> _loggerMock;
    private readonly Dictionary<string, string> _configSettings;
    private readonly ConfigurationBuilder _configBuilder;
    private readonly ManualResetEventSlim _testSignal;
    private CancellationTokenSource? _cts;

    public RoomPrunerTests()
    {
        _roomManagerMock = new Mock<IRoomManager>();
        _keyManagerMock = new Mock<IKeyManager>();
        _loggerMock = new Mock<ILogger<RoomPruner>>();
        _configSettings = new Dictionary<string, string>
        {
            ["RoomPruner:CleanupIntervalMinutes"] = "0.1", // 6 seconds for testing
            ["RoomPruner:KeyRotationHours"] = "0.001", // ~3.6 seconds for testing
            ["RoomPruner:InactivityThresholdMinutes"] = "30"
        };
        _configBuilder = new ConfigurationBuilder();
        _testSignal = new ManualResetEventSlim(false);
    }

    public void Dispose()
    {
        _cts?.Dispose();
        _testSignal.Dispose();
    }

    private IConfiguration CreateConfiguration()
    {
        return _configBuilder.AddInMemoryCollection(_configSettings.Select(kvp => new KeyValuePair<string, string?>(kvp.Key, kvp.Value))).Build();
    }

    private RoomPruner CreateRoomPruner()
    {
        var config = CreateConfiguration();
        return new RoomPruner(_roomManagerMock.Object, _keyManagerMock.Object, _loggerMock.Object, config);
    }

    [Fact]
    public void Constructor_LoadsDefaultConfigurationValues()
    {
        // Arrange
        var config = CreateConfiguration();

        // Act
        var pruner = new RoomPruner(_roomManagerMock.Object, _keyManagerMock.Object, _loggerMock.Object, config);

        // Assert - verify default values are loaded
        var cleanupIntervalField = typeof(RoomPruner).GetField("_roomCleanupInterval", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var rotationIntervalField = typeof(RoomPruner).GetField("_keyRotationInterval", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var inactivityField = typeof(RoomPruner).GetField("_roomInactivityThreshold", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var cleanupInterval = (TimeSpan)cleanupIntervalField!.GetValue(pruner)!;
        var rotationInterval = (TimeSpan)rotationIntervalField!.GetValue(pruner)!;
        var inactivityThreshold = (TimeSpan)inactivityField!.GetValue(pruner)!;

        Assert.Equal(TimeSpan.FromMinutes(0.1), cleanupInterval);
        Assert.Equal(TimeSpan.FromHours(0.001), rotationInterval);
        Assert.Equal(TimeSpan.FromMinutes(30), inactivityThreshold);
    }

    [Fact]
    public void Constructor_LoadsCustomConfigurationValues()
    {
        // Arrange
        _configSettings["RoomPruner:CleanupIntervalMinutes"] = "10";
        _configSettings["RoomPruner:KeyRotationHours"] = "2";
        _configSettings["RoomPruner:InactivityThresholdMinutes"] = "60";

        // Act
        var pruner = CreateRoomPruner();

        // Assert
        var cleanupIntervalField = typeof(RoomPruner).GetField("_roomCleanupInterval", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var rotationIntervalField = typeof(RoomPruner).GetField("_keyRotationInterval", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var inactivityField = typeof(RoomPruner).GetField("_roomInactivityThreshold", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var cleanupInterval = (TimeSpan)cleanupIntervalField!.GetValue(pruner)!;
        var rotationInterval = (TimeSpan)rotationIntervalField!.GetValue(pruner)!;
        var inactivityThreshold = (TimeSpan)inactivityField!.GetValue(pruner)!;

        Assert.Equal(TimeSpan.FromMinutes(10), cleanupInterval);
        Assert.Equal(TimeSpan.FromHours(2), rotationInterval);
        Assert.Equal(TimeSpan.FromMinutes(60), inactivityThreshold);
    }

    [Fact]
    public async Task ExecuteAsync_LogsStartupAndShutdown()
    {
        // Arrange
        _cts = new CancellationTokenSource();
        var pruner = CreateRoomPruner();

        _roomManagerMock.Setup(x => x.GetInactiveRooms(It.IsAny<TimeSpan>()))
            .Returns(new List<string>());

        // Act - start and let it run one cleanup cycle, then stop
        var executeTask = pruner.StartAsync(_cts.Token);
        await Task.Delay(7000); // Wait for startup and one cleanup cycle (6 seconds + buffer)
        await pruner.StopAsync(_cts.Token);
        await executeTask;

        // Assert
        _loggerMock.Verify(x => x.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("RoomPruner started")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        // Note: "stopped" log may not occur if service is cancelled before completion
        _loggerMock.Verify(x => x.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("RoomPruner stopping")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ProcessesRoomCleanupSuccessfully()
    {
        // Arrange
        _cts = new CancellationTokenSource();
        var pruner = CreateRoomPruner();
        var inactiveRooms = new List<string> { "room1", "room2" };

        _roomManagerMock.Setup(x => x.GetInactiveRooms(It.IsAny<TimeSpan>()))
            .Returns(inactiveRooms);

        // Act - start service, let it run one cleanup cycle, then stop
        var executeTask = pruner.StartAsync(_cts.Token);
        await Task.Delay(7000); // Wait for startup and first cleanup cycle
        await pruner.StopAsync(_cts.Token);
        await executeTask;

        // Assert
        _roomManagerMock.Verify(x => x.GetInactiveRooms(TimeSpan.FromMinutes(30)), Times.AtLeastOnce);
        _roomManagerMock.Verify(x => x.RemoveRoom("room1"), Times.Once);
        _roomManagerMock.Verify(x => x.RemoveRoom("room2"), Times.Once);

        _loggerMock.Verify(x => x.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Pruned inactive room: room1")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _loggerMock.Verify(x => x.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Pruned inactive room: room2")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_HandlesRoomRemovalFailure()
    {
        // Arrange
        _cts = new CancellationTokenSource();
        var pruner = CreateRoomPruner();
        var inactiveRooms = new List<string> { "failingRoom" };

        _roomManagerMock.Setup(x => x.GetInactiveRooms(It.IsAny<TimeSpan>()))
            .Returns(inactiveRooms);
        _roomManagerMock.Setup(x => x.RemoveRoom("failingRoom"))
            .Throws(new Exception("Room removal failed"));

        // Act
        var executeTask = pruner.StartAsync(_cts.Token);
        await Task.Delay(7000);
        await pruner.StopAsync(_cts.Token);
        await executeTask;

        // Assert - service continues despite room removal failure
        _loggerMock.Verify(x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Failed to remove inactive room failingRoom")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_RotatesKeysWhenIntervalElapsed()
    {
        // Arrange
        _cts = new CancellationTokenSource();
        var pruner = CreateRoomPruner();

        _roomManagerMock.Setup(x => x.GetInactiveRooms(It.IsAny<TimeSpan>()))
            .Returns(new List<string>());

        // Set last rotation check to more than an hour ago
        var lastRotationField = typeof(RoomPruner).GetField("_lastKeyRotationCheck", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        lastRotationField!.SetValue(pruner, DateTime.UtcNow.AddHours(-2));

        // Act
        var executeTask = pruner.StartAsync(_cts.Token);
        await Task.Delay(7000);
        await pruner.StopAsync(_cts.Token);
        await executeTask;

        // Assert
        _keyManagerMock.Verify(x => x.RotateKeys(), Times.Once);

        _loggerMock.Verify(x => x.Log(
            LogLevel.Debug,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Checked key rotation")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsKeyRotationWhenIntervalNotElapsed()
    {
        // Arrange
        _cts = new CancellationTokenSource();
        var pruner = CreateRoomPruner();

        // Set last rotation check to recently
        var lastRotationField = typeof(RoomPruner).GetField("_lastKeyRotationCheck", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        lastRotationField!.SetValue(pruner, DateTime.UtcNow.AddMinutes(-30));

        // Act
        var executeTask = pruner.StartAsync(_cts.Token);
        await Task.Delay(100);
        await pruner.StopAsync(_cts.Token);
        await executeTask;

        // Assert - key rotation should not be called
        _keyManagerMock.Verify(x => x.RotateKeys(), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_HandlesKeyRotationFailure()
    {
        // Arrange
        _cts = new CancellationTokenSource();
        var pruner = CreateRoomPruner();

        _roomManagerMock.Setup(x => x.GetInactiveRooms(It.IsAny<TimeSpan>()))
            .Returns(new List<string>());

        // Set last rotation check to trigger rotation
        var lastRotationField = typeof(RoomPruner).GetField("_lastKeyRotationCheck", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        lastRotationField!.SetValue(pruner, DateTime.UtcNow.AddHours(-2));

        _keyManagerMock.Setup(x => x.RotateKeys())
            .Throws(new Exception("Key rotation failed"));

        // Act
        var executeTask = pruner.StartAsync(_cts.Token);
        await Task.Delay(7000);
        await pruner.StopAsync(_cts.Token);
        await executeTask;

        // Assert - error is logged but service continues
        _loggerMock.Verify(x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Key rotation failed")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_HandlesUnexpectedErrorsInLoop()
    {
        // Arrange
        _cts = new CancellationTokenSource();
        var pruner = CreateRoomPruner();

        _roomManagerMock.Setup(x => x.GetInactiveRooms(It.IsAny<TimeSpan>()))
            .Throws(new Exception("Unexpected database error"));

        // Act
        var executeTask = pruner.StartAsync(_cts.Token);
        await Task.Delay(100);
        await pruner.StopAsync(_cts.Token);
        await executeTask;

        // Assert - error is logged and service continues
        _loggerMock.Verify(x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Unexpected error in RoomPruner execution loop")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task StopAsync_LogsStoppingMessage()
    {
        // Arrange
        var pruner = CreateRoomPruner();

        // Act
        await pruner.StopAsync(CancellationToken.None);

        // Assert
        _loggerMock.Verify(x => x.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("RoomPruner stopping")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_RespectsCancellationToken()
    {
        // Arrange
        _cts = new CancellationTokenSource();
        var pruner = CreateRoomPruner();

        // Act - start and immediately cancel
        var executeTask = pruner.StartAsync(_cts.Token);
        _cts.Cancel();
        await executeTask;

        // Assert - should complete without throwing
        Assert.True(executeTask.IsCompleted);
    }

    [Fact]
    public async Task ExecuteAsync_HandlesMultipleCleanupCycles()
    {
        // Arrange
        _cts = new CancellationTokenSource();
        var pruner = CreateRoomPruner();
        var callCount = 0;

        _roomManagerMock.Setup(x => x.GetInactiveRooms(It.IsAny<TimeSpan>()))
            .Returns(() =>
            {
                callCount++;
                return callCount == 1 ? new List<string> { "room1" } : new List<string>();
            });

        // Act - let it run multiple cycles
        var executeTask = pruner.StartAsync(_cts.Token);
        await Task.Delay(15000); // Wait for multiple cycles (2-3 cycles)
        await pruner.StopAsync(_cts.Token);
        await executeTask;

        // Assert - GetInactiveRooms called multiple times
        _roomManagerMock.Verify(x => x.GetInactiveRooms(It.IsAny<TimeSpan>()), Times.AtLeast(2));
    }

    [Fact]
    public async Task ExecuteAsync_UpdatesLastKeyRotationCheckAfterRotation()
    {
        // Arrange
        _cts = new CancellationTokenSource();
        var pruner = CreateRoomPruner();

        _roomManagerMock.Setup(x => x.GetInactiveRooms(It.IsAny<TimeSpan>()))
            .Returns(new List<string>());

        var initialTime = DateTime.UtcNow.AddHours(-2);
        var lastRotationField = typeof(RoomPruner).GetField("_lastKeyRotationCheck", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        lastRotationField!.SetValue(pruner, initialTime);

        // Act
        var executeTask = pruner.StartAsync(_cts.Token);
        await Task.Delay(7000);
        await pruner.StopAsync(_cts.Token);
        await executeTask;

        // Assert - last rotation check should be updated
        var updatedTime = (DateTime)lastRotationField.GetValue(pruner)!;
        Assert.True(updatedTime > initialTime);
    }

    [Fact]
    public async Task ExecuteAsync_NoInactiveRooms_NoRoomRemovalCalls()
    {
        // Arrange
        _cts = new CancellationTokenSource();
        var pruner = CreateRoomPruner();

        _roomManagerMock.Setup(x => x.GetInactiveRooms(It.IsAny<TimeSpan>()))
            .Returns(new List<string>());

        // Act
        var executeTask = pruner.StartAsync(_cts.Token);
        await Task.Delay(100);
        await pruner.StopAsync(_cts.Token);
        await executeTask;

        // Assert - RemoveRoom should not be called
        _roomManagerMock.Verify(x => x.RemoveRoom(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_MixedRoomRemoval_SuccessAndFailure()
    {
        // Arrange
        _cts = new CancellationTokenSource();
        var pruner = CreateRoomPruner();
        var inactiveRooms = new List<string> { "successRoom", "failRoom" };

        _roomManagerMock.Setup(x => x.GetInactiveRooms(It.IsAny<TimeSpan>()))
            .Returns(inactiveRooms);
        _roomManagerMock.Setup(x => x.RemoveRoom("failRoom"))
            .Throws(new Exception("Removal failed"));

        // Act
        var executeTask = pruner.StartAsync(_cts.Token);
        await Task.Delay(7000);
        await pruner.StopAsync(_cts.Token);
        await executeTask;

        // Assert - success logged for first room, error for second
        _loggerMock.Verify(x => x.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Pruned inactive room: successRoom")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _loggerMock.Verify(x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Failed to remove inactive room failRoom")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
