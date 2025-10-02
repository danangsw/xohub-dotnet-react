using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using XoHub.Server.Hubs;
using XoHub.Server.Models;
using XoHub.Server.Services;

namespace XoHub.Server.Tests.Hubs;

public class TicTacToeHubTests
{
    private readonly Mock<IRoomManager> _roomManagerMock;
    private readonly Mock<IAIEngine> _aiEngineMock;
    private readonly Mock<ILogger<TicTacToeHub>> _loggerMock;
    private readonly Mock<IHubCallerClients> _clientsMock;
    private readonly Mock<IClientProxy> _clientProxyMock;
    private readonly Mock<ISingleClientProxy> _singleClientProxyMock;
    private readonly Mock<IGroupManager> _groupsMock;
    private readonly Mock<HubCallerContext> _contextMock;
    private readonly TicTacToeHub _hub;

    public TicTacToeHubTests()
    {
        _roomManagerMock = new Mock<IRoomManager>();
        _aiEngineMock = new Mock<IAIEngine>();
        _loggerMock = new Mock<ILogger<TicTacToeHub>>();
        _clientsMock = new Mock<IHubCallerClients>();
        _clientProxyMock = new Mock<IClientProxy>();
        _singleClientProxyMock = new Mock<ISingleClientProxy>();
        _groupsMock = new Mock<IGroupManager>();
        _contextMock = new Mock<HubCallerContext>();

        // Setup context
        _contextMock.Setup(c => c.ConnectionId).Returns("test-connection-id");

        // Setup clients
        _clientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(_clientProxyMock.Object);
        _clientsMock.Setup(c => c.Caller).Returns(_singleClientProxyMock.Object);

        _hub = new TicTacToeHub(_roomManagerMock.Object, _aiEngineMock.Object, _loggerMock.Object)
        {
            Clients = _clientsMock.Object,
            Groups = _groupsMock.Object,
            Context = _contextMock.Object
        };
    }

    [Fact]
    public async Task JoinRoom_ShouldCallRoomManagerJoin_WhenValidInput()
    {
        // Arrange
        var roomId = "test-room";
        var playerName = "TestPlayer";

        _roomManagerMock.Setup(rm => rm.JoinRoom(roomId, It.IsAny<Player>())).Returns(true);
        _roomManagerMock.Setup(rm => rm.GetRoom(roomId)).Returns(new GameRoom { RoomId = roomId, Status = GameStatus.WaitingForPlayers });

        // Act
        await _hub.JoinRoom(roomId, playerName);

        // Assert
        _roomManagerMock.Verify(rm => rm.JoinRoom(roomId, It.Is<Player>(p =>
            p.ConnectionId == "test-connection-id" && p.Name == playerName)), Times.Once);
        _groupsMock.Verify(g => g.AddToGroupAsync("test-connection-id", roomId, default), Times.Once);
    }

    [Fact]
    public async Task JoinRoom_ShouldBroadcastGameStarted_WhenGameStarts()
    {
        // Arrange
        var roomId = "test-room";
        var playerName = "TestPlayer";
        var room = new GameRoom { RoomId = roomId, Status = GameStatus.InProgress };

        _roomManagerMock.Setup(rm => rm.JoinRoom(roomId, It.IsAny<Player>())).Returns(true);
        _roomManagerMock.Setup(rm => rm.GetRoom(roomId)).Returns(room);

        // Act
        await _hub.JoinRoom(roomId, playerName);

        // Assert
        _roomManagerMock.Verify(rm => rm.JoinRoom(roomId, It.Is<Player>(p =>
            p.ConnectionId == "test-connection-id" && p.Name == playerName)), Times.Once);
        _groupsMock.Verify(g => g.AddToGroupAsync("test-connection-id", roomId, default), Times.Once);
    }

    [Fact]
    public async Task JoinRoom_ShouldHandleExceptions()
    {
        // Arrange
        var roomId = "test-room";
        var playerName = "TestPlayer";

        _roomManagerMock.Setup(rm => rm.JoinRoom(roomId, It.IsAny<Player>())).Throws(new Exception("Test exception"));

        // Act
        await _hub.JoinRoom(roomId, playerName);

        // Assert
        _roomManagerMock.Verify(rm => rm.JoinRoom(roomId, It.IsAny<Player>()), Times.Once);
    }

    [Fact]
    public async Task MakeMove_ShouldCallRoomManagerMakeMove_WhenValidInput()
    {
        // Arrange
        var roomId = "test-room";
        var row = 0;
        var col = 0;

        _roomManagerMock.Setup(rm => rm.MakeMove(roomId, row, col, "test-connection-id")).Returns(true);
        _roomManagerMock.Setup(rm => rm.GetRoom(roomId)).Returns(new GameRoom { RoomId = roomId, Status = GameStatus.InProgress });

        // Act
        await _hub.MakeMove(roomId, row, col);

        // Assert
        _roomManagerMock.Verify(rm => rm.MakeMove(roomId, row, col, "test-connection-id"), Times.Once);
    }

