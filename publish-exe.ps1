# Builds a double-clickable, self-contained Vyral.exe (includes the .NET runtime —
# runs on any Windows 10/11 x64 machine, no installs needed) and places it on the Desktop.
# Usage: powershell -ExecutionPolicy Bypass -File publish-exe.ps1
# Update note: close Vyral first, then re-run this script to replace the exe with a new build.

$ErrorActionPreference = "Stop"
$repo     = Split-Path -Parent $MyInvocation.MyCommand.Path
$project  = Join-Path $repo "VyralBot\src\VyralBot.UI\VyralBot.UI.csproj"
$outRoot  = [Environment]::GetFolderPath("Desktop")
$outDir   = Join-Path $outRoot "Vyral"

dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $outDir
if ($LASTEXITCODE -ne 0) { Write-Error "Publish failed."; exit 1 }

$exe = Join-Path $outDir "VyralBot.UI.exe"
if (Test-Path $exe) { Move-Item -Force $exe (Join-Path $outDir "Vyral.exe") }

Write-Host ""
Write-Host "Done. Launch it with: $outDir\Vyral.exe"
