# NABCONS Intranet — Setup Guide

## Upgrading to 1.4.0 (from 1.3.x): Message Board

1. Download `release/NABCONS-Intranet-v1.4.0-win-x64.zip` and check its SHA-256. Extract it to a new folder, then right-click `Upgrade-Existing-Intranet.bat` → *Run as administrator*. If you prefer, upgrade the database first by hand: with the intranet stopped, run `NabconsIntranet.exe --migrate`. The database goes from schema 5 to 6 after a full backup. Nothing existing is changed, and your settings (including e-Notes switched off) are kept.
2. Check *Administration → System information*: version 1.4.0, schema 6.
3. The **Message Board** tab appears for everyone. Optional settings are in the `"MessageBoard"` section (see `docs/DEPLOYMENT-GUIDE.md`). Board photos are stored in `data\uploads\board` and are in every backup ZIP.
4. Tell staff: see the *Message Board* section of `docs/STAFF-GUIDE.md`.

Roll back with `Rollback-Upgrade.bat`; version 1.3.0 runs on the upgraded database.

---


## Upgrading to 1.3.0 (from 1.2.x): My Desk, notifications and e-Note

1. **Same steps as before:**
   1. Download `release/NABCONS-Intranet-v1.3.0-win-x64.zip` and check its SHA-256.
   2. Extract it to a new folder.
   3. Right-click `Upgrade-Existing-Intranet.bat` → *Run as administrator*.
   4. If you prefer, upgrade the database first by hand: with the intranet stopped, run `NabconsIntranet.exe --migrate` in the live folder.

   Either way the database goes from **schema 4 to 5**, after a full backup and a database safety copy. Nothing existing is changed.
2. **Allow-listing:** the package has one new DLL, `libHarfBuzzSharp.dll` (PDF text shaping). If IT allow-lists programs (AppLocker/WDAC), approve it together with the new `NabconsIntranet.exe`. Checksums are in `SHA256SUMS.txt`.
3. **Check:** *Administration → System information* shows version **1.3.0**, schema **5**.
4. **Set the e-Note number codes:**
   - Go to *Administration → Master lists → Verticals* and give each vertical a short **e-Note code** (e.g. HR, FA, RID, BDU).
   - Without a code, notes are numbered `NCS/EN/GEN/…`.
   - Review *Master lists → e-Note categories*.
5. **Optional settings** (defaults apply if you add nothing; see `docs/DEPLOYMENT-GUIDE.md` section 5):
   - `"Notifications"`: how often badges refresh (60 s) and how long notifications are kept;
   - `"ENote"`: who sees approved notes (`AllStaff`), attachment limits (10 MB per file / 100 MB per note) and the ageing reminder (7 days).
6. **Tell staff:**
   - Signing in now opens **My Desk** (each person can switch this off under *My Profile → Preferences*).
   - `docs/STAFF-GUIDE.md` has a one-page guide to e-Notes.
7. **Backups:** e-Note attachments and PDFs are under `documents\enotes`, which is already in every backup ZIP. Nothing to change.

Roll back with `Rollback-Upgrade.bat`; version 1.2.0 runs on the upgraded database (tested).

---

# Upgrading to 1.2.0 (from 1.1.x)

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
