# NABCONS Intranet — Deployment Guide

This guide covers the office-PC pilot (no installation) and the later move to a Windows Server / IIS. Nothing in the intranet connects to the internet at runtime: there is no CDN, no cloud API, no licence check, no telemetry and no update check.

---

## 1. What you need

| Item | Requirement |
|---|---|
| PC / server | Windows 10 or 11 (64-bit), or Windows Server 2016 or later |
| Memory / disk | 4 GB RAM, 1 GB free disk plus space for documents and backups |
| Software | **None.** The package contains its own .NET runtime and SQLite. No .NET, IIS, Java, Docker or database server needs to be installed. |
| Network | A fixed IP address or DNS name on the NABARD LAN, and one inbound TCP port (default **8080**) |
| Browsers | Microsoft Edge or Google Chrome (current versions) |

## 2. Get the package

1. On an approved internet-connected machine, open the GitHub repository and download
   `release/NABCONS-Intranet-v1.3.0-win-x64.zip`. Its SHA-256 checksum is in the `.sha256` file next to it.
2. Transfer the ZIP into the NABARD LAN **only by the IT-approved method** (approved media or transfer gateway), with malware scanning and a change ticket.
3. On the office PC, check the checksum in PowerShell:
   ```powershell
   Get-FileHash .\NABCONS-Intranet-v1.3.0-win-x64.zip -Algorithm SHA256
   ```
   It must match the `.sha256` file. Inside the ZIP, `SHA256SUMS.txt` lists the checksum of every file, which helps with allow-listing.
4. Right-click the ZIP → **Properties** → tick **Unblock** if shown → OK. Then extract it to `C:\NABCONSIntranet\`.

Resulting layout (the suggested structure from the design note):

```
C:\NABCONSIntranet\NABCONS-Intranet\
  NabconsIntranet.exe          application (self-contained)
  e_sqlite3.dll                SQLite engine
  appsettings.json             all settings (edit with Notepad)
  wwwroot\                     local styles, scripts and icons
  *.bat                        start / service / backup / restore helpers
  docs\                        these guides
  data\intranet.db             created on first start — restrict to administrators
  documents\                   uploaded files — never share this folder
  logs\                        daily log files
  backups\                     daily backup ZIPs + .sha256 checksums
```

## 3. First start (pilot mode)

1. Double-click **`Start-Intranet.bat`**. A console window opens and shows:
   ```
   NABCONS Intranet is running.  KEEP THIS WINDOW OPEN.
   On this PC:         http://localhost:8080
   From other LAN PCs: http://officepc01:8080
   ```
2. If Windows Defender Firewall asks, allow **Domain/Private** networks only. To let other PCs connect, right-click **`Open-Firewall-Port.bat`** → *Run as administrator*. Opening the port needs local administrator rights. If you do not have them, ask IT to create the inbound rule for TCP 8080.
3. Browse to `http://localhost:8080` and sign in with a demo account (password `Nabcons@2026`):
   `admin`, `publisher`, `approver1`, `approver2`, `staff`, `auditor`.

The first start creates the database, the folders, the master lists and the synthetic demo data: 25 staff, 11 circulars in 3 categories, 4 requests with approval history, and 2 notices.

> If the window says the port is already in use, either the intranet is already running (check for another window or the Windows service), or another program is using port 8080. Change `"Urls"` in `appsettings.json` to another approved port (e.g. `http://0.0.0.0:8081`) and update `PORT` in `Open-Firewall-Port.bat`.

## 4. Run permanently as a Windows service

Once the pilot works, right-click **`Install-As-Windows-Service.bat`** → *Run as administrator*. This:

- registers the service **NABCONS Intranet** (`NABCONSIntranet`), with delayed automatic start at boot;
- restarts it automatically if it stops unexpectedly;
- runs it without anyone signed in. No console window is needed.

`Stop-Service.bat`, `Start-Service.bat` and `Uninstall-Windows-Service.bat` manage it. Uninstalling never deletes data.

