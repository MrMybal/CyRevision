@echo off
setlocal
if "%~1"=="" (
  echo Usage: %~nx0 VERSION
  echo Example: %~nx0 0.1.0
  exit /b 2
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-release.ps1" -Version "%~1"
exit /b %errorlevel%
