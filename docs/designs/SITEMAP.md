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

## Glossary
| Term | Meaning |
|---|---|
| NABARD / NABCONS | National Bank for Agriculture and Rural Development / its subsidiary NABARD Consultancy Services, the owner of this intranet |
| Vertical | A department or business unit (e.g. HR & Administration, BDU). Kept in *Administration → Master lists → Verticals*. A user's vertical is on their user record; changing it from My Profile needs administrator approval. It decides access to Business Leads (BDU) and vertical-only circulars, and the e-Note number code |
| Location / office | Office where a person sits; a master list used in the directory |
| Master lists | Administrator-maintained drop-down values (`MasterItems` table): verticals, locations, categories, lead lists, e-Note categories |
| BDU | Business Development Unit — the vertical that manages business leads (setting `BusinessDevelopmentVerticalName`, default "BDU") |
| LeadViewer | Role for the COO (Chief Operating Officer) and MD (Managing Director): sees and manages all business leads |
| FY | Indian financial year, 1 April – 31 March, written `2026-27`. Lead and e-Note serials restart each FY |
| IST | Indian Standard Time, UTC+5:30 (setting `DisplayUtcOffsetMinutes = 330`) |
| CSP | Content-Security-Policy: a browser rule that only allows scripts, styles and images from this server |
| WAL | SQLite "write-ahead log" mode: readers do not block the single writer |
| FTS5 | SQLite's built-in full-text search |
| PBKDF2 | Standard slow password-hashing method (210,000 rounds, SHA-256) |
| ETag / 304 | HTTP mechanism that lets the badge poll answer "nothing changed" without sending data |
| vCard | Standard contact file (`.vcf`) that Outlook and phones can import |
| SkiaSharp / HarfBuzz | Open-source (MIT) image and text libraries bundled in the program: photos, PDFs, Hindi text shaping |
| Green sheet | NABARD/NABCONS name for the paper note-sheet that e-Note replaces |
| EGM / Elephant / Goldfish | Elephant–Goldfish Model: the author (Elephant, full context) writes the design; fresh reviewers with no context (Goldfish) check it |
| YAGNI | "You aren't gonna need it": do not build for speculative future needs |

## Release 1.7.0 — Inward register and invoices
Design: [DD-08-inward-and-invoices.md](DD-08-inward-and-invoices.md). Reviews: `reviews/DD-08_consolidated_review_report.md`.
- `/Inward` dashboard · `/Inward/Register` · `/Inward/New` · `/Inward/Mine` · `/Inward/Item/{id}` (with `?handler=Ack` slip) · `/Inward/Edit/{id}` · `/Inward/Invoices` · `/Inward/Payments` (Accounts) · `/Inward/ProjectCodes` (Accounts) · `/Inward/Transfer` (Viewer, Administrator) · `/Inward/Access` (Administrator).

## Release 1.6.0 — Compliance Management
Design: [DD-07-compliance-and-inward.md](DD-07-compliance-and-inward.md) (R1 built in 1.6.0; R2 Inward Register is an outline for a later release). Reviews: `reviews/2026-11-08_DD-07_G1_comprehension_report.md`, `reviews/DD-07_consolidated_review_report.md`.
