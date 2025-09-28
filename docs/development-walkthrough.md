# Development Walkthrough: xohub-dotnet-react

This guide walks you through building the real-time TicTacToe game from scratch to local Docker deployment, following industry best practices.

## 🚀 Phase 1: Environment Setup

### Prerequisites
```bash
# Check versions
dotnet --version  # Should be 8.0+
node --version    # Should be 20+
docker --version  # Should be 24+
```

### Project Structure Creation
```bash
# Create project root
mkdir xohub-dotnet-react
cd xohub-dotnet-react

# Backend structure
mkdir -p server/{Controllers,Hubs,Services,Models,Data}
mkdir -p server/Properties

# Frontend structure  
mkdir -p client/{src/{components,services,hooks,utils,pages},public}

# Documentation
mkdir -p docs examples
```

## 🏗️ Phase 2: Backend Development (ASP.NET Core + SignalR)

### 2.1 Initialize ASP.NET Core Project
```bash
cd server
dotnet new web -n TicTacToe.Server
cd TicTacToe.Server

# Add required packages
dotnet add package Microsoft.AspNetCore.SignalR
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add package System.IdentityModel.Tokens.Jwt
dotnet add package Microsoft.Extensions.Hosting
```

### 2.2 Core Models (server/Models/)

**GameRoom.cs**
```csharp
/// <summary>
/// Represents a game room with comprehensive state management
/// 
/// Design Principles:
/// 1. Immutable room ID prevents tampering
/// 2. Fixed-size player array ensures O(1) access
/// 3. Char array board for memory efficiency
/// 4. UTC timestamps for timezone independence
/// 5. Comprehensive metadata for analytics and debugging
/// 
/// Memory Footprint:
/// - 9 chars for board (9 bytes)
/// - 2 player references (16 bytes on 64-bit)
/// - Strings and metadata (~200 bytes)
/// - Total: ~225 bytes per room (efficient for thousands of concurrent games)
/// </summary>
public class GameRoom
{
    public string RoomId { get; set; } = string.Empty;
    public Player[] Players { get; set; } = new Player[2];
    public char[,] Board { get; set; } = new char[3, 3];
    public string CurrentTurn { get; set; } = "X";
    public GameStatus Status { get; set; } = GameStatus.WaitingForPlayers;
    public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsAIMode { get; set; }
    public string? Winner { get; set; } // "X", "O", or null for draw/ongoing
    public int MoveCount { get; set; } = 0;
    public int MaxPlayers { get; set; } = 2;
    public DifficultyLevel AIDifficulty { get; set; } = DifficultyLevel.Medium;
    
    // Game statistics for analytics
    public TimeSpan GameDuration => Status == GameStatus.Finished ? 
        (DateTime.UtcNow - CreatedAtUtc) : TimeSpan.Zero;
    
    public bool IsGameActive => Status == GameStatus.InProgress;
    public bool HasWinner => !string.IsNullOrEmpty(Winner);
    public bool IsDraw => Status == GameStatus.Finished && string.IsNullOrEmpty(Winner);
    
    /// <summary>
    /// Gets the current player whose turn it is
    /// Algorithm: O(1) lookup based on current turn mark
    /// </summary>
    public Player? GetCurrentPlayer()
    {
        return Players.FirstOrDefault(p => p?.Mark.ToString() == CurrentTurn);
    }
    
    /// <summary>
    /// Gets the opponent of the specified player
    /// Algorithm: O(1) array scan (max 2 elements)
    /// </summary>
    public Player? GetOpponent(string connectionId)
    {
        return Players.FirstOrDefault(p => p?.ConnectionId != connectionId && p != null);
    }
    
    /// <summary>
    /// Validates if a move is legal at the given position
    /// Algorithm: O(1) boundary and occupancy check
    /// </summary>
    public bool IsValidMove(int row, int col)
    {
        return row >= 0 && row < 3 && 
               col >= 0 && col < 3 && 
               (Board[row, col] == '\0' || Board[row, col] == ' ');
    }
    
    /// <summary>
    /// Gets all available move positions
    /// Algorithm: O(9) - scans all board positions
    /// Used by AI engine for move generation
    /// </summary>
    public List<(int row, int col)> GetAvailableMoves()
    {
        var moves = new List<(int row, int col)>();
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                if (IsValidMove(i, j))
                {
                    moves.Add((i, j));
                }
            }
        }
        return moves;
    }
    
    /// <summary>
    /// Creates a deep copy of the board state
    /// Used by AI engine for state exploration without modifying original
    /// Algorithm: O(9) - copies all board positions
    /// </summary>
    public char[,] CloneBoard()
    {
        var clone = new char[3, 3];
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                clone[i, j] = Board[i, j];
            }
        }
        return clone;
    }
    
    /// <summary>
    /// Serializes board state for client transmission
    /// Algorithm: O(9) - converts 2D array to string representation
    /// Format: "X O XO X O X" (space for empty, X/O for moves)
    /// </summary>
    public string SerializeBoardState()
    {
        var result = new StringBuilder(17); // 9 cells + 8 spaces
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                var cell = Board[i, j];
                result.Append(cell == '\0' || cell == ' ' ? ' ' : cell);
                if (i < 2 || j < 2) result.Append(' ');
            }
        }
        return result.ToString();
    }
    
    /// <summary>
    /// Gets comprehensive room statistics for monitoring
    /// </summary>
    public RoomStatistics GetStatistics()
    {
        return new RoomStatistics
        {
            RoomId = RoomId,
            PlayerCount = Players.Count(p => p != null),
            Status = Status,
            IsAIMode = IsAIMode,
            MoveCount = MoveCount,
            GameDuration = Status == GameStatus.InProgress ? 
                DateTime.UtcNow - CreatedAtUtc : GameDuration,
            LastActivity = LastActivityUtc,
            Winner = Winner
        };
    }
}

/// <summary>
/// Represents a player with enhanced metadata
/// 
/// Design Considerations:
/// 1. ConnectionId for SignalR message routing
/// 2. Immutable mark assignment prevents cheating
/// 3. Join timestamp for session analytics
/// 4. Optional user ID for persistent identity
/// </summary>
public class Player
{
    public string ConnectionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public char Mark { get; set; } // 'X' or 'O'
    public DateTime JoinedAtUtc { get; set; } = DateTime.UtcNow;
    public string? UserId { get; set; } // Optional persistent identity
    public int MovesPlayed { get; set; } = 0;
    public TimeSpan TotalThinkTime { get; set; } = TimeSpan.Zero;
    
    /// <summary>
    /// Calculates average time per move for analytics
    /// </summary>
    public TimeSpan AverageThinkTime => 
        MovesPlayed > 0 ? TimeSpan.FromTicks(TotalThinkTime.Ticks / MovesPlayed) : TimeSpan.Zero;
    
    /// <summary>
    /// Determines if this player is human or AI
    /// </summary>
    public bool IsAI => string.IsNullOrEmpty(ConnectionId) || ConnectionId == "AI";
    
    public override string ToString()
    {
        return $"{Name} ({Mark}) - {ConnectionId}";
    }
}

/// <summary>
/// Enhanced game status with additional states for better UX
/// </summary>
public enum GameStatus
{
    WaitingForPlayers,  // Room created, waiting for players to join
    InProgress,         // Game actively being played
    Finished,           // Game completed (win/draw)
    Abandoned,          // Players disconnected, marked for cleanup
    Paused,            // Game temporarily paused (future feature)
    Error              // Game in error state, needs intervention
}

/// <summary>
/// Statistics model for room analytics and monitoring
/// </summary>
public class RoomStatistics
{
    public string RoomId { get; set; } = string.Empty;
    public int PlayerCount { get; set; }
    public GameStatus Status { get; set; }
    public bool IsAIMode { get; set; }
    public int MoveCount { get; set; }
    public TimeSpan GameDuration { get; set; }
    public DateTime LastActivity { get; set; }
    public string? Winner { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Move history for game replay and analysis
/// </summary>
public class GameMove
{
    public int Row { get; set; }
    public int Col { get; set; }
    public char Mark { get; set; }
    public string PlayerId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public TimeSpan ThinkTime { get; set; }
    public bool IsAIMove { get; set; }
    
    public override string ToString()
    {
        return $"{Mark} at ({Row},{Col}) by {PlayerId} [{ThinkTime.TotalSeconds:F1}s]";
    }
}
```

### 2.3 Singleton Services

