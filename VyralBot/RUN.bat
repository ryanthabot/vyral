@echo off
echo Starting VyralBot in debug mode...
echo Logging output to run-debug.log
echo.
cd /d "%~dp0src\VyralBot.UI\bin\Debug\net10.0-windows"
powershell -Command ".\VyralBot.UI.exe 2>&1 | Tee-Object -FilePath '..\..\..\..\run-debug.log'"
