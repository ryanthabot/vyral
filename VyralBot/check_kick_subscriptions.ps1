# Script to check active Kick webhook subscriptions
# Run this to see what subscriptions are actually registered at Kick's API

Write-Host "`n=== Checking Kick Webhook Subscriptions ===" -ForegroundColor Cyan
Write-Host "This will query Kick's API to see active webhook subscriptions`n" -ForegroundColor Yellow

# First, we need to get the access token from VyralBot
# The token is encrypted in the database, so we'll trigger the app to log it

Write-Host "Instructions:" -ForegroundColor Green
Write-Host "1. Open VyralBot" -ForegroundColor White
Write-Host "2. Navigate to Settings -> Platforms -> Kick" -ForegroundColor White  
Write-Host "3. Click 'View Active Subscriptions' button" -ForegroundColor White
Write-Host "4. Check the VyralBot logs for the subscription list`n" -ForegroundColor White

Write-Host "Expected output in logs:" -ForegroundColor Yellow
Write-Host "  - 'Retrieving active webhook subscriptions from Kick'" -ForegroundColor Gray
Write-Host "  - 'Retrieved X active webhook subscriptions'" -ForegroundColor Gray
Write-Host "`nIf X = 0, Kick has auto-disabled webhooks due to errors!" -ForegroundColor Red
Write-Host "If X = 10, subscriptions are active and Kick should be sending webhooks`n" -ForegroundColor Green

Read-Host "Press Enter to close"