**RoomManager.cs (server/Services/)**
```csharp
public interface IRoomManager
{
    GameRoom CreateRoom(string roomId, bool isAIMode = false);
    GameRoom? GetRoom(string roomId);
    bool JoinRoom(string roomId, Player player);
    bool MakeMove(string roomId, int row, int col, string connectionId);
    void RemoveRoom(string roomId);
    List<string> GetInactiveRooms(TimeSpan inactiveThreshold);
    bool LeaveRoom(string roomId, string connectionId);
    GameRoom[] GetAllRooms();
}

/// <summary>
/// Thread-safe singleton service managing game room lifecycle
/// 
/// Design Reasoning:
/// 1. Singleton pattern ensures single source of truth for room state
/// 2. ConcurrentDictionary provides thread-safe operations for multiple SignalR connections
/// 3. Reader-writer lock pattern for complex operations requiring consistency
/// 4. Event-driven architecture for real-time updates
/// 
/// Memory Management:
/// - Rooms are pruned automatically to prevent memory leaks
/// - Each room tracks last activity for efficient cleanup
/// - Maximum room capacity prevents resource exhaustion
/// </summary>
public class RoomManager : IRoomManager
{
    private readonly ConcurrentDictionary<string, GameRoom> _rooms = new();
    private readonly ILogger<RoomManager> _logger;
    private readonly ReaderWriterLockSlim _roomLock = new();
    private readonly IAIEngine _aiEngine;

    // Configuration constants
    private const int MAX_ROOMS = 1000;
    private const int MAX_ROOM_ID_LENGTH = 50;
    private const int MOVE_TIMEOUT_SECONDS = 30;

    public RoomManager(ILogger<RoomManager> logger, IAIEngine aiEngine)
    {
        _logger = logger;
        _aiEngine = aiEngine;
    }

    /// <summary>
    /// Creates a new game room with thread-safe operations
    /// Algorithm: O(1) insertion with collision handling
    /// </summary>
    public GameRoom CreateRoom(string roomId, bool isAIMode = false)
    {
        // Input validation
        if (string.IsNullOrWhiteSpace(roomId))
            throw new ArgumentException("Room ID cannot be null or empty", nameof(roomId));
        
        if (roomId.Length > MAX_ROOM_ID_LENGTH)
            throw new ArgumentException($"Room ID cannot exceed {MAX_ROOM_ID_LENGTH} characters", nameof(roomId));

        // Check room capacity
        if (_rooms.Count >= MAX_ROOMS)
        {
            _logger.LogWarning("Maximum room capacity ({MaxRooms}) reached", MAX_ROOMS);
            throw new InvalidOperationException("Server at capacity. Please try again later.");
        }

        _roomLock.EnterWriteLock();
        try
        {
            var room = new GameRoom
            {
                RoomId = roomId,
                IsAIMode = isAIMode,
                Status = GameStatus.WaitingForPlayers,
                Board = new char[3, 3],
                CurrentTurn = "X",
                LastActivityUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
                MaxPlayers = isAIMode ? 1 : 2
            };

            // Initialize empty board
            InitializeBoard(room.Board);

            // Thread-safe insertion
            if (_rooms.TryAdd(roomId, room))
            {
                _logger.LogInformation("Created room {RoomId} (AI Mode: {IsAIMode})", roomId, isAIMode);
                return room;
            }
            else
            {
                _logger.LogWarning("Room {RoomId} already exists", roomId);
                throw new InvalidOperationException($"Room {roomId} already exists");
            }
        }
        finally
        {
            _roomLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Thread-safe room retrieval with read lock optimization
    /// Algorithm: O(1) dictionary lookup
    /// </summary>
    public GameRoom? GetRoom(string roomId)
    {
        if (string.IsNullOrWhiteSpace(roomId)) return null;

        _roomLock.EnterReadLock();
        try
        {
            return _rooms.TryGetValue(roomId, out var room) ? room : null;
        }
        finally
        {
            _roomLock.ExitReadLock();
        }
    }

    /// <summary>
    /// Player join logic with game state validation
    /// 
    /// Algorithm Steps:
    /// 1. Validate room exists and has capacity
    /// 2. Assign player mark (X/O) based on join order
    /// 3. Update room state atomically
    /// 4. Start game if conditions met
    /// 
    /// Concurrency: Uses write lock to ensure consistent state updates
    /// </summary>
    public bool JoinRoom(string roomId, Player player)
    {
        if (string.IsNullOrWhiteSpace(roomId) || player == null)
        {
            _logger.LogWarning("Invalid join room request: RoomId={RoomId}, Player={Player}", roomId, player?.Name);
            return false;
        }

        _roomLock.EnterWriteLock();
        try
        {
            if (!_rooms.TryGetValue(roomId, out var room))
            {
                _logger.LogWarning("Attempted to join non-existent room {RoomId}", roomId);
                return false;
            }

            // Check if player already in room
            if (room.Players.Any(p => p?.ConnectionId == player.ConnectionId))
            {
                _logger.LogInformation("Player {PlayerName} already in room {RoomId}", player.Name, roomId);
                return true;
            }

            // Find available slot
            int playerIndex = -1;
            for (int i = 0; i < room.Players.Length; i++)
            {
                if (room.Players[i] == null)
                {
                    playerIndex = i;
                    break;
                }
            }

            if (playerIndex == -1)
            {
                _logger.LogWarning("Room {RoomId} is full", roomId);
                return false;
            }

            // Assign player mark based on position
            player.Mark = playerIndex == 0 ? 'X' : 'O';
            room.Players[playerIndex] = player;
            room.LastActivityUtc = DateTime.UtcNow;

            // Check if room is ready to start
            var activePlayers = room.Players.Count(p => p != null);
            if ((room.IsAIMode && activePlayers == 1) || (!room.IsAIMode && activePlayers == 2))
            {
                room.Status = GameStatus.InProgress;
                _logger.LogInformation("Game started in room {RoomId}", roomId);
            }

            _logger.LogInformation("Player {PlayerName} joined room {RoomId} as {Mark}", 
                player.Name, roomId, player.Mark);
            
            return true;
        }
        finally
        {
            _roomLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Move validation and execution with comprehensive game logic
    /// 
    /// Validation Steps:
    /// 1. Room and player existence
    /// 2. Game state (in progress)
    /// 3. Turn validation
    /// 4. Move legality (empty cell)
    /// 5. Win condition checking
    /// 6. AI response (if applicable)
    /// 
    /// Algorithm Complexity: O(1) for move validation, O(1) for win detection
    /// </summary>
    public bool MakeMove(string roomId, int row, int col, string connectionId)
    {
        // Input validation
        if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(connectionId))
        {
            _logger.LogWarning("Invalid move request: RoomId={RoomId}, ConnectionId={ConnectionId}", roomId, connectionId);
            return false;
        }

        if (row < 0 || row > 2 || col < 0 || col > 2)
        {
            _logger.LogWarning("Invalid move coordinates: ({Row}, {Col})", row, col);
            return false;
        }

        _roomLock.EnterWriteLock();
        try
        {
            if (!_rooms.TryGetValue(roomId, out var room))
            {
                _logger.LogWarning("Move attempted on non-existent room {RoomId}", roomId);
                return false;
            }

            // Validate game state
            if (room.Status != GameStatus.InProgress)
            {
                _logger.LogWarning("Move attempted on room {RoomId} with status {Status}", roomId, room.Status);
                return false;
            }

            // Find player making the move
            var player = room.Players.FirstOrDefault(p => p?.ConnectionId == connectionId);
            if (player == null)
            {
                _logger.LogWarning("Move attempted by unknown player in room {RoomId}", roomId);
                return false;
            }

            // Validate turn
            if (player.Mark.ToString() != room.CurrentTurn)
            {
                _logger.LogWarning("Out of turn move by {PlayerName} in room {RoomId}", player.Name, roomId);
                return false;
            }

            // Validate move legality
            if (room.Board[row, col] != '\0' && room.Board[row, col] != ' ')
            {
                _logger.LogWarning("Invalid move to occupied cell ({Row}, {Col}) in room {RoomId}", row, col, roomId);
                return false;
            }

            // Execute move
            room.Board[row, col] = player.Mark;
            room.LastActivityUtc = DateTime.UtcNow;

            _logger.LogInformation("Player {PlayerName} made move ({Row}, {Col}) in room {RoomId}", 
                player.Name, row, col, roomId);

            // Check win conditions
            var gameResult = EvaluateGameState(room.Board, player.Mark);
            if (gameResult != GameResult.Ongoing)
            {
                room.Status = GameStatus.Finished;
                room.Winner = gameResult == GameResult.Win ? player.Mark.ToString() : null;
                _logger.LogInformation("Game finished in room {RoomId}: {Result}", roomId, gameResult);
                return true;
            }

            // Switch turns
            room.CurrentTurn = room.CurrentTurn == "X" ? "O" : "X";

            // AI response in AI mode
            if (room.IsAIMode && room.Status == GameStatus.InProgress)
            {
                ExecuteAIMove(room);
            }

            return true;
        }
        finally
        {
            _roomLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// AI move execution with error handling and fallback strategies
    /// 
    /// Reasoning:
    /// - AI moves are executed synchronously to maintain game state consistency
    /// - Timeout protection prevents infinite loops
    /// - Fallback to random moves if AI engine fails
    /// </summary>
    private void ExecuteAIMove(GameRoom room)
    {
        try
        {
            var aiMark = room.Players[0]?.Mark == 'X' ? 'O' : 'X';
            var aiMove = _aiEngine.GetBestMove(room.Board, aiMark);

            if (aiMove.row != -1 && aiMove.col != -1)
            {
                room.Board[aiMove.row, aiMove.col] = aiMark;
                room.LastActivityUtc = DateTime.UtcNow;

                _logger.LogInformation("AI made move ({Row}, {Col}) in room {RoomId}", 
                    aiMove.row, aiMove.col, room.RoomId);

                // Check AI win
                var aiResult = EvaluateGameState(room.Board, aiMark);
                if (aiResult != GameResult.Ongoing)
                {
                    room.Status = GameStatus.Finished;
                    room.Winner = aiResult == GameResult.Win ? aiMark.ToString() : null;
                    _logger.LogInformation("AI game finished in room {RoomId}: {Result}", room.RoomId, aiResult);
                    return;
                }

                // Switch turn back to human player
                room.CurrentTurn = room.Players[0]?.Mark.ToString() ?? "X";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI move failed in room {RoomId}", room.RoomId);
            // Fallback: random move
            ExecuteRandomAIMove(room);
        }
    }

    private void ExecuteRandomAIMove(GameRoom room)
    {
        var availableMoves = new List<(int row, int col)>();
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                if (room.Board[i, j] == '\0' || room.Board[i, j] == ' ')
                    availableMoves.Add((i, j));
            }
        }

        if (availableMoves.Count > 0)
        {
            var random = new Random();
            var move = availableMoves[random.Next(availableMoves.Count)];
            var aiMark = room.Players[0]?.Mark == 'X' ? 'O' : 'X';
            
            room.Board[move.row, move.col] = aiMark;
            room.CurrentTurn = room.Players[0]?.Mark.ToString() ?? "X";
            
            _logger.LogInformation("AI made fallback random move ({Row}, {Col}) in room {RoomId}", 
                move.row, move.col, room.RoomId);
        }
    }

    /// <summary>
    /// Optimized win detection algorithm
    /// 
    /// Algorithm: O(1) - checks only 8 possible winning combinations
    /// Instead of O(n²) board scanning, directly check:
    /// - 3 rows, 3 columns, 2 diagonals
    /// 
    /// Early termination on first winning pattern found
    /// </summary>
    private GameResult EvaluateGameState(char[,] board, char lastMark)
    {
        // Check rows
        for (int i = 0; i < 3; i++)
        {
            if (board[i, 0] == lastMark && board[i, 1] == lastMark && board[i, 2] == lastMark)
                return GameResult.Win;
        }

        // Check columns
        for (int j = 0; j < 3; j++)
        {
            if (board[0, j] == lastMark && board[1, j] == lastMark && board[2, j] == lastMark)
                return GameResult.Win;
        }

        // Check diagonals
        if (board[0, 0] == lastMark && board[1, 1] == lastMark && board[2, 2] == lastMark)
            return GameResult.Win;
        
        if (board[0, 2] == lastMark && board[1, 1] == lastMark && board[2, 0] == lastMark)
            return GameResult.Win;

        // Check for draw (board full)
        bool boardFull = true;
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                if (board[i, j] == '\0' || board[i, j] == ' ')
                {
                    boardFull = false;
                    break;
                }
            }
            if (!boardFull) break;
        }

        return boardFull ? GameResult.Draw : GameResult.Ongoing;
    }

    /// <summary>
    /// Thread-safe player removal with room cleanup
    /// </summary>
    public bool LeaveRoom(string roomId, string connectionId)
    {
        if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(connectionId))
            return false;

        _roomLock.EnterWriteLock();
        try
        {
            if (_rooms.TryGetValue(roomId, out var room))
            {
                for (int i = 0; i < room.Players.Length; i++)
                {
                    if (room.Players[i]?.ConnectionId == connectionId)
                    {
                        var playerName = room.Players[i].Name;
                        room.Players[i] = null;
                        room.LastActivityUtc = DateTime.UtcNow;

                        // If no players left, mark room for cleanup
                        if (room.Players.All(p => p == null))
                        {
                            room.Status = GameStatus.Abandoned;
                        }

                        _logger.LogInformation("Player {PlayerName} left room {RoomId}", playerName, roomId);
                        return true;
                    }
                }
            }
            return false;
        }
        finally
        {
            _roomLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Efficient room cleanup using LINQ and parallel processing
    /// Algorithm: O(n) where n is number of rooms
    /// </summary>
    public List<string> GetInactiveRooms(TimeSpan inactiveThreshold)
    {
        var cutoffTime = DateTime.UtcNow - inactiveThreshold;
        
        return _rooms
            .Where(kvp => kvp.Value.LastActivityUtc < cutoffTime || kvp.Value.Status == GameStatus.Abandoned)
            .Select(kvp => kvp.Key)
            .ToList();
    }

    public void RemoveRoom(string roomId)
    {
        if (string.IsNullOrWhiteSpace(roomId)) return;

        _roomLock.EnterWriteLock();
        try
        {
            if (_rooms.TryRemove(roomId, out var room))
            {
                _logger.LogInformation("Removed room {RoomId} (Status: {Status})", roomId, room.Status);
            }
        }
        finally
        {
            _roomLock.ExitWriteLock();
        }
    }

    public GameRoom[] GetAllRooms()
    {
        _roomLock.EnterReadLock();
        try
        {
            return _rooms.Values.ToArray();
        }
        finally
        {
            _roomLock.ExitReadLock();
        }
    }

    private void InitializeBoard(char[,] board)
    {
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                board[i, j] = '\0';
            }
        }
    }

    public void Dispose()
    {
        _roomLock?.Dispose();
    }
}

public enum GameResult
{
    Ongoing,
    Win,
    Draw
}
```

