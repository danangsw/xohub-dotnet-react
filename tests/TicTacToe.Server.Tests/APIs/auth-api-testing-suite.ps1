# Comprehensive End-to-End and Integration Testing for Auth API Controller
# Tests the enhanced ApiControllerBase functionality and complete auth flow

param(
    [switch]$Verbose,
    [switch]$SkipCleanup,
    [int]$ServerTimeout = 30,
    [string]$BaseUrl = "http://localhost:5000"
)

# Test configuration
$TestConfig = @{
    BaseUrl            = $BaseUrl
    ServerTimeout      = $ServerTimeout
    TestUsers          = @(
        @{ UserName = "testuser"; Password = "TestPass123!"; ExpectedUserId = "user_test_004" },
        @{ UserName = "admin"; Password = "AdminPass123!"; ExpectedUserId = "user_admin_001" }
    )
    InvalidCredentials = @(
        @{ UserName = "testuser"; Password = "wrongpass"; ExpectedStatus = 401 },
        @{ UserName = "nonexistent"; Password = "TestPass123!"; ExpectedStatus = 401 },
        @{ UserName = ""; Password = "TestPass123!"; ExpectedStatus = 400 },
        @{ UserName = "testuser"; Password = ""; ExpectedStatus = 400 }
    )
    RateLimitTests     = 5  # Number of rapid requests to test rate limiting
}

# Test results tracking
$TestResults = @{
    Total   = 0
    Passed  = 0
    Failed  = 0
    Skipped = 0
    Details = @()
}

function Write-TestHeader {
    param([string]$Title)
    Write-Host "`n==========================================" -ForegroundColor Cyan
    Write-Host "TEST: $Title" -ForegroundColor Cyan
    Write-Host "==========================================" -ForegroundColor Cyan
}

function Write-TestResult {
    param(
        [string]$TestName,
        [bool]$Passed,
        [string]$Message = "",
        [string]$Details = ""
    )

    $TestResults.Total++
    if ($Passed) {
        $TestResults.Passed++
        Write-Host "PASS: $TestName" -ForegroundColor Green
    }
    else {
        $TestResults.Failed++
        Write-Host "FAIL: $TestName" -ForegroundColor Red
    }

    if ($Message) {
        Write-Host "   $Message" -ForegroundColor Yellow
    }

    if ($Verbose -and $Details) {
        Write-Host "   Details: $Details" -ForegroundColor Gray
    }

    $TestResults.Details += @{
        Name      = $TestName
        Passed    = $Passed
        Message   = $Message
        Details   = $Details
        Timestamp = Get-Date
    }
}

function Test-JwksEndpoint {
    Write-TestHeader "JWKS Endpoint Integration Test"

    try {
        # Test JWKS endpoint (public, no auth required)
        $response = Invoke-WebRequest -Uri "$($TestConfig.BaseUrl)/.well-known/jwks.json" -Method GET -UseBasicParsing -TimeoutSec 10

        Write-TestResult "JWKS Status Code" ($response.StatusCode -eq 200) "Expected 200, got $($response.StatusCode)"

        if ($response.StatusCode -eq 200) {
            $jwks = $response.Content | ConvertFrom-Json

            # Verify JWKS structure
            $hasKeys = $jwks.keys -and $jwks.keys.Count -gt 0
            Write-TestResult "JWKS Has Keys" $hasKeys "JWKS should contain at least one key"

            if ($hasKeys) {
                $firstKey = $jwks.keys[0]
                $hasRequiredFields = $firstKey.kid -and $firstKey.kty -and $firstKey.n -and $firstKey.e
                Write-TestResult "JWKS Key Structure" $hasRequiredFields "Key should have kid, kty, n, e fields"
            }

            # Test caching by making second request
            $response2 = Invoke-WebRequest -Uri "$($TestConfig.BaseUrl)/.well-known/jwks.json" -Method GET -UseBasicParsing -TimeoutSec 10
            $cached = ($response.Content -eq $response2.Content)
            Write-TestResult "JWKS Caching" $cached "Second request should return cached response"
        }

        # Test rate limiting for JWKS
        Write-Host "   Testing JWKS rate limiting..." -ForegroundColor Gray
        $rateLimitExceeded = $false
        for ($i = 0; $i -lt 10; $i++) {
            try {
                $rateResponse = Invoke-WebRequest -Uri "$($TestConfig.BaseUrl)/.well-known/jwks.json" -Method GET -UseBasicParsing -TimeoutSec 5
                if ($rateResponse.StatusCode -eq 429) {
                    $rateLimitExceeded = $true
                    break
                }
            }
            catch {
                if ($_.Exception.Response.StatusCode -eq 429) {
                    $rateLimitExceeded = $true
                    break
                }
            }
            Start-Sleep -Milliseconds 100
        }
        Write-TestResult "JWKS Rate Limiting" $rateLimitExceeded "JWKS endpoint should be rate limited"

    }
    catch {
        Write-TestResult "JWKS Endpoint Test" $false "Exception: $($_.Exception.Message)"
    }
}

