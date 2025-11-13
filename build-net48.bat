@echo off
REM Build script for .NET Framework 4.8 (Windows executable for Mono on Raspberry Pi)

echo Building Paycheck4 for .NET Framework 4.8...

REM Clean previous builds
dotnet clean src\Paycheck4.Console\Paycheck4.Console.csproj -c Release

REM Build the project
dotnet build src\Paycheck4.Console\Paycheck4.Console.csproj -c Release

if %ERRORLEVEL% EQU 0 (
    echo.
    echo Build successful!
    echo Output: src\Paycheck4.Console\bin\Release\net48\
    echo.
    echo To run on Raspberry Pi with Mono:
    echo   1. Copy contents of bin\Release\net48\ to Pi
    echo   2. Run: mono Paycheck4.Console.exe
) else (
    echo.
    echo Build failed!
)

pause
