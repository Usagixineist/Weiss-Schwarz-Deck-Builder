@echo off
rem One-click sync: copy DeckBuilder and the BepInEx plugin files from the game
rem folder into ..\_repo, then print git status (ready for GitHub Desktop).
rem Extra arguments are passed through to sync-to-repo.ps1, for example:
rem   -DryRun                          only list what would be copied
rem   -Commit -Message "fix: ..."      also commit the synced files
rem   -Commit -Push -Message "..."     commit and push to GitHub
setlocal
set "SCRIPT=%~dp0sync-to-repo.ps1"
if not exist "%SCRIPT%" goto :missing

echo Syncing DeckBuilder to _repo ...
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" %*
set "CODE=%ERRORLEVEL%"
echo.
if not "%CODE%"=="0" echo Sync failed with exit code %CODE%.
pause
exit /b %CODE%

:missing
echo [ERROR] sync-to-repo.ps1 not found next to this file: "%SCRIPT%"
pause
exit /b 1