function Test-AuthenticationFlow {
    param([hashtable]$User)

    Write-TestHeader "Authentication Flow Test - $($User.UserName)"

    $token = $null
    $headers = @{
        "Content-Type" = "application/json"
    }

    try {
        # 1. Test successful login
        $loginBody = @{ userName = $User.UserName; password = $User.Password } | ConvertTo-Json
        $loginResponse = Invoke-WebRequest -Uri "$($TestConfig.BaseUrl)/api/v1/auth/login" -Method POST -Body $loginBody -Headers $headers -UseBasicParsing -TimeoutSec 15

        $loginSuccess = $loginResponse.StatusCode -eq 200
        Write-TestResult "Login Success" $loginSuccess "Expected 200, got $($loginResponse.StatusCode)"

        if ($loginSuccess) {
            $loginData = $loginResponse.Content | ConvertFrom-Json

            # Verify login response structure
            $hasToken = $loginData.Token -and $loginData.Token.Length -gt 0
            $hasUserId = $loginData.UserId -eq $User.ExpectedUserId
            $hasExpiresIn = $loginData.ExpiresIn -eq 3600
            $hasTokenType = $loginData.TokenType -eq "Bearer"

            Write-TestResult "Login Response Structure" ($hasToken -and $hasUserId -and $hasExpiresIn -and $hasTokenType) "Response should contain valid token, userId, expiresIn, tokenType"

            # Verify JWT token format (basic check)
            $tokenParts = $loginData.Token -split '\.'
            $validJwtFormat = $tokenParts.Count -eq 3
            Write-TestResult "JWT Token Format" $validJwtFormat "Token should be in JWT format (header.payload.signature)"

            $token = $loginData.Token
            $headers["Authorization"] = "Bearer $token"

            # 2. Test status endpoint with valid token
            $statusResponse = Invoke-WebRequest -Uri "$($TestConfig.BaseUrl)/api/v1/auth/status" -Method GET -Headers $headers -UseBasicParsing -TimeoutSec 10

            $statusSuccess = $statusResponse.StatusCode -eq 200
            Write-TestResult "Status Check Success" $statusSuccess "Expected 200, got $($statusResponse.StatusCode)"

            if ($statusSuccess) {
                $statusData = $statusResponse.Content | ConvertFrom-Json

                # Verify status response structure
                $correctUserId = $statusData.UserId -eq $User.ExpectedUserId
                $correctUserName = $statusData.UserName -eq $User.UserName
                $isAuthenticated = $statusData.IsAuthenticated -eq $true
                $hasLastActivity = $statusData.LastActivity

                Write-TestResult "Status Response Structure" ($correctUserId -and $correctUserName -and $isAuthenticated -and $hasLastActivity) "Status should show correct user info and authenticated state"
            }

            # 3. Test logout endpoint
            $logoutResponse = Invoke-WebRequest -Uri "$($TestConfig.BaseUrl)/api/v1/auth/logout" -Method POST -Headers $headers -UseBasicParsing -TimeoutSec 10

            $logoutSuccess = $logoutResponse.StatusCode -eq 200
            Write-TestResult "Logout Success" $logoutSuccess "Expected 200, got $($logoutResponse.StatusCode)"

            # 4. Test status endpoint after logout (token should still be valid, but this tests the endpoint)
            $statusAfterLogout = Invoke-WebRequest -Uri "$($TestConfig.BaseUrl)/api/v1/auth/status" -Method GET -Headers $headers -UseBasicParsing -TimeoutSec 10
            Write-TestResult "Status After Logout" ($statusAfterLogout.StatusCode -eq 200) "Status should still work after logout (JWT stateless)"
        }

    }
    catch {
        Write-TestResult "Authentication Flow Test" $false "Exception: $($_.Exception.Message)"
    }
}

