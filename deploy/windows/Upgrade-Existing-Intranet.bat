@echo off
rem =====================================================================================
rem  NABCONS Intranet - upgrade an EXISTING installation to this new version.
rem  Run this file from the NEW (freshly extracted) folder, as Administrator.
rem  Your data, documents, backups, logs and appsettings.json are NOT replaced.
rem =====================================================================================
setlocal
cd /d "%~dp0"
set "NEW=%~dp0."
set "DEFAULT=C:\NABCONSIntranet\NABCONS-Intranet"
set "SERVICE=NABCONSIntranet"
set "FAILED=0"

net session >nul 2>&1
if %errorlevel% neq 0 (
  echo Please right-click this file and choose "Run as administrator".
  pause & exit /b 1
)

echo.
echo  This upgrades your existing NABCONS Intranet to the version in this folder.
echo  Users, circulars, requests, documents, backups and settings are kept.
echo.
set /p "TARGET=Folder of the EXISTING intranet [%DEFAULT%]: "
if "%TARGET%"=="" set "TARGET=%DEFAULT%"
set "TARGET=%TARGET:"=%"
if "%TARGET:~-1%"=="\" set "TARGET=%TARGET:~0,-1%"

if not exist "%TARGET%\NabconsIntranet.exe" (
  echo.
  echo  No intranet found in "%TARGET%". Nothing was changed.
  pause & exit /b 1
)
if /i "%TARGET%"=="%~dp0" goto samefolder
if /i "%TARGET%\"=="%~dp0" goto samefolder
if not exist "%TARGET%\data\intranet.db" (
  echo  WARNING: no database found in "%TARGET%\data". Continuing will simply update the program files.
)

rem ---- 1. stop the intranet --------------------------------------------------------
set "WASSERVICE=0"
sc query %SERVICE% >nul 2>&1
if %errorlevel% equ 0 (
  set "WASSERVICE=1"
  echo  [1/5] Stopping the Windows service ...
  sc stop %SERVICE% >nul 2>&1
  timeout /t 10 /nobreak >nul
) else (
  echo  [1/5] Intranet is not installed as a service.
)
tasklist /FI "IMAGENAME eq NabconsIntranet.exe" 2>nul | find /I "NabconsIntranet.exe" >nul
if %errorlevel% equ 0 (
  echo.
  echo  The intranet is still running. Close its black window, then press any key.
  pause >nul
  tasklist /FI "IMAGENAME eq NabconsIntranet.exe" 2>nul | find /I "NabconsIntranet.exe" >nul
  if not errorlevel 1 ( echo  Still running - upgrade cancelled, nothing was changed. & pause & exit /b 1 )
)

rem ---- 2. full backup with the CURRENT program -------------------------------------
echo  [2/5] Taking a full backup ...
pushd "%TARGET%"
NabconsIntranet.exe --backup
set "RC=%errorlevel%"
popd
if not "%RC%"=="0" (
  echo  Backup FAILED - upgrade cancelled, nothing was changed.
  set "FAILED=1"
  goto restart
)

rem ---- 3. keep the current program files for roll-back -----------------------------
echo  [3/5] Keeping the current program in "%TARGET%\previous-version" ...
if exist "%TARGET%\previous-version" rmdir /s /q "%TARGET%\previous-version"
robocopy "%TARGET%" "%TARGET%\previous-version" /E /XD data documents backups logs previous-version "data.*" "documents.*" /NFL /NDL /NJH /NJS /NP >nul
if %errorlevel% geq 8 ( echo  Could not save the current program - upgrade cancelled. & set "FAILED=1" & goto restart )

rem ---- 4. copy the new program (never data, documents, backups, logs, settings) -----
echo  [4/5] Installing the new program files ...
robocopy "%NEW%" "%TARGET%" /E /XD data documents backups logs previous-version "data.*" "documents.*" /XF appsettings.json /NFL /NDL /NJH /NJS /NP >nul
if %errorlevel% geq 8 (
  echo  Copy FAILED. Restoring the previous program ...
  robocopy "%TARGET%\previous-version" "%TARGET%" /E /NFL /NDL /NJH /NJS /NP >nul
  set "FAILED=1"
  goto restart
)
copy /y "%NEW%\appsettings.json" "%TARGET%\appsettings.new-version.json" >nul

:restart
rem ---- 5. start again ---------------------------------------------------------------
if "%FAILED%"=="1" (
  echo.
  echo  UPGRADE NOT APPLIED. Your intranet is unchanged and will be restarted as it was.
)
if "%WASSERVICE%"=="1" (
  echo  [5/5] Starting the Windows service ...
  sc start %SERVICE% >nul
  echo.
  echo  Open the intranet in a browser and check Administration ^> System information
  echo  shows the expected version.
) else (
  echo  [5/5] Start the intranet with "%TARGET%\Start-Intranet.bat".
)
echo.
echo  If anything is wrong, run "%TARGET%\Rollback-Upgrade.bat" as Administrator.
pause
exit /b 0

:samefolder
echo.
echo  Run this file from the NEW extracted folder, not from the existing installation.
pause & exit /b 1
