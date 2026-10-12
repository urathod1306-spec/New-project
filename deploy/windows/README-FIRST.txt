NABCONS INTRANET - QUICK START (no installation, no internet needed)
=====================================================================

1. COPY THIS FOLDER
   Copy the whole "NABCONS-Intranet" folder to the office PC, for example to
       C:\NABCONSIntranet\
   (Use the transfer method approved by NABARD IT. Do not run it from a USB stick.)

2. START IT
   Double-click  Start-Intranet.bat
   A black window opens and shows the web address. KEEP THAT WINDOW OPEN.
   If Windows asks "Allow access?" choose "Private / Domain networks" only.

3. OPEN IT
   On this PC:          http://localhost
   From other LAN PCs:  http://<THIS-PC-NAME>      (address shown in the black window)
   The intranet uses the standard web port 80, so no ":port" is needed in the address.
   If other PCs cannot connect, right-click Open-Firewall-Port.bat > Run as administrator.

4. SIGN IN (pilot demo accounts, password  Nabcons@2026 )
       admin      - Administrator
       publisher  - publishes circulars and notices
       approver1  - Reviewing Officer
       approver2  - Sanctioning Authority
       staff      - ordinary staff member
       auditor    - read-only audit access
   All staff, circulars and requests you see are SYNTHETIC sample data.

5. RUN IT PERMANENTLY (optional, after the pilot works)
   Right-click Install-As-Windows-Service.bat > Run as administrator.
   The intranet then runs in the background and restarts automatically with the PC.

6. GOING LIVE WITH REAL DATA
   See docs\ADMIN-GUIDE.md, section "Going live". In short: stop the intranet,
   open appsettings.json in Notepad, set "SeedDemoData": false and
   "ShowDemoAccountsOnLogin": false, delete the "data" and "documents" folders,
   start again and sign in as  admin / Admin@12345  (you must change it at once).

OTHER TOOLS IN THIS FOLDER
   Backup-Now.bat              make a backup now (also runs automatically every day at 19:30)
   Restore-Backup.bat          restore a backup (stop the intranet first)
   Reset-Admin-Password.bat    if the administrator is locked out
   Verify-Audit-Log.bat        check the audit log has not been tampered with
   Stop-Service.bat / Start-Service.bat / Uninstall-Windows-Service.bat

FOLDERS (created automatically)
   data\        the database (intranet.db)      - restrict to administrators
   documents\   uploaded circulars/attachments  - never share this folder
   backups\     daily backup ZIPs + checksums   - copy off this PC regularly
   logs\        daily log files

If the window says the port is already in use, another web server (for example IIS)
is using port 80 - ask IT, or change "Urls" in appsettings.json to another approved port.

Settings (port, organisation name, backup time, upload limits, Windows sign-in)
are in appsettings.json - edit with Notepad, then restart the intranet.
