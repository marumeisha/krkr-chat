@echo off
setlocal
cd /d "%~dp0.."
powershell.exe -ExecutionPolicy Bypass -File ".\scripts\build-desktop-client-installer.ps1"