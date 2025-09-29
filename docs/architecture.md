# Architecture Overview: xohub-dotnet-react

This document provides a detailed technical breakdown of the architecture and design decisions behind the **xohub-dotnet-react** project. It is intended for contributors, maintainers, and learners who want to understand the system internals and extend or audit the codebase.

## Backend Architecture (ASP.NET Core 8.0 + SignalR)

### System Architecture Overview

```text
Client (React + Vite) <--> SignalR WebSocket <--> ASP.NET Core Backend
            ↓                                  ↓
JWT Auth, JWKS Validation        RoomManager, AIEngine, KeyManager  
```

```text
+-------------------------------------------------------------+
|                         Client (Browser)                    |
|-------------------------------------------------------------|
|  React + TypeScript + Vite                                  |
|  - RoomUI.tsx (Game UI)                                     |
|  - SignalRClient.ts (Real-time connection)                  |
|  - Auth.tsx / useAuth.ts (JWT login & storage)              |
|  - Localization.ts (Bahasa/English toggle)                  |
|  - AI Mode Toggle                                           |
+-------------------------------------------------------------+
                |                     ↑
                | SignalR (WebSocket) |
                ↓                     |
+-------------------------------------------------------------+
|                    Backend (ASP.NET Core 8.0)               |
|-------------------------------------------------------------|
|  TicTacToeHub (SignalR Hub)                                 |
|  - JoinRoom, MakeMove, LeaveRoom                            |
|  - Broadcasts: MoveMade, PlayerJoined, PlayerLeft           |
|                                                             |
|  RoomManager (Singleton)                                    |
|  - Room lifecycle, move validation, turn switching          |
|                                                             |
|  AIEngine                                                   |
|  - Minimax algorithm for AI moves                           |
|                                                             |
|  KeyManager                                                 |
|  - RSA key rotation (hourly)                                |
|  - JWKS endpoint: /.well-known/jwks.json                    |
|                                                             |
|  RoomPruner (BackgroundService)                             |
|  - Cleans inactive rooms and expired keys                   |
+-------------------------------------------------------------+
                ↑                     ↓
                | REST API (Login, JWKS)
                ↓
+-------------------------------------------------------------+
|                    Security & Auth                          |
|-------------------------------------------------------------|
|  JWT Issuance (on login)                                    |
|  - Signed with rotating RSA keys                            |
|                                                             |
|  JWKS Validation                                            |
|  - Frontend fetches public keys for token verification      |
|                                                             |
|  HTTPS (Dev Certs / Self-Signed)                            |
+-------------------------------------------------------------+
                ↓
+-------------------------------------------------------------+
|                    Deployment Layer                         |
|-------------------------------------------------------------|
|  Docker Compose                                             |
|  - Backend: ASP.NET Core container (port 5000)              |
|  - Frontend: Nginx static server (port 3000)                |
|  - Volumes: Persist JWT keys                                |
+-------------------------------------------------------------+
```

### Core Components

- **TicTacToeHub**: SignalR hub that manages real-time communication between clients.

  - Handles `JoinRoom`, `LeaveRoom`, `MakeMove` methods.
  - Broadcasts `MoveMade`, `PlayerJoined`, `PlayerLeft` events.
- **RoomManager** (Singleton Service):
  - Maintains in-memory dictionary of active rooms.
  - Handles room creation, player assignment, move validation, and turn switching.
  - Tracks `lastActivityUtc` for pruning.
- **PlayerManager** (Optional):
  - Validates player identity via JWT.
  - Assigns marks (X/O) and connection IDs.
- **AIEngine**:
  - Implements Minimax algorithm for AI move generation.
  - Triggered when playing in `VsComputer` mode.
- **KeyManager**:
  - Generates RSA key pairs hourly.
  - Stores private keys securely.
  - Exposes public keys via JWKS endpoint.
- **RoomPruner** (BackgroundService):
  - Runs every 5 minutes.
  - Removes rooms inactive for more than 30 minutes.
  - Cleans up expired JWT keys.
- **JWKS Endpoint**:
  - Exposes public keys at `/.well-known/jwks.json`.
  - Used by frontend and external validators.

## Backend Design Principles

- **Modularity**: Each service (RoomManager, KeyManager, etc.) is isolated and testable.
- **Stateless JWT Auth**: No session storage; all auth is via signed tokens.
- **In-Memory Game State**: Rooms and boards are stored in memory for speed.
- **Pruning Strategy**: Ensures memory is not bloated by stale rooms.
- **Secure Key Rotation**: Prevents long-lived JWT vulnerabilities.

## Frontend Architecture (React + TypeScript + Vite)

### Core Modules

- **SignalRClient.ts**:
  - Wraps SignalR connection logic.
  - Handles reconnection and event binding.
- **RoomUI.tsx**:
  - Displays game board and player info.
  - Handles move clicks and turn indicators.
