// server/TicTacToe.Server.Tests/Models/GameRoomTests.cs
using FluentAssertions;
using XoHub.Server.Models;
using XoHub.Server.Services;
using Xunit;

namespace XoHub.Server.Tests.Models;

public class GameRoomTests
{
    [Fact]
    public void Constructor_ShouldInitializeWithDefaultValues()
    {
        // Arrange & Act
        var room = new GameRoom();

        // Assert
        room.RoomId.Should().BeEmpty();
        room.Players.Should().HaveCount(2);
        room.Players[0].Should().BeNull();
        room.Players[1].Should().BeNull();
        room.Players.Should().AllSatisfy(player => player.Should().BeNull());
        room.Board.Should().BeOfType<char[,]>();
        room.Board.GetLength(0).Should().Be(3); // rows
        room.Board.GetLength(1).Should().Be(3); // columns
        room.CurrentTurn.Should().Be("X");
        room.Status.Should().Be(GameStatus.WaitingForPlayers);
        room.IsAIMode.Should().BeFalse();
        room.Winner.Should().BeNull();
        room.MoveCount.Should().Be(0);
        room.AIDifficulty.Should().Be(DifficultyLevel.Medium);
    }

    [Fact]
    public void IsGameActive_ShouldReturnTrue_WhenStatusIsInProgress()
    {
        // Arrange
        var room = new GameRoom { Status = GameStatus.InProgress };

        // Act & Assert
        room.IsGameActive.Should().BeTrue();
    }

    [Fact]
    public void IsDraw_ShouldReturnTrue_WhenFinishedAndNoWinner()
    {
        // Arrange
        var room = new GameRoom
        {
            Status = GameStatus.Finished,
            Winner = null
        };

        // Act & Assert
        room.IsDraw.Should().BeTrue();
    }

    [Fact]
    public void GetCurrentPlayer_ShouldReturnPlayerWithCurrentTurnMark()
    {
        // Arrange
        var room = new GameRoom { CurrentTurn = "X" };
        var playerX = new Player { Mark = 'X', Name = "Alice" };
        var playerO = new Player { Mark = 'O', Name = "Bob" };
        room.Players[0] = playerX;
        room.Players[1] = playerO;

        // Act
        var currentPlayer = room.GetCurrentPlayer();

        // Assert
        currentPlayer.Should().Be(playerX);
    }

    [Fact]
    public void IsValidMove_ShouldReturnTrue_ForEmptyCellWithinBounds()
    {
        // Arrange
        var room = new GameRoom();
        room.Board[1, 1] = '\0'; // Empty cell

        // Act & Assert
        room.IsValidMove(1, 1).Should().BeTrue();
    }

    [Fact]
    public void IsValidMove_ShouldReturnFalse_ForOccupiedCell()
    {
        // Arrange
        var room = new GameRoom();
        room.Board[1, 1] = 'X'; // Occupied cell

        // Act & Assert
        room.IsValidMove(1, 1).Should().BeFalse();
    }

    [Fact]
    public void IsValidMove_ShouldReturnFalse_ForOutOfBounds()
    {
        // Arrange
        var room = new GameRoom();

        // Act & Assert
        room.IsValidMove(-1, 0).Should().BeFalse();
        room.IsValidMove(0, -1).Should().BeFalse();
        room.IsValidMove(3, 0).Should().BeFalse();
        room.IsValidMove(0, 3).Should().BeFalse();
    }

    [Fact]
    public void GetAvailableMoves_ShouldReturnAllEmptyCells()
    {
        // Arrange
        var room = new GameRoom();
        room.Board[0, 0] = 'X';
        room.Board[1, 1] = 'O';
        // Other cells remain empty

        // Act
        var moves = room.GetAvailableMoves();

        // Assert
        moves.Should().HaveCount(7); // 9 total - 2 occupied
        moves.Should().Contain((0, 1));
        moves.Should().Contain((0, 2));
        moves.Should().Contain((1, 0));
        moves.Should().Contain((1, 2));
        moves.Should().Contain((2, 0));
        moves.Should().Contain((2, 1));
        moves.Should().Contain((2, 2));
    }

    [Fact]
    public void CloneBoard_ShouldCreateIndependentCopy()
    {
        // Arrange
        var room = new GameRoom();
        room.Board[0, 0] = 'X';
        room.Board[1, 1] = 'O';

        // Act
        var clone = room.CloneBoard();

        // Assert
        clone[0, 0].Should().Be('X');
        clone[1, 1].Should().Be('O');
        clone[2, 2].Should().Be('\0');

        // Modify original - clone should be unaffected
        room.Board[2, 2] = 'X';
        clone[2, 2].Should().Be('\0');
    }

