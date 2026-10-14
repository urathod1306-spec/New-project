@echo off
rem =====================================================================================
rem  NABCONS Intranet - go back to the program version that was installed before the
rem  last upgrade. Run from the installed folder, as Administrator.
rem =====================================================================================
setlocal
cd /d "%~dp0"
set "SERVICE=NABCONSIntranet"
net session >nul 2>&1
if %errorlevel% neq 0 (
  echo Please right-click this file and choose "Run as administrator".
  pause & exit /b 1
)
if not exist "previous-version\NabconsIntranet.exe" (
  echo No previous version is stored in this folder. Nothing was changed.
  pause & exit /b 1
)
echo This puts back the previous PROGRAM files. Data is not touched.
choice /M "Continue"
if errorlevel 2 exit /b 1
set "WASSERVICE=0"
sc query %SERVICE% >nul 2>&1
if %errorlevel% equ 0 ( set "WASSERVICE=1" & sc stop %SERVICE% >nul 2>&1 & timeout /t 10 /nobreak >nul )
robocopy "previous-version" "." /E /NFL /NDL /NJH /NJS /NP >nul
if "%WASSERVICE%"=="1" sc start %SERVICE% >nul
echo.
echo Previous program restored.
echo Your data is unchanged and works with the previous program. Only if something looks
echo wrong with the DATA itself, run Restore-Backup.bat and choose the backup ZIP taken at the
echo time of the upgrade (newest ZIP in the "backups" folder from that date).
pause