- **Auth.tsx / useAuth.ts**:
  - Manages login flow and JWT storage.
  - Refreshes token if expired.
- **AI Toggle & Mode Selector**:
  - Allows switching between multiplayer and AI mode.
- **Localization.ts**:
  - Provides Bahasa Indonesia and English translations.
  - Uses context provider for language switching.

## Frontend Design Principles

- **Reactive State**: Uses React hooks and context for state management.
- **Resilient Connection**: Auto-reconnects SignalR if dropped.
- **Accessible UI**: Large buttons, high contrast, keyboard navigation.
- **Localized UX**: All strings translatable via JSON dictionaries.
- **Secure Storage**: JWT stored in `localStorage`, never exposed in logs.

## Authentication & Security

- **JWT Issuance**:
  - Backend issues JWT on login.
  - Tokens signed with hourly-rotated RSA keys.
- **JWKS Validation**:
  - Frontend fetches `/.well-known/jwks.json` to validate tokens.
  - Supports key rollover without downtime.
- **HTTPS**:
  - All traffic encrypted.
  - Dev certs or self-signed certs used in Docker.

## Testing & Observability

Testing Approach (Test-Driven Development - TDD)
The project follows a comprehensive TDD approach with extensive unit, integration, and concurrency testing to ensure reliability, especially for real-time multiplayer functionality.

### Unit Tests

- **Models**: 100% coverage for `GameRoom`, `Player`, `GameStatus`, `RoomStatistics`
  - Constructor initialization, property validation, computed properties
  - Edge cases: invalid moves, win conditions, draw detection
- **Services**: 90%+ coverage for `RoomManager`, `AIEngine`, `KeyManager`
  - Room lifecycle: creation, joining, leaving, removal
  - Move validation: bounds checking, turn validation, win detection
  - AI algorithm: minimax correctness, difficulty levels, optimal moves
  - Key management: RSA generation, rotation, JWKS exposure
- **Frameworks**: xUnit for test execution, Moq for mocking, FluentAssertions for readable assertions

### Integration Tests

- **SignalR Flow**: Full game lifecycle from room creation to completion
- **JWT Validation**: Token issuance, validation, and key rotation
- **JWKS Endpoint**: Public key exposure and client consumption
- **AI Gameplay**: Human vs AI interactions with move validation
- **Multiplayer Scenarios**: Concurrent player joins and moves

### Concurrency Tests

- **Thread Safety**: RoomManager operations under concurrent access
- **Race Conditions**: Simultaneous room creation, player joins, and moves
- **Performance**: Memory usage and cleanup under load

### Test Execution

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run specific test class
dotnet test --filter "RoomManagerTests"

# Run in watch mode (TDD development)
dotnet watch test

# Run Quick Coverage Check
dotnet test --collect:"XPlat Code Coverage"

# Run Full Coverage with HTML Report
# Run tests and collect coverage
dotnet test --collect:"XPlat Code Coverage"

# Generate HTML report
reportgenerator -reports:"TestResults/*/coverage.cobertura.xml" -targetdir:"TestResults/CoverageReport" -reporttypes:Html

```

### CI/CD Integration

- **GitHub Actions**: Automated test execution on push/PR
- **Coverage Reports**: Codecov integration for coverage tracking
- **Quality Gates**: Minimum coverage thresholds (90% services, 100% models)

### Observability

- **Structured Logging**: Via `ILogger` with correlation IDs
- **Metrics**: Room count, active games, AI performance
- **Health Check**s: Endpoint for service availability
- **Error Tracking**: Global exception middleware with detailed logging
- **Performance Monitoring**: Slow operation detection and optimization

## Deployment Architecture

- **Dockerized Services**:
  - **Backend**: ASP.NET Core image, port 5000
  - **Frontend**: Node build + Nginx static server, port 3000
- **Orchestration**:
  - `docker-compose.yml` defines services, volumes, and networks.
- **Volumes**:
  - JWT keys stored in mounted volume for persistence
- **HTTPS**:
  - Enabled via dev certs

## Industry Best Practices Included

- **Singleton pattern** for room management
- **Background services** for cleanup tasks
- **JWT with key rotation** for security
- **Structured logging** without sensitive data
- **Docker multi-stage builds** for optimization
- **Health checks** and monitoring
- **Backup strategies** for data persistence
- **Global exception** handling middleware
- **Auto-reconnection** for SignalR clients
- **Comprehensive TDD** with high test coverage

## References

- [SignalR Docs](https://learn.microsoft.com/en-us/aspnet/core/signalr/introduction)
- [JWT & JWKS](https://auth0.com/docs/secure/tokens/json-web-tokens/json-web-key-sets)
- [Minimax Algorithm](https://en.wikipedia.org/wiki/Minimax)
- [Docker Compose](https://docs.docker.com/compose/)
- [xUnit Testing](https://xunit.net/)
- [FluentAssertions](https://fluentassertions.com/)