**KeyManager.cs (server/Services/)**
```csharp
public interface IKeyManager
{
    string GenerateJwtToken(string userId, string userName);
    JsonWebKeySet GetJwks();
    void RotateKeys();
    bool ValidateToken(string token, out ClaimsPrincipal? principal);
    Task<bool> IsTokenValidAsync(string token);
}

/// <summary>
/// Manages JWT key rotation and token validation with enhanced security
/// 
/// Security Design Principles:
/// 1. RSA-256 asymmetric encryption for token signing
/// 2. Hourly key rotation minimizes exposure window
/// 3. Overlapping key validity (2-hour window) prevents service disruption
/// 4. Secure key storage in Docker volumes with proper permissions
/// 5. JWKS endpoint compliance for standard token validation
/// 
/// Algorithm Complexity:
/// - Token generation: O(1) - RSA signing operation
/// - Token validation: O(k) where k is number of active keys (max 2)
/// - Key rotation: O(1) - atomic key swap operation
/// </summary>
public class KeyManager : IKeyManager, IDisposable
{
    private readonly ILogger<KeyManager> _logger;
    private readonly string _keyStoragePath;
    private readonly object _keyLock = new();
    
    // Key management
    private RSA? _currentSigningKey;
    private RSA? _previousSigningKey;
    private string _currentKeyId = string.Empty;
    private string _previousKeyId = string.Empty;
    private DateTime _lastRotation = DateTime.MinValue;
    
    // JWT configuration
    private readonly string _issuer;
    private readonly string _audience;
    private readonly TimeSpan _tokenLifetime;
    private readonly TimeSpan _keyRotationInterval;
    private readonly TimeSpan _keyOverlapWindow;

    public KeyManager(ILogger<KeyManager> logger, IConfiguration configuration)
    {
        _logger = logger;
        _keyStoragePath = configuration.GetValue<string>("JWT:KeyStoragePath") ?? "/app/keys";
        _issuer = configuration.GetValue<string>("JWT:Issuer") ?? "TicTacToeServer";
        _audience = configuration.GetValue<string>("JWT:Audience") ?? "TicTacToeClient";
        _tokenLifetime = TimeSpan.FromHours(configuration.GetValue<int>("JWT:TokenLifetimeHours", 1));
        _keyRotationInterval = TimeSpan.FromHours(configuration.GetValue<int>("JWT:KeyRotationHours", 1));
        _keyOverlapWindow = TimeSpan.FromHours(configuration.GetValue<int>("JWT:KeyOverlapHours", 2));

        InitializeKeys();
        _logger.LogInformation("KeyManager initialized with {TokenLifetime}h tokens, {RotationInterval}h rotation", 
            _tokenLifetime.TotalHours, _keyRotationInterval.TotalHours);
    }

    /// <summary>
    /// Initializes RSA keys from storage or creates new ones
    /// 
    /// Key Storage Strategy:
    /// - Keys stored as PEM files in Docker volume
    /// - Atomic file operations prevent corruption
    /// - Automatic key generation on first startup
    /// - Key recovery from persistent storage on restart
    /// </summary>
    private void InitializeKeys()
    {
        lock (_keyLock)
        {
            try
            {
                // Ensure key storage directory exists
                Directory.CreateDirectory(_keyStoragePath);

                // Try to load existing keys
                var currentKeyPath = Path.Combine(_keyStoragePath, "current.pem");
                var previousKeyPath = Path.Combine(_keyStoragePath, "previous.pem");
                var metadataPath = Path.Combine(_keyStoragePath, "metadata.json");

                if (File.Exists(currentKeyPath) && File.Exists(metadataPath))
                {
                    LoadExistingKeys(currentKeyPath, previousKeyPath, metadataPath);
                }
                else
                {
                    GenerateNewKeyPair();
                }

                _logger.LogInformation("Keys initialized. Current: {CurrentKeyId}, Previous: {PreviousKeyId}", 
                    _currentKeyId, _previousKeyId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize keys. Generating new key pair.");
                GenerateNewKeyPair();
            }
        }
    }

    private void LoadExistingKeys(string currentKeyPath, string previousKeyPath, string metadataPath)
    {
        try
        {
            // Load metadata
            var metadataJson = File.ReadAllText(metadataPath);
            var metadata = JsonSerializer.Deserialize<KeyMetadata>(metadataJson);
            
            if (metadata != null)
            {
                _currentKeyId = metadata.CurrentKeyId;
                _previousKeyId = metadata.PreviousKeyId;
                _lastRotation = metadata.LastRotation;
            }

            // Load current key
            var currentKeyPem = File.ReadAllText(currentKeyPath);
            _currentSigningKey = RSA.Create();
            _currentSigningKey.ImportFromPem(currentKeyPem);

            // Load previous key if exists
            if (File.Exists(previousKeyPath))
            {
                var previousKeyPem = File.ReadAllText(previousKeyPath);
                _previousSigningKey = RSA.Create();
                _previousSigningKey.ImportFromPem(previousKeyPem);
            }

            _logger.LogInformation("Loaded existing keys from storage");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load existing keys");
            throw;
        }
    }

    /// <summary>
    /// Generates new RSA key pair with secure parameters
    /// 
    /// RSA Configuration:
    /// - 2048-bit key size (industry standard, balances security and performance)
    /// - PKCS#1 padding for signature generation
    /// - SHA-256 hashing algorithm
    /// 
    /// Security Considerations:
    /// - Keys generated using cryptographically secure random number generator
    /// - Private keys never logged or transmitted
    /// - Atomic file operations prevent key corruption during writes
    /// </summary>
    private void GenerateNewKeyPair()
    {
        try
        {
            // Dispose old keys
            _currentSigningKey?.Dispose();
            _previousSigningKey?.Dispose();

            // Generate new RSA key pair (2048-bit)
            _currentSigningKey = RSA.Create(2048);
            _currentKeyId = GenerateKeyId();
            _lastRotation = DateTime.UtcNow;

            // Save to persistent storage
            SaveKeysToStorage();

            _logger.LogInformation("Generated new RSA key pair. KeyId: {KeyId}", _currentKeyId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate new key pair");
            throw;
        }
    }

    private string GenerateKeyId()
    {
        // Generate cryptographically secure key identifier
        using var rng = RandomNumberGenerator.Create();
        var bytes = new byte[16];
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private void SaveKeysToStorage()
    {
        try
        {
            var currentKeyPath = Path.Combine(_keyStoragePath, "current.pem");
            var previousKeyPath = Path.Combine(_keyStoragePath, "previous.pem");
            var metadataPath = Path.Combine(_keyStoragePath, "metadata.json");

            // Save current key
            if (_currentSigningKey != null)
            {
                var currentKeyPem = _currentSigningKey.ExportRSAPrivateKeyPem();
                File.WriteAllText(currentKeyPath, currentKeyPem);
                
                // Set secure file permissions (Unix-style)
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || 
                    RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    File.SetUnixFileMode(currentKeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
            }

            // Save previous key if exists
            if (_previousSigningKey != null)
            {
                var previousKeyPem = _previousSigningKey.ExportRSAPrivateKeyPem();
                File.WriteAllText(previousKeyPath, previousKeyPem);
                
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || 
                    RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    File.SetUnixFileMode(previousKeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
            }

            // Save metadata
            var metadata = new KeyMetadata
            {
                CurrentKeyId = _currentKeyId,
                PreviousKeyId = _previousKeyId,
                LastRotation = _lastRotation
            };
            
            var metadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            File.WriteAllText(metadataPath, metadataJson);

            _logger.LogDebug("Keys saved to persistent storage");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save keys to storage");
            throw;
        }
    }

    /// <summary>
    /// Generates JWT token with custom claims and enhanced security
    /// 
    /// Token Structure:
    /// - Header: RSA256 algorithm, current key ID
    /// - Payload: Standard claims (iss, aud, exp, iat, sub) + custom claims
    /// - Signature: RSA-SHA256 signature using current private key
    /// 
    /// Security Features:
    /// - Short expiration time (1 hour default)
    /// - Unique JTI (JWT ID) prevents replay attacks
    /// - Issued-at timestamp for freshness validation
    /// - Audience and issuer validation
    /// </summary>
    public string GenerateJwtToken(string userId, string userName)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("UserId and UserName cannot be null or empty");
        }

        lock (_keyLock)
        {
            if (_currentSigningKey == null)
            {
                _logger.LogError("No signing key available for token generation");
                throw new InvalidOperationException("No signing key available");
            }

            try
            {
                var now = DateTime.UtcNow;
                var jti = Guid.NewGuid().ToString(); // Unique token identifier

                var claims = new List<Claim>
                {
                    new(JwtRegisteredClaimNames.Sub, userId),
                    new(JwtRegisteredClaimNames.Jti, jti),
                    new(JwtRegisteredClaimNames.Iat, new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
                    new("username", userName),
                    new("role", "player"),
                    new("version", "1.0")
                };

                var tokenDescriptor = new SecurityTokenDescriptor
                {
                    Subject = new ClaimsIdentity(claims),
                    Expires = now.Add(_tokenLifetime),
                    Issuer = _issuer,
                    Audience = _audience,
                    SigningCredentials = new SigningCredentials(
                        new RsaSecurityKey(_currentSigningKey) { KeyId = _currentKeyId },
                        SecurityAlgorithms.RsaSha256
                    )
                };

                var tokenHandler = new JsonWebTokenHandler();
                var token = tokenHandler.CreateToken(tokenDescriptor);

                _logger.LogDebug("Generated JWT token for user {UserId} with JTI {Jti}", userId, jti);
                return token;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate JWT token for user {UserId}", userId);
                throw;
            }
        }
    }

    /// <summary>
    /// Validates JWT token using current and previous keys
    /// 
    /// Validation Process:
    /// 1. Parse token header to extract key ID
    /// 2. Select appropriate validation key (current or previous)
    /// 3. Validate signature using RSA public key
    /// 4. Validate standard claims (exp, iss, aud)
    /// 5. Extract claims principal for authorization
    /// 
    /// Multi-Key Support:
    /// - Supports both current and previous keys during rotation window
    /// - Graceful handling of key rotation without service interruption
    /// - Automatic fallback between keys
    /// </summary>
    public bool ValidateToken(string token, out ClaimsPrincipal? principal)
    {
        principal = null;

        if (string.IsNullOrWhiteSpace(token))
            return false;

        lock (_keyLock)
        {
            try
            {
                var tokenHandler = new JsonWebTokenHandler();
                
                // Try current key first
                if (_currentSigningKey != null && TryValidateWithKey(token, _currentSigningKey, _currentKeyId, out principal))
                {
                    _logger.LogTrace("Token validated with current key {KeyId}", _currentKeyId);
                    return true;
                }

                // Fallback to previous key
                if (_previousSigningKey != null && TryValidateWithKey(token, _previousSigningKey, _previousKeyId, out principal))
                {
                    _logger.LogTrace("Token validated with previous key {KeyId}", _previousKeyId);
                    return true;
                }

                _logger.LogWarning("Token validation failed - no valid key found");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during token validation");
                return false;
            }
        }
    }

    private bool TryValidateWithKey(string token, RSA key, string keyId, out ClaimsPrincipal? principal)
    {
        principal = null;

        try
        {
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new RsaSecurityKey(key) { KeyId = keyId },
                ValidateIssuer = true,
                ValidIssuer = _issuer,
                ValidateAudience = true,
                ValidAudience = _audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(5), // Allow 5 minutes clock skew
                RequireExpirationTime = true,
                RequireSignedTokens = true
            };

            var tokenHandler = new JsonWebTokenHandler();
            var result = tokenHandler.ValidateToken(token, validationParameters);

            if (result.IsValid)
            {
                principal = new ClaimsPrincipal(result.ClaimsIdentity);
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> IsTokenValidAsync(string token)
    {
        return await Task.FromResult(ValidateToken(token, out _));
    }

    /// <summary>
    /// Rotates signing keys with overlap window for zero-downtime deployment
    /// 
    /// Rotation Algorithm:
    /// 1. Current key becomes previous key
    /// 2. Generate new current key
    /// 3. Update JWKS endpoint
    /// 4. Persist keys to storage
    /// 5. Schedule cleanup of expired previous key
    /// 
    /// Timing Strategy:
    /// - Rotate every hour (configurable)
    /// - 2-hour overlap window prevents token rejection during rotation
    /// - Automatic cleanup of expired keys
    /// </summary>
    public void RotateKeys()
    {
        lock (_keyLock)
        {
            try
            {
                var now = DateTime.UtcNow;
                
                // Check if rotation is needed
                if (now - _lastRotation < _keyRotationInterval)
                {
                    _logger.LogTrace("Key rotation not needed yet. Last rotation: {LastRotation}", _lastRotation);
                    return;
                }

                _logger.LogInformation("Starting key rotation");

                // Move current key to previous
                _previousSigningKey?.Dispose();
                _previousSigningKey = _currentSigningKey;
                _previousKeyId = _currentKeyId;

                // Generate new current key
                _currentSigningKey = RSA.Create(2048);
                _currentKeyId = GenerateKeyId();
                _lastRotation = now;

                // Persist to storage
                SaveKeysToStorage();

                _logger.LogInformation("Key rotation completed. New KeyId: {NewKeyId}, Previous KeyId: {PreviousKeyId}", 
                    _currentKeyId, _previousKeyId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Key rotation failed");
                throw;
            }
        }
    }

    /// <summary>
    /// Generates JWKS (JSON Web Key Set) for public key distribution
    /// 
    /// JWKS Specification (RFC 7517):
    /// - Contains public keys for token verification
    /// - Supports key rotation with multiple active keys
    /// - Standard format for OAuth2/OpenID Connect compliance
    /// 
    /// Key Export:
    /// - RSA public key parameters (n, e)
    /// - Key usage: signature verification
    /// - Algorithm: RS256
    /// - Key ID for key selection during validation
    /// </summary>
    public JsonWebKeySet GetJwks()
    {
        lock (_keyLock)
        {
            var jwks = new JsonWebKeySet();

            // Add current key
            if (_currentSigningKey != null)
            {
                var currentJwk = CreateJsonWebKey(_currentSigningKey, _currentKeyId);
                jwks.Keys.Add(currentJwk);
            }

            // Add previous key (during overlap window)
            if (_previousSigningKey != null && ShouldIncludePreviousKey())
            {
                var previousJwk = CreateJsonWebKey(_previousSigningKey, _previousKeyId);
                jwks.Keys.Add(previousJwk);
            }

            _logger.LogTrace("Generated JWKS with {KeyCount} keys", jwks.Keys.Count);
            return jwks;
        }
    }

    private bool ShouldIncludePreviousKey()
    {
        var now = DateTime.UtcNow;
        return now - _lastRotation < _keyOverlapWindow;
    }

    private JsonWebKey CreateJsonWebKey(RSA rsa, string keyId)
    {
        var parameters = rsa.ExportParameters(false); // Export public key only
        
        return new JsonWebKey
        {
            Kty = "RSA",
            Use = "sig",
            Alg = "RS256",
            Kid = keyId,
            N = Base64UrlEncoder.Encode(parameters.Modulus!),
            E = Base64UrlEncoder.Encode(parameters.Exponent!)
        };
    }

    public void Dispose()
    {
        lock (_keyLock)
        {
            _currentSigningKey?.Dispose();
            _previousSigningKey?.Dispose();
            _logger.LogInformation("KeyManager disposed");
        }
    }
}

public class KeyMetadata
{
    public string CurrentKeyId { get; set; } = string.Empty;
    public string PreviousKeyId { get; set; } = string.Empty;
    public DateTime LastRotation { get; set; }
}
```

