# Goldfish Review — Stage 6: Comprehension Test (2026-11-01)

Reviewer: Comprehension Goldfish (fresh context; read only the design documents and README, no source code).
Target: docs/designs/*.md as of v1.4.0.

## Paraphrase: What the system is
A self-contained web intranet for NABARD Consultancy Services, run as a single Windows program on one office desktop in an isolated NABARD LAN. Staff (up to 500 in total, 50 at once) use it in a browser to:
- find colleagues;
- read circulars and notices;
- raise two-level administrative approval requests;
- submit business leads;
- post on a message board;
- see pending work on "My Desk".

An e-Note digital note-sheet module is built but switched off on the live server. Roles are Staff, Publisher, Approver, Administrator, Auditor and LeadViewer. Business Leads access also depends on the user's "vertical". Hard constraints:
- no internet at run time;
- one process and one SQLite file;
- server-rendered pages;
- a strict Content-Security-Policy;
- additive-only migrations, with roll-back compatibility;
- owner-confirmed merges.

## Paraphrase: How it works
- **Serving:** Kestrel inside `NabconsIntranet.exe` on port 8080, run as a console program or a Windows service.
- **Sign-in:**
  - Local mode: cookie session, PBKDF2-hashed passwords, lockout after 5 failures, 30-minute idle timeout.
  - Windows mode: Negotiate/AD, with roles from the user record and AD groups.
  - Every request re-checks that the account is active and its security stamp is unchanged.
- **Authorisation:** a fallback policy requires a signed-in user; role policies are applied per folder or page; modules add their own checks; others get 403 or 404.
- **Data:** one SQLite file (WAL), parameterised SQL, short transactions, `BEGIN IMMEDIATE` and `RowVersion` for workflow steps and serials.
- **Uploads:** checked by extension, size and magic bytes; stored under random names outside the web root; images re-encoded.
- **Notifications:** written in the same transaction as the action; the browser polls a summary endpoint every 60 s, which is mostly served from memory.
- **Audit:** written right after commit into an append-only, hash-chained table; can be verified in the app or from the command line.
- **Backups:** daily ZIP (`VACUUM INTO` database copy, uploads, documents, configuration, audit CSV) with SHA-256, optionally copied to a LAN share. Housekeeping runs after the backup. Restore moves current data aside. Upgrades run pending migrations once, after a safety copy.

## Findings
| # | Severity | Location | Finding | Suggested addition |
|---|---|---|---|---|
| C1 | Major | ENOTE.md vs README | "No fixed hierarchy" vs README "following the office hierarchy"; it is unclear whether the system enforces any hierarchy | State that the system enforces none and that users follow the paper hierarchy; fix the README wording |
| C2 | Major | All docs | "Vertical" (main access key), "masters", "location/office" are never defined | Glossary in SITEMAP/PLATFORM |
| C3 | Major | All docs | Acronyms not expanded: NABARD, FY (April–March? when serials reset), WAL, FTS5, PBKDF2, COO, MD, YAGNI, MP, ETag, vCard, SkiaSharp/HarfBuzz, "green sheets", BDU (only in Leads), CSP (only rule 4), IST (absent) | Glossary |
| C4 | Major | All docs | Time handling never described: UTC or IST storage; which time zone the FY, backup time, ageing, due dates and retention use | "Dates and times" bullet in PLATFORM |
| C5 | Major | PLATFORM Roles; README | README lists 5 roles, PLATFORM 6; no role × module matrix; Auditor scope unclear; no reason given for "Administrators get no lead powers" | Permission table with rationale |
| C6 | Major | PLATFORM Configuration | Settings named without section, default or effect; Message Board enable switch undocumented | Settings table |
| C7 | Major | REQUESTS States | No transition table: actors, reject at level 1?, what happens after Returned, who closes, draft deletion, numbers used by drafts | From / action / actor / to table |
| C8 | Major | PLATFORM Audit | "Failure is logged and raised": what the user sees and who is alerted are unclear; what to do when verification fails is unclear | Failure path and procedure |
| C9 | Major | PLATFORM Backup | Default time, retention, backup-failure signal, whether restore needs the service stopped, whether the checksum is checked, where logs go | Operations subsection |
| C10 | Major | PLATFORM Migrations | Schema versions vs program versions unmapped; where the version is stored; what the "note" for a newer database is | Migration → release → tables table |
| C11 | Major | PLATFORM Windows mode; DIRECTORY Self-registration | How an AD user gets a user record; which features apply per mode; which mode is live | Windows-mode lifecycle paragraph |
| C12 | Major | ENOTE status vs README / DESK | e-Note is off but presented as a feature; do ageing alerts run while it is disabled?; how to enable it | Enabling/disabling note; mark it in README |
| C13 | Nit | MESSAGE-BOARD | "Remove", "delete" and "hide" used for the same thing; README says only admins remove; can admins or auditors see removed posts? | One term; visibility of removed posts |
| C14 | Nit | REQUESTS / LEADS / ENOTE | Reference formats differ (prefix, year type, when assigned) without rationale | One line of rationale |
| C15 | Major | DESK-NOTIFICATIONS | No list of notification events and recipients; Desk sections ("admin approvals", "due leads") undefined; Leads doc never mentions a due date | Event table |
| C16 | Nit | CIRCULARS Search | What site search covers (e-Notes? leads? notices? board?) | Coverage statement |
| C17 | Nit | DIRECTORY Self-registration | Rate-limit values, rejection path (password hash removed?), provisional code format, who is notified | States and rejection path |
| C18 | Nit | SITEMAP vs PLATFORM | Load figures and test conditions do not line up | One consistent statement |
| C19 | Major | PLATFORM Risks (HTTP) | Is the live server on HTTPS? Cookie Secure flag? Why was HTTP accepted for the pilot? | Live configuration and decision |
| C20 | Nit | SITEMAP / design docs | No request-lifecycle overview; no run/test pointers | Request lifecycle and "Run and test" |
