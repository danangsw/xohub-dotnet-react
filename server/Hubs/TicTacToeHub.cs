using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using XoHub.Server.Models;
using XoHub.Server.Services;
namespace XoHub.Server.Hubs;

[Authorize]
public class TicTacToeHub : Hub
{
    private readonly IRoomManager _roomManager;
    private readonly IAIEngine _aiEngine;
    private readonly ILogger<TicTacToeHub> _logger;

    public TicTacToeHub(IRoomManager roomManager, IAIEngine aiEngine, ILogger<TicTacToeHub> logger)
    {
        _roomManager = roomManager;
        _aiEngine = aiEngine;
        _logger = logger;
    }

    public async Task JoinRoom(string roomId, string playerName, bool isAIMode = false)
    {
        try
        {
            // JWT validation from Context.User
            var player = new Player
            {
                ConnectionId = Context.ConnectionId,
                Name = playerName
            };

            if (_roomManager.JoinRoom(roomId, player))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
                await Clients.Group(roomId).SendAsync("PlayerJoined", player);

                // Check if game should start
                var room = _roomManager.GetRoom(roomId);
                if (room != null && room.Status == GameStatus.InProgress)
                {
                    await Clients.Group(roomId).SendAsync("GameStarted", room);
                }

                _logger.LogInformation("Player {PlayerName} joined room {RoomId}", playerName, roomId);
            }
            else
            {
                await Clients.Caller.SendAsync("Error", "Failed to join room");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error joining room {RoomId}", roomId);
            await Clients.Caller.SendAsync("Error", "Failed to join room");
        }
    }

    public async Task MakeMove(string roomId, int row, int col)
    {
        try
        {
            if (_roomManager.MakeMove(roomId, row, col, Context.ConnectionId))
            {
                var room = _roomManager.GetRoom(roomId);
                if (room != null)
                {
                    // Broadcast the move to all players in the room
                    await Clients.Group(roomId).SendAsync("MoveMade", new
                    {
                        Row = row,
                        Col = col,
                        PlayerMark = room.CurrentTurn == "X" ? "O" : "X", // The player who just moved
                        Board = room.Board,
                        CurrentTurn = room.CurrentTurn
                    });

                    // Check if game finished
                    if (room.Status == GameStatus.Finished)
                    {
                        await Clients.Group(roomId).SendAsync("GameFinished", new
                        {
                            Winner = room.Winner,
                            Board = room.Board,
                            IsDraw = string.IsNullOrEmpty(room.Winner)
                        });
                    }
                }
            }
            else
            {
                await Clients.Caller.SendAsync("Error", "Invalid move");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error making move in room {RoomId}", roomId);
            await Clients.Caller.SendAsync("Error", "Failed to make move");
        }
    }

    public async Task LeaveRoom(string roomId)
    {
        try
        {
            var connectionId = Context.ConnectionId;

            if (_roomManager.LeaveRoom(roomId, connectionId))
            {
                await Groups.RemoveFromGroupAsync(connectionId, roomId);
                await Clients.Group(roomId).SendAsync("PlayerLeft", new
                {
                    ConnectionId = connectionId,
                    RoomId = roomId
                });

                // Check if room should be abandoned
                var room = _roomManager.GetRoom(roomId);
                if (room != null && room.Status == GameStatus.Abandoned)
                {
                    await Clients.Group(roomId).SendAsync("RoomAbandoned", roomId);
                }

                _logger.LogInformation("Player {ConnectionId} left room {RoomId}", connectionId, roomId);
            }
            else
            {
                await Clients.Caller.SendAsync("Error", "Failed to leave room");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leaving room {RoomId}", roomId);
            await Clients.Caller.SendAsync("Error", "Failed to leave room");
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        try
        {
            var connectionId = Context.ConnectionId;
            var rooms = _roomManager.GetAllRooms();

            foreach (var room in rooms)
            {
                if (room.Players.Any(p => p?.ConnectionId == connectionId))
                {
                    if (_roomManager.LeaveRoom(room.RoomId, connectionId))
                    {
                        await Clients.Group(room.RoomId).SendAsync("PlayerLeft", new
                        {
                            ConnectionId = connectionId,
                            RoomId = room.RoomId
                        });

                        // Check if room should be abandoned
                        var updatedRoom = _roomManager.GetRoom(room.RoomId);
                        if (updatedRoom != null && updatedRoom.Status == GameStatus.Abandoned)
                        {
                            await Clients.Group(room.RoomId).SendAsync("RoomAbandoned", room.RoomId);
                        }
                    }
                }
            }

            _logger.LogInformation("Player disconnected: {ConnectionId}", connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling disconnection for {ConnectionId}", Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }
}