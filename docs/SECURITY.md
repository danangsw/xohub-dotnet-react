# Security: Practices & Vulnerabilities

This document outlines the security measures, authentication mechanisms, and vulnerability mitigation strategies implemented in the xohub-dotnet-react project.

## Authentication

### JWT (RS256)

- **Tokens signed** with rotating RSA keys
- **Expire** in 1 hour
- **Stored** in localStorage

### JWKS Endpoint

- **Public keys** exposed at `/.well-known/jwks.json`
- **Supports** seamless key rollover

### SignalR Auth

- **JWT passed** via query string
- **Validated** on `OnConnectedAsync`

## Key Management

### KeyManager

- **Generates** new RSA key pair hourly
- **Stores** private keys securely in Docker volume
- **Prunes** expired keys after 2 hours

## HTTPS

- **All traffic** encrypted
- **Dev certs** or self-signed certs used in Docker
- **Prevents** MITM and downgrade attacks

## Input Validation

- **All moves** validated server-side
- **Prevents** illegal board states and cheating
- **Sanitizes** inputs to avoid injection

## Pruning Strategy

- **RoomPruner** removes stale rooms every 5 minutes
- **Cleans** expired keys
- **Logs** pruning events

## Logging & Audit

- **Structured logging** via `ILogger`
- **Logs** room lifecycle, JWT issuance, pruning
- **No sensitive data** in logs

## Vulnerability Mitigation

| Risk | Mitigation |
|------|------------|
| Long-lived tokens | Hourly key rotation, 1-hour expiry |
| Key leakage | Secure volume storage, pruning |
| Replay attacks | Short token lifespan, HTTPS enforced |
| Unauthorized access | JWT validation on every request |
| SignalR abuse | Token validation on connect |
| Memory bloat | RoomPruner cleans inactive rooms |

## Security Best Practices

### Token Handling

- Tokens are validated on every SignalR connection
- Automatic token refresh implemented in frontend
- Secure storage in browser localStorage

### Key Rotation

- RSA keys rotate every hour automatically
- Old keys are kept for 2 hours to handle token overlap
- JWKS endpoint provides current public keys

### Network Security

- All communications encrypted via HTTPS
- SignalR connections use secure WebSocket (WSS)
- CORS policies configured for production

### Data Protection

- No sensitive user data stored in logs
- Input sanitization prevents injection attacks
- Server-side validation for all game moves
