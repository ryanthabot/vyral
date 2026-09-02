# Fresh Start - Reset VyralBot to Clean State
# This script removes all user data (profiles, logs) for a clean installation

Write-Host "VyralBot Fresh Start" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "This will delete ALL user data including:" -ForegroundColor Yellow
Write-Host "  - All profiles and settings" -ForegroundColor Yellow
Write-Host "  - OAuth credentials" -ForegroundColor Yellow
Write-Host "  - Application logs" -ForegroundColor Yellow
Write-Host ""

# Get the script directory (VyralBot root)
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$appDataPath = Join-Path $scriptDir "appdata"
$logsPath = Join-Path $scriptDir "logs"
$debugLogPath = Join-Path $scriptDir "run-debug.log"

Write-Host "Scanning for user data..." -ForegroundColor White
Write-Host ""

$foundData = $false

# Check appdata folder
if (Test-Path $appDataPath) {
    $foundData = $true
    Write-Host "Found appdata folder:" -ForegroundColor Green
    
    $profilesPath = Join-Path $appDataPath "profiles"
    if (Test-Path $profilesPath) {
        $profiles = Get-ChildItem -Path $profilesPath -Directory -ErrorAction SilentlyContinue
        Write-Host "  - Profiles: $($profiles.Count)" -ForegroundColor White
        foreach ($profile in $profiles) {
            Write-Host "    * $($profile.Name)" -ForegroundColor Gray
        }
    }
    
    $currentProfileFile = Join-Path $appDataPath "current_profile.txt"
    if (Test-Path $currentProfileFile) {
        $currentProfile = Get-Content $currentProfileFile -ErrorAction SilentlyContinue
        Write-Host "  - Current Profile: $currentProfile" -ForegroundColor White
    }
}

# Check logs folder
if (Test-Path $logsPath) {
    $foundData = $true
    $logFiles = Get-ChildItem -Path $logsPath -File -ErrorAction SilentlyContinue
    Write-Host "  - Log files: $($logFiles.Count)" -ForegroundColor White
}

# Check debug log
if (Test-Path $debugLogPath) {
    $foundData = $true
    Write-Host "  - Debug log: run-debug.log" -ForegroundColor White
}

Write-Host ""

if (-not $foundData) {
    Write-Host "No user data found. VyralBot is already in fresh state." -ForegroundColor Green
    Write-Host ""
    Write-Host "Press any key to exit..."
    $null = $Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown')
    exit 0
}

# Confirm deletion
Write-Host "WARNING: This action cannot be undone!" -ForegroundColor Red
Write-Host ""
$confirmation = Read-Host "Type 'DELETE' to confirm fresh start"

if ($confirmation -ne "DELETE") {
    Write-Host ""
    Write-Host "Operation cancelled." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Press any key to exit..."
    $null = $Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown')
    exit 0
}

Write-Host ""
Write-Host "Deleting user data..." -ForegroundColor Red

$errors = @()

# Delete appdata folder
if (Test-Path $appDataPath) {
    try {
        Remove-Item -Path $appDataPath -Recurse -Force -ErrorAction Stop
        Write-Host "  ✓ Deleted appdata folder" -ForegroundColor Green
    }
    catch {
        $errors += "appdata: $_"
        Write-Host "  ✗ Failed to delete appdata: $_" -ForegroundColor Red
    }
}

# Delete logs folder
if (Test-Path $logsPath) {
    try {
        Remove-Item -Path $logsPath -Recurse -Force -ErrorAction Stop
        Write-Host "  ✓ Deleted logs folder" -ForegroundColor Green
    }
    catch {
        $errors += "logs: $_"
        Write-Host "  ✗ Failed to delete logs: $_" -ForegroundColor Red
    }
}

# Delete debug log
if (Test-Path $debugLogPath) {
    try {
        Remove-Item -Path $debugLogPath -Force -ErrorAction Stop
        Write-Host "  ✓ Deleted run-debug.log" -ForegroundColor Green
    }
    catch {
        $errors += "run-debug.log: $_"
        Write-Host "  ✗ Failed to delete run-debug.log: $_" -ForegroundColor Red
    }
}

Write-Host ""

if ($errors.Count -eq 0) {
    Write-Host "✓ Fresh start complete!" -ForegroundColor Green
    Write-Host ""
    Write-Host "VyralBot will now start in first-run mode." -ForegroundColor Cyan
    Write-Host "You'll need to create a new profile and re-authenticate platforms." -ForegroundColor Cyan
}
else {
    Write-Host "⚠ Fresh start completed with errors:" -ForegroundColor Yellow
    foreach ($error in $errors) {
        Write-Host "  - $error" -ForegroundColor Red
    }
    Write-Host ""
    Write-Host "Some files may still be in use. Try closing VyralBot and running this script again." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Press any key to exit..."
$null = $Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown')
