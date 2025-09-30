using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using XoHub.Server.Models;
using XoHub.Server.Services;

namespace XoHub.Server.Tests.Services;

public class RoomManagerTests : IDisposable
{
    private readonly Mock<ILogger<RoomManager>> _loggerMock;
    private readonly Mock<IAIEngine> _aiEngineMock;
    private readonly RoomManager _roomManager;

    public RoomManagerTests()
    {
        _loggerMock = new Mock<ILogger<RoomManager>>();
        _aiEngineMock = new Mock<IAIEngine>();
        _roomManager = new RoomManager(_loggerMock.Object, _aiEngineMock.Object);
    }

    public void Dispose()
    {
        _roomManager.Dispose();
    }

    [Fact]
    public void CreateRoom_ShouldCreateRoomSuccessfully_WhenValidInput()
    {
        // Arrange
        var roomId = "test-room";

        // Act
        var room = _roomManager.CreateRoom(roomId);

        // Assert
        room.Should().NotBeNull();
        room.RoomId.Should().Be(roomId);
        room.Status.Should().Be(GameStatus.WaitingForPlayers);
        room.IsAIMode.Should().BeFalse();
    }

    [Fact]
    public void CreateRoom_ShouldThrowException_WhenRoomIdAlreadyExists()
    {
        // Arrange
        var roomId = "duplicate-room";
        _roomManager.CreateRoom(roomId);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            _roomManager.CreateRoom(roomId));
        exception.Message.Should().Contain("already exists");
    }

    [Theory]
    [InlineData("", "Room ID cannot be null or empty")]
    [InlineData(null, "Room ID cannot be null or empty")]
    [InlineData("   ", "Room ID cannot be null or empty")]
    public void CreateRoom_ShouldThrowArgumentException_WhenInvalidRoomId(string roomId, string expectedMessage)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            _roomManager.CreateRoom(roomId));
        exception.Message.Should().Contain(expectedMessage);
    }

    [Fact]
    public void MakeMove_ShouldDetectWinCondition_WhenThreeInRow()
    {
        // Arrange
        var room = _roomManager.CreateRoom("win-test");
        var playerX = new Player { ConnectionId = "player1", Name = "Player X" };
        var playerO = new Player { ConnectionId = "player2", Name = "Player O" };
        _roomManager.JoinRoom("win-test", playerX);
        _roomManager.JoinRoom("win-test", playerO);

        // Act
        _roomManager.MakeMove("win-test", 0, 0, "player1"); // X
        _roomManager.MakeMove("win-test", 1, 0, "player2"); // O
        _roomManager.MakeMove("win-test", 0, 1, "player1"); // X
        _roomManager.MakeMove("win-test", 1, 1, "player2"); // O
        _roomManager.MakeMove("win-test", 0, 2, "player1"); // X - wins

        // Assert
        var updatedRoom = _roomManager.GetRoom("win-test");
        updatedRoom.Should().NotBeNull();
        updatedRoom?.Status.Should().Be(GameStatus.Finished);
        updatedRoom?.Winner.Should().Be("X");
        updatedRoom?.GameDuration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void MakeMove_ShouldTriggerAIMove_WhenAIModeEnabled()
    {
        // Arrange
        var room = _roomManager.CreateRoom("ai-test", isAIMode: true);
        var player = new Player { ConnectionId = "human", Name = "Human" };
        _roomManager.JoinRoom("ai-test", player);

        _aiEngineMock.Setup(a => a.GetBestMove(It.IsAny<char[,]>(), 'O'))
            .Returns((1, 1));

        // Act
        _roomManager.MakeMove("ai-test", 0, 0, "human");

        // Assert
        var updatedRoom = _roomManager.GetRoom("ai-test");
        updatedRoom?.Board[1, 1].Should().Be('O'); // AI move
        updatedRoom?.CurrentTurn.Should().Be("X"); // Back to human
    }
}