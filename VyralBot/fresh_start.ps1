# VyralBot Fresh Start Script
# Deletes appdata, logs, run-debug.log and launches VyralBot

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$appDataPath = Join-Path $scriptDir "appdata"
$logsPath = Join-Path $scriptDir "logs"
$debugLogPath = Join-Path $scriptDir "run-debug.log"

Write-Host ""
Write-Host "======================================" -ForegroundColor Cyan
Write-Host " VyralBot Fresh Start" -ForegroundColor Cyan
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""

# Scan for existing data
$foundItems = @()
if (Test-Path $appDataPath) { $foundItems += "appdata/" }
if (Test-Path $logsPath) { $foundItems += "logs/" }
if (Test-Path $debugLogPath) { $foundItems += "run-debug.log" }

if ($foundItems.Count -eq 0) {
    Write-Host "No user data found." -ForegroundColor Green
}
else {
    Write-Host "Will delete:" -ForegroundColor Yellow
    foreach ($item in $foundItems) {
        Write-Host "  • $item" -ForegroundColor Red
    }
    Write-Host ""
    Write-Host "Type 'DELETE' to confirm:" -ForegroundColor Yellow
    $confirmation = Read-Host

    if ($confirmation -ne "DELETE") {
        Write-Host "Cancelled." -ForegroundColor Green
        exit 0
    }

    Write-Host ""
    Write-Host "Deleting..." -ForegroundColor Yellow
    
    if (Test-Path $appDataPath) {
        Remove-Item -Path $appDataPath -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "  ✓ Deleted appdata" -ForegroundColor Green
    }
    
    if (Test-Path $logsPath) {
        Remove-Item -Path $logsPath -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "  ✓ Deleted logs" -ForegroundColor Green
    }
    
    if (Test-Path $debugLogPath) {
        Remove-Item -Path $debugLogPath -Force -ErrorAction SilentlyContinue
        Write-Host "  ✓ Deleted debug log" -ForegroundColor Green
    }
    
    Write-Host ""
    Write-Host "✓ Fresh start complete!" -ForegroundColor Green
}

Write-Host ""
Write-Host "Launching VyralBot..." -ForegroundColor Cyan
Write-Host ""

$exePath = Join-Path $scriptDir "src\VyralBot.UI\bin\Debug\net9.0-windows\VyralBot.UI.exe"
$logPath = Join-Path $scriptDir "run-debug.log"

if (Test-Path $exePath) {
    $exeDir = Split-Path $exePath
    Set-Location $exeDir
    
    Write-Host "Starting VyralBot from: $exeDir" -ForegroundColor Gray
    Write-Host "Logging to: $logPath" -ForegroundColor Gray
    Write-Host ""
    
    # Launch VyralBot directly (not in separate PowerShell window)
    Start-Process -FilePath ".\VyralBot.UI.exe" -WorkingDirectory $exeDir
    
    Write-Host "VyralBot launched!" -ForegroundColor Green
    Write-Host ""
}
else {
    Write-Host "Error: VyralBot.UI.exe not found" -ForegroundColor Red
    Write-Host "Please build the project first." -ForegroundColor Yellow
    pause
}
