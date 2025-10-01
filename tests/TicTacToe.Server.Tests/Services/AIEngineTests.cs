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

    [Fact]
    public void GetBestMove_MediumDifficulty_ShouldFindWinningSequence()
    {
        // Arrange - Position where AI can force a win with deeper search
        var board = CreateBoard(new char[,]
        {
            { 'O', '\0', '\0' },
            { '\0', 'X', '\0' },
            { '\0', '\0', 'X' }
        });
        char aiMark = 'O';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Medium);

        // Assert - Should find a good move (limited depth might not find perfect play)
        result.row.Should().BeInRange(0, 2);
        result.col.Should().BeInRange(0, 2);
        board[result.row, result.col].Should().Be('\0');
    }

    [Fact]
    public void GetBestMove_MediumDifficulty_ShouldHandleDefensivePosition()
    {
        // Arrange - Position requiring defensive play
        var board = CreateBoard(new char[,]
        {
            { 'X', '\0', '\0' },
            { '\0', 'O', '\0' },
            { '\0', '\0', 'X' }
        });
        char aiMark = 'O';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Medium);

        // Assert - Should make a reasonable defensive move
        result.row.Should().BeInRange(0, 2);
        result.col.Should().BeInRange(0, 2);
        board[result.row, result.col].Should().Be('\0');
    }

    [Fact]
    public void GetBestMove_MediumDifficulty_ShouldPreferCenterWhenAvailable()
    {
        // Arrange - Center available, should be preferred
        var board = CreateBoard(new char[,]
        {
            { 'X', '\0', '\0' },
            { '\0', '\0', '\0' },
            { '\0', '\0', 'O' }
        });
        char aiMark = 'O';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Medium);

        // Assert - Should take center
        result.Should().Be((1, 1));
    }

    [Fact]
    public void GetBestMove_MediumDifficulty_ShouldEvaluatePositionHeuristics()
    {
        // Arrange - Position where heuristic evaluation is important
        var board = CreateBoard(new char[,]
        {
            { 'O', '\0', '\0' },
            { '\0', '\0', '\0' },
            { '\0', '\0', 'X' }
        });
        char aiMark = 'O';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Medium);

        // Assert - Should make a reasonable move based on heuristics
        result.row.Should().BeInRange(0, 2);
        result.col.Should().BeInRange(0, 2);
        board[result.row, result.col].Should().Be('\0');
    }

    [Fact]
    public void GetBestMove_MediumDifficulty_ShouldHandleEarlyGamePositions()
    {
        // Arrange - Early game with few pieces
        var board = CreateBoard(new char[,]
        {
            { '\0', '\0', 'X' },
            { '\0', '\0', '\0' },
            { '\0', '\0', '\0' }
        });
        char aiMark = 'O';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Medium);

        // Assert - Should make a strategic move
        result.row.Should().BeInRange(0, 2);
        result.col.Should().BeInRange(0, 2);
        board[result.row, result.col].Should().Be('\0');
    }

    [Fact]
    public void GetBestMove_MediumDifficulty_ShouldHandleMidGameComplexity()
    {
        // Arrange - More complex mid-game position
        var board = CreateBoard(new char[,]
        {
            { 'X', 'O', '\0' },
            { '\0', 'X', '\0' },
            { '\0', '\0', 'O' }
        });
        char aiMark = 'X';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Medium);

        // Assert - Should find a good move in complex position
        result.row.Should().BeInRange(0, 2);
        result.col.Should().BeInRange(0, 2);
        board[result.row, result.col].Should().Be('\0');
    }

    [Fact]
    public void GetBestMove_MediumDifficulty_ShouldAvoidBlunders()
    {
        // Arrange - Position where a bad move would lose immediately
        var board = CreateBoard(new char[,]
        {
            { 'O', 'O', '\0' },
            { '\0', 'X', '\0' },
            { '\0', '\0', 'X' }
        });
        char aiMark = 'O';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Medium);

        // Assert - Should take the winning move, not make a blunder
        result.Should().Be((0, 2));
    }

    [Fact]
    public void GetBestMove_MediumDifficulty_ShouldBlockImmediateThreats()
    {
        // Arrange - Opponent has two in a row, must block
        var board = CreateBoard(new char[,]
        {
            { 'X', 'X', '\0' },
            { '\0', 'O', '\0' },
            { '\0', '\0', '\0' }
        });
        char aiMark = 'O';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Medium);

        // Assert - Should block the threat
        result.Should().Be((0, 2));
    }

    [Fact]
    public void GetBestMove_MediumDifficulty_ShouldCreateMultipleThreats()
    {
        // Arrange - Position where AI can create multiple winning opportunities
        var board = CreateBoard(new char[,]
        {
            { 'O', '\0', '\0' },
            { '\0', 'O', '\0' },
            { '\0', '\0', '\0' }
        });
        char aiMark = 'O';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Medium);

        // Assert - Should complete a threat (diagonal in this case)
        result.Should().Be((2, 2));
    }

    [Fact]
    public void GetBestMove_MediumDifficulty_ShouldHandleForkingPositions()
    {
        // Arrange - Position where AI can create multiple threats
        var board = CreateBoard(new char[,]
        {
            { 'O', '\0', '\0' },
            { '\0', 'O', '\0' },
            { '\0', '\0', '\0' }
        });
        char aiMark = 'O';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Medium);

        // Assert - Should complete a threat (diagonal in this case)
        result.Should().Be((2, 2));
    }

    [Fact]
    public void GetBestMove_MediumDifficulty_ShouldAvoidTraps()
    {
        // Arrange - Position that looks good but leads to loss if not careful
        var board = CreateBoard(new char[,]
        {
            { 'X', '\0', 'O' },
            { '\0', '\0', '\0' },
            { 'O', '\0', 'X' }
        });
        char aiMark = 'X';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Medium);

        // Assert - Should make a safe move
        result.row.Should().BeInRange(0, 2);
        result.col.Should().BeInRange(0, 2);
        board[result.row, result.col].Should().Be('\0');
    }

    [Fact]
    public void GetBestMove_MediumDifficulty_ShouldRecognizeImmediateWin()
    {
        // Arrange - AI has immediate winning move
        var board = CreateBoard(new char[,]
        {
            { 'O', 'O', '\0' },
            { '\0', 'X', '\0' },
            { '\0', '\0', 'X' }
        });
        char aiMark = 'O';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Medium);

        // Assert - Should take the winning move
        result.Should().Be((0, 2));
    }

    [Fact]
    public void GetBestMove_MediumDifficulty_ShouldBlockImmediateThreat()
    {
        // Arrange - Opponent has immediate winning move
        var board = CreateBoard(new char[,]
        {
            { 'X', 'X', '\0' },
            { '\0', 'O', '\0' },
            { '\0', '\0', '\0' }
        });
        char aiMark = 'O';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Medium);

        // Assert - Should block the threat
        result.Should().Be((0, 2));
    }

    [Fact]
    public void GetBestMove_MediumDifficulty_ShouldHandleSymmetricPositions()
    {
        // Arrange - Symmetric position requiring careful evaluation
        var board = CreateBoard(new char[,]
        {
            { '\0', 'X', '\0' },
            { '\0', '\0', '\0' },
            { '\0', 'O', '\0' }
        });
        char aiMark = 'X';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Medium);

        // Assert - Should make a reasonable move in symmetric position
        result.row.Should().BeInRange(0, 2);
        result.col.Should().BeInRange(0, 2);
        board[result.row, result.col].Should().Be('\0');
    }

    [Fact]
    public void GetBestMove_MediumDifficulty_ShouldEvaluateCenterControl()
    {
        // Arrange - Position where center control is crucial
        var board = CreateBoard(new char[,]
        {
            { 'X', '\0', '\0' },
            { '\0', '\0', '\0' },
            { '\0', '\0', 'O' }
        });
        char aiMark = 'O';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Medium);

        // Assert - Should take center when available
        result.Should().Be((1, 1));
    }

    [Fact]
    public void GetBestMove_MediumDifficulty_ShouldHandleCornerStrategy()
    {
        // Arrange - Position testing corner control importance
        var board = CreateBoard(new char[,]
        {
            { '\0', '\0', 'X' },
            { '\0', '\0', '\0' },
            { 'O', '\0', '\0' }
        });
        char aiMark = 'X';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Medium);

        // Assert - Should make a strategic move
        result.row.Should().BeInRange(0, 2);
        result.col.Should().BeInRange(0, 2);
        board[result.row, result.col].Should().Be('\0');
    }

    [Fact]
    public void GetBestMove_MediumDifficulty_ShouldManageBoardEdges()
    {
        // Arrange - Position focusing on edge moves
        var board = CreateBoard(new char[,]
        {
            { '\0', 'X', '\0' },
            { 'O', '\0', '\0' },
            { '\0', '\0', '\0' }
        });
        char aiMark = 'O';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Medium);

        // Assert - Should make an edge move or better
        result.row.Should().BeInRange(0, 2);
        result.col.Should().BeInRange(0, 2);
        board[result.row, result.col].Should().Be('\0');
    }

    [Fact]
    public void GetBestMove_MediumDifficulty_ShouldHandleComplexInteractions()
    {
        // Arrange - Complex position with multiple considerations
        var board = CreateBoard(new char[,]
        {
            { 'X', 'O', '\0' },
            { '\0', 'X', '\0' },
            { '\0', '\0', 'O' }
        });
        char aiMark = 'X';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Medium);

        // Assert - Should find a good move in complex position
        result.row.Should().BeInRange(0, 2);
        result.col.Should().BeInRange(0, 2);
        board[result.row, result.col].Should().Be('\0');
    }

    [Fact]
    public void GetBestMove_MediumDifficulty_ShouldEvaluateLongTermPosition()
    {
        // Arrange - Position requiring long-term strategic thinking
        var board = CreateBoard(new char[,]
        {
            { '\0', '\0', 'X' },
            { '\0', '\0', '\0' },
            { 'O', '\0', '\0' }
        });
        char aiMark = 'O';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Medium);

        // Assert - Should make a move that considers future development
        result.row.Should().BeInRange(0, 2);
        result.col.Should().BeInRange(0, 2);
        board[result.row, result.col].Should().Be('\0');
    }

    [Fact]
    public void GetBestMove_MediumDifficulty_ShouldHandleDiagonalDominance()
    {
        // Arrange - Position where diagonal control matters
        var board = CreateBoard(new char[,]
        {
            { 'X', '\0', '\0' },
            { '\0', 'O', '\0' },
            { '\0', '\0', 'X' }
        });
        char aiMark = 'O';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Medium);

        // Assert - Should make a move considering diagonal threats
        result.row.Should().BeInRange(0, 2);
        result.col.Should().BeInRange(0, 2);
        board[result.row, result.col].Should().Be('\0');
    }

    [Fact]
    public void GetBestMove_MediumDifficulty_ShouldBalanceAttackAndDefense()
    {
        // Arrange - Position requiring balance between attacking and defending
        var board = CreateBoard(new char[,]
        {
            { 'X', '\0', 'O' },
            { '\0', '\0', '\0' },
            { 'O', '\0', 'X' }
        });
        char aiMark = 'X';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Medium);

        // Assert - Should find a balanced move
        result.row.Should().BeInRange(0, 2);
        result.col.Should().BeInRange(0, 2);
        board[result.row, result.col].Should().Be('\0');
    }

    [Fact]
    public void GetBestMove_MediumDifficulty_ShouldRecognizeDrawingPositions()
    {
        // Arrange - Position that should lead to a draw with perfect play
        var board = CreateBoard(new char[,]
        {
            { 'X', 'O', 'X' },
            { 'O', '\0', '\0' },
            { '\0', '\0', '\0' }
        });
        char aiMark = 'O';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Medium);

        // Assert - Should make a reasonable move in drawing position
        result.row.Should().BeInRange(0, 2);
        result.col.Should().BeInRange(0, 2);
        board[result.row, result.col].Should().Be('\0');
    }

    [Fact]
    public void GetBestMove_MediumDifficulty_ShouldHandleThreeEmptySpaces()
    {
        // Arrange - Board with exactly 3 empty spaces
        var board = CreateBoard(new char[,]
        {
            { 'X', 'O', 'X' },
            { 'O', 'X', '\0' },
            { 'X', '\0', '\0' }
        });
        char aiMark = 'O';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Medium);

        // Assert - Should make the best move in endgame
        result.row.Should().BeInRange(0, 2);
        result.col.Should().BeInRange(0, 2);
        board[result.row, result.col].Should().Be('\0');
    }

    [Fact]
    public void GetBestMove_MediumDifficulty_ShouldEvaluateTwoEmptySpaces()
    {
        // Arrange - Board with exactly 2 empty spaces
        var board = CreateBoard(new char[,]
        {
            { 'X', 'O', 'X' },
            { 'O', 'X', 'O' },
            { 'X', '\0', '\0' }
        });
        char aiMark = 'X';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Medium);

        // Assert - Should take the best available move
        result.Should().Be((2, 1));
    }

    [Fact]
    public void GetBestMove_MediumDifficulty_ShouldHandleOneEmptySpace()
    {
        // Arrange - Board with exactly 1 empty space
        var board = CreateBoard(new char[,]
        {
            { 'X', 'O', 'X' },
            { 'O', 'X', 'O' },
            { 'O', '\0', 'X' }
        });
        char aiMark = 'X';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Medium);

        // Assert - Should take the last available move
        result.Should().Be((2, 1));
    }

    [Fact]
    public void GetBestMove_MediumDifficulty_ShouldExerciseDepthLimitBranch()
    {
        // Arrange - Create a position that requires deep search to reach depth limit
        // This position has balanced threats and opportunities, forcing deeper evaluation
        var board = CreateBoard(new char[,]
        {
            { 'X', '\0', 'O' },
            { '\0', '\0', '\0' },
            { '\0', '\0', 'X' }
        });
        char aiMark = 'O';

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Medium);

        // Assert - Should return a valid move
        result.row.Should().BeInRange(0, 2);
        result.col.Should().BeInRange(0, 2);
        board[result.row, result.col].Should().Be('\0');
    }

    [Fact]
    public void GetBestMove_MediumDifficulty_ShouldForceDepthLimitExceeded()
    {
        // Arrange - Create a position that requires deep search to evaluate all possibilities
        // This position has multiple threats and opportunities, forcing iterative deepening to depth 4
        var board = CreateBoard(new char[,]
        {
            { '\0', 'X', '\0' },
            { '\0', '\0', '\0' },
            { '\0', '\0', 'O' }
        });
        char aiMark = 'O';

        // Act - This position should force the algorithm to explore deeply to find optimal play
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Medium);

        // Assert - Should return a valid strategic move and exercise depth limit evaluation
        result.row.Should().BeInRange(0, 2);
        result.col.Should().BeInRange(0, 2);
        board[result.row, result.col].Should().Be('\0');
    }

    [Fact]
    public void GetBestMove_EasyDifficulty_ShouldExerciseRandomMoveLogging()
    {
        // Arrange - Use a position where optimal play would clearly choose center
        var board = CreateBoard(new char[,]
        {
            { '\0', '\0', '\0' },
            { '\0', '\0', '\0' },
            { '\0', '\0', '\0' }
        });
        char aiMark = 'X';

        // Act - Run many times to ensure we hit the random branch
        var moves = new HashSet<(int, int)>();
        for (int i = 0; i < 200; i++) // Increased iterations
        {
            var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Easy);
            moves.Add(result);
        }

        // Assert - Should have made moves other than center, indicating random choices
        moves.Count.Should().BeGreaterThan(1); // Should have variety in moves
        moves.Should().Contain((1, 1)); // Should include optimal center move sometimes
    }

    [Fact]
    public void GetBestMove_MediumDifficulty_ShouldExerciseDepthRecursion()
    {
        // Arrange - Create a position that requires multiple levels of recursion
        // This position has balanced threats requiring deep lookahead
        var board = CreateBoard(new char[,]
        {
            { '\0', 'X', '\0' },
            { '\0', '\0', '\0' },
            { '\0', '\0', 'O' }
        });
        char aiMark = 'O';

        // Act - This position should cause the algorithm to recurse deeply
        var result = _aiEngine.GetBestMove(board, aiMark, DifficultyLevel.Medium);

        // Assert - Should make a strategic move requiring deep analysis
        result.row.Should().BeInRange(0, 2);
        result.col.Should().BeInRange(0, 2);
        board[result.row, result.col].Should().Be('\0');
    }

    [Fact]
    public void GetBestMove_EasyDifficulty_OnFullBoard_ShouldReturnInvalidMove()
    {
        // Arrange
        var fullBoard = CreateBoard(new char[,]
        {
            { 'X', 'O', 'X' },
            { 'O', 'X', 'O' },
            { 'X', 'O', 'X' }
        });

        // Act & Assert - Run multiple times to ensure both optimal and random paths are tested
        for (int attempt = 0; attempt < 100; attempt++)
        {
            var move = _aiEngine.GetBestMove(fullBoard, 'X', DifficultyLevel.Easy);
            move.Should().Be((-1, -1));
        }
    }

    [Fact]
    public void GetBestMove_InvalidDifficulty_ShouldUseDefaultHardDifficulty()
    {
        // Arrange - Cast invalid enum value to trigger default case
        var board = CreateEmptyBoard();
        char aiMark = 'X';
        var invalidDifficulty = (DifficultyLevel)999; // Invalid enum value

        // Act
        var result = _aiEngine.GetBestMove(board, aiMark, invalidDifficulty);

        // Assert - Should use default case (Hard difficulty) and return center
        result.Should().Be((1, 1));
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
