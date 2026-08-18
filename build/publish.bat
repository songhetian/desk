@echo off
cd /d "%~dp0.."
powershell -ExecutionPolicy Bypass -File "build\publish.ps1"
pause
