@echo off
rem ===== Emergency: re-enable the "admin" account with the InitialAdminPassword from appsettings.json =====
cd /d "%~dp0"
NabconsIntranet.exe --reset-admin-password
pause
