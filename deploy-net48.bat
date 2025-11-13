@echo off
REM Deploy script for copying .NET Framework 4.8 build to Raspberry Pi

set PI_USER=kcondict
set PI_HOST=192.168.68.69
set PI_PATH=~/paycheck4/
set LOCAL_PATH=src\Paycheck4.Console\bin\Release\net48\*

echo Deploying to %PI_USER%@%PI_HOST%:%PI_PATH%...

scp -r %LOCAL_PATH% %PI_USER%@%PI_HOST%:%PI_PATH%

if %ERRORLEVEL% EQU 0 (
    echo.
    echo Deployment successful!
    echo.
    echo To run on Pi:
    echo   ssh %PI_USER%@%PI_HOST%
    echo   cd paycheck4
    echo   mono Paycheck4.Console.exe
) else (
    echo.
    echo Deployment failed!
)

pause
