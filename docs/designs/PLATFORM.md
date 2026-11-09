# Platform

## Problem Statement
NABCONS needs an intranet on an isolated NABARD LAN that a non-technical owner can run from an office PC, upgrade without losing live data, and defend to NABARD IT (audit trail, backups, no outbound traffic).

## Goals & Non-Goals
**Goals:** single self-contained Windows program; zero runtime internet; tamper-evident audit log; daily backups that are integrity-checked before they are kept, and a restore path; safe in-place upgrades; strict browser security; two sign-in modes (local accounts, Windows/AD).
**Non-Goals:** multi-server or load-balanced deployment; external (internet) access; e-mail/SMS; mobile apps; single sign-on beyond Windows Negotiate.

## Architecture (as built)
- **Process:** ASP.NET Core 10 Razor Pages, published self-contained single-file `NabconsIntranet.exe` (win-x64) with native DLLs beside it (`e_sqlite3.dll`, `libSkiaSharp.dll`, `libHarfBuzzSharp.dll`). Runs in a console window (`Start-Intranet.bat`) or as a Windows service (`Install-As-Windows-Service.bat`). Default URL `http://0.0.0.0:8080` (`appsettings.json` → `Urls`). See `Program.cs`, `build/package.sh`, `deploy/windows/`.
- **Configuration:** `appsettings.json` sections `Intranet` (`IntranetOptions`), `Notifications` (`NotificationOptions`), `ENote` (`ENoteOptions`), `MessageBoard` (`BoardOptions`). Every setting has a default in code, because upgrades keep the owner's existing file. Folders: `data/` (database, `uploads/photos`, `uploads/board`), `documents/` (uploaded files, e-Note PDFs), `backups/`, `logs/` — all relative to the program folder unless configured.
- **Data access:** `Infrastructure/Db.cs` — a thin helper over Microsoft.Data.Sqlite. Parameterised SQL only; rows mapped to classes by column name. One SQLite file `data/intranet.db` in WAL mode, foreign keys on. Writes are short transactions. Microsoft.Data.Sqlite's `BeginTransaction()` already starts `BEGIN IMMEDIATE`; `deferred: false` is written out where the read-then-write matters (serials, workflow steps). Busy time-out 30 seconds.
- **Schema & migrations:** `Infrastructure/Schema.cs`. `Schema.Sql` is the version-1 baseline (never altered). `Schema.Migrations` is an ordered list (versions 2–6); each runs once, in its own transaction, after a `VACUUM INTO` safety copy in `backups/`. A database newer than the program is accepted with a note (roll-back case). `NabconsIntranet.exe --migrate` takes a full backup ZIP first and is safe to repeat (`Infrastructure/Tools.cs`).
- **Sign-in:** `Local` mode: cookie authentication, PBKDF2-SHA256 (210,000 iterations) passwords (`Infrastructure/Security.cs`), lockout after 5 failures for 15 minutes, 30-minute idle timeout with sliding expiration, forced password change for temporary passwords, `SameSite=Strict`, HttpOnly. Every request re-validates the account (`IdentityBuilder.ValidateCookie`: active and security stamp unchanged) and refreshes claims when vertical, user name or display name change. `Windows` mode: Negotiate; roles from the intranet user record, `WindowsAdministrators` and AD group mappings (`WindowsRoleTransformation`).
- **Roles:** Staff, Publisher, Approver, Administrator, Auditor, LeadViewer (`Roles` in `Security.cs`). Fallback policy requires a signed-in user with the `nab:uid` claim on every page and endpoint; folder/page conventions add role policies (`Program.cs`).
- **Browser security:** a middleware sets `Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; ...`, `X-Frame-Options: DENY`, `nosniff`, `no-referrer`, `Cache-Control: no-store` (endpoints that serve images relax caching). One script (`wwwroot/js/site.js`) and one stylesheet (`wwwroot/css/site.css`); no inline script or style. Anti-forgery tokens on every form (Razor Pages default).
- **Audit log:** `Infrastructure/AuditService.cs`. Append-only table protected by triggers; each entry stores the SHA-256 of the previous entry (hash chain); verification on the Audit page and `--verify-audit`. The chain is not keyed (no secret), so it detects edits and deletions in the middle made by someone who does not recompute the following hashes; it cannot detect removal of the newest entries, or a rewrite by someone who recomputes every hash. Off-machine backups (each holds a CSV export of the log) are the second line of evidence. Writes are serialised by a process-wide semaphore and happen right after the business transaction commits.
- **Uploads:** `Infrastructure/FileStore.cs`. Extension allow-list, size limit, magic-byte check of the real content, random stored names (`<guid>.dat`) outside the web root, SHA-256 recorded, content type set by the server, served as attachments through authorisation-checked handlers. Images (photos, board) are decoded and re-encoded by SkiaSharp (`PhotoService`).
- **Backup & restore:** `Infrastructure/BackupService.cs`. ZIP of a `VACUUM INTO` database copy, `data/uploads/**`, `documents/**`, configuration and an audit CSV, with a `.sha256`; retention; optional copy to `SecondaryBackupFolder`. Daily at `DailyBackupTime` by `BackupScheduler`; `--backup`, `--restore` (moves current data aside, never deletes), `Backup-Now.bat`, `Restore-Backup.bat`.
- **Housekeeping:** `Infrastructure/Housekeeping.cs`, run by `BackupScheduler` right after the daily backup: notification retention, `PRAGMA optimize`, e-Note ageing alerts and orphan-file report (module steps).
- **Packaging:** `build/package.sh` publishes, adds `.bat` helpers and docs (CRLF), writes `SHA256SUMS.txt`, zips to `release/`.