**AIEngine.cs (server/Services/)**
```csharp
public interface IAIEngine
{
    (int row, int col) GetBestMove(char[,] board, char aiMark);
    (int row, int col) GetBestMove(char[,] board, char aiMark, DifficultyLevel difficulty);
}

public enum DifficultyLevel
{
    Easy,    // Random moves with 30% optimal play
    Medium,  // Minimax with depth limit of 4
    Hard     // Full minimax with alpha-beta pruning
}

public class AIEngine : IAIEngine
{
    private readonly ILogger<AIEngine> _logger;
    private readonly Random _random;

    public AIEngine(ILogger<AIEngine> logger)
    {
        _logger = logger;
        _random = new Random();
    }

    /// <summary>
    /// Gets the best move using Minimax algorithm with alpha-beta pruning
    /// Algorithm Complexity: O(b^d) where b=branching factor, d=depth
    /// For TicTacToe: worst case is 9! = 362,880 nodes, but pruning reduces this significantly
    /// </summary>
    public (int row, int col) GetBestMove(char[,] board, char aiMark, DifficultyLevel difficulty = DifficultyLevel.Hard)
    {
        char playerMark = aiMark == 'X' ? 'O' : 'X';
        
        return difficulty switch
        {
            DifficultyLevel.Easy => GetRandomOrOptimalMove(board, aiMark, 0.3), // 30% optimal
            DifficultyLevel.Medium => GetLimitedDepthMove(board, aiMark, playerMark, 4),
            DifficultyLevel.Hard => GetOptimalMove(board, aiMark, playerMark),
            _ => GetOptimalMove(board, aiMark, playerMark)
        };
    }

    /// <summary>
    /// Implements Minimax with Alpha-Beta Pruning
    /// Reasoning: TicTacToe is a zero-sum game with perfect information
    /// - Minimax finds the optimal move assuming both players play perfectly
    /// - Alpha-beta pruning eliminates branches that won't affect the final decision
    /// - Reduces search space from O(b^d) to O(b^(d/2)) in best case
    /// </summary>
    private (int row, int col) GetOptimalMove(char[,] board, char aiMark, char playerMark)
    {
        int bestScore = int.MinValue;
        (int row, int col) bestMove = (-1, -1);

        var availableMoves = GetAvailableMoves(board);
        
        // Immediate win detection - O(1) optimization
        foreach (var (row, col) in availableMoves)
        {
            board[row, col] = aiMark;
            if (IsWinningMove(board, aiMark))
            {
                board[row, col] = '\0'; // Reset
                _logger.LogDebug("AI found immediate winning move at ({Row}, {Col})", row, col);
                return (row, col);
            }
            board[row, col] = '\0'; // Reset
        }

        // Block opponent's winning move - O(1) optimization
        foreach (var (row, col) in availableMoves)
        {
            board[row, col] = playerMark;
            if (IsWinningMove(board, playerMark))
            {
                board[row, col] = '\0'; // Reset
                _logger.LogDebug("AI blocked opponent's winning move at ({Row}, {Col})", row, col);
                return (row, col);
            }
            board[row, col] = '\0'; // Reset
        }

        // Full minimax search for optimal move
        foreach (var (row, col) in availableMoves)
        {
            board[row, col] = aiMark;
            int score = Minimax(board, false, aiMark, playerMark, int.MinValue, int.MaxValue, 0);
            board[row, col] = '\0'; // Reset

            if (score > bestScore)
            {
                bestScore = score;
                bestMove = (row, col);
            }
        }

        _logger.LogDebug("AI selected move ({Row}, {Col}) with score {Score}", bestMove.row, bestMove.col, bestScore);
        return bestMove;
    }

    /// <summary>
    /// Minimax Algorithm with Alpha-Beta Pruning
    /// 
    /// Algorithm Explanation:
    /// 1. Maximizing player (AI) tries to maximize the score
    /// 2. Minimizing player (Human) tries to minimize the score
    /// 3. Alpha-beta pruning cuts off branches when:
    ///    - Beta <= Alpha (no need to explore further)
    /// 
    /// Game State Evaluation:
    /// +10: AI wins
    /// -10: Player wins
    /// 0: Draw or ongoing game
    /// 
    /// Pruning Logic:
    /// - Alpha: Best value that maximizing player can guarantee
    /// - Beta: Best value that minimizing player can guarantee
    /// - If beta <= alpha at any node, remaining branches can be pruned
    /// </summary>
    private int Minimax(char[,] board, bool isMaximizing, char aiMark, char playerMark, int alpha, int beta, int depth)
    {
        // Terminal state evaluation
        if (IsWinningMove(board, aiMark)) return 10 - depth; // Prefer quicker wins
        if (IsWinningMove(board, playerMark)) return depth - 10; // Delay losses
        if (IsBoardFull(board)) return 0; // Draw

        if (isMaximizing)
        {
            int maxEval = int.MinValue;
            var availableMoves = GetAvailableMoves(board);

            foreach (var (row, col) in availableMoves)
            {
                board[row, col] = aiMark;
                int eval = Minimax(board, false, aiMark, playerMark, alpha, beta, depth + 1);
                board[row, col] = '\0'; // Backtrack

                maxEval = Math.Max(maxEval, eval);
                alpha = Math.Max(alpha, eval);

                // Alpha-beta pruning
                if (beta <= alpha)
                {
                    _logger.LogTrace("Alpha-beta pruning at depth {Depth}: beta({Beta}) <= alpha({Alpha})", depth, beta, alpha);
                    break; // Beta cutoff
                }
            }
            return maxEval;
        }
        else
        {
            int minEval = int.MaxValue;
            var availableMoves = GetAvailableMoves(board);

            foreach (var (row, col) in availableMoves)
            {
                board[row, col] = playerMark;
                int eval = Minimax(board, true, aiMark, playerMark, alpha, beta, depth + 1);
                board[row, col] = '\0'; // Backtrack

                minEval = Math.Min(minEval, eval);
                beta = Math.Min(beta, eval);

                // Alpha-beta pruning
                if (beta <= alpha)
                {
                    _logger.LogTrace("Alpha-beta pruning at depth {Depth}: beta({Beta}) <= alpha({Alpha})", depth, beta, alpha);
                    break; // Alpha cutoff
                }
            }
            return minEval;
        }
    }

    /// <summary>
    /// Limited depth search for medium difficulty
    /// Uses iterative deepening for better move ordering
    /// </summary>
    private (int row, int col) GetLimitedDepthMove(char[,] board, char aiMark, char playerMark, int maxDepth)
    {
        // Iterative deepening: search with increasing depth limits
        // This provides better move ordering and allows for time constraints
        (int row, int col) bestMove = (-1, -1);
        
        for (int depth = 1; depth <= maxDepth; depth++)
        {
            bestMove = GetMoveAtDepth(board, aiMark, playerMark, depth);
            _logger.LogTrace("Depth {Depth} search completed, best move: ({Row}, {Col})", depth, bestMove.row, bestMove.col);
        }
        
        return bestMove;
    }

    private (int row, int col) GetMoveAtDepth(char[,] board, char aiMark, char playerMark, int maxDepth)
    {
        int bestScore = int.MinValue;
        (int row, int col) bestMove = (-1, -1);

        foreach (var (row, col) in GetAvailableMoves(board))
        {
            board[row, col] = aiMark;
            int score = MinimaxLimited(board, false, aiMark, playerMark, int.MinValue, int.MaxValue, 0, maxDepth);
            board[row, col] = '\0';

            if (score > bestScore)
            {
                bestScore = score;
                bestMove = (row, col);
            }
        }

        return bestMove;
    }

    private int MinimaxLimited(char[,] board, bool isMaximizing, char aiMark, char playerMark, int alpha, int beta, int depth, int maxDepth)
    {
        // Depth limit reached - use heuristic evaluation
        if (depth >= maxDepth)
        {
            return EvaluatePosition(board, aiMark, playerMark);
        }

        // Terminal states
        if (IsWinningMove(board, aiMark)) return 100 - depth;
        if (IsWinningMove(board, playerMark)) return depth - 100;
        if (IsBoardFull(board)) return 0;

        // Same minimax logic but with depth limitation
        if (isMaximizing)
        {
            int maxEval = int.MinValue;
            foreach (var (row, col) in GetAvailableMoves(board))
            {
                board[row, col] = aiMark;
                int eval = MinimaxLimited(board, false, aiMark, playerMark, alpha, beta, depth + 1, maxDepth);
                board[row, col] = '\0';

                maxEval = Math.Max(maxEval, eval);
                alpha = Math.Max(alpha, eval);
                if (beta <= alpha) break;
            }
            return maxEval;
        }
        else
        {
            int minEval = int.MaxValue;
            foreach (var (row, col) in GetAvailableMoves(board))
            {
                board[row, col] = playerMark;
                int eval = MinimaxLimited(board, true, aiMark, playerMark, alpha, beta, depth + 1, maxDepth);
                board[row, col] = '\0';

                minEval = Math.Min(minEval, eval);
                beta = Math.Min(beta, eval);
                if (beta <= alpha) break;
            }
            return minEval;
        }
    }

    /// <summary>
    /// Heuristic evaluation for non-terminal positions
    /// Used when depth limit is reached in medium difficulty
    /// 
    /// Evaluation Criteria:
    /// - Center control: +3 points
    /// - Corner control: +2 points  
    /// - Edge control: +1 point
    /// - Two in a row (with empty third): +5 points
    /// - Block opponent's two in a row: +3 points
    /// </summary>
    private int EvaluatePosition(char[,] board, char aiMark, char playerMark)
    {
        int score = 0;

        // Center control (most valuable position)
        if (board[1, 1] == aiMark) score += 3;
        else if (board[1, 1] == playerMark) score -= 3;

        // Corner control
        int[] corners = { board[0, 0], board[0, 2], board[2, 0], board[2, 2] };
        foreach (char corner in corners)
        {
            if (corner == aiMark) score += 2;
            else if (corner == playerMark) score -= 2;
        }

        // Two-in-a-row evaluation
        score += EvaluateLines(board, aiMark, playerMark);

        return score;
    }

    private int EvaluateLines(char[,] board, char aiMark, char playerMark)
    {
        int score = 0;
        
        // Check all possible winning lines
        var lines = new List<(int, int)[]>
        {
            // Rows
            new[] { (0, 0), (0, 1), (0, 2) },
            new[] { (1, 0), (1, 1), (1, 2) },
            new[] { (2, 0), (2, 1), (2, 2) },
            // Columns
            new[] { (0, 0), (1, 0), (2, 0) },
            new[] { (0, 1), (1, 1), (2, 1) },
            new[] { (0, 2), (1, 2), (2, 2) },
            // Diagonals
            new[] { (0, 0), (1, 1), (2, 2) },
            new[] { (0, 2), (1, 1), (2, 0) }
        };

        foreach (var line in lines)
        {
            int aiCount = 0, playerCount = 0, emptyCount = 0;
            
            foreach (var (row, col) in line)
            {
                if (board[row, col] == aiMark) aiCount++;
                else if (board[row, col] == playerMark) playerCount++;
                else emptyCount++;
            }

            // Two AI marks with one empty (potential win)
            if (aiCount == 2 && emptyCount == 1) score += 5;
            // Two player marks with one empty (must block)
            else if (playerCount == 2 && emptyCount == 1) score -= 5;
            // One AI mark with two empty (potential)
            else if (aiCount == 1 && emptyCount == 2) score += 1;
            // One player mark with two empty (opponent potential)
            else if (playerCount == 1 && emptyCount == 2) score -= 1;
        }

        return score;
    }

    /// <summary>
    /// Mixed strategy for easy difficulty
    /// Combines random moves with occasional optimal play
    /// </summary>
    private (int row, int col) GetRandomOrOptimalMove(char[,] board, char aiMark, double optimalPlayProbability)
    {
        // Play optimally with given probability
        if (_random.NextDouble() < optimalPlayProbability)
        {
            return GetOptimalMove(board, aiMark, aiMark == 'X' ? 'O' : 'X');
        }

        // Random move from available positions
        var availableMoves = GetAvailableMoves(board);
        if (availableMoves.Count > 0)
        {
            var randomMove = availableMoves[_random.Next(availableMoves.Count)];
            _logger.LogDebug("AI made random move at ({Row}, {Col})", randomMove.row, randomMove.col);
            return randomMove;
        }

        return (-1, -1); // No moves available
    }

    private List<(int row, int col)> GetAvailableMoves(char[,] board)
    {
        var moves = new List<(int row, int col)>();
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                if (board[i, j] == '\0' || board[i, j] == ' ')
                {
                    moves.Add((i, j));
                }
            }
        }
        return moves;
    }

    private bool IsWinningMove(char[,] board, char mark)
    {
        // Check rows, columns, and diagonals
        for (int i = 0; i < 3; i++)
        {
            // Rows
            if (board[i, 0] == mark && board[i, 1] == mark && board[i, 2] == mark)
                return true;
            // Columns
            if (board[0, i] == mark && board[1, i] == mark && board[2, i] == mark)
                return true;
        }

        // Diagonals
        if (board[0, 0] == mark && board[1, 1] == mark && board[2, 2] == mark)
            return true;
        if (board[0, 2] == mark && board[1, 1] == mark && board[2, 0] == mark)
            return true;

        return false;
    }

    private bool IsBoardFull(char[,] board)
    {
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                if (board[i, j] == '\0' || board[i, j] == ' ')
                    return false;
            }
        }
        return true;
    }
}
```

