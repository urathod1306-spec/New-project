@echo off
rem ===== Create a backup ZIP in the "backups" folder (can run while the intranet is running) =====
cd /d "%~dp0"
NabconsIntranet.exe --backup
pause