function Test-InvalidCredentials {
    Write-TestHeader "Invalid Credentials and Input Validation Tests"

    $headers = @{
        "Content-Type" = "application/json"
    }

    foreach ($invalidCred in $TestConfig.InvalidCredentials) {
        try {
            $loginBody = @{ userName = $invalidCred.UserName; password = $invalidCred.Password } | ConvertTo-Json
            $response = Invoke-WebRequest -Uri "$($TestConfig.BaseUrl)/api/v1/auth/login" -Method POST -Body $loginBody -Headers $headers -UseBasicParsing -TimeoutSec 10

            $expectedStatus = $response.StatusCode -eq $invalidCred.ExpectedStatus
            Write-TestResult "Invalid Login $($invalidCred.UserName):$($invalidCred.Password)" $expectedStatus "Expected $($invalidCred.ExpectedStatus), got $($response.StatusCode)"

        }
        catch {
            if ($_.Exception.Response) {
                $actualStatus = $_.Exception.Response.StatusCode.value__
                $expectedStatus = $actualStatus -eq $invalidCred.ExpectedStatus
                Write-TestResult "Invalid Login $($invalidCred.UserName):$($invalidCred.Password)" $expectedStatus "Expected $($invalidCred.ExpectedStatus), got $actualStatus"
            }
            else {
                Write-TestResult "Invalid Login $($invalidCred.UserName):$($invalidCred.Password)" $false "Exception: $($_.Exception.Message)"
            }
        }
    }
}

function Test-SecurityValidations {
    Write-TestHeader "Security and Input Validation Tests"

    # Test invalid content type
    try {
        $loginBody = @{ userName = "testuser"; password = "TestPass123!" } | ConvertTo-Json
        $response = Invoke-WebRequest -Uri "$($TestConfig.BaseUrl)/api/v1/auth/login" -Method POST -Body $loginBody -ContentType "text/plain" -UseBasicParsing -TimeoutSec 10
        Write-TestResult "Invalid Content-Type" $false "Should reject non-JSON content type"
    }
    catch {
        $statusCode = $_.Exception.Response.StatusCode.value__
        $correctRejection = $statusCode -eq 400
        Write-TestResult "Invalid Content-Type" $correctRejection "Expected 400 for invalid content-type, got $statusCode"
    }

    # Test malformed JSON
    try {
        $malformedBody = '{"userName": "testuser", "password": "TestPass123!"'  # Missing closing brace
        $response = Invoke-WebRequest -Uri "$($TestConfig.BaseUrl)/api/v1/auth/login" -Method POST -Body $malformedBody -ContentType "application/json" -UseBasicParsing -TimeoutSec 10
        Write-TestResult "Malformed JSON" $false "Should reject malformed JSON"
    }
    catch {
        $statusCode = $_.Exception.Response.StatusCode.value__
        $correctRejection = $statusCode -eq 400
        Write-TestResult "Malformed JSON" $correctRejection "Expected 400 for malformed JSON, got $statusCode"
    }

    # Test oversized request
    try {
        $largeBody = @{ userName = "testuser"; password = "TestPass123!"; extra = "x" * 5000 } | ConvertTo-Json
        $response = Invoke-WebRequest -Uri "$($TestConfig.BaseUrl)/api/v1/auth/login" -Method POST -Body $largeBody -ContentType "application/json" -UseBasicParsing -TimeoutSec 10
        Write-TestResult "Oversized Request" $false "Should reject oversized request"
    }
    catch {
        $statusCode = $_.Exception.Response.StatusCode.value__
        $correctRejection = $statusCode -eq 400
        Write-TestResult "Oversized Request" $correctRejection "Expected 400 for oversized request, got $statusCode"
    }

    # Test invalid username format
    $invalidUsernames = @("us", "user@domain.com", "user name", "user-name!")
    foreach ($invalidUser in $invalidUsernames) {
        try {
            $loginBody = @{ userName = $invalidUser; password = "TestPass123!" } | ConvertTo-Json
            $response = Invoke-WebRequest -Uri "$($TestConfig.BaseUrl)/api/v1/auth/login" -Method POST -Body $loginBody -ContentType "application/json" -UseBasicParsing -TimeoutSec 10
            Write-TestResult "Invalid Username: $invalidUser" $false "Should reject invalid username format"
        }
        catch {
            $statusCode = $_.Exception.Response.StatusCode.value__
            $correctRejection = $statusCode -eq 400
            Write-TestResult "Invalid Username: $invalidUser" $correctRejection "Expected 400 for invalid username, got $statusCode"
        }
    }

    # Test invalid password format
    $invalidPasswords = @("short", "nouppercase123!", "NOLOWERCASE123!", "NoSpecialChar123", "NoNumber!")
    foreach ($invalidPass in $invalidPasswords) {
        try {
            $loginBody = @{ userName = "testuser"; password = $invalidPass } | ConvertTo-Json
            $response = Invoke-WebRequest -Uri "$($TestConfig.BaseUrl)/api/v1/auth/login" -Method POST -Body $loginBody -ContentType "application/json" -UseBasicParsing -TimeoutSec 10
            Write-TestResult "Invalid Password Format" $false "Should reject invalid password format"
        }
        catch {
            $statusCode = $_.Exception.Response.StatusCode.value__
            $correctRejection = $statusCode -eq 400
            Write-TestResult "Invalid Password Format" $correctRejection "Expected 400 for invalid password, got $statusCode"
        }
    }
}