### Request lifecycle
Browser → Kestrel (one port) → security-header middleware → static files (`wwwroot`, cached 1 hour) → routing → authentication (cookie re-validated against the user record on every request; Windows mode: Negotiate + role transformation) → authorisation (fallback policy: signed-in user; folder/page role policies) → "must change password" gate → Razor Page or endpoint → module repository (permission checks, short SQLite transaction, notifications in the same transaction) → audit entry after commit → HTML/JSON response.

### Dates and times
All timestamps are stored in **UTC** as ISO-8601 text. They are shown in IST (`DisplayUtcOffsetMinutes`, default 330). "Today", the financial year for serials, due dates, ageing and notice start/end dates use the **office date in IST** (`Fmt.Today`). `DailyBackupTime` is the **PC's local clock** (the office PC is expected to be set to IST). Retention periods count from UTC timestamps.

### Roles and what they can do
| Capability | Staff | Publisher | Approver | Administrator | Auditor | LeadViewer |
|---|---|---|---|---|---|---|
| Directory, published circulars, notices, My Profile, My Desk, Message Board (post) | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Draft/publish/withdraw circulars, manage notices | | ✓ | | ✓ | | |
| See draft and restricted circulars | | ✓ | | ✓ | ✓ | |
| Act on requests assigned to them | | | ✓ | | | |
| See all requests (read-only) | | | | ✓ | ✓ | |
| Users, roles, masters, directory import, backups, registrations, vertical changes | | | | ✓ | | |
| Pin / remove any message-board post | | | | ✓ | | |
| Audit log (search, export, verify) | | | | ✓ | ✓ | |
| e-Note register (metadata only) | | | | ✓ | ✓ | |
| All business leads (with BDU vertical members) | | | | | | ✓ |
| Compliance (v1.6) | Not through these roles: compliance grants (Compliance Admin / Viewer) are held in the module and given only by Compliance Admins; officers see their own points; the Auditor role reads non-restricted events (DD-07 §5) | | | | | |
| Inward (v1.7) | Not through these roles: Inward Operator (per office), Inward Viewer and Invoice Accounts grants held in the module, given by the Administrator (never to themselves); every user can self-inward and sees items they hold, were copied or inwarded; the Auditor role sees all items, confidential ones as metadata only (DD-08 §5) | | | | | |

Business-lead access deliberately does not follow the Administrator role: client and tender information is limited to BDU and the COO/MD, and administrators manage the system, not the business. Every user can submit leads and see their own.

### Sign-in modes in practice
- **Local** (default on the live server): accounts created by administrators or by approved self-registration (user name = mobile number). Password policy, lockout, forced change and self-registration apply.
- **Windows**: the first sign-in of a domain user creates a Staff user record automatically (`WindowsAutoProvisionStaff`); roles come from that record, `WindowsAdministrators` and `WindowsGroupRoles`. Self-registration, passwords and lockout do not apply (Windows handles them).

