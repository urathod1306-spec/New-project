@echo off
rem ===== Restore a backup ZIP. STOP THE INTRANET FIRST (close its window or run Stop-Service.bat). =====
cd /d "%~dp0"
echo This will replace the current data with the contents of a backup.
echo The current data and documents folders are kept (renamed with ".before-restore-<date>").
echo.
set /p ZIP=Drag the backup .zip file into this window and press Enter: 
if "%ZIP%"=="" ( echo No file given. & pause & exit /b 1 )
NabconsIntranet.exe --restore %ZIP%
pause