function Test-RateLimiting {
    Write-TestHeader "Rate Limiting Tests"

    $headers = @{
        "Content-Type" = "application/json"
    }

    # Test auth rate limiting
    $rateLimitHit = $false
    Write-Host "   Making $($TestConfig.RateLimitTests) rapid login requests..." -ForegroundColor Gray

    for ($i = 1; $i -le $TestConfig.RateLimitTests; $i++) {
        try {
            $loginBody = @{ userName = "testuser"; password = "TestPass123!" } | ConvertTo-Json
            $response = Invoke-WebRequest -Uri "$($TestConfig.BaseUrl)/api/v1/auth/login" -Method POST -Body $loginBody -Headers $headers -UseBasicParsing -TimeoutSec 5

            if ($response.StatusCode -eq 429) {
                $rateLimitHit = $true
                Write-Host "   Rate limit triggered on attempt $i" -ForegroundColor Yellow
                break
            }

            Write-Host "   Attempt $i successful" -ForegroundColor Gray
            Start-Sleep -Milliseconds 200  # Small delay between requests

        }
        catch {
            if ($_.Exception.Response.StatusCode -eq 429) {
                $rateLimitHit = $true
                Write-Host "   Rate limit triggered on attempt $i" -ForegroundColor Yellow
                break
            }
            Write-Host "   Attempt $i failed: $($_.Exception.Message)" -ForegroundColor Red
        }
    }

    Write-TestResult "Auth Rate Limiting" $rateLimitHit "Rate limiting should trigger after multiple rapid requests"
}

