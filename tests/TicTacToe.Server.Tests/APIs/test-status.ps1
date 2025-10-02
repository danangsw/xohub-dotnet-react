# Complete flow: Login to get fresh token, then test Auth/GetStatus endpoint

Write-Host "Step 1: Getting fresh JWT token via login..."
$loginBody = @{userName = 'testuser'; password = 'TestPass123!' } | ConvertTo-Json
$loginResponse = Invoke-WebRequest -Uri 'http://localhost:5000/api/v1/auth/login' -Method POST -Body $loginBody -ContentType 'application/json' -UseBasicParsing
$loginData = $loginResponse.Content | ConvertFrom-Json
$jwtToken = $loginData.Token

Write-Host "Fresh token obtained: $($jwtToken.Substring(0,50))..."
Write-Host ""

$headers = @{
    "Authorization" = "Bearer $jwtToken"
    "Content-Type"  = "application/json"
}

try {
    Write-Host "Testing Auth/GetStatus endpoint with JWT token..."
    $response = Invoke-WebRequest -Uri 'http://localhost:5000/api/v1/auth/status' -Method GET -Headers $headers -UseBasicParsing -TimeoutSec 10

    Write-Host "SUCCESS! Status: $($response.StatusCode)"
    Write-Host "Response:"
    $response.Content | ConvertFrom-Json | ConvertTo-Json
}
catch {
    Write-Host "FAILED: $($_.Exception.Message)"
    if ($_.Exception.Response) {
        $stream = $_.Exception.Response.GetResponseStream()
        $reader = New-Object System.IO.StreamReader($stream)
        $errorResponse = $reader.ReadToEnd()
        Write-Host "Error Response: $errorResponse"
    }
}