**Service account (recommended by IT):** by default the service runs as *Local System*. For least privilege, IT can change it in *services.msc → NABCONS Intranet → Log On* to a dedicated low-privilege account with:
- Read & execute on the application folder;
- Modify on `data\`, `documents\`, `logs\` and `backups\` only;
- Write access to the `SecondaryBackupFolder` share, if one is configured.

## 5. Settings (`appsettings.json`)

Edit with Notepad, save, then restart the intranet (close and reopen the window, or `Stop-Service` / `Start-Service`).

| Setting | Default | Meaning |
|---|---|---|
| `Urls` | `http://0.0.0.0:8080` | Address and port. Change only if IT assigns a different port (e.g. `http://0.0.0.0:80` for no port in the URL, if port 80 is free and permitted). |
| `OrganisationName`, `SiteTitle`, `Tagline`, `FooterText` | NABCONS | Branding shown on every page |
| `AuthenticationMode` | `Local` | `Local` = intranet accounts; `Windows` = Windows Authentication / AD (section 7) |
| `InitialAdminPassword` | `Admin@12345` | Password given to `admin` on a fresh database or after `Reset-Admin-Password.bat` (it must be changed at first sign-in) |
| `SeedDemoData` | `true` | Loads synthetic demo data into an **empty** database |
| `ShowDemoAccountsOnLogin` | `true` | Shows the demo-accounts box on the sign-in page |
| `DailyBackupTime` | `19:30` | Time of the automatic daily backup (PC local time) |
| `BackupRetentionDays` | `30` | Older backup ZIPs are removed |
| `SecondaryBackupFolder` | *(empty)* | Approved LAN share for an off-machine copy, e.g. `\\\\fileserver\\NABCONSBackup` (backslashes are doubled in JSON) |
| `MaxUploadMB` | `20` | Maximum size of one uploaded file |
| `AllowedUploadExtensions` | pdf, docx, xlsx, … | Allowed file types. File content is also checked. |
| `SessionTimeoutMinutes` | `30` | Idle sign-out (local accounts) |
| `MaxFailedLogins` / `LockoutMinutes` | `5` / `15` | Account lockout |
| `Notifications:PollSeconds` (v1.3) | `60` | How often open pages refresh the bell and badges (one small request; minimum 30). The request pauses while the browser tab is hidden and never extends a session. |
| `Notifications:RetainReadDays` (v1.3) | `90` | Read notifications older than this are removed by the daily housekeeping (after the daily backup) |
| `Notifications:RetainUnreadDays` (v1.3) | `365` | Unread notifications older than this are removed |
| `ENote:Enabled` (v1.3) | `true` | `false` hides e-Note and makes its pages unavailable (data is kept) |
| `ENote:ApprovedVisibility` | `AllStaff` | `AllStaff`: approved, non-confidential notes are visible to every signed-in user. `Participants`: only to the people the note passed through |
| `ENote:MaxBodyChars` | `50000` | Maximum length of a note's text |
| `ENote:MaxFileMB` / `MaxNoteTotalMB` / `MaxFilesPerNote` | `10` / `100` / `50` | Attachment limits per file and per note |
| `ENote:AgeingAlertDays` | `7` | A holder gets a daily reminder for notes pending with them longer than this |
| `ENote:NumberPrefix` | `NCS/EN` | First part of e-Note numbers (`NCS/EN/<vertical code>/<FY>/<serial>`) |
| `ENote:AllowedExtensions` | pdf, docx, doc, xlsx, xls, pptx, jpg, png, txt, csv | Attachment types (content is also checked) |
| `DataFolder`, `DocumentsFolder`, `BackupFolder`, `LogFolder` | relative | May be absolute paths, e.g. `D:\\NABCONSData\\documents` |

The `Notifications` section (and, from 1.3, `ENote`) sits at the top level of `appsettings.json`, next to `"Intranet"`. If a section or setting is missing, the default above applies.

