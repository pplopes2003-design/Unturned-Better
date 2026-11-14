@echo off
setlocal EnableExtensions EnableDelayedExpansion
rem Windows CMD entrypoint avoids PowerShell execution-policy restrictions.
rem Builds diagnostic-only F8-F12 module; it does not enable NPC control.
if "%~1"=="" (
  echo Usage: build_diagnostics_windows.cmd "C:\Program Files (x86)\Steam\steamapps\common\Unturned"
  exit /b 2
)
set "GAME=%~1"
set "PROJECT=%~dp0..\ClientModule\Unturned2.Line2.Probe.csproj"
set "BUILT=%~dp0..\ClientModule\bin\Release\net472\Unturned2.Line2.Probe.dll"
set "MANIFEST=%~dp0..\ClientModule\Unturned2.Line2.module"
set "DEST=%GAME%\Modules\Unturned2.Line2"

if not exist "%GAME%\Unturned.exe" (
  echo ERROR: Unturned.exe not found in "%GAME%"
  exit /b 3
)
where dotnet >nul 2>nul
if errorlevel 1 (
  echo ERROR: Install .NET SDK and the .NET Framework 4.7.2 Developer Pack.
  exit /b 4
)
dotnet --list-sdks
dotnet build "%PROJECT%" -c Release "-p:UnturnedDir=%GAME%"
if errorlevel 1 (
  echo BUILD FAILED. Nothing was installed. Copy the full error log.
  exit /b 5
)
if not exist "%BUILT%" (
  echo ERROR: Compiler succeeded but module DLL was not found at "%BUILT%"
  exit /b 6
)
tasklist /FI "IMAGENAME eq Unturned.exe" | find /I "Unturned.exe" >nul
if not errorlevel 1 (
  echo ERROR: Close Unturned before installing or replacing its module.
  exit /b 7
)
if exist "%DEST%" (
  rem Preserve the previously installed module before replacing it.
  for /f %%i in ('powershell -NoProfile -Command "Get-Date -Format yyyyMMdd-HHmmss"') do set "STAMP=%%i"
  if not defined STAMP (
    echo ERROR: Could not create a backup timestamp. Existing module was left untouched.
    exit /b 8
  )
  set "BACKUP=%DEST%.backup-!STAMP!"
  if exist "!BACKUP!" (
    echo ERROR: Backup destination already exists: "!BACKUP!"
    exit /b 8
  )
  move "%DEST%" "!BACKUP!" >nul
  if errorlevel 1 (
    echo ERROR: Could not back up existing module. Nothing was replaced.
    exit /b 8
  )
  echo Previous module backed up to "!BACKUP!"
)
mkdir "%DEST%"
if errorlevel 1 exit /b 9
copy /Y "%BUILT%" "%DEST%\Unturned2.Line2.Probe.dll" >nul
if errorlevel 1 exit /b 10
copy /Y "%MANIFEST%" "%DEST%\Unturned2.Line2.module" >nul
if errorlevel 1 exit /b 11
echo SUCCESS: diagnostics installed to "%DEST%"
echo Test with Steam Unturned without BattlEye: F8 world, F9 AI, F10 APIs, F11 zombies, F12 NPCs.
exit /b 0