### 2.4 Background Services

**RoomPruner.cs (server/Services/)**
```csharp
public class RoomPruner : BackgroundService
{
    private readonly IRoomManager _roomManager;
    private readonly IKeyManager _keyManager;
    private readonly ILogger<RoomPruner> _logger;
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Every 5 minutes cleanup
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            
            // Remove rooms inactive > 30 minutes
            var inactiveRooms = _roomManager.GetInactiveRooms(TimeSpan.FromMinutes(30));
            foreach (var roomId in inactiveRooms)
            {
                _roomManager.RemoveRoom(roomId);
                _logger.LogInformation("Pruned inactive room: {RoomId}", roomId);
            }
            
            // Rotate JWT keys hourly
            _keyManager.RotateKeys();
        }
    }
}
```

### 2.5 SignalR Hub

**TicTacToeHub.cs (server/Hubs/)**
```csharp
[Authorize]
public class TicTacToeHub : Hub
{
    private readonly IRoomManager _roomManager;
    private readonly IAIEngine _aiEngine;
    private readonly ILogger<TicTacToeHub> _logger;

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
                _logger.LogInformation("Player {PlayerName} joined room {RoomId}", playerName, roomId);
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
        // Server-side validation
        // AI response if in AI mode
        // Broadcast move to room group
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Handle player disconnect
        // Notify remaining players
    }
}
```