## 6. HTTPS (recommended before wider LAN use)

Ask NABARD IT for a certificate from the internal certificate authority, issued to the intranet's DNS name (e.g. `nabcons-intranet.<internal-domain>`), as a `.pfx` file. Copy it to the application folder and replace the `"Urls"` line in `appsettings.json` with:

```json
"Kestrel": {
  "Endpoints": {
    "Https": {
      "Url": "https://0.0.0.0:443",
      "Certificate": { "Path": "nabcons-intranet.pfx", "Password": "<pfx password>" }
    }
  }
},
```

Then open port 443 instead of 8080 in the firewall (edit `PORT` in `Open-Firewall-Port.bat`). Users browse to `https://nabcons-intranet.<internal-domain>`. Cookies are automatically marked *Secure* over HTTPS. Restrict NTFS access to the `.pfx` and `appsettings.json` to administrators and the service account.

## 7. Windows Authentication / Active Directory (if NABARD IT permits)

When enabled, staff are signed in automatically with their Windows account and no intranet passwords exist.

1. In `appsettings.json` set:
   ```json
   "AuthenticationMode": "Windows",
   "WindowsAdministrators": [ "NABARD\\your.username" ],
   "WindowsGroupRoles": {
     "NABARD\\NABCONS-Intranet-Publishers": "Publisher",
     "NABARD\\NABCONS-Intranet-Approvers": "Approver",
     "NABARD\\NABCONS-Intranet-Auditors": "Auditor"
   },
   "WindowsAutoProvisionStaff": true,
   ```
   (Use the real domain and group names supplied by IT.)
2. The PC or server must be joined to the domain. For Kerberos, IT registers an SPN for the service account and host name, e.g. `setspn -S HTTP/nabcons-intranet NABARD\svc-nabcons-intranet`. Without an SPN, Windows falls back to NTLM.
3. Add the intranet URL to the browsers' **Local intranet** zone (usually through Group Policy) so that Edge and Chrome sign in silently.
4. Restart. Every user who signs in appears under *Administration → Users & roles* with the Staff role. Additional roles come from AD groups (above) or can be assigned there.

## 8. Moving to Windows Server + IIS (optional, later)

The pilot runs on the application's built-in web server (Kestrel), which is a supported production server. If IT prefers IIS:

1. Install IIS and the **ASP.NET Core 10 Hosting Bundle** on the server.
2. Build a framework-dependent package (`dotnet publish -c Release -r win-x64 --self-contained false`) or use the same folder.
3. Create an IIS site pointing to the folder, with a dedicated application pool (*No Managed Code*). Bind it to the exact host name with the internal certificate.
4. Grant the application-pool identity (`IIS AppPool\NABCONSIntranet`) *Modify* on `data\`, `documents\`, `logs\` and `backups\`, and *Read & execute* on the rest.
5. For Windows Authentication under IIS, enable *Windows Authentication* and disable *Anonymous* on the site, and keep `"AuthenticationMode": "Windows"`.
6. Do not run the Windows-service and IIS versions at the same time against the same folder.

## 9. Upgrading to a new version

Extract the new version to a temporary folder and run its **`Upgrade-Existing-Intranet.bat`** as administrator. It backs up, keeps the previous program for roll-back, and replaces only the program files: data, documents, backups, logs and `appsettings.json` are kept. Full steps, checks and roll-back are in **[UPGRADE-GUIDE.md](UPGRADE-GUIDE.md)**.

## 10. Rebuilding the package yourself (developers only)

On any machine with the .NET 10 SDK, run `build/package.sh`, which writes `release/NABCONS-Intranet-v<version>-win-x64.zip`. The GitHub Actions workflow `.github/workflows/build-release.yml` does the same automatically and attaches the ZIP to the workflow run. All NuGet package versions are pinned (see `docs/SBOM.md`).