function Test-UnauthorizedAccess {
    Write-TestHeader "Unauthorized Access Tests"

    # Test status endpoint without token
    try {
        $response = Invoke-WebRequest -Uri "$($TestConfig.BaseUrl)/api/v1/auth/status" -Method GET -UseBasicParsing -TimeoutSec 10
        Write-TestResult "Status Without Token" $false "Should reject unauthorized access"
    }
    catch {
        $statusCode = $_.Exception.Response.StatusCode.value__
        $correctRejection = $statusCode -eq 401
        Write-TestResult "Status Without Token" $correctRejection "Expected 401 for unauthorized access, got $statusCode"
    }

    # Test logout endpoint without token
    try {
        $response = Invoke-WebRequest -Uri "$($TestConfig.BaseUrl)/api/v1/auth/logout" -Method POST -UseBasicParsing -TimeoutSec 10
        Write-TestResult "Logout Without Token" $false "Should reject unauthorized access"
    }
    catch {
        $statusCode = $_.Exception.Response.StatusCode.value__
        $correctRejection = $statusCode -eq 401
        Write-TestResult "Logout Without Token" $correctRejection "Expected 401 for unauthorized access, got $statusCode"
    }

    # Test with invalid token
    $headers = @{
        "Authorization" = "Bearer invalid.jwt.token"
        "Content-Type"  = "application/json"
    }

    try {
        $response = Invoke-WebRequest -Uri "$($TestConfig.BaseUrl)/api/v1/auth/status" -Method GET -Headers $headers -UseBasicParsing -TimeoutSec 10
        Write-TestResult "Invalid Token" $false "Should reject invalid token"
    }
    catch {
        $statusCode = $_.Exception.Response.StatusCode.value__
        $correctRejection = $statusCode -eq 401
        Write-TestResult "Invalid Token" $correctRejection "Expected 401 for invalid token, got $statusCode"
    }
}

function Test-Performance {
    Write-TestHeader "Performance Tests"

    $headers = @{
        "Content-Type" = "application/json"
    }

    # Test login performance
    $loginTimes = @()
    for ($i = 1; $i -le 3; $i++) {
        $startTime = Get-Date
        try {
            $loginBody = @{ userName = "testuser"; password = "TestPass123!" } | ConvertTo-Json
            $response = Invoke-WebRequest -Uri "$($TestConfig.BaseUrl)/api/v1/auth/login" -Method POST -Body $loginBody -Headers $headers -UseBasicParsing -TimeoutSec 10
            $endTime = Get-Date
            $duration = ($endTime - $startTime).TotalMilliseconds
            $loginTimes += $duration
            Write-Host "   Login attempt $i took $($duration.ToString("F2"))ms" -ForegroundColor Gray
        }
        catch {
            Write-Host "   Login attempt $i failed" -ForegroundColor Red
        }
        Start-Sleep -Seconds 1  # Wait between requests
    }

    if ($loginTimes.Count -gt 0) {
        $avgTime = ($loginTimes | Measure-Object -Average).Average
        $maxTime = ($loginTimes | Measure-Object -Maximum).Maximum
        Write-TestResult "Login Performance" ($avgTime -lt 2000) "Average login time: $($avgTime.ToString("F2"))ms, Max: $($maxTime.ToString("F2"))ms (should be < 2000ms)"
    }

    # Test JWKS performance
    $jwksTimes = @()
    for ($i = 1; $i -le 3; $i++) {
        $startTime = Get-Date
        try {
            $response = Invoke-WebRequest -Uri "$($TestConfig.BaseUrl)/.well-known/jwks.json" -Method GET -UseBasicParsing -TimeoutSec 10
            $endTime = Get-Date
            $duration = ($endTime - $startTime).TotalMilliseconds
            $jwksTimes += $duration
            Write-Host "   JWKS attempt $i took $($duration.ToString("F2"))ms" -ForegroundColor Gray
        }
        catch {
            Write-Host "   JWKS attempt $i failed" -ForegroundColor Red
        }
    }

    if ($jwksTimes.Count -gt 0) {
        $avgTime = ($jwksTimes | Measure-Object -Average).Average
        Write-TestResult "JWKS Performance" ($avgTime -lt 500) "Average JWKS time: $($avgTime.ToString("F2"))ms (should be < 500ms, cached)"
    }
}

function Test-HealthEndpoint {
    Write-TestHeader "Health Check Test"

    try {
        $response = Invoke-WebRequest -Uri "$($TestConfig.BaseUrl)/health" -Method GET -UseBasicParsing -TimeoutSec 10

        $healthOk = $response.StatusCode -eq 200
        Write-TestResult "Health Check" $healthOk "Expected 200, got $($response.StatusCode)"

        if ($healthOk) {
            $healthData = $response.Content | ConvertFrom-Json
            Write-TestResult "Health Response" ($healthData.status -eq "Healthy") "Health status should be 'Healthy'"
        }

    }
    catch {
        Write-TestResult "Health Check" $false "Exception: $($_.Exception.Message)"
    }
}

