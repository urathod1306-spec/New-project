@echo off
rem ===== Stop the background service (e.g. before a restore). Run as Administrator. =====
net session >nul 2>&1
if %errorlevel% neq 0 (
  echo Please right-click this file and choose "Run as administrator".
  pause & exit /b 1
)
sc stop NABCONSIntranet
pause
