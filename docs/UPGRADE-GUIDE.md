# NABCONS Intranet — Upgrading an Existing Installation (keeps all data)

Use this every time a new version is released. **Your users, circulars, documents, requests, notices, audit log, backups and settings stay exactly as they are.** Only the program files are replaced.

## Why your data is safe

| What | Where it lives | During an upgrade |
|---|---|---|
| Users, roles, staff directory, circulars, requests, notices, audit log | `data\intranet.db` | **Never replaced.** If a new version needs extra fields, it *adds* them automatically on first start, after saving a safety copy of the database in `backups\` |
| Uploaded circulars and attachments | `documents\` | Never touched |
| Backups | `backups\` | Never touched; a fresh full backup is taken first |
| Your settings (port, branding, sign-in mode, backup time) | `appsettings.json` | **Kept.** The new version's default file is saved alongside as `appsettings.new-version.json` for reference only |
| Program (`NabconsIntranet.exe`, `wwwroot\`, `.bat` files, `docs\`) | application folder | Replaced; the old program is kept in `previous-version\` for roll-back |

Rules followed by every release:
- Database changes are **additive only**: new tables or columns, never deletions or renames.
- New settings always have safe built-in defaults, so your existing `appsettings.json` keeps working.
- The demo data is only ever loaded into an *empty* database, so it can never mix into live data.

## The release cycle

1. **You ask for a change.** The change is made on a separate GitHub branch, for example `claude/v1.0.2-<topic>`, never directly on `main`.
2. **You review it.** The branch contains the rebuilt package in `release\`. Try it on a spare PC or a copy (see "Trying a new version safely" below).
3. **You confirm.** Only then is the branch merged into `main`, the version that counts as approved.
4. **You upgrade the live intranet** with the steps below.

## Upgrade steps (about 5 minutes; do it outside office hours)

1. Download the new ZIP (for example `NABCONS-Intranet-v1.0.2-win-x64.zip`) from the `release` folder of the approved `main` branch. Transfer it to the office PC by the IT-approved method and check its SHA-256 (Deployment Guide section 2).
2. Extract it to a **new, temporary** folder, for example `C:\NABCONSIntranet\new-version\`. Do **not** extract it over the existing installation.
3. In that new folder, right-click **`Upgrade-Existing-Intranet.bat`** → **Run as administrator**.
4. When asked, type the folder of the existing intranet, or press Enter for `C:\NABCONSIntranet\NABCONS-Intranet`.
5. The script then:
   1. stops the intranet (the Windows service, or asks you to close the black window);
   2. takes a **full backup** with the current program (it stops if the backup fails, and nothing is changed);
   3. saves the current program to `previous-version\`;
   4. copies in the new program files, skipping `data`, `documents`, `backups`, `logs` and `appsettings.json`;
   5. restarts the service, or tells you to run `Start-Intranet.bat`.
6. Open the intranet and check:
   - **Administration → System information** shows the new version;
   - a few known users can sign in, a known circular opens and downloads;
   - **Audit → Verify integrity** passes.
7. Delete the temporary `new-version` folder.

If the new version changed the database, the black window and *Audit log* show "Database upgraded to schema version N", and the safety copy is in `backups\pre-upgrade-schema…db`.

## If something is wrong: roll back

1. In the installed folder, right-click **`Rollback-Upgrade.bat`** → *Run as administrator*. It puts back the previous program from `previous-version\`. Your data is untouched and still works with the previous program.
2. Only if the *data itself* looks wrong (very unlikely): stop the intranet, run `Restore-Backup.bat` and choose the backup ZIP created at the start of the upgrade (the newest ZIP in `backups\` from that date).

## Trying a new version safely before upgrading (recommended)

On a spare PC, or in a different folder on the same PC while the live intranet is stopped:

1. Copy the whole live application folder, or restore last night's backup ZIP into a copy (`Restore-Backup.bat`).
2. In that copy, change `"Urls"` in `appsettings.json` to another port (e.g. `http://0.0.0.0:8090`) so it cannot clash with the live intranet.
3. Run `Upgrade-Existing-Intranet.bat` from the new version, pointing it at the **copy**.
4. Test with your real data. When satisfied, confirm the merge and upgrade the live installation.

## Upgrading from 1.0.0 (the first release)

Version 1.0.0 did not include the upgrade script, but that doesn't matter: you always run the script from the **new** version's folder. Follow the steps above as written.
