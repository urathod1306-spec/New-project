# NABCONS Intranet 1.2.0 — Setup Guide (upgrade from 1.1.x)

> The intranet is one Windows program (`NabconsIntranet.exe`) with everything bundled. **Nothing needs to be installed and nothing is downloaded** — there is no Python, pip or Pillow. Photo processing uses the bundled `libSkiaSharp.dll`, which ships in the ZIP next to the program.

## 1. Upgrade the live intranet (keeps all data)
1. Download `release/NABCONS-Intranet-v1.2.0-win-x64.zip` from the approved branch. Transfer it by the IT-approved route and check its SHA-256.
2. Extract it to a **new** folder, e.g. `C:\NABCONSIntranet\new-version`.
3. Right-click **`Upgrade-Existing-Intranet.bat`** → *Run as administrator* → press Enter (or type the live folder). It:
   1. stops the intranet;
   2. takes a full backup;
   3. keeps the old program in `previous-version\`;
   4. copies the new program files (never `data`, `documents`, `backups`, `logs` or `appsettings.json`);
   5. restarts the intranet.
4. **Database upgrade.** It runs automatically on first start. To run it yourself first, with the intranet stopped, open a Command Prompt in the live folder and type:
   ```
   NabconsIntranet.exe --migrate
   ```
   This takes a full backup ZIP, plus a database safety copy (`backups\pre-upgrade-schema3-to-4-<date>.db`), then adds the new tables and lists. **Existing rows are never changed.** Running it again does nothing and says "Database already up to date".
5. Check *Administration → System information*: version 1.2.0, database schema 4.

Roll back with `Rollback-Upgrade.bat` if needed. The older program keeps working on the upgraded database.

## 2. New settings (`appsettings.json`)
Your existing file is kept, and new settings use the safe defaults below even if they are absent. To change one, add it inside the `"Intranet"` section and restart:

| Setting | Default | Meaning |
|---|---|---|
| `VerticalChangeRequiresApproval` | `true` | Vertical changes on My Profile wait for an administrator |
| `BusinessDevelopmentVerticalName` | `"BDU"` | Name of the Business Development vertical in *Master lists → Verticals* |
| `BusinessDevelopmentVerticalId` | `0` | Alternatively the vertical's ID. If not 0 it wins over the name, so renaming the vertical won't break access. The ID is the number in the master list (ask your developer, or use the name) |
| `OfficialEmailDomain` | `""` | e.g. `"nabcons.in"` to require official e-mails on My Profile to use that domain |
| `MaxPhotoMB` / `MaxLeadFileMB` / `MaxLeadTotalMB` | `2` / `10` / `25` | Upload limits |

*Administration → System information* shows which vertical the intranet treats as BDU.

## 3. After upgrading — one-time tasks for the administrator
1. **Check the BDU vertical.** *Master lists → Verticals* must contain **BDU**; the upgrade adds it if missing. Business Development staff must have BDU as their vertical on their user account (*Users & roles → Edit → Vertical*), or request it on My Profile and have it approved.
2. **Give the COO and MD the lead-viewer role.** *Administration → Users & roles* → open the COO's account → tick **LeadViewer** → Save. Repeat for the MD. They then see the pipeline, dashboard and export, and can update leads. Being an Administrator does **not** give access to all leads.
3. **Link accounts to directory entries.** The upgrade links accounts automatically only when unambiguous: a self-registration record, a mobile-number sign-in, or the same official e-mail. *Users & roles* shows **Not linked** for the rest. Open each one → *Staff directory entry* → choose the person → *Save link*. Until then, that person's My Profile asks them to contact you.
4. **Optional:** review the new master lists under *Master lists*: lead client types, sectors, sources and lost/dropped reasons.

## 4. Where things are stored
| What | Where | In backups |
|---|---|---|
| Staff photos | `data\uploads\photos\<userId>.jpg` and `<userId>_t.jpg` (outside the web folder) | Yes, inside each backup ZIP under `data/uploads/` |
| Lead attachments | `documents\leads\<year>\<random>.dat` (original name kept only as information) | Yes, under `documents/` |
| Leads, timeline, profile changes, audit | `data\intranet.db` | Yes |

**Office backup routine:** if you copy backups off the PC (recommended: `SecondaryBackupFolder`), nothing else is needed, because the backup ZIP already contains `data\uploads`. If IT backs up the application folder directly instead, make sure `data\uploads\` is included along with `data\` and `documents\`.

## 5. Firewall, port, start/stop
Unchanged from 1.1.x (port 8080; see `docs/DEPLOYMENT-GUIDE.md`).

## 6. For developers
- Build the package: `bash build/package.sh`, which writes `release/*.zip`.
- Automated tests: `dotnet test tests/NabconsIntranet.Tests` (24 tests). Browser tests: `bash tests/e2e/run-all.sh` (needs Node + Playwright + Python/Pillow, test machine only).
- The e-mail notification hook for leads is `ILeadNotifier` (`src/NabconsIntranet/Data/Leads.cs`). Replace `NoLeadNotifications` with a real implementation in `Program.cs`.
