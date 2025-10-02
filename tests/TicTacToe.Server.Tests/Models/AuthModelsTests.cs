using System.ComponentModel.DataAnnotations;
using XoHub.Server.Models;
using Xunit;

namespace TicTacToe.Server.Tests.Models;

public class AuthModelsTests
{
    #region LoginRequest Tests

    [Fact]
    public void LoginRequest_DefaultConstructor_SetsDefaultValues()
    {
        // Act
        var request = new LoginRequest();

        // Assert
        Assert.Equal(string.Empty, request.UserName);
        Assert.Equal(string.Empty, request.Password);
    }

    [Fact]
    public void LoginRequest_PropertyGettersAndSetters_WorkCorrectly()
    {
        // Arrange
        var request = new LoginRequest();
        var expectedUserName = "testuser";
        var expectedPassword = "ValidPass123!";

        // Act
        request.UserName = expectedUserName;
        request.Password = expectedPassword;

        // Assert
        Assert.Equal(expectedUserName, request.UserName);
        Assert.Equal(expectedPassword, request.Password);
    }

    [Fact]
    public void LoginRequest_ObjectInitializer_WorksCorrectly()
    {
        // Act
        var request = new LoginRequest
        {
            UserName = "testuser",
            Password = "ValidPass123!"
        };

        // Assert
        Assert.Equal("testuser", request.UserName);
        Assert.Equal("ValidPass123!", request.Password);
    }

    [Theory]
    [InlineData("ab", false)] // Too short
    [InlineData("valid_user123", true)]
    [InlineData("TestUser", true)]
    [InlineData("user_123", true)]
    [InlineData("user@name", false)] // Invalid character
    [InlineData("user name", false)] // Space not allowed
    [InlineData("user-name", false)] // Dash not allowed
    [InlineData("", false)] // Empty
    [InlineData(null, false)] // Null
    public void LoginRequest_UserName_Validation(string userName, bool shouldBeValid)
    {
        // Arrange
        var request = new LoginRequest { UserName = userName ?? string.Empty, Password = "ValidPass123!" };
        var context = new ValidationContext(request);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(request, context, results, true);

        // Assert
        if (shouldBeValid)
        {
            Assert.True(isValid, $"Expected valid for UserName: '{userName}', but got errors: {string.Join(", ", results.Select(r => r.ErrorMessage))}");
        }
        else
        {
            Assert.False(isValid, $"Expected invalid for UserName: '{userName}', but validation passed");
        }
    }

    [Theory]
    [InlineData("12345", false)] // Too short
    [InlineData("ValidPass123!", true)]
    [InlineData("Complex@Pass123", true)]
    [InlineData("Test$User999", true)]
    [InlineData("", false)] // Empty
    [InlineData(null, false)] // Null
    public void LoginRequest_Password_Validation(string password, bool shouldBeValid)
    {
        // Arrange
        var request = new LoginRequest { UserName = "testuser", Password = password ?? string.Empty };
        var context = new ValidationContext(request);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(request, context, results, true);

        // Assert
        if (shouldBeValid)
        {
            Assert.True(isValid, $"Expected valid for Password: '{password}', but got errors: {string.Join(", ", results.Select(r => r.ErrorMessage))}");
        }
        else
        {
            Assert.False(isValid, $"Expected invalid for Password: '{password}', but validation passed");
        }
    }

    [Fact]
    public void LoginRequest_Validation_WithValidData_Passes()
    {
        // Arrange
        var request = new LoginRequest
        {
            UserName = "testuser",
            Password = "ValidPass123!"
        };
        var context = new ValidationContext(request);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(request, context, results, true);

        // Assert
        Assert.True(isValid);
        Assert.Empty(results);
    }

