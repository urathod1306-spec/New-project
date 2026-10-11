@echo off
rem ===== Start the background service again. Run as Administrator. =====
net session >nul 2>&1
if %errorlevel% neq 0 (
  echo Please right-click this file and choose "Run as administrator".
  pause & exit /b 1
)
sc start NABCONSIntranet
pause
