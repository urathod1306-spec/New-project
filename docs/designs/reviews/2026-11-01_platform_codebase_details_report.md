# Goldfish Review — Codebase Details: Platform (2026-11-01)

Reviewer: Codebase Details Goldfish (fresh context, read-only).

Verdict (reviewer):
- **Accuracy:** PLATFORM.md is broadly accurate but overstates three guarantees:
  - authorisation on every endpoint;
  - "verified" backups;
  - detection of gaps in the audit chain.
- **Real risks:** partial backup ZIPs; a failed restore can lead to a seeded empty database; the lockout counter under concurrency; a roll-back script that fails silently.
- **Checked and fine:**
  - no CSP violations;
  - migrations 2–6, WAL and foreign keys as documented;
  - PBKDF2 at 210k iterations;
  - SameSite=Strict and HttpOnly cookies;
  - ValidateCookie;
  - zip-slip protection on restore;
  - FileStore path containment;
  - package checksums.

| # | Sev | Location | Finding | Suggested fix |
|---|---|---|---|---|
| P1 | Blocker | Endpoints.cs (all `.RequireAuthorization()`) vs Program.cs fallback policy | Minimal-API endpoints use the DefaultPolicy (authenticated only), not the fallback (`nab:uid` claim). In Windows mode a deactivated or never-admitted domain user stays authenticated without `nab:uid` and could read contact cards, vCards and photos, and list users via `/api/users/lookup` (which also ignores `ENote:Enabled`) | Make the DefaultPolicy equal to the fallback policy |
| P2 | Blocker | BackupService.Create vs "daily verified backups" | Backups are checksummed, never verified (no integrity check of the DB copy, ZIP not re-read) | Verify, or change the wording |
| P3 | Major | BackupService.Create | ZIP written directly to its final name; a failure mid-way leaves a valid-looking partial ZIP (no .sha256) that is listed, retained and accepted by restore with only a warning | Write to `.partial`, rename on success, delete on failure |
| P4 | Major | BackupService.Restore, Tools, Seeder | A restore failing mid-way leaves `data` empty; the next start creates a fresh database with the default admin password and demo data | Extract to staging first, then swap; roll back on failure |
| P5 | Major | UserRepository.RecordFailedLogin, Login | Failed-login count read-then-write, not atomic; parallel guesses bypass the lockout | Atomic `FailedLogins = FailedLogins + 1` |
| P6 | Major | Login.cshtml.cs | Unknown/deactivated user names return before the slow hash, so timing reveals valid names; no per-IP throttle; each attempt adds an audit row | Dummy hash verify; per-IP throttle |
| P7 | Major | AuditService vs doc | The chain is unkeyed and does not detect tail truncation; the payload is "|"-joined without the Id | Say so plainly; optionally anchor the last hash elsewhere |
| P8 | Major | deploy/windows/Rollback-Upgrade.bat | Stops only the service; robocopy result unchecked; no wait for STOPPED; always says "restored" | Check for a running console exe, robocopy exit code and service state |
| P9 | Major | BackupScheduler | One attempt per day even on failure; a day is skipped if the PC is off at 19:30; an invalid `DailyBackupTime` also disables housekeeping — undocumented | Document; consider a catch-up backup at start-up |
| P10 | Major | Retention | Secondary folder copies, pre-upgrade snapshots, logs, `*.before-restore-*` folders and extra `--migrate` ZIPs are never pruned; retention uses creation time | Document; prune |
| P11 | Major | Backups and logs | Backup ZIPs (database, appsettings with the initial admin password, audit CSV) and plain-text logs hold sensitive data, unencrypted — undocumented | Document; advise ACLs/encryption |
| P12 | Nit | PhotoService.Orient (EXIF orientation 7) | Transform maps the image outside the bitmap; such photos come out blank | Remove the final translate |
| P13 | Nit | Db.Initialize | Schema version read outside a transaction; two processes migrating at once: the second crashes (no data loss) | Read the version inside BEGIN IMMEDIATE |
| P14 | Nit | WindowsRoleTransformation | First-sign-in auto-provisioning race gives a 500 | Insert-or-ignore, then re-read |
| P15 | Nit | PLATFORM Data access | Parameterless `BeginTransaction()` is already IMMEDIATE; 30-second busy timeout undocumented | Correct the doc |
| P16 | Nit | Program/Tools | "Database newer than program" note logged as `System.Upgraded` on every start | Separate action name |
| P17 | Nit | PLATFORM Sign-in | Settings names, counter reset, role change signs the user out, cookie not Secure over HTTP — undocumented | Document |
| P18 | Nit | PLATFORM Uploads | Photo naming/limits, txt/csv check depth undocumented | Document |
| P19 | Nit | PLATFORM headers | Header list incomplete; static-file caching; request size limit undocumented | Document |
| P20 | Nit | Restore | Configuration not restored; skipped zip-slip entries not reported | Document; print skipped entries |
| P21 | Nit | AuditService/Backup gates | Blocking `SemaphoreSlim.Wait()` on request threads with the 30-second busy timeout | Document or WaitAsync |
