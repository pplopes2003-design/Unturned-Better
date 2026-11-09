@echo off
setlocal
cd /d "%~dp0AIService"
where py >nul 2>&1
if %errorlevel%==0 (
  py -3 server.py --port 8765
) else (
  python server.py --port 8765
)
echo.
echo Servico IA finalizado. Pressione qualquer tecla para fechar.
pause >nul