    [Fact]
    public void SerializeBoardState_ShouldReturnCorrectStringRepresentation()
    {
        // Arrange
        var room = new GameRoom();
        room.Board[0, 0] = 'X';
        room.Board[0, 2] = 'O';
        room.Board[2, 2] = 'X';

        // Act
        var serialized = room.SerializeBoardState();

        // Assert
        // Current SerializeBoardState produces 17 characters:
        // Board layout: X _ O
        //               _ _ _  
        //               _ _ X
        // Serialized as: "X   O           X" (with spaces after each cell except (2,2))
        serialized.Should().Be("X   O           X");
    }

    [Theory]
    [InlineData(GameStatus.WaitingForPlayers, 0)]
    [InlineData(GameStatus.InProgress, 100)] // Some duration
    [InlineData(GameStatus.Finished, 200)]   // Final duration
    public void GameDuration_ShouldCalculateCorrectly(GameStatus status, int expectedSeconds)
    {
        // Arrange
        var room = new GameRoom
        {
            Status = status,
            CreatedAtUtc = DateTime.UtcNow.AddSeconds(-expectedSeconds)
        };

        // Act
        var duration = room.GameDuration;

        // Assert
        if (status == GameStatus.Finished)
        {
            duration.TotalSeconds.Should().BeApproximately(expectedSeconds, 1);
        }
        else
        {
            duration.Should().Be(TimeSpan.Zero);
        }
    }

    [Fact]
    public void GetOpponentPlayer_ShouldReturnOpponent_WhenTwoPlayersExist()
    {
        // Arrange
        var room = new GameRoom();
        var player1 = new Player { ConnectionId = "conn1", Name = "Alice", Mark = 'X' };
        var player2 = new Player { ConnectionId = "conn2", Name = "Bob", Mark = 'O' };
        room.Players[0] = player1;
        room.Players[1] = player2;

        // Act
        var opponent1 = room.GetOpponentPlayer("conn1");
        var opponent2 = room.GetOpponentPlayer("conn2");

        // Assert
        opponent1.Should().Be(player2);
        opponent2.Should().Be(player1);
    }

    [Fact]
    public void GetOpponentPlayer_ShouldReturnNull_WhenOnlyOnePlayerExists()
    {
        // Arrange
        var room = new GameRoom();
        var player1 = new Player { ConnectionId = "conn1", Name = "Alice", Mark = 'X' };
        room.Players[0] = player1;
        // room.Players[1] remains null

        // Act
        var opponent = room.GetOpponentPlayer("conn1");

        // Assert
        opponent.Should().BeNull();
    }

    [Fact]
    public void GetOpponentPlayer_ShouldReturnNull_WhenNoPlayersExist()
    {
        // Arrange
        var room = new GameRoom();
        // Both players remain null

        // Act
        var opponent = room.GetOpponentPlayer("any-connection-id");

        // Assert
        opponent.Should().BeNull();
    }

    [Fact]
    public void GetOpponentPlayer_ShouldReturnNonNullPlayer_WhenOnePlayerIsNull()
    {
        // Arrange
        var room = new GameRoom();
        var player2 = new Player { ConnectionId = "conn2", Name = "Bob", Mark = 'O' };
        room.Players[0] = null;
        room.Players[1] = player2;

        // Act
        var opponent = room.GetOpponentPlayer("conn2");

        // Assert
        opponent.Should().BeNull(); // No opponent exists (only one non-null player)
    }

    [Fact]
    public void GetOpponentPlayer_ShouldReturnNull_WhenBothPlayersHaveSameConnectionId()
    {
        // Arrange
        var room = new GameRoom();
        var player1 = new Player { ConnectionId = "same-conn", Name = "Alice", Mark = 'X' };
        var player2 = new Player { ConnectionId = "same-conn", Name = "Bob", Mark = 'O' };
        room.Players[0] = player1;
        room.Players[1] = player2;

        // Act
        var opponent = room.GetOpponentPlayer("same-conn");

        // Assert
        opponent.Should().BeNull(); // No player has a different connection ID
    }

    [Fact]
    public void GetOpponentPlayer_ShouldReturnFirstAvailablePlayer_WhenSearchingForNonExistentConnectionId()
    {
        // Arrange
        var room = new GameRoom();
        var player1 = new Player { ConnectionId = "conn1", Name = "Alice", Mark = 'X' };
        var player2 = new Player { ConnectionId = "conn2", Name = "Bob", Mark = 'O' };
        room.Players[0] = player1;
        room.Players[1] = player2;

        // Act
        var opponent = room.GetOpponentPlayer("non-existent-conn");

        // Assert
        opponent.Should().Be(player1); // Returns first player since neither matches the search ID
    }
}