### Settings (`appsettings.json`)
| Section:key | Default | Effect |
|---|---|---|
| `Urls` | `http://0.0.0.0:8080` | Listening address/port |
| `Intranet:AuthenticationMode` | `Local` | `Local` or `Windows` |
| `Intranet:SessionTimeoutMinutes` / `MaxFailedLogins` / `LockoutMinutes` | 30 / 5 / 15 | Idle sign-out and lockout |
| `Intranet:DailyBackupTime` / `BackupRetentionDays` / `SecondaryBackupFolder` | 19:30 / 30 / empty | Daily backup time (PC clock; `off` disables backups **and** housekeeping), retention, off-machine copy |
| `Intranet:MaxUploadMB` / `AllowedUploadExtensions` | 20 / pdf, docx, … | Circular and request uploads |
| `Intranet:SelfRegistrationEnabled`, `VerticalChangeRequiresApproval` | true, true | Registration and vertical approval |
| `Intranet:BusinessDevelopmentVerticalId` / `...Name` | 0 / BDU | Which vertical is BDU |
| `Intranet:OfficialEmailDomain`, `MaxPhotoMB`, `MaxLeadFileMB`, `MaxLeadTotalMB` | empty, 2, 10, 25 | Profile and lead limits |
| `Intranet:SeedDemoData` / `ShowDemoAccountsOnLogin` | true / true | Demo data on an **empty** database only; demo box on sign-in |
| `Intranet:DisplayUtcOffsetMinutes` | 330 | Display time zone |
| `Notifications:PollSeconds` / `RetainReadDays` / `RetainUnreadDays` | 60 / 90 / 365 | Badge refresh and retention |
| `ENote:*` | Enabled true, … | See ENOTE.md (live server: `Enabled false`) |
| `MessageBoard:*` | Enabled true, MaxChars 2000, MaxPhotosPerPost 4, MaxPhotoMB 5, MaxPinned 3, PostsPerHour 10 | See MESSAGE-BOARD.md |

### Migrations by release
| Schema | Release | Adds |
|---|---|---|
| 1 | 1.0.0 | Baseline: users, masters, staff, circulars (+files, FTS), notices, requests, approval actions, attachments, audit log, `SchemaInfo` |
| 2 | 1.1.0 | `RegistrationRequests` |
| 3 | 1.1.1 | State/UT capitals in locations |
| 4 | 1.2.0 | Users.StaffId/PhotoVersion, profile change requests and audit, leads (+serials, events, attachments), lead master lists, BDU vertical |
| 5 | 1.3.0 | Notifications, Users.OpenDeskAfterSignIn, e-Note tables, MasterItems.Code, e-Note categories |
| 6 | 1.4.0 | Message board posts and photos, Users.BoardSeenAt |
| 7 | 1.6.0 | Compliance: events, points, support officers, updates, revisions, history, imports, snapshots (+events, items, voids), members (+history), serials, state; compliance master lists (see DD-07) |
| 8 | 1.7.0 | Inward: items, participants, movements, copies, notes, invoice details, payments, adjustments, project codes, members (+history), serials, state, imports, search index; `ENotes.SourceInwardId`; inward categories (see DD-08) |

The applied versions are rows in `SchemaInfo`. If the database is newer than the program (after a roll-back), start-up logs "the database is at schema version N, newer than this program …; no data is affected" and continues.

