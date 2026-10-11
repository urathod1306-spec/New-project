@echo off
rem ===== Allow other PCs on the office LAN to reach the intranet (run as Administrator) =====
set PORT=8080
net session >nul 2>&1
if %errorlevel% neq 0 (
  echo Please right-click this file and choose "Run as administrator".
  pause & exit /b 1
)
netsh advfirewall firewall delete rule name="NABCONS Intranet" >nul 2>&1
netsh advfirewall firewall add rule name="NABCONS Intranet" dir=in action=allow protocol=TCP localport=%PORT% profile=domain,private
echo.
echo Done. Port %PORT% is open for the Domain and Private networks only (not Public).
echo If NABARD IT restricts access to certain LAN segments, they can edit the rule "NABCONS Intranet"
echo in "Windows Defender Firewall with Advanced Security" and set allowed Remote IP addresses.
pause
