# TicTacToe API Integration Testing with Postman

This guide explains how to perform comprehensive integration testing of the TicTacToe API using Postman.

## Prerequisites

1. **Postman Installed**: Download from [postman.com](https://www.postman.com/downloads/)
2. **TicTacToe API Running**: Ensure your ASP.NET Core server is running on `http://localhost:5000`
3. **Test User Account**: The API should have a test user configured (username: `testuser`, password: `TestPass123!`)

## Setup Instructions

### 1. Import Collection and Environment

1. Open Postman
2. Click **Import** button
3. Select **File**
4. Import both files:
   - `TicTacToe-API-Tests.postman_collection.json`
   - `TicTacToe-Dev-Environment.postman_environment.json`

### 2. Select Environment

1. Click the environment dropdown (top-right)
2. Select **"TicTacToe Development"**

## Test Structure

### Health Check Tests

- **Health Endpoint**: Verifies the API is running and responsive
- Tests response time and status codes

### JWKS (JSON Web Key Set) Tests

- **Public Keys Endpoint**: Tests the `/.well-known/jwks.json` endpoint
- Validates key format and security headers

### Authentication Tests

- **Login Success**: Tests successful authentication with valid credentials
- **Login Failure**: Tests authentication with invalid credentials
- **Rate Limiting**: Tests that rate limiting works for failed login attempts
- **User Status**: Tests protected endpoint access with valid JWT
- **Logout**: Tests logout functionality
- **Post-Logout Access**: Tests token behavior after logout

### Security Tests

- **Unauthorized Access**: Tests accessing protected endpoints without authentication
- **Invalid Token**: Tests accessing protected endpoints with invalid JWT

## Running Tests

### Individual Request Testing

1. Select a request from the collection
2. Click **Send**
3. View response in the bottom panel
4. Check **Tests** tab to see automated test results

### Collection Runner (Batch Testing)

1. Click **Runner** button (top-left)
2. Select **"TicTacToe API Integration Tests"** collection
3. Choose **"TicTacToe Development"** environment
4. Click **Run TicTacToe API...**
5. View results summary

### Newman (Command Line)

For CI/CD integration, use Newman:

```bash
# Install Newman globally
npm install -g newman

# Run collection
newman run TicTacToe-API-Tests.postman_collection.json \
  --environment TicTacToe-Dev-Environment.postman_environment.json \
  --reporters cli,json \
  --reporter-json-export results.json
```

## Test Scenarios

### Happy Path Testing

1. **Health Check** → Should return 200 OK
2. **JWKS** → Should return valid key set
3. **Login** → Should return JWT token
4. **Status** → Should return user info (with token)
5. **Logout** → Should succeed

### Negative Testing

1. **Invalid Login** → Should return 401
2. **No Token Access** → Should return 401
3. **Invalid Token** → Should return 401
4. **Rate Limit** → Should return 429 after multiple failures

### Security Testing

1. **Input Validation** → Test with malformed JSON, oversized payloads
2. **Header Injection** → Test with malicious headers
3. **SQL Injection** → Test with SQL-like input in username/password
4. **XSS Attempts** → Test with script tags in input

## Advanced Testing Features

### Environment Variables

The collection uses variables for:

- `{{baseUrl}}`: API base URL
- `{{jwt_token}}`: Stores token from login response
- `{{user_id}}`: Stores user ID from login response

### Test Scripts

Each request includes JavaScript tests that:

- Validate HTTP status codes
- Check response structure
- Verify security headers
- Store tokens for subsequent requests

### Rate Limiting Tests

To test rate limiting:

1. Run the "Login - Invalid Credentials" request multiple times quickly
2. Eventually it should return 429 (Too Many Requests)
3. Check the `Retry-After` header

## Extending the Tests

### Adding New Test Cases

1. Right-click on a folder → **Add Request**
2. Configure the request (method, URL, headers, body)
3. Add test scripts in the **Tests** tab

### Testing SignalR (WebSocket)

Postman doesn't natively support WebSocket testing. For SignalR testing, consider:

1. **Browser Developer Tools**: Manual testing via browser console
2. **WebSocket Clients**: Tools like [WebSocket King](https://websocketking.com/)
3. **Custom Test Scripts**: Use JavaScript with WebSocket API
4. **Unit Tests**: Test SignalR hub methods in C# unit tests

Example SignalR test script:

```javascript
// This would require a custom test runner
const connection = new signalR.HubConnectionBuilder()
    .withUrl("http://localhost:5000/tictactoehub")
    .build();

connection.start()
    .then(() => connection.invoke("JoinRoom", "test-room", "testuser"))
    .then(() => console.log("SignalR test passed"))
    .catch(err => console.error("SignalR test failed:", err));
```

## Best Practices

### Test Organization

- Group related tests in folders
- Use descriptive names
- Add comments to complex tests

### Data Management

- Use environment variables for dynamic data
- Reset state between test runs
- Avoid hardcoding sensitive data

### Error Handling

- Test both success and failure scenarios
- Validate error response formats
- Check appropriate HTTP status codes

### Performance Testing

- Use Postman's Runner for load testing
- Monitor response times
- Set up alerts for slow responses

### CI/CD Integration

- Export collections as JSON files
- Use Newman for automated testing
- Integrate with build pipelines
- Generate test reports

## Troubleshooting

### Common Issues

1. **Connection Refused**
   - Ensure API server is running on port 5000
   - Check firewall settings

2. **Authentication Failures**
   - Verify test user credentials
   - Check JWT token expiration
   - Ensure proper Authorization header format

3. **Rate Limiting**
   - Wait for rate limit reset (check Retry-After header)
   - Use different IP addresses for testing

4. **CORS Issues**
   - API should handle CORS for testing
   - Check server logs for CORS errors

### Debug Tips

1. **View Request Details**: Check the **Console** tab in Postman
2. **Server Logs**: Monitor API server logs for errors
3. **Network Tab**: Use browser dev tools for additional debugging
4. **Environment Variables**: Verify variables are set correctly

## Next Steps

1. **Automated Testing**: Set up CI/CD pipeline with Newman
2. **Performance Testing**: Add load testing scenarios
3. **Contract Testing**: Validate API contracts with tools like Pact
4. **Security Testing**: Integrate with security scanning tools
5. **Monitoring**: Set up API monitoring and alerting

This Postman collection provides a solid foundation for testing your TicTacToe API. As you add more features, extend the collection with additional test cases following the same patterns.
