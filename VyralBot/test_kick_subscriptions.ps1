# Test script to verify Kick webhook subscriptions
# This will call Kick's API to list active subscriptions

$accessToken = "YOUR_KICK_ACCESS_TOKEN_HERE"  # Replace with actual token from database

$headers = @{
    'Authorization' = "Bearer $accessToken"
    'Content-Type' = 'application/json'
}

Write-Host "Fetching active Kick webhook subscriptions..." -ForegroundColor Cyan

try {
    $response = Invoke-RestMethod -Uri 'https://api.kick.com/public/v1/events/subscriptions' -Method GET -Headers $headers
    
    Write-Host "`nResponse received:" -ForegroundColor Green
    $response | ConvertTo-Json -Depth 10 | Write-Host
    
    if ($response.data) {
        Write-Host "`n✅ Found $($response.data.Count) active subscriptions" -ForegroundColor Green
        foreach ($sub in $response.data) {
            Write-Host "  - $($sub.event_type) (ID: $($sub.subscription_id))" -ForegroundColor Yellow
        }
    } else {
        Write-Host "`n❌ No active subscriptions found!" -ForegroundColor Red
    }
}
catch {
    Write-Host "`n❌ Error: $_" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
}
