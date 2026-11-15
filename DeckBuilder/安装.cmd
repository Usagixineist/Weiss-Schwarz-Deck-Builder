@echo off
rem Weiss Schwarz Deck Builder - double-click installer for Windows.
rem It only verifies the program files and copies the optional BepInEx plugin.
setlocal
set "SCRIPT=%~dp0install.ps1"
if not exist "%SCRIPT%" goto :missing

echo Installing Weiss Schwarz Deck Builder ...
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" %*
set "CODE=%ERRORLEVEL%"
echo.
if not "%CODE%"=="0" echo Install failed with exit code %CODE%.
pause
exit /b %CODE%

:missing
echo [ERROR] install.ps1 not found next to this file: "%SCRIPT%"
pause
exit /b 1