# Main test execution
Write-Host "Starting Comprehensive Auth API End-to-End & Integration Tests" -ForegroundColor Cyan
Write-Host "Base URL: $($TestConfig.BaseUrl)" -ForegroundColor Cyan
Write-Host "Server Timeout: $($TestConfig.ServerTimeout)s" -ForegroundColor Cyan
Write-Host "Verbose Mode: $Verbose" -ForegroundColor Cyan
Write-Host ""

# Start server
Write-Host "Starting server..." -ForegroundColor Yellow
$serverProcess = Start-Process -NoNewWindow -FilePath "dotnet" -ArgumentList "run --project server --urls $($TestConfig.BaseUrl)" -PassThru -WorkingDirectory "f:\programming\csharp\xohub-dotnet-react"

# Wait for server to start
Write-Host "Waiting for server to start..." -ForegroundColor Yellow
$serverReady = $false
$elapsed = 0

while (-not $serverReady -and $elapsed -lt $TestConfig.ServerTimeout) {
    try {
        $healthResponse = Invoke-WebRequest -Uri "$($TestConfig.BaseUrl)/health" -Method GET -UseBasicParsing -TimeoutSec 5
        if ($healthResponse.StatusCode -eq 200) {
            $serverReady = $true
            Write-Host "Server is ready!" -ForegroundColor Green
        }
    }
    catch {
        Start-Sleep -Seconds 2
        $elapsed += 2
        Write-Host "   Waiting... ($elapsed/$($TestConfig.ServerTimeout)s)" -ForegroundColor Gray
    }
}

if (-not $serverReady) {
    Write-Host "Server failed to start within $($TestConfig.ServerTimeout) seconds" -ForegroundColor Red
    exit 1
}

try {
    # Run all test suites
    Test-HealthEndpoint
    Test-JwksEndpoint

    foreach ($user in $TestConfig.TestUsers) {
        Test-AuthenticationFlow -User $user
    }

    Test-InvalidCredentials
    Test-SecurityValidations
    Test-RateLimiting
    Test-UnauthorizedAccess
    Test-Performance

    # Test summary
    Write-Host "`n==========================================" -ForegroundColor Cyan
    Write-Host "TEST SUMMARY" -ForegroundColor Cyan
    Write-Host "==========================================" -ForegroundColor Cyan
    Write-Host "Total Tests: $($TestResults.Total)" -ForegroundColor White
    Write-Host "Passed: $($TestResults.Passed)" -ForegroundColor Green
    Write-Host "Failed: $($TestResults.Failed)" -ForegroundColor Red
    Write-Host "Skipped: $($TestResults.Skipped)" -ForegroundColor Yellow

    $successRate = if ($TestResults.Total -gt 0) { ($TestResults.Passed / $TestResults.Total) * 100 } else { 0 }
    Write-Host "Success Rate: $($successRate.ToString("F1"))%" -ForegroundColor $(if ($successRate -ge 90) { "Green" } elseif ($successRate -ge 75) { "Yellow" } else { "Red" })

    if ($TestResults.Failed -gt 0) {
        Write-Host "   - $($test.Name): $($test.Message)" -ForegroundColor Red
    }

    Write-Host "Auth API Controller Testing Complete!" -ForegroundColor $(if ($TestResults.Failed -eq 0) { "Green" } else { "Yellow" })

}
catch {
    Write-Host "Test execution failed: $($_.Exception.Message)" -ForegroundColor Red
}
finally {
    if (-not $SkipCleanup) {
        Write-Host "Cleaning up..." -ForegroundColor Yellow
        try {
            if ($serverProcess -and -not $serverProcess.HasExited) {
                Stop-Process -Id $serverProcess.Id -Force -ErrorAction SilentlyContinue
                Write-Host "Server stopped" -ForegroundColor Green
            }
        }
        catch {
            Write-Host "Could not stop server process" -ForegroundColor Yellow
        }
    }
    else {
        Write-Host "Server left running (use -SkipCleanup `$false` to auto-stop)" -ForegroundColor Yellow
    }
}