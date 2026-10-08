@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-platform\Run.ps1"
if errorlevel 1 pause
