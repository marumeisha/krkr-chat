@echo off
powershell.exe -ExecutionPolicy Bypass -File ".\scripts\build-client-installer.ps1" -Version 1.0.2 -ApiBaseUrl "https://krkr.chat"
pause