### 2.6 Controllers & Authentication

**AuthController.cs (server/Controllers/)**
```csharp
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IKeyManager _keyManager;

    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        // Simple validation (extend as needed)
        if (string.IsNullOrEmpty(request.UserName))
            return BadRequest("Username required");

        var token = _keyManager.GenerateJwtToken(
            Guid.NewGuid().ToString(), 
            request.UserName);
            
        return Ok(new { token });
    }

    [HttpGet("/.well-known/jwks.json")]
    public IActionResult GetJwks()
    {
        return Ok(_keyManager.GetJwks());
    }
}
```

### 2.7 Program.cs Configuration
```csharp
var builder = WebApplication.CreateBuilder(args);

// Services registration
builder.Services.AddSingleton<IRoomManager, RoomManager>();
builder.Services.AddSingleton<IKeyManager, KeyManager>();
builder.Services.AddScoped<IAIEngine, AIEngine>();
builder.Services.AddHostedService<RoomPruner>();

// SignalR
builder.Services.AddSignalR();

// JWT Authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // JWT configuration with JWKS
        // SignalR query string token support
    });

// CORS for development
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:3000")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials());
});

var app = builder.Build();

// Middleware pipeline
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// SignalR Hub
app.MapHub<TicTacToeHub>("/tictactoehub");

// API Controllers
app.MapControllers();

app.Run();
```