    [Fact]
    public async Task LeaveRoom_ShouldCallRoomManagerLeave_WhenValidInput()
    {
        // Arrange
        var roomId = "test-room";

        _roomManagerMock.Setup(rm => rm.LeaveRoom(roomId, "test-connection-id")).Returns(true);
        _roomManagerMock.Setup(rm => rm.GetRoom(roomId)).Returns(new GameRoom { RoomId = roomId, Status = GameStatus.Abandoned });

        // Act
        await _hub.LeaveRoom(roomId);

        // Assert
        _roomManagerMock.Verify(rm => rm.LeaveRoom(roomId, "test-connection-id"), Times.Once);
        _groupsMock.Verify(g => g.RemoveFromGroupAsync("test-connection-id", roomId, default), Times.Once);
    }

    [Fact]
    public async Task MakeMove_ShouldBroadcastGameFinished_WhenGameEnds()
    {
        // Arrange
        var roomId = "test-room";
        var row = 0;
        var col = 0;
        var room = new GameRoom
        {
            RoomId = roomId,
            Status = GameStatus.Finished,
            Winner = "X",
            Board = new char[3, 3]
        };

        _roomManagerMock.Setup(rm => rm.MakeMove(roomId, row, col, "test-connection-id")).Returns(true);
        _roomManagerMock.Setup(rm => rm.GetRoom(roomId)).Returns(room);

        // Act
        await _hub.MakeMove(roomId, row, col);

        // Assert
        _roomManagerMock.Verify(rm => rm.MakeMove(roomId, row, col, "test-connection-id"), Times.Once);
    }

    [Fact]
    public async Task MakeMove_ShouldSendError_WhenMoveFails()
    {
        // Arrange
        var roomId = "test-room";
        var row = 0;
        var col = 0;

        _roomManagerMock.Setup(rm => rm.MakeMove(roomId, row, col, "test-connection-id")).Returns(false);

        // Act
        await _hub.MakeMove(roomId, row, col);

        // Assert
        _roomManagerMock.Verify(rm => rm.MakeMove(roomId, row, col, "test-connection-id"), Times.Once);
    }

    [Fact]
    public async Task MakeMove_ShouldHandleExceptions()
    {
        // Arrange
        var roomId = "test-room";
        var row = 0;
        var col = 0;

        _roomManagerMock.Setup(rm => rm.MakeMove(roomId, row, col, "test-connection-id")).Throws(new Exception("Test exception"));

        // Act
        await _hub.MakeMove(roomId, row, col);

        // Assert
        _roomManagerMock.Verify(rm => rm.MakeMove(roomId, row, col, "test-connection-id"), Times.Once);
    }

    [Fact]
    public async Task LeaveRoom_ShouldSendError_WhenLeaveFails()
    {
        // Arrange
        var roomId = "test-room";

        _roomManagerMock.Setup(rm => rm.LeaveRoom(roomId, "test-connection-id")).Returns(false);

        // Act
        await _hub.LeaveRoom(roomId);

        // Assert
        _roomManagerMock.Verify(rm => rm.LeaveRoom(roomId, "test-connection-id"), Times.Once);
    }

    [Fact]
    public async Task LeaveRoom_ShouldHandleExceptions()
    {
        // Arrange
        var roomId = "test-room";

        _roomManagerMock.Setup(rm => rm.LeaveRoom(roomId, "test-connection-id")).Throws(new Exception("Test exception"));

        // Act
        await _hub.LeaveRoom(roomId);

        // Assert
        _roomManagerMock.Verify(rm => rm.LeaveRoom(roomId, "test-connection-id"), Times.Once);
    }

    [Fact]
    public async Task OnDisconnectedAsync_ShouldBroadcastRoomAbandoned_WhenRoomBecomesAbandoned()
    {
        // Arrange
        var room1 = new GameRoom { RoomId = "room1", Players = new[] { new Player { ConnectionId = "test-connection-id" }, null } };
        var abandonedRoom = new GameRoom { RoomId = "room1", Status = GameStatus.Abandoned };
        var rooms = new List<GameRoom> { room1 };

        _roomManagerMock.Setup(rm => rm.GetAllRooms()).Returns(rooms);
        _roomManagerMock.Setup(rm => rm.LeaveRoom("room1", "test-connection-id")).Returns(true);
        _roomManagerMock.Setup(rm => rm.GetRoom("room1")).Returns(abandonedRoom);

        // Act
        await _hub.OnDisconnectedAsync(null);

        // Assert
        _roomManagerMock.Verify(rm => rm.LeaveRoom("room1", "test-connection-id"), Times.Once);
    }

