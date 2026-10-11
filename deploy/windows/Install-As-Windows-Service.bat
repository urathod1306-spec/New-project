@echo off
rem ===== Run the intranet permanently in the background, starting automatically with Windows =====
rem ===== (run as Administrator). Use this after the pilot works with Start-Intranet.bat.     =====
set SERVICE=NABCONSIntranet
net session >nul 2>&1
if %errorlevel% neq 0 (
  echo Please right-click this file and choose "Run as administrator".
  pause & exit /b 1
)
cd /d "%~dp0"
sc query %SERVICE% >nul 2>&1
if %errorlevel% equ 0 (
  echo The service is already installed. Use Uninstall-Windows-Service.bat first to reinstall.
  pause & exit /b 1
)
sc create %SERVICE% binPath= "\"%~dp0NabconsIntranet.exe\"" start= delayed-auto DisplayName= "NABCONS Intranet"
sc description %SERVICE% "NABCONS Intranet web application (LAN only). Folder: %~dp0"
sc failure %SERVICE% reset= 86400 actions= restart/60000/restart/60000/restart/300000
sc start %SERVICE%
echo.
echo Installed and started. The intranet now runs in the background and restarts with the PC.
echo Manage it from: Start menu ^> "Services" ^> "NABCONS Intranet".
echo Logs are in the "logs" folder next to this file.
pause