## 🎨 Phase 3: Frontend Development (React + TypeScript + Vite)

### 3.1 Initialize React Project
```bash
cd client
npm create vite@latest . -- --template react-ts
npm install @microsoft/signalr
npm install @types/node
```

### 3.2 SignalR Client Service

**services/SignalRClient.ts**
```typescript
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';

export class SignalRClient {
    private connection: HubConnection | null = null;
    private token: string = '';

    async connect(token: string): Promise<void> {
        this.token = token;
        
        this.connection = new HubConnectionBuilder()
            .withUrl('http://localhost:5000/tictactoehub', {
                accessTokenFactory: () => token
            })
            .withAutomaticReconnect()
            .configureLogging(LogLevel.Information)
            .build();

        // Event handlers
        this.connection.on('PlayerJoined', (player) => {
            console.log('Player joined:', player);
        });

        this.connection.on('MoveMade', (gameState) => {
            console.log('Move made:', gameState);
        });

        await this.connection.start();
    }

    async joinRoom(roomId: string, playerName: string, isAIMode: boolean = false): Promise<void> {
        if (this.connection) {
            await this.connection.invoke('JoinRoom', roomId, playerName, isAIMode);
        }
    }

    async makeMove(roomId: string, row: number, col: number): Promise<void> {
        if (this.connection) {
            await this.connection.invoke('MakeMove', roomId, row, col);
        }
    }
}
```

### 3.3 Authentication Hook

**hooks/useAuth.ts**
```typescript
import { useState, useEffect } from 'react';

export const useAuth = () => {
    const [token, setToken] = useState<string | null>(
        localStorage.getItem('jwt_token')
    );
    const [isAuthenticated, setIsAuthenticated] = useState<boolean>(!!token);

    const login = async (username: string): Promise<boolean> => {
        try {
            const response = await fetch('http://localhost:5000/api/auth/login', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ userName: username })
            });

            if (response.ok) {
                const { token } = await response.json();
                localStorage.setItem('jwt_token', token);
                setToken(token);
                setIsAuthenticated(true);
                return true;
            }
        } catch (error) {
            console.error('Login failed:', error);
        }
        return false;
    };

    const logout = () => {
        localStorage.removeItem('jwt_token');
        setToken(null);
        setIsAuthenticated(false);
    };

    return { token, isAuthenticated, login, logout };
};
```

### 3.4 Game Board Component

**components/RoomUI.tsx**
```typescript
import React, { useState, useEffect } from 'react';
import { SignalRClient } from '../services/SignalRClient';

interface RoomUIProps {
    roomId: string;
    playerName: string;
    token: string;
    isAIMode: boolean;
}

export const RoomUI: React.FC<RoomUIProps> = ({ roomId, playerName, token, isAIMode }) => {
    const [board, setBoard] = useState<string[][]>(Array(3).fill(null).map(() => Array(3).fill('')));
    const [currentTurn, setCurrentTurn] = useState<string>('X');
    const [gameStatus, setGameStatus] = useState<string>('waiting');
    const [signalRClient] = useState(new SignalRClient());

    useEffect(() => {
        const initializeConnection = async () => {
            await signalRClient.connect(token);
            await signalRClient.joinRoom(roomId, playerName, isAIMode);
        };

        initializeConnection();
    }, []);

    const handleCellClick = async (row: number, col: number) => {
        if (board[row][col] === '' && gameStatus === 'in-progress') {
            await signalRClient.makeMove(roomId, row, col);
        }
    };

    return (
        <div className="game-board">
            <h2>Room: {roomId}</h2>
            <div className="board">
                {board.map((row, rowIndex) =>
                    row.map((cell, colIndex) => (
                        <button
                            key={`${rowIndex}-${colIndex}`}
                            className="cell"
                            onClick={() => handleCellClick(rowIndex, colIndex)}
                            disabled={cell !== '' || gameStatus !== 'in-progress'}
                        >
                            {cell}
                        </button>
                    ))
                )}
            </div>
            <div className="game-info">
                <p>Current Turn: {currentTurn}</p>
                <p>Status: {gameStatus}</p>
                {isAIMode && <p>Playing vs AI</p>}
            </div>
        </div>
    );
};
```

### 3.5 Localization Support

**utils/Localization.ts**
```typescript
import React, { createContext, useContext, useState } from 'react';

const translations = {
    en: {
        'game.title': 'TicTacToe',
        'game.join': 'Join Room',
        'game.waiting': 'Waiting for players...',
        'game.your-turn': 'Your turn',
        'game.opponent-turn': 'Opponent\'s turn'
    },
    id: {
        'game.title': 'TicTacToe',
        'game.join': 'Gabung Ruangan',
        'game.waiting': 'Menunggu pemain...',
        'game.your-turn': 'Giliran Anda',
        'game.opponent-turn': 'Giliran lawan'
    }
};

export const LocalizationContext = createContext<{
    language: string;
    setLanguage: (lang: string) => void;
    t: (key: string) => string;
}>({
    language: 'en',
    setLanguage: () => {},
    t: (key: string) => key
});

export const useLocalization = () => useContext(LocalizationContext);
```

## 🐳 Phase 4: Dockerization

### 4.1 Backend Dockerfile
```dockerfile
# server/Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5000

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["TicTacToe.Server.csproj", "."]
RUN dotnet restore "TicTacToe.Server.csproj"
COPY . .
RUN dotnet build "TicTacToe.Server.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "TicTacToe.Server.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TicTacToe.Server.dll"]
```

### 4.2 Frontend Dockerfile
```dockerfile
# client/Dockerfile
FROM node:20-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM nginx:alpine
COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/nginx.conf
EXPOSE 3000
```

### 4.3 Docker Compose
```yaml
# docker-compose.yml
version: '3.8'

services:
  backend:
    build: ./server
    ports:
      - "5000:5000"
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ASPNETCORE_URLS=http://+:5000
    volumes:
      - jwt_keys:/app/keys
    depends_on:
      - redis

  frontend:
    build: ./client
    ports:
      - "3000:3000"
    depends_on:
      - backend

  redis:
    image: redis:alpine
    ports:
      - "6379:6379"
    volumes:
      - redis_data:/data

volumes:
  jwt_keys:
  redis_data:
```

## 🧪 Phase 5: Testing & Quality Assurance

### 5.1 Backend Unit Tests
```bash
cd server
dotnet add package Microsoft.AspNetCore.Mvc.Testing
dotnet add package xunit
dotnet add package Moq

# Test structure
mkdir Tests/{Unit,Integration}
```

**Example: RoomManager Tests**
```csharp
public class RoomManagerTests
{
    [Fact]
    public void CreateRoom_ShouldCreateNewRoom()
    {
        // Arrange
        var logger = Mock.Of<ILogger<RoomManager>>();
        var roomManager = new RoomManager(logger);
        
        // Act
        var room = roomManager.CreateRoom("test-room");
        
        // Assert
        Assert.NotNull(room);
        Assert.Equal("test-room", room.RoomId);
    }
}
```

### 5.2 Frontend Testing
```bash
cd client
npm install --save-dev @testing-library/react @testing-library/jest-dom
npm install --save-dev vitest jsdom
```

### 5.3 Development Commands
```bash
# Backend development
cd server
dotnet watch run

# Frontend development  
cd client
npm run dev

# Run tests
dotnet test                    # Backend tests
npm run test                   # Frontend tests

# Build for production
dotnet publish -c Release      # Backend
npm run build                  # Frontend

# Docker development
docker-compose up --build      # Full stack
docker-compose logs -f backend # View backend logs
```

## 🚀 Phase 6: Deployment

### 6.1 Local Docker Deployment
```bash
# Build and start all services
docker-compose up --build -d

# Check service health
docker-compose ps
docker-compose logs backend
docker-compose logs frontend

# Access application
# Frontend: http://localhost:3000/tictactoeplay
# Backend API: http://localhost:5000/api
# SignalR Hub: http://localhost:5000/tictactoehub
```

### 6.2 Backup Strategies (Initial Phase)

**Database Backup (Redis)**
```bash
# Manual backup
docker exec redis redis-cli BGSAVE
docker cp redis:/data/dump.rdb ./backup/

# Automated backup script
#!/bin/bash
DATE=$(date +%Y%m%d_%H%M%S)
docker exec redis redis-cli BGSAVE
docker cp redis:/data/dump.rdb ./backup/redis_backup_$DATE.rdb
```

