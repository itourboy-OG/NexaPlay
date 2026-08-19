@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-installer.ps1"
set "NEXAPLAY_EXIT=%ERRORLEVEL%"
if not "%NEXAPLAY_EXIT%"=="0" pause
exit /b %NEXAPLAY_EXIT%
