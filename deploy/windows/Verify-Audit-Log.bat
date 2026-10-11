@echo off
rem ===== Check that the audit log has not been tampered with =====
cd /d "%~dp0"
NabconsIntranet.exe --verify-audit
pause