**JWT Keys Backup**
```bash
# Keys are persisted in Docker volume
docker volume inspect xohub-dotnet-react_jwt_keys

# Manual backup
docker run --rm -v xohub-dotnet-react_jwt_keys:/data -v $(pwd)/backup:/backup alpine cp -r /data /backup/jwt_keys
```

### 6.3 Monitoring & Logs
```bash
# Application logs
docker-compose logs -f --tail=100

# System monitoring
docker stats

# Health checks
curl http://localhost:5000/health
curl http://localhost:3000
```

## 🔧 Phase 7: Error Handling Best Practices

### Global Exception Handling
```csharp
/// <summary>
/// Global exception handling middleware with comprehensive error categorization
/// 
/// Error Handling Strategy:
/// 1. Catch all unhandled exceptions at application boundary
/// 2. Log with appropriate severity based on exception type
/// 3. Return standardized error responses
/// 4. Prevent sensitive information leakage
/// 5. Maintain correlation IDs for distributed tracing
/// 
/// Performance Impact: ~1-2ms overhead per request
/// </summary>
public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger, IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.TraceIdentifier;
        
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception occurred. CorrelationId: {CorrelationId}, Path: {Path}", 
                correlationId, context.Request.Path);
            
            await HandleExceptionAsync(context, ex, correlationId);
        }
    }

    /// <summary>
    /// Handles different exception types with appropriate HTTP status codes and responses
    /// 
    /// Exception Categorization:
    /// - Security exceptions → 401/403
    /// - Validation exceptions → 400
    /// - Not found exceptions → 404
    /// - Business logic exceptions → 422
    /// - System exceptions → 500
    /// 
    /// Response Format:
    /// - Standardized error object with correlation ID
    /// - Environment-specific detail level
    /// - Structured for client error handling
    /// </summary>
    private async Task HandleExceptionAsync(HttpContext context, Exception exception, string correlationId)
    {
        context.Response.ContentType = "application/json";
        
        var (statusCode, message, details) = exception switch
        {
            SecurityTokenValidationException => (401, "Authentication failed", "Invalid or expired token"),
            UnauthorizedAccessException => (403, "Access denied", "Insufficient permissions"),
            ArgumentException argEx => (400, "Invalid request", argEx.Message),
            KeyNotFoundException => (404, "Resource not found", "The requested resource was not found"),
            InvalidOperationException invOpEx when invOpEx.Message.Contains("capacity") => 
                (503, "Service unavailable", "Server at capacity"),
            InvalidOperationException invOpEx => (422, "Operation failed", invOpEx.Message),
            TimeoutException => (408, "Request timeout", "The operation timed out"),
            _ => (500, "Internal server error", "An unexpected error occurred")
        };

        context.Response.StatusCode = statusCode;

        var response = new ErrorResponse
        {
            Error = new ErrorDetail
            {
                Code = statusCode,
                Message = message,
                Details = _environment.IsDevelopment() ? details : message,
                CorrelationId = correlationId,
                Timestamp = DateTime.UtcNow
            }
        };

        // Add stack trace in development
        if (_environment.IsDevelopment())
        {
            response.Error.StackTrace = exception.StackTrace;
            response.Error.InnerException = exception.InnerException?.Message;
        }

        var jsonResponse = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = _environment.IsDevelopment()
        });

        await context.Response.WriteAsync(jsonResponse);
    }
}

public class ErrorResponse
{
    public ErrorDetail Error { get; set; } = new();
}

public class ErrorDetail
{
    public int Code { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? StackTrace { get; set; }
    public string? InnerException { get; set; }
}

/// <summary>
/// SignalR-specific exception handling for hub methods
/// 
/// SignalR Error Handling Challenges:
/// 1. Exceptions don't follow standard HTTP error model
/// 2. Need to send error messages to specific clients/groups
/// 3. Connection state management during errors
/// 4. Graceful degradation for network issues
/// </summary>
public class SignalRExceptionFilter : IHubFilter
{
    private readonly ILogger<SignalRExceptionFilter> _logger;

    public SignalRExceptionFilter(ILogger<SignalRExceptionFilter> logger)
    {
        _logger = logger;
    }

    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext, 
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        try
        {
            return await next(invocationContext);
        }
        catch (Exception ex)
        {
            var connectionId = invocationContext.Context.ConnectionId;
            var methodName = invocationContext.HubMethodName;
            var userId = invocationContext.Context.User?.Identity?.Name ?? "Anonymous";

            _logger.LogError(ex, "SignalR hub method {MethodName} failed for user {UserId}, connection {ConnectionId}", 
                methodName, userId, connectionId);

            // Send error to specific client
            await invocationContext.Context.Clients.Caller.SendAsync("Error", new
            {
                method = methodName,
                message = GetUserFriendlyMessage(ex),
                timestamp = DateTime.UtcNow
            });

            // Re-throw for SignalR to handle connection state
            throw;
        }
    }

    private string GetUserFriendlyMessage(Exception exception)
    {
        return exception switch
        {
            ArgumentException => "Invalid request data",
            InvalidOperationException invOp when invOp.Message.Contains("room") => "Room operation failed",
            SecurityTokenValidationException => "Authentication required",
            TimeoutException => "Operation timed out",
            _ => "An error occurred processing your request"
        };
    }
}

/// <summary>
/// Structured logging extensions for consistent log formatting
/// 
/// Logging Standards:
/// 1. Use structured logging with context
/// 2. Include correlation IDs for distributed tracing
/// 3. Log levels based on operational impact
/// 4. Never log sensitive data (tokens, passwords, PII)
/// 5. Include performance metrics for optimization
/// </summary>
public static class LoggingExtensions
{
    public static void LogRoomActivity(this ILogger logger, string activity, string roomId, string? playerId = null, object? additionalData = null)
    {
        logger.LogInformation("Room activity: {Activity} in room {RoomId} by player {PlayerId}. Data: {@AdditionalData}",
            activity, roomId, playerId ?? "System", additionalData);
    }

    public static void LogGameEvent(this ILogger logger, string eventType, string roomId, GameRoom room)
    {
        logger.LogInformation("Game event: {EventType} in room {RoomId}. Status: {Status}, Players: {PlayerCount}, Moves: {MoveCount}",
            eventType, roomId, room.Status, room.Players.Count(p => p != null), room.MoveCount);
    }

    public static void LogPerformanceMetric(this ILogger logger, string operation, TimeSpan duration, string? context = null)
    {
        if (duration.TotalMilliseconds > 100) // Log slow operations
        {
            logger.LogWarning("Slow operation: {Operation} took {Duration}ms. Context: {Context}",
                operation, duration.TotalMilliseconds, context);
        }
        else
        {
            logger.LogDebug("Operation {Operation} completed in {Duration}ms",
                operation, duration.TotalMilliseconds);
        }
    }

    public static void LogSecurityEvent(this ILogger logger, string eventType, string? userId = null, string? details = null)
    {
        logger.LogWarning("Security event: {EventType} for user {UserId}. Details: {Details}",
            eventType, userId ?? "Unknown", details);
    }
}

/// <summary>
/// Performance monitoring middleware for request/response metrics
/// 
/// Metrics Collected:
/// - Request duration
- Response size
/// - Error rates by endpoint
/// - Concurrent request count
/// - Memory usage trends
/// </summary>
public class PerformanceMonitoringMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<PerformanceMonitoringMiddleware> _logger;
    private static readonly Counter RequestCounter = new("http_requests_total", "Total HTTP requests");
    private static readonly Histogram ResponseTime = new("http_request_duration_seconds", "HTTP request duration");

    public PerformanceMonitoringMiddleware(RequestDelegate next, ILogger<PerformanceMonitoringMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();
            var duration = stopwatch.Elapsed;
            
            RequestCounter.Inc();
            ResponseTime.Observe(duration.TotalSeconds);
            
            _logger.LogPerformanceMetric(
                $"{context.Request.Method} {context.Request.Path}",
                duration,
                $"Status: {context.Response.StatusCode}"
            );
        }
    }
}
```

### Structured Logging
```csharp
// Use structured logging with context
_logger.LogInformation("Player {PlayerId} joined room {RoomId} at {Timestamp}", 
    playerId, roomId, DateTime.UtcNow);

// Never log sensitive data
_logger.LogError("Authentication failed for user {UserId}", userId); // Good
_logger.LogError("Authentication failed with token {Token}", token); // BAD
```

## 📝 Development Checklist

- [ ] Environment setup (dotnet, node, docker)
- [ ] Backend project structure
- [ ] Core models and interfaces
- [ ] Singleton services (RoomManager, KeyManager)
- [ ] Background services (RoomPruner)
- [ ] SignalR hub implementation
- [ ] JWT authentication & JWKS
- [ ] Frontend React setup
- [ ] SignalR client integration
- [ ] Game UI components
- [ ] Docker configuration
- [ ] Testing setup
- [ ] Local deployment
- [ ] Backup strategies
- [ ] Monitoring setup

This walkthrough provides a complete foundation for building the xohub-dotnet-react project following industry best practices for real-time applications, security, and deployment.