    [Fact]
    public async Task OnDisconnectedAsync_ShouldHandleExceptions()
    {
        // Arrange
        var room1 = new GameRoom { RoomId = "room1", Players = new[] { new Player { ConnectionId = "test-connection-id" }, null } };
        var rooms = new List<GameRoom> { room1 };

        _roomManagerMock.Setup(rm => rm.GetAllRooms()).Returns(rooms);
        _roomManagerMock.Setup(rm => rm.LeaveRoom("room1", "test-connection-id")).Throws(new Exception("Test exception"));

        // Act
        await _hub.OnDisconnectedAsync(null);

        // Assert
        _roomManagerMock.Verify(rm => rm.LeaveRoom("room1", "test-connection-id"), Times.Once);
    }

    [Fact]
    public async Task JoinRoom_ShouldSendError_WhenJoinRoomFails()
    {
        // Arrange
        var roomId = "test-room";
        var playerName = "TestPlayer";

        _roomManagerMock.Setup(rm => rm.JoinRoom(roomId, It.IsAny<Player>())).Returns(false);

        // Act
        await _hub.JoinRoom(roomId, playerName);

        // Assert
        _roomManagerMock.Verify(rm => rm.JoinRoom(roomId, It.Is<Player>(p =>
            p.ConnectionId == "test-connection-id" && p.Name == playerName)), Times.Once);
        _groupsMock.Verify(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task JoinRoom_ShouldNotBroadcastGameStarted_WhenRoomExistsButNotInProgress()
    {
        // Arrange
        var roomId = "test-room";
        var playerName = "TestPlayer";
        var room = new GameRoom { RoomId = roomId, Status = GameStatus.Finished }; // Not InProgress

        _roomManagerMock.Setup(rm => rm.JoinRoom(roomId, It.IsAny<Player>())).Returns(true);
        _roomManagerMock.Setup(rm => rm.GetRoom(roomId)).Returns(room);

        // Act
        await _hub.JoinRoom(roomId, playerName);

        // Assert
        _roomManagerMock.Verify(rm => rm.JoinRoom(roomId, It.Is<Player>(p =>
            p.ConnectionId == "test-connection-id" && p.Name == playerName)), Times.Once);
        _groupsMock.Verify(g => g.AddToGroupAsync("test-connection-id", roomId, default), Times.Once);
        // GameStarted should not be broadcast since status is not InProgress
    }

    [Fact]
    public async Task LeaveRoom_ShouldNotBroadcastRoomAbandoned_WhenRoomExistsButNotAbandoned()
    {
        // Arrange
        var roomId = "test-room";

        _roomManagerMock.Setup(rm => rm.LeaveRoom(roomId, "test-connection-id")).Returns(true);
        _roomManagerMock.Setup(rm => rm.GetRoom(roomId)).Returns(new GameRoom { RoomId = roomId, Status = GameStatus.InProgress }); // Not Abandoned

        // Act
        await _hub.LeaveRoom(roomId);

        // Assert
        _roomManagerMock.Verify(rm => rm.LeaveRoom(roomId, "test-connection-id"), Times.Once);
        _groupsMock.Verify(g => g.RemoveFromGroupAsync("test-connection-id", roomId, default), Times.Once);
        // RoomAbandoned should not be broadcast since status is not Abandoned
    }

    [Fact]
    public async Task OnDisconnectedAsync_ShouldNotBroadcastRoomAbandoned_WhenRoomExistsButNotAbandoned()
    {
        // Arrange
        var room1 = new GameRoom { RoomId = "room1", Players = new[] { new Player { ConnectionId = "test-connection-id" }, null } };
        var activeRoom = new GameRoom { RoomId = "room1", Status = GameStatus.InProgress }; // Not Abandoned
        var rooms = new List<GameRoom> { room1 };

        _roomManagerMock.Setup(rm => rm.GetAllRooms()).Returns(rooms);
        _roomManagerMock.Setup(rm => rm.LeaveRoom("room1", "test-connection-id")).Returns(true);
        _roomManagerMock.Setup(rm => rm.GetRoom("room1")).Returns(activeRoom);

        // Act
        await _hub.OnDisconnectedAsync(null);

        // Assert
        _roomManagerMock.Verify(rm => rm.LeaveRoom("room1", "test-connection-id"), Times.Once);
        // RoomAbandoned should not be broadcast since status is not Abandoned
    }

    [Fact]
    public async Task JoinRoom_ShouldNotBroadcastGameStarted_WhenRoomIsNull()
    {
        // Arrange
        var roomId = "test-room";
        var playerName = "TestPlayer";
        GameRoom? gameRoom = null;

        _roomManagerMock.Setup(rm => rm.JoinRoom(roomId, It.IsAny<Player>())).Returns(true);
        _roomManagerMock.Setup(rm => rm.GetRoom(roomId)).Returns(gameRoom); // Room is null

        // Act
        await _hub.JoinRoom(roomId, playerName);

        // Assert
        _roomManagerMock.Verify(rm => rm.JoinRoom(roomId, It.Is<Player>(p =>
            p.ConnectionId == "test-connection-id" && p.Name == playerName)), Times.Once);
        _groupsMock.Verify(g => g.AddToGroupAsync("test-connection-id", roomId, default), Times.Once);
        // GameStarted should not be broadcast since room is null
    }
}