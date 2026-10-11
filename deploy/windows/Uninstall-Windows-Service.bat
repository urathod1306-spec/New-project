@echo off
rem ===== Stop and remove the background service (data is NOT deleted). Run as Administrator. =====
set SERVICE=NABCONSIntranet
net session >nul 2>&1
if %errorlevel% neq 0 (
  echo Please right-click this file and choose "Run as administrator".
  pause & exit /b 1
)
sc stop %SERVICE% >nul 2>&1
timeout /t 5 /nobreak >nul
sc delete %SERVICE%
echo Service removed. Your data, documents and backups are untouched.
pause
