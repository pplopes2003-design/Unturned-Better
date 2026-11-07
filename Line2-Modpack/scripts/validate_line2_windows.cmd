@echo off
setlocal EnableExtensions
REM One consolidated offline validation for Line 2. NO Steam game files modified.
REM Usage: scripts\validate_line2_windows.cmd "C:\Program Files (x86)\Steam\steamapps\common\Unturned"
set "ROOT=%~dp0.."
set "OUTPUT=%ROOT%\test-results"
if not exist "%OUTPUT%" mkdir "%OUTPUT%"
if "%~1"=="" (
  echo Usage: scripts\validate_line2_windows.cmd "PATH_TO_STEAM_UNTURNED"
  exit /b 2
)
set "GAME=%~1"
if not exist "%GAME%\Unturned.exe" (
  echo ERROR: Missing Unturned.exe in "%GAME%"
  exit /b 3
)
where python >nul 2>nul
if errorlevel 1 (
  echo ERROR: Python 3.11+ not found.
  exit /b 4
)
echo [1/4] Python syntax
python -m compileall -q "%ROOT%"
if errorlevel 1 (
  echo FAILED: syntax / compileall
  exit /b 5
)
echo [2/4] AI Service tests
python "%ROOT%\run_tests.py" AIService
if errorlevel 1 (
  echo FAILED: AIService tests; see "%OUTPUT%\AIService-unittest.txt"
  exit /b 6
)
echo [3/4] Modpack Manager tests
python "%ROOT%\run_tests.py" ModpackManager
if errorlevel 1 (
  echo FAILED: ModpackManager tests; see "%OUTPUT%\ModpackManager-unittest.txt"
  exit /b 7
)
echo [4/4] Steam C# compile check
where dotnet >nul 2>nul
if errorlevel 1 (
  echo BLOCKED: .NET SDK unavailable. No Steam module installed.
  exit /b 8
)
dotnet --list-sdks
dotnet build "%ROOT%\ClientModule\Unturned2.Line2.Probe.csproj" -c Release "-p:UnturnedDir=%GAME%"
if errorlevel 1 (
  echo FAILED: C# Steam compile; no game files modified.
  exit /b 9
)
echo VALIDATION PASSED: Python tests and local C# compilation completed.
echo WARNING: This does NOT mean NPCs, radio or zombie changes work in-game.
exit /b 0
