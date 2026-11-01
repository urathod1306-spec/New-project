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
if %errorlevel% equ 0 (
  set "WASSERVICE=1"
  echo Stopping the Windows service ...
  sc stop %SERVICE% >nul 2>&1
  rem wait up to 60 seconds for the service to report STOPPED
  for /L %%i in (1,1,30) do (
    sc query %SERVICE% | find "STOPPED" >nul && goto stopped
    timeout /t 2 /nobreak >nul
  )
)
:stopped
tasklist /FI "IMAGENAME eq NabconsIntranet.exe" 2>nul | find /I "NabconsIntranet.exe" >nul
if %errorlevel% equ 0 (
  echo.
  echo The intranet is still running. Close its black window, then press any key.
  pause >nul
  tasklist /FI "IMAGENAME eq NabconsIntranet.exe" 2>nul | find /I "NabconsIntranet.exe" >nul
  if not errorlevel 1 ( echo Still running - roll-back cancelled, nothing was changed. & pause & exit /b 1 )
)
robocopy "previous-version" "." /E /NFL /NDL /NJH /NJS /NP >nul
if %errorlevel% geq 8 (
  echo.
  echo ROLL-BACK FAILED: the previous program files could not be copied back ^(robocopy error %errorlevel%^).
  echo Nothing in data or documents was touched. Close any open program windows and run this again,
  echo or ask IT to copy the contents of "previous-version" into this folder.
  if "%WASSERVICE%"=="1" sc start %SERVICE% >nul
  pause & exit /b 1
)
if "%WASSERVICE%"=="1" sc start %SERVICE% >nul
echo.
echo Previous program restored.
echo Your data is unchanged and works with the previous program. Only if something looks
echo wrong with the DATA itself, run Restore-Backup.bat and choose the backup ZIP taken at the
echo time of the upgrade (newest ZIP in the "backups" folder from that date).
pause
