@echo off
rem ===== Make an existing, active intranet user a Compliance Admin (first one, or recovery when none is left) =====
rem The grant is audited and every existing Compliance Admin is notified in the intranet.
cd /d "%~dp0"
set /p NABUSER=Sign-in name of the user: 
NabconsIntranet.exe --grant-compliance-admin "%NABUSER%"
pause
