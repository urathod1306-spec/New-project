# Platform

## Problem Statement
NABCONS needs an intranet on an isolated NABARD LAN that a non-technical owner can run from an office PC, upgrade without losing live data, and defend to NABARD IT (audit trail, backups, no outbound traffic).

## Goals & Non-Goals
**Goals:** single self-contained Windows program; zero runtime internet; tamper-evident audit log; daily verified backups and a restore path; safe in-place upgrades; strict browser security; two sign-in modes (local accounts, Windows/AD).
**Non-Goals:** multi-server or load-balanced deployment; external (internet) access; e-mail/SMS; mobile apps; single sign-on beyond Windows Negotiate.

## Architecture (as built)
- **Process:** ASP.NET Core 10 Razor Pages, published self-contained single-file `NabconsIntranet.exe` (win-x64) with native DLLs beside it (`e_sqlite3.dll`, `libSkiaSharp.dll`, `libHarfBuzzSharp.dll`). Runs in a console window (`Start-Intranet.bat`) or as a Windows service (`Install-As-Windows-Service.bat`). Default URL `http://0.0.0.0:8080` (`appsettings.json` → `Urls`). See `Program.cs`, `build/package.sh`, `deploy/windows/`.
- **Configuration:** `appsettings.json` sections `Intranet` (`IntranetOptions`), `Notifications` (`NotificationOptions`), `ENote` (`ENoteOptions`), `MessageBoard` (`BoardOptions`). Every setting has a default in code, because upgrades keep the owner's existing file. Folders: `data/` (database, `uploads/photos`, `uploads/board`), `documents/` (uploaded files, e-Note PDFs), `backups/`, `logs/` — all relative to the program folder unless configured.
- **Data access:** `Infrastructure/Db.cs` — a thin helper over Microsoft.Data.Sqlite. Parameterised SQL only; rows mapped to classes by column name. One SQLite file `data/intranet.db` in WAL mode, foreign keys on. Writes are short transactions; `BEGIN IMMEDIATE` (`BeginTransaction(deferred: false)`) where a read-then-write must not interleave (serials, workflow steps).
- **Schema & migrations:** `Infrastructure/Schema.cs`. `Schema.Sql` is the version-1 baseline (never altered). `Schema.Migrations` is an ordered list (versions 2–6); each runs once, in its own transaction, after a `VACUUM INTO` safety copy in `backups/`. A database newer than the program is accepted with a note (roll-back case). `NabconsIntranet.exe --migrate` takes a full backup ZIP first and is safe to repeat (`Infrastructure/Tools.cs`).
- **Sign-in:** `Local` mode: cookie authentication, PBKDF2-SHA256 (210,000 iterations) passwords (`Infrastructure/Security.cs`), lockout after 5 failures for 15 minutes, 30-minute idle timeout with sliding expiration, forced password change for temporary passwords, `SameSite=Strict`, HttpOnly. Every request re-validates the account (`IdentityBuilder.ValidateCookie`: active and security stamp unchanged) and refreshes claims when vertical, user name or display name change. `Windows` mode: Negotiate; roles from the intranet user record, `WindowsAdministrators` and AD group mappings (`WindowsRoleTransformation`).
- **Roles:** Staff, Publisher, Approver, Administrator, Auditor, LeadViewer (`Roles` in `Security.cs`). Fallback policy requires a signed-in user with the `nab:uid` claim on every page and endpoint; folder/page conventions add role policies (`Program.cs`).
- **Browser security:** a middleware sets `Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; ...`, `X-Frame-Options: DENY`, `nosniff`, `no-referrer`, `Cache-Control: no-store` (endpoints that serve images relax caching). One script (`wwwroot/js/site.js`) and one stylesheet (`wwwroot/css/site.css`); no inline script or style. Anti-forgery tokens on every form (Razor Pages default).
- **Audit log:** `Infrastructure/AuditService.cs`. Append-only table protected by triggers; each entry stores the SHA-256 of the previous entry (hash chain); verification on the Audit page and `--verify-audit`. Writes are serialised by a process-wide semaphore and happen right after the business transaction commits.
- **Uploads:** `Infrastructure/FileStore.cs`. Extension allow-list, size limit, magic-byte check of the real content, random stored names (`<guid>.dat`) outside the web root, SHA-256 recorded, content type set by the server, served as attachments through authorisation-checked handlers. Images (photos, board) are decoded and re-encoded by SkiaSharp (`PhotoService`).
- **Backup & restore:** `Infrastructure/BackupService.cs`. ZIP of a `VACUUM INTO` database copy, `data/uploads/**`, `documents/**`, configuration and an audit CSV, with a `.sha256`; retention; optional copy to `SecondaryBackupFolder`. Daily at `DailyBackupTime` by `BackupScheduler`; `--backup`, `--restore` (moves current data aside, never deletes), `Backup-Now.bat`, `Restore-Backup.bat`.
- **Housekeeping:** `Infrastructure/Housekeeping.cs`, run by `BackupScheduler` right after the daily backup: notification retention, `PRAGMA optimize`, e-Note ageing alerts and orphan-file report (module steps).
- **Packaging:** `build/package.sh` publishes, adds `.bat` helpers and docs (CRLF), writes `SHA256SUMS.txt`, zips to `release/`.

## Alternatives Considered
- **SQL Server / PostgreSQL:** rejected for the pilot — needs installation, a service account and DBA time; SQLite handles the stated load (measured: p95 under 10 ms for main pages at 500 users / 20,000 notes). Documented migration threshold: >200 concurrent users or multiple servers.
- **IIS-only hosting:** supported but not required; Kestrel in a service avoids IIS configuration on a desktop.
- **External identity provider / OAuth:** impossible on an isolated LAN; Windows Negotiate covers AD where IT permits.
- **Front-end framework (React/Angular):** rejected — build pipeline, larger payloads, CSP friction; server-rendered pages with one small script suffice.

## Risks & Mitigations
| Risk | Mitigation |
|---|---|
| Single PC is a single point of failure | Daily verified backup; `SecondaryBackupFolder` for an off-machine copy; documented restore; roll-back script keeps the previous program |
| Plain HTTP exposes passwords on the LAN | HTTPS supported via Kestrel certificate settings (Deployment Guide §6); recommended before wider use |
| Audit writes are outside the business transaction | Entry written immediately after commit; failure is logged and raised; hash chain makes gaps or edits detectable |
| In-memory caches (badge counts, throttles) assume one process | Documented: exe, service, or IIS with a single worker |
| Upgrades on live data | Additive migrations, pre-upgrade snapshot and backup, roll-back tested each release |

## Tests
`tests/NabconsIntranet.Tests` (xUnit, real app in memory per test class), `tests/e2e/*.e2e.js` (Playwright against the published app), `tests/LoadTest` (50 virtual users). Each release: upgrade from a copy of the previous release's database, row counts unchanged, `--migrate` twice, `--verify-audit`, roll-back with the previous program.
