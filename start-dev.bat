@echo off
setlocal

set "PROJECT_ROOT=%~dp0"
set "BACKEND_URL=http://127.0.0.1:5065/swagger/index.html"
set "FRONTEND_URL=http://127.0.0.1:5173"

title AI Study Hub - Development Launcher

where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] .NET SDK was not found in PATH.
    pause
    exit /b 1
)

where npm.cmd >nul 2>&1
if errorlevel 1 (
    echo [ERROR] npm was not found in PATH.
    pause
    exit /b 1
)

if not exist "%PROJECT_ROOT%frontend\node_modules" (
    echo [ERROR] Frontend dependencies are missing.
    echo Run: npm.cmd ci
    echo From: %PROJECT_ROOT%frontend
    pause
    exit /b 1
)

echo [1/3] Checking backend...
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
    "try { $response = Invoke-WebRequest -Uri '%BACKEND_URL%' -UseBasicParsing -TimeoutSec 2; if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) { exit 0 } } catch {}; exit 1"

if errorlevel 1 (
    echo Starting backend...
    start "AI Study Hub - Backend" /D "%PROJECT_ROOT%backend" cmd /k dotnet run --project "src\AIStudyHub.Api\AIStudyHub.Api.csproj" --launch-profile http
) else (
    echo Backend is already running.
)

echo Waiting for backend at %BACKEND_URL% ...
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
    "$deadline = (Get-Date).AddSeconds(60); while ((Get-Date) -lt $deadline) { try { $response = Invoke-WebRequest -Uri '%BACKEND_URL%' -UseBasicParsing -TimeoutSec 2; if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) { exit 0 } } catch {}; Start-Sleep -Seconds 1 }; exit 1"

if errorlevel 1 (
    echo [ERROR] Backend did not become ready within 60 seconds.
    echo Check the Backend window for the actual startup error.
    pause
    exit /b 1
)

echo [2/3] Backend is ready. Checking frontend...
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
    "try { $response = Invoke-WebRequest -Uri '%FRONTEND_URL%' -UseBasicParsing -TimeoutSec 2; if ($response.StatusCode -eq 200) { exit 0 } } catch {}; exit 1"

if errorlevel 1 (
    echo Starting frontend...
    start "AI Study Hub - Frontend" /D "%PROJECT_ROOT%frontend" cmd /k npm.cmd run dev -- --host 127.0.0.1 --port 5173 --strictPort
) else (
    echo Frontend is already running.
)

echo Waiting for frontend at %FRONTEND_URL% ...
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
    "$deadline = (Get-Date).AddSeconds(45); while ((Get-Date) -lt $deadline) { try { $response = Invoke-WebRequest -Uri '%FRONTEND_URL%' -UseBasicParsing -TimeoutSec 2; if ($response.StatusCode -eq 200) { exit 0 } } catch {}; Start-Sleep -Seconds 1 }; exit 1"

if errorlevel 1 (
    echo [ERROR] Frontend did not become ready within 45 seconds.
    echo Check the Frontend window for the actual startup error.
    pause
    exit /b 1
)

echo [3/3] Opening AI Study Hub...
start "" "%FRONTEND_URL%"

echo.
echo AI Study Hub is running.
echo Backend:  http://127.0.0.1:5065
echo Frontend: %FRONTEND_URL%
echo Close the Backend and Frontend windows to stop the servers.
echo.
pause

endlocal
