/// <summary>
/// Represents a game room for managing players and game state
/// 
/// Design Considerations:
/// 1. Immutable room ID for consistent identification
/// 2. Fixed-size player list ensure O(1) access and prevent overflows
/// 3. Char array board for memory efficiency and fast access
/// 4. UTC timestamps for consistency accross time zones
/// 5. Comprehensive game status and metadata for analytics and monitoring
/// 6. Support for AI players with distinct handling
///
/// Memory Optimization:
/// - 9 chars for the board (3x3)
/// - 2 Player objects (assuming 100 bytes each)
/// - Log string and metadata (approx 200 bytes)
/// - Total estimated memory per room: ~500 bytes, allowing thousands of rooms in memory
/// </summary>
public class GameRoom
{
    public string RoomId { get; set; } = string.Empty;
    public Player[] Players { get; set; } = new Player[2]; // Max 2 players per room
    public char[,] Board { get; set; } = new char[3, 3]; // 3x3 Tic-Tac-Toe board
    public string CurrentTurn { get; set; } = "X";
    public GameStatus Status { get; set; } = GameStatus.WaitingForPlayers;
    public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsAIMode { get; set; } = false; // True if one player is AI
    public string? Winner { get; set; } // "X", "O", or null for no winner yet
    public int MoveCount { get; set; } = 0; // Total moves made in the game
    public DifficultyLevel AIDifficulty { get; set; } = DifficultyLevel.Medium; // AI difficulty setting

    // Game statistics for analytics
    public TimeSpan GameDuration => Status == GameStatus.Finished ?
        (DateTime.UtcNow - CreatedAtUtc) : TimeSpan.Zero;

    public bool IsGameActive => Status == GameStatus.InProgress;
    public bool HasWinner => !string.IsNullOrEmpty(Winner); // True if there's a winner, winner is not null
    public bool IsDraw => Status == GameStatus.Finished && string.IsNullOrEmpty(Winner);

    /// <summary>
    /// Gets the player whose turn it is currently
    /// </summary>
    /// <returns></returns>
    public Player? GetCurrentPlayer()
    {
        return Players.FirstOrDefault(p => p?.Mark.ToString() == CurrentTurn);
    }

    /// <summary>
    /// Gets the opponent player based on the provided connection ID
    /// Algorithm: O(1) array scan (max 2 elements)
    /// </summary>
    /// <param name="connectionId"></param>
    /// <returns></returns>
    public Player? GetOpponentPlayer(string connectionId)
    {
        return Players.FirstOrDefault(p => p?.ConnectionId != connectionId && p != null);
    }

    /// <summary>
    /// Validates if a move is within bounds and the cell is unoccupied
    /// Algorithm: O(1) boundary and occupancy check
    /// </summary>
    /// <param name="row"></param>
    /// <param name="col"></param>
    /// <returns></returns>
    public bool IsValidMove(int row, int col)
    {
        // Simple 3x3 bounds check and cell availability
        // '\0' indicates an empty cell
        return row >= 0 && row < 3 && col >= 0 && col < 3 && Board[row, col] == '\0';
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
    public string? UserId { get; set; } // Optional persistent user ID
    public string ConnectionId { get; set; } = string.Empty; // SignalR connection ID
    public string Name { get; set; } = "Guest"; // Display name
    public DateTime JoinedAtUtc { get; set; } = DateTime.UtcNow;
    public int MovesPlayed { get; set; } = 0; // How many moves the player has made
    public char Mark { get; set; } // 'X' or 'O', assigned at join time
    public TimeSpan TotalThinkTime { get; set; } = TimeSpan.Zero; // Cumulative time taken for all moves

    /// <summary>
    /// Indicates if the player is an AI
    /// </summary>
    public bool IsAI =>
        (string.IsNullOrEmpty(ConnectionId) && UserId == string.IsNullOrEmpty(UserId)) || (ConnectionId == "AI");

    /// <summary>
    /// Average time taken per move for analytics
    /// </summary>
    public TimeSpan AverageThinkTime =>
        MovesPlayed == 0 ? TimeSpan.Zero : TimeSpan.FromTicks(TotalThinkTime.Ticks / MovesPlayed);

    public override string ToString()
    {
        return $"{UserId} | {Name} - ({ConnectionId})";
    }
}

public enum GameStatus
{
    WaitingForPlayers,  // Room created, waiting for players to join
    InProgress,         // Game actively being played
    Finished,           // Game completed (win/draw)
    Abandoned,         // Players disconnected or left, marked for cleanup
    Paused,            // Game temporarily paused
    Error              // An error occurred
}

/// <summary>
/// Statistics model for room monitoring and analytics
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

public class GameMove
{
    public int Row { get; set; }
    public int Column { get; set; }
    public char Mark { get; set; } // 'X' or 'O'
    public string PlayerId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public TimeSpan ThinkTime { get; set; } // Time taken to make the move
    public bool IsAIMove { get; set; } // True if the move was made by AI

    public override string ToString()
    {
        return $"{PlayerId} placed {Mark} at ({Row}, {Column}) after {ThinkTime.TotalSeconds} s";
    }
}
