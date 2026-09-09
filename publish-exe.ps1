# Builds a double-clickable, self-contained Vyral.exe (includes the .NET runtime —
# runs on any Windows 10/11 x64 machine, no installs needed) and updates the
# Desktop\Vyral folder — WITHOUT touching your data (profiles, logs, database).
#
# Usage: powershell -ExecutionPolicy Bypass -File publish-exe.ps1
# Update note: close Vyral first, then re-run this script.

$ErrorActionPreference = "Stop"
$repo     = Split-Path -Parent $MyInvocation.MyCommand.Path
$project  = Join-Path $repo "VyralBot\src\VyralBot.UI\VyralBot.UI.csproj"
$outRoot  = [Environment]::GetFolderPath("Desktop")
$outDir   = Join-Path $outRoot "Vyral"
$staging  = Join-Path $env:TEMP "vyral-publish"

# 1. Publish to a STAGING folder (never to the live folder — MSBuild cleans the
#    publish directory, which would wipe the profiles/database living there).
if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $staging
if ($LASTEXITCODE -ne 0) { Write-Error "Publish failed."; exit 1 }

# 2. Make sure the live folder exists and back up its data before touching it.
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
$profilesDir = Join-Path $outDir "profiles"
if (Test-Path $profilesDir) {
    $backup = Join-Path $outDir ("profiles-backup-{0}.zip" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
    Compress-Archive -Path (Join-Path $profilesDir "*") -DestinationPath $backup -Force
    Write-Host "Profiles backed up to $backup"
}

# 3. Copy ONLY the application files (never delete anything in the live folder).
Copy-Item -Path (Join-Path $staging "*") -Destination $outDir -Recurse -Force

# 4. Rename to Vyral.exe.
$exe = Join-Path $outDir "VyralBot.UI.exe"
if (Test-Path $exe) { Move-Item -Force $exe (Join-Path $outDir "Vyral.exe") }

Remove-Item -Recurse -Force $staging

Write-Host ""
Write-Host "Done. Launch it with: $outDir\Vyral.exe"
Write-Host "Your profiles, database and logs were not modified."
