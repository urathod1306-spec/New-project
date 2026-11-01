# Design Sitemap — NABCONS Intranet

Design documents in the Elephant–Goldfish Model (EGM) style: each explains *why* a part of the system exists the way it does, and points to the code that implements it. They describe the system **as built in v1.4.0** (retrospective specifications), so they can be reviewed by fresh "Goldfish" reviewers and checked for drift against the code. Review reports live in `reviews/`.

| Document | Covers | Main code |
|---|---|---|
| [PLATFORM.md](PLATFORM.md) | Hosting, configuration, data access, migrations, security headers, sign-in, roles, audit log, backup/restore, uploads, housekeeping, packaging | `Program.cs`, `Infrastructure/` |
| [DIRECTORY-PROFILES.md](DIRECTORY-PROFILES.md) | Staff directory, contact card, My Profile, staff photos, self-registration, account–directory linking | `Data/DirectoryRepositories.cs`, `Data/ProfileRepository.cs`, `Data/RegistrationRepository.cs`, `Infrastructure/PhotoService.cs` |
| [CIRCULARS-NOTICES.md](CIRCULARS-NOTICES.md) | Circulars (versions, visibility, search, supersession, withdrawal), notice board, site search | `Data/CircularRepository.cs`, `Pages/Circulars/`, `Pages/Notices/`, `Pages/Search.cshtml.cs` |
| [REQUESTS.md](REQUESTS.md) | General Administrative Approval Request (two-level approval) | `Data/RequestWorkflow.cs`, `Pages/Requests/` |
| [BUSINESS-LEADS.md](BUSINESS-LEADS.md) | Lead submission, pipeline, dashboard, export, BDU/LeadViewer access | `Data/Leads.cs`, `Pages/Leads/` |
| [DESK-NOTIFICATIONS.md](DESK-NOTIFICATIONS.md) | In-app notifications, badge poll, My Desk | `Infrastructure/Notifications.cs`, `Data/Desk.cs`, `Infrastructure/Endpoints.cs` |
| [ENOTE.md](ENOTE.md) | e-Note (digital note-sheet), archival PDF | `Data/ENotes.cs`, `Data/ENoteWorkflow.cs`, `Infrastructure/ENotePdf.cs`, `Pages/ENotes/` |
| [MESSAGE-BOARD.md](MESSAGE-BOARD.md) | Message board posts, photos, pinning, removal | `Data/Board.cs`, `Pages/Board/` |

## Rules that apply to every design (from the owner)

1. **Fully on the intranet.** No internet dependency at run time: no CDN, web font, external script, telemetry, licence or update check; the application makes no outbound connections. Everything is bundled.
2. **Low system profile.** One Windows desktop (7th-gen Core i5, 16 GB) serving up to 50 concurrent / 500 total users. One process, one SQLite file, server-rendered pages, no SPA framework, no background CPU-heavy work in office hours.
3. **Live data survives every upgrade.** Database changes are additive migrations only; older program versions must keep working on an upgraded database (roll-back).
4. **Strict web security.** Content-Security-Policy without inline script or style; anti-forgery on every POST; authorisation on every page, download and JSON endpoint.
5. **Change process.** Every change on a new `claude/v<version>-<topic>` branch; merged into `main` only after the owner confirms.
