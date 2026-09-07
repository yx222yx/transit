@echo off
powershell.exe -NoProfile -File "%~dp0start-windows-dev.ps1" -SkipBuild
if errorlevel 1 pause
