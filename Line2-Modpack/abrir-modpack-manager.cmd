@echo off
setlocal
cd /d "%~dp0ModpackManager"
where py >nul 2>&1
if %errorlevel%==0 (
  py -3 gui.py
) else (
  python gui.py
)
echo.
echo Modpack Manager finalizado. Pressione qualquer tecla para fechar.
pause >nul
