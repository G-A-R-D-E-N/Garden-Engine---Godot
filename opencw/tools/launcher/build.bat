@echo off
setlocal
REM Builds the standalone dev.exe launcher (Avalonia, cross-platform - see build.sh for the Linux
REM equivalent) and drops it at the repo root. Self-contained + single-file, so a fresh clone can
REM run it with no .NET installed.

set "HERE=%~dp0"
set "ROOT=%HERE%..\..\"

where dotnet >nul 2>&1
if errorlevel 1 (
    echo [X] .NET SDK not found on PATH. Install .NET SDK 10+: https://dotnet.microsoft.com/download
    exit /b 1
)

echo Publishing dev.exe ...
dotnet publish "%HERE%OpenCommonwealth.Launcher.csproj" -c Release -r win-x64 -o "%HERE%publish" --nologo
if errorlevel 1 (
    echo [X] Publish failed.
    exit /b 1
)

copy /y "%HERE%publish\dev.exe" "%ROOT%dev.exe" >nul
if errorlevel 1 (
    echo [X] Could not copy dev.exe to the repo root.
    exit /b 1
)

echo.
echo [OK] Built: %ROOT%dev.exe
echo      Run it from the repo root:  dev.exe
endlocal