    [Fact]
    public void LoginRequest_Validation_WithInvalidData_Fails()
    {
        // Arrange
        var request = new LoginRequest
        {
            UserName = "ab", // Too short
            Password = "123" // Too short
        };
        var context = new ValidationContext(request);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(request, context, results, true);

        // Assert
        Assert.False(isValid);
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.MemberNames.Contains("UserName"));
        Assert.Contains(results, r => r.MemberNames.Contains("Password"));
    }

    #endregion

    #region LoginResponse Tests

    [Fact]
    public void LoginResponse_DefaultConstructor_SetsDefaultValues()
    {
        // Act
        var response = new LoginResponse();

        // Assert
        Assert.Equal(string.Empty, response.Token);
        Assert.Equal(string.Empty, response.UserId);
        Assert.Equal(0, response.ExpiresIn);
        Assert.Equal(string.Empty, response.TokenType);
    }

    [Fact]
    public void LoginResponse_PropertyGettersAndSetters_WorkCorrectly()
    {
        // Arrange
        var response = new LoginResponse();
        var expectedToken = "jwt-token-123";
        var expectedUserId = "user-123";
        var expectedExpiresIn = 3600;
        var expectedTokenType = "Bearer";

        // Act
        response.Token = expectedToken;
        response.UserId = expectedUserId;
        response.ExpiresIn = expectedExpiresIn;
        response.TokenType = expectedTokenType;

        // Assert
        Assert.Equal(expectedToken, response.Token);
        Assert.Equal(expectedUserId, response.UserId);
        Assert.Equal(expectedExpiresIn, response.ExpiresIn);
        Assert.Equal(expectedTokenType, response.TokenType);
    }

    [Fact]
    public void LoginResponse_ObjectInitializer_WorksCorrectly()
    {
        // Act
        var response = new LoginResponse
        {
            Token = "jwt-token-123",
            UserId = "user-123",
            ExpiresIn = 3600,
            TokenType = "Bearer"
        };

        // Assert
        Assert.Equal("jwt-token-123", response.Token);
        Assert.Equal("user-123", response.UserId);
        Assert.Equal(3600, response.ExpiresIn);
        Assert.Equal("Bearer", response.TokenType);
    }

    #endregion

    #region UserStatusResponse Tests

    [Fact]
    public void UserStatusResponse_DefaultConstructor_SetsDefaultValues()
    {
        // Act
        var response = new UserStatusResponse();

        // Assert
        Assert.Equal(string.Empty, response.UserId);
        Assert.Equal(string.Empty, response.UserName);
        Assert.False(response.IsAuthenticated);
        Assert.Equal(default(DateTime), response.LastActivity);
    }

    [Fact]
    public void UserStatusResponse_PropertyGettersAndSetters_WorkCorrectly()
    {
        // Arrange
        var response = new UserStatusResponse();
        var expectedUserId = "user-123";
        var expectedUserName = "testuser";
        var expectedIsAuthenticated = true;
        var expectedLastActivity = DateTime.UtcNow;

        // Act
        response.UserId = expectedUserId;
        response.UserName = expectedUserName;
        response.IsAuthenticated = expectedIsAuthenticated;
        response.LastActivity = expectedLastActivity;

        // Assert
        Assert.Equal(expectedUserId, response.UserId);
        Assert.Equal(expectedUserName, response.UserName);
        Assert.Equal(expectedIsAuthenticated, response.IsAuthenticated);
        Assert.Equal(expectedLastActivity, response.LastActivity);
    }

    [Fact]
    public void UserStatusResponse_ObjectInitializer_WorksCorrectly()
    {
        // Arrange
        var expectedLastActivity = DateTime.UtcNow;

        // Act
        var response = new UserStatusResponse
        {
            UserId = "user-123",
            UserName = "testuser",
            IsAuthenticated = true,
            LastActivity = expectedLastActivity
        };

        // Assert
        Assert.Equal("user-123", response.UserId);
        Assert.Equal("testuser", response.UserName);
        Assert.True(response.IsAuthenticated);
        Assert.Equal(expectedLastActivity, response.LastActivity);
    }

    #endregion
}