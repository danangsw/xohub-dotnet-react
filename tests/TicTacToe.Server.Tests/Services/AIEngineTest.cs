using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using XoHub.Server.Services;
using Xunit;

namespace XoHub.Server.Tests.Services;

public class AIEngineTests : IDisposable
{
    private readonly Mock<ILogger<AIEngine>> _loggerMock;
    private readonly AIEngine _aiEngine;

    public AIEngineTests()
    {
        _loggerMock = new Mock<ILogger<AIEngine>>();
        _aiEngine = new AIEngine(_loggerMock.Object);
    }

    public void Dispose()
    {
        // Cleanup if needed
    }

    #region GetBestMove (Default Difficulty) Integration Tests

    [Fact]
    public void GetBestMove_DefaultDifficulty_ShouldCallMediumDifficulty()
    {
        // Arrange
        var board = CreateEmptyBoard();
        char aiMark = 'X';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark);

        // Assert - Should return a valid move (center is optimal for empty board)
        result.Should().Be((1, 1));
    }

    #endregion

    #region GetBestMove with Difficulty Integration Tests

    [Fact]
    public void GetBestMove_EasyDifficulty_ShouldReturnValidMove()
    {
        // Arrange
        var board = CreateEmptyBoard();
        char aiMark = 'X';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Easy);

        // Assert
        result.row.Should().BeInRange(0, 2);
        result.col.Should().BeInRange(0, 2);
        board[result.row, result.col].Should().Be('\0'); // Should be empty
    }

    [Fact]
    public void GetBestMove_MediumDifficulty_ShouldReturnValidMove()
    {
        // Arrange
        var board = CreateEmptyBoard();
        char aiMark = 'X';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Medium);

        // Assert
        result.Should().Be((1, 1)); // Center is optimal
    }

    [Fact]
    public void GetBestMove_HardDifficulty_ShouldReturnValidMove()
    {
        // Arrange
        var board = CreateEmptyBoard();
        char aiMark = 'X';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Hard);

        // Assert
        result.Should().Be((1, 1)); // Center is optimal
    }

    [Fact]
    public void GetBestMove_EasyDifficulty_OnPartiallyFilledBoard_ShouldReturnValidMove()
    {
        // Arrange
        var board = CreateBoard(new char[,]
        {
            { 'X', '\0', '\0' },
            { '\0', '\0', '\0' },
            { '\0', '\0', '\0' }
        });
        char aiMark = 'O';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Easy);

        // Assert
        result.row.Should().BeInRange(0, 2);
        result.col.Should().BeInRange(0, 2);
        board[result.row, result.col].Should().Be('\0');
    }

    [Fact]
    public void GetBestMove_MediumDifficulty_OnPartiallyFilledBoard_ShouldReturnOptimalMove()
    {
        // Arrange - AI can win immediately
        var board = CreateBoard(new char[,]
        {
            { 'O', 'O', '\0' },
            { '\0', '\0', '\0' },
            { '\0', '\0', '\0' }
        });
        char aiMark = 'O';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Medium);

        // Assert - Should take the winning move
        result.Should().Be((0, 2));
    }

    [Fact]
    public void GetBestMove_HardDifficulty_OnPartiallyFilledBoard_ShouldReturnOptimalMove()
    {
        // Arrange - AI can win immediately
        var board = CreateBoard(new char[,]
        {
            { 'O', 'O', '\0' },
            { '\0', '\0', '\0' },
            { '\0', '\0', '\0' }
        });
        char aiMark = 'O';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Hard);

        // Assert - Should take the winning move
        result.Should().Be((0, 2));
    }

    [Fact]
    public void GetBestMove_HardDifficulty_ShouldBlockOpponentWin()
    {
        // Arrange - Opponent has two in a row, AI must block
        var board = CreateBoard(new char[,]
        {
            { 'X', 'X', '\0' },
            { '\0', '\0', '\0' },
            { '\0', '\0', '\0' }
        });
        char aiMark = 'O';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Hard);

        // Assert - Should block the win
        result.Should().Be((0, 2));
    }

    [Fact]
    public void GetBestMove_HardDifficulty_ShouldWinWhenPossible()
    {
        // Arrange - AI has winning opportunity
        var board = CreateBoard(new char[,]
        {
            { 'O', '\0', '\0' },
            { 'O', '\0', '\0' },
            { '\0', '\0', '\0' }
        });
        char aiMark = 'O';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Hard);

        // Assert - Should complete the column
        result.Should().Be((2, 0));
    }

    [Fact]
    public void GetBestMove_HardDifficulty_OnDrawPosition_ShouldReturnValidMove()
    {
        // Arrange - Position leading to draw
        var board = CreateBoard(new char[,]
        {
            { 'X', 'O', 'X' },
            { 'O', '\0', '\0' },
            { '\0', '\0', '\0' }
        });
        char aiMark = 'O';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Hard);

        // Assert - Should return a valid move
        result.row.Should().BeInRange(0, 2);
        result.col.Should().BeInRange(0, 2);
        board[result.row, result.col].Should().Be('\0');
    }

    [Fact]
    public void GetBestMove_HardDifficulty_OnFullBoard_ShouldReturnInvalidMove()
    {
        // Arrange - Full board
        var board = CreateBoard(new char[,]
        {
            { 'X', 'O', 'X' },
            { 'O', 'X', 'O' },
            { 'O', 'X', 'O' }
        });
        char aiMark = 'X';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Hard);

        // Assert - Should return invalid move since no moves available
        result.Should().Be((-1, -1));
    }

    [Fact]
    public void GetBestMove_MediumDifficulty_OnComplexPosition_ShouldReturnReasonableMove()
    {
        // Arrange - Complex position requiring evaluation
        var board = CreateBoard(new char[,]
        {
            { 'X', '\0', 'O' },
            { '\0', 'X', '\0' },
            { '\0', '\0', 'O' }
        });
        char aiMark = 'X';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Medium);

        // Assert - Should return a valid move
        result.row.Should().BeInRange(0, 2);
        result.col.Should().BeInRange(0, 2);
        board[result.row, result.col].Should().Be('\0');
    }

    [Fact]
    public void GetBestMove_EasyDifficulty_ShouldOccasionallyPlayOptimally()
    {
        // Arrange - Position where optimal play is clear
        var board = CreateBoard(new char[,]
        {
            { 'X', 'X', '\0' },
            { '\0', '\0', '\0' },
            { '\0', '\0', '\0' }
        });
        char aiMark = 'O';

        // Act - Run multiple times to check for optimal play
        bool foundOptimal = false;
        for (int i = 0; i < 50; i++) // Run enough times to likely see optimal play
        {
            var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Easy);
            if (result == (0, 2)) // Block the win
            {
                foundOptimal = true;
                break;
            }
        }

        // Assert - Should occasionally play optimally (30% chance)
        foundOptimal.Should().BeTrue();
    }

    [Fact]
    public void GetBestMove_EasyDifficulty_ShouldMakeRandomMoves()
    {
        // Arrange
        var board = CreateBoard(new char[,]
        {
            { '\0', '\0', '\0' },
            { '\0', '\0', '\0' },
            { '\0', '\0', '\0' }
        });
        char aiMark = 'X';

        // Act - Collect moves over multiple calls
        var moves = new HashSet<(int, int)>();
        for (int i = 0; i < 20; i++)
        {
            var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Easy);
            if (result.row != -1 && result.col != -1)
            {
                moves.Add(result);
            }
        }

        // Assert - Should make various moves (randomness)
        moves.Count.Should().BeGreaterThan(1);
    }

    [Fact]
    public void GetBestMove_HardDifficulty_ShouldPreferCenterOnEmptyBoard()
    {
        // Arrange
        var board = CreateEmptyBoard();
        char aiMark = 'X';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Hard);

        // Assert
        result.Should().Be((1, 1));
    }

    [Fact]
    public void GetBestMove_HardDifficulty_ShouldHandleDiagonalWin()
    {
        // Arrange - AI can win on diagonal
        var board = CreateBoard(new char[,]
        {
            { 'O', '\0', '\0' },
            { '\0', 'O', '\0' },
            { '\0', '\0', '\0' }
        });
        char aiMark = 'O';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Hard);

        // Assert - Should complete the diagonal
        result.Should().Be((2, 2));
    }

    [Fact]
    public void GetBestMove_HardDifficulty_ShouldBlockDiagonalThreat()
    {
        // Arrange - Opponent threatening diagonal win
        var board = CreateBoard(new char[,]
        {
            { 'X', '\0', '\0' },
            { '\0', 'X', '\0' },
            { '\0', '\0', '\0' }
        });
        char aiMark = 'O';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Hard);

        // Assert - Should block the diagonal
        result.Should().Be((2, 2));
    }

    [Fact]
    public void GetBestMove_MediumDifficulty_ShouldUseLimitedDepth()
    {
        // Arrange - Position requiring deeper analysis
        var board = CreateBoard(new char[,]
        {
            { 'X', '\0', '\0' },
            { '\0', '\0', '\0' },
            { '\0', '\0', '\0' }
        });
        char aiMark = 'O';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Medium);

        // Assert - Should return center (good move)
        result.Should().Be((1, 1));
    }

    [Fact]
    public void GetBestMove_DefaultDifficulty_ShouldUseMedium()
    {
        // Arrange
        var board = CreateEmptyBoard();
        char aiMark = 'X';

        // Act
        var defaultResult = _aiEngine.GetBestMove(board, aiMark);
        var mediumResult = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Medium);

        // Assert - Should be the same
        defaultResult.Should().Be(mediumResult);
    }

    #endregion

    #region Helper Methods

    private static char[,] CreateEmptyBoard()
    {
        return new char[3, 3]
        {
            { '\0', '\0', '\0' },
            { '\0', '\0', '\0' },
            { '\0', '\0', '\0' }
        };
    }

    private static char[,] CreateBoard(char[,] board)
    {
        var newBoard = new char[3, 3];
        Array.Copy(board, newBoard, board.Length);
        return newBoard;
    }

    #endregion
}