### Operations and failure handling
- **Logs:** `logs/intranet-<yyyyMMdd>.log` (information and above) and the console window.
- **Audit write fails:** the business change is already committed; the error is logged, and the user sees the standard error page. The next successful entry continues the chain from the last stored hash, so a missing entry is not detectable by the chain itself, only from the logs. **Verify integrity fails:** the hash of the entry at the reported Id does not match, so someone changed the database outside the application. Restore the last good backup to a copy, compare, and report it as a security incident.
- **Backup:** the database is copied with `VACUUM INTO`, checked with `PRAGMA quick_check`, zipped to `<name>.zip.partial`, re-opened and read back, and only then renamed to `.zip` with a `.sha256` file (v1.5.0). A failed backup leaves no ZIP.
- **Backup fails:** a `Backup.Failed` audit entry; the Administration page shows the date of the last backup (red alert if none). One attempt per day; if the PC is off at the backup time that day is skipped (take a manual backup from the Backup page). An invalid `DailyBackupTime` turns off both the scheduled backup and the nightly housekeeping (a warning is logged).
- **Restore:** run with the intranet stopped (`Restore-Backup.bat`); the ZIP's checksum file is checked when present. Since v1.5.0 the backup is unpacked to `*.restoring-<time>` folders first; only then are the current `data` and `documents` moved aside (to `*.before-restore-<time>`, never deleted) and the restored folders moved in; if any step fails the previous folders are put back. Skipped unsafe ZIP entries are listed. `appsettings.json` is not restored (a copy is inside the ZIP under `config/`).
- **Retention:** only the main backup folder is pruned (by file creation time, `BackupRetentionDays`). Copies in `SecondaryBackupFolder`, pre-upgrade snapshots, `*.before-restore-*` folders and log files are kept until removed by hand; check them monthly.
- **Sensitive files:** backup ZIPs contain the database (password hashes, profiles), `appsettings.json` and the audit export; logs contain user names and IP addresses. They are not encrypted: keep the backup folders readable only by the administrator account and the service account.
- **Orphan files:** the nightly housekeeping reports (never deletes) stored documents of any module that no record points to (v1.5.0; e-Note only before).
- **Sign-in protection:** a failed sign-in adds to the account's counter in one database statement (5 failures → 15-minute lock; the counter restarts after a lock or a successful sign-in); unknown and deactivated user names take as long as a wrong password; at most 30 failed sign-ins per computer in 10 minutes (v1.5.0). A role change signs the user out on the next request. Over plain HTTP the cookie cannot be marked Secure.
- **Photo processing:** at most two photos are decoded and resized at a time (each can need ~160 MB); others wait (v1.5.0).
- **Endpoints:** every page and endpoint requires a signed-in, known user (`nab:uid` claim) unless marked anonymous; since v1.5.0 this also applies to the small JSON/photo endpoints, which previously only required Windows authentication in Windows mode.
- **Concurrency notes:** audit writes and backups are serialised with a process-wide semaphore (blocking wait; acceptable at this load). Two processes migrating the same database at once: the second waits and skips migrations already applied (v1.5.0).

## Alternatives Considered
- **SQL Server / PostgreSQL:** rejected for the pilot — needs installation, a service account and DBA time; SQLite handles the stated load (measured: p95 under 10 ms for main pages at 500 users / 20,000 notes). Documented migration threshold: >200 concurrent users or multiple servers.
- **IIS-only hosting:** supported but not required; Kestrel in a service avoids IIS configuration on a desktop.
- **External identity provider / OAuth:** impossible on an isolated LAN; Windows Negotiate covers AD where IT permits.
- **Front-end framework (React/Angular):** rejected — build pipeline, larger payloads, CSP friction; server-rendered pages with one small script suffice.

## Risks & Mitigations
| Risk | Mitigation |
|---|---|
| Single PC is a single point of failure | Daily integrity-checked backup; `SecondaryBackupFolder` for an off-machine copy; documented restore; roll-back script keeps the previous program |
| Plain HTTP exposes passwords on the LAN | HTTPS supported via Kestrel certificate settings (Deployment Guide §6); recommended before wider use |
| Audit writes are outside the business transaction | Entry written immediately after commit; failure is logged and raised (the user sees an error although the action was saved). Moving audit inside each business transaction is deferred (review finding Z7) |
| In-memory caches (badge counts, throttles) assume one process | Documented: exe, service, or IIS with a single worker |
| Upgrades on live data | Additive migrations, pre-upgrade snapshot and backup, roll-back tested each release |

## Tests
`tests/NabconsIntranet.Tests` (xUnit, real app in memory per test class), `tests/e2e/*.e2e.js` (Playwright against the published app), `tests/LoadTest` (50 virtual users). Each release: upgrade from a copy of the previous release's database, row counts unchanged, `--migrate` twice, `--verify-audit`, roll-back with the previous program.
