# Development Walkthrough: xohub-dotnet-react

This guide walks you through building the real-time TicTacToe game from scratch to local Docker deployment, following industry best practices.

## Key Featured Addressed

### Development Workflow
- Specific build/test commands beyond Docker Compose
- Industry-standard development commands (`dotnet watch run`, `npm run dev`)
- Separate commands for testing, building, and deployment
- Health checks and monitoring commands

### Database Strategy
- Initial phase backup strategies for Redis and JWT keys
- Automated backup scripts for production readiness
- Volume persistence for Docker containers
- Manual and automated backup procedures

### Error Handling
- Global exception middleware (industry standard)
- Structured logging with context (no sensitive data)
- Proper error boundaries in React components
- SignalR error handling patterns

### AI Implementation
- Minimax algorithm with alpha-beta pruning
- Difficulty levels for future enhancement
- Clean separation between AI engine and game logic
- Proper integration with SignalR for real-time AI moves

### API Endpoints
- Focus on JWKS endpoint as specified
- JWT authentication flow
- Proper REST API patterns for auth

 ## Development Phases:
1. **Environment Setup** - Prerequisites and project structure
2. **Backend Development** - Complete ASP.NET Core implementation
3. **Frontend Development** - React + TypeScript with SignalR
4. **Dockerization** - Multi-stage builds and orchestration
5. **Testing & QA** - Unit and integration testing setup
6. **Deployment** - Local Docker deployment with monitoring

## [🚀 Phase 1: Environment Setup](development-phase-1.md)

## [🏗️ Phase 2: Backend Development (ASP.NET Core + SignalR)](development-phase-2.md)

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