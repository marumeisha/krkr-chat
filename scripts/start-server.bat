@echo off
setlocal
cd /d "%~dp0.."
powershell.exe -ExecutionPolicy Bypass -File ".\scripts\start-server.ps1"
