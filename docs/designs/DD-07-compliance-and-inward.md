# DD-07 — Compliance Management (R1 = 1.6.0) and Inward Register with Invoice Inward (R2 = 1.7.0)

**Status:** Draft for Goldfish review · **Author:** Elephant session · **Date:** 08.11.2026 · **Baseline:** release 1.5.0, schema 6.

This document is the single source of truth for both releases. An engineer, or a fresh AI session, should be able to build either release from it without this conversation. Existing behaviour is cited by file (paths relative to `src/NabconsIntranet/` unless they start with `docs/` or `tests/`).

### 0.1 Decisions taken for the owner (interview stand-ins)
The owner asked for 1.6.0 to be built in one pass, so the interview questions (brief §C) were answered with the recommended option. Each is easy to change before the 1.6.0 pull request is merged. Those marked ⚙ are settings.

| # | Question | Decision for R1 | Why |
|---|---|---|---|
| D1 | Who forms the Compliance Cell? | One cell for all sources: users with the **ComplianceAdmin** role (Company Secretary's office; F&A staff can be given the role too). | One queue, one ATR process; per-source administrators can be added later as a filter without a schema change. |
| D2 | Ownership by person or vertical? | By **person** (one primary dealing officer, optional supporting officers); the owner vertical is recorded for reports. **Bulk reassignment** page for transfers. | Reminders and accountability need a person; vertical-only ownership leaves no one answerable. |
| D3 | Board item closure | Only the Compliance Cell closes, and **every** closure needs a closure-authority reference (e.g. "Taken on record by the Board, 99th meeting, Memo 7"). The officer can only *propose* closure. | Matches the Board practice that a point closes when the forum takes it on record; makes the trail auditable. |
| D4 | ATR headings, signature, house template | Headings exactly as in brief §B1; organisation name at top; signature block ⚙ `Compliance:SignatureBlock` = "Company Secretary". No `.dotx` in R1; a house template can be adopted later (risk R-3). | DOCX is editable, so final touches can be made in Word. |
| D5 | C&AG / NABARD reports for officers | Officers see the **event header and their own points only**. | Least privilege; the Cell can share more on paper. |
| D6–D8, D10 | Inward/invoice questions | **Open for the R2 interview** (section 15). R2 below states the proposed defaults. | R1 does not depend on them. |
| D9 | Retention | Compliance rows are kept for the life of the system (no deletion). | Board and audit records are permanent records; volume is small (section 10). |
| D11 | Bulk import format | **CSV** (Excel → *Save as CSV UTF-8*), parsed in memory. | A CSV parser exists (`Infrastructure/Csv.cs`); an XLSX reader adds risk for no gain. |
| D12 | Who sees restricted sources (Board, Audit Committee, and any confidential event) | ComplianceAdmin, ComplianceViewer, and the assigned officers (their points only). **Auditor** sees them only if ⚙ `Compliance:AuditorSeesRestricted` = true (default false). The intranet **Administrator** role gives no compliance access. | The Auditor role (internal reviewer) normally has no remit over Board papers; IT administrators must not read Board matters. |

---

## 1. The Problem

**Compliance Management (R1).** NABCONS receives action points from Board and committee meetings, NABARD inspections, statutory/C&AG audits, internal audits and review meetings. Today they are tracked in Word and Excel files kept by different people, so nobody can say at a glance what is open or overdue, officers are chased by phone, and every Action Taken Report (ATR) is retyped from the previous one. The module gives each point an owner, a due date and a dated history of compliance updates vetted by the Compliance Cell. It also prints the ATR in the formats the Board, the auditors and internal audit already use, and keeps a tamper-evident record of exactly what each forum was told.

**Inward Register (R2).** Letters and e-mails go unanswered because they are never inwarded, assigned or tracked, and invoices are received with no view of how many are unpaid or for how long. R2 gives every item an inward number, a current holder, an ageing clock and a closure record, and for invoices a payment lifecycle with MSME 45-day monitoring. Both modules are **registers only**: no file is uploaded or stored.

## 2. Glossary

| Term | Meaning |
|---|---|
| **Event** | A meeting, audit or inspection that produces action points: e.g. "99th Board meeting held on 12.09.2026", "Internal audit Q2 FY 2026-27". |
| **Source type** | The kind of event: one of the fixed codes in §4.1 (Board, AuditCommittee, BoardCommittee, NabardInspection, StatutoryAudit, InternalAudit, ReviewMeeting, Other). It decides the default ATR template and whether the event is *restricted*. |
| **Forum** | The body that held the event or issued the report (master list `ComplianceForum`, e.g. "Audit Committee", "C&AG"). |
| **Restricted** | An event whose source type is in ⚙ `Compliance:RestrictedSourceTypes` **or** that is marked confidential. See decision D12. |
| **Action point** (point) | One thing to be done, under one event. It has a system number `CMP/<FY>/<nnnnn>`, the source reference (memorandum, agenda, para or observation number), the text, a due date and a dealing officer. |
| **Dealing officer** | The **primary** officer answerable for a point; optional **supporting** officers may also see it and submit updates. **Officer code** is the short code printed in the internal-audit sheet (e.g. "AC(AA)"). |
| **Compliance update** | A dated statement by an officer: what has been done "as on" a date, the proposed status, and where supporting papers are kept. Immutable once submitted. |
| **Compliance Cell** | Users with the ComplianceAdmin role. They review updates, decide revisions, close points and produce ATRs. |
| **ATR text** | The vetted "Action Taken" wording that the Cell accepts (and may edit) from an update; this is what is printed. The officer's own text is kept unchanged alongside it. |
| **Due-date revision** | A request to move a point's current due date; the original due date never changes. |
| **Closure authority** | The reference that justifies closure, e.g. "Taken on record by the Board, 99th meeting, Memo 7" or "Dropped by the auditor in the Q3 report". |
| **ATR** | Action Taken Report: the printed statement for a forum, in one of three templates (§7). |
| **Snapshot** | The database copy of an ATR marked *Final — submitted to {forum} on {date}*: every row exactly as printed, plus options and a SHA-256. Immutable. The next ATR's "Action presented in previous meeting" column is filled from it. |
| **Supporting papers kept at** | A text reference to where evidence lives (paper file number, shared-folder path, ERP report). No file is uploaded. |
| **FY** | Indian financial year, April–March, written `2026-27` (same function as `Data/Leads.cs` `LeadRepository.FinancialYear`). |
| **Inward item / holder / copy-to** (R2) | A registered letter, e-mail or invoice; the user who currently has to act on it; users told of it for information only. |

## 3. Technical Plan

### 3.1 Principles
- Same architecture as every module (`docs/designs/PLATFORM.md`): Razor Pages, singleton repository/workflow classes over `Infrastructure/Db.cs`, parameterised SQL, short `BEGIN IMMEDIATE` transactions with `RowVersion` checks, the hash-chained audit log written after commit (`Infrastructure/AuditService.cs`), in-app notifications inside the business transaction (`Infrastructure/Notifications.cs`), My Desk counts via `Data/Desk.cs`.
- **No files.** No `IFormFile` anywhere in the module except the CSV import, which is read into memory and dropped. A middleware refuses any `multipart/form-data` request to `/Compliance/*` other than `/Compliance/Import` (and, in R2, to `/Inward/*` entirely). Nothing is written to `documents/`, `data/tmp` or anywhere else.
- **Generated outputs are streamed and never stored.** The snapshot stores rows, not files.
- No new NuGet package. DOCX is written with `System.IO.Compression` + WordprocessingML text (as `Infrastructure/XlsxWriter.cs` does for XLSX); PDF uses SkiaSharp + HarfBuzz with the embedded Noto fonts (as `Infrastructure/ENotePdf.cs`).

### 3.2 Components (R1)
```
Pages/Compliance/*  ──►  ComplianceAccess (who may see/do what)
        │                 ComplianceRepository (queries: lists, dashboard, desk rows)
        │                 ComplianceWorkflow (every state change; one transaction each)
        │                 AtrBuilder (live data or snapshot ──► ReportDocument)
        │                 ComplianceImport (CSV ─► validated rows, in memory)
        ▼
   Infrastructure/Reports:  ReportDocument (model) ─► DocxWriter | ReportPdfWriter | XlsxWriter.BuildBook
        │                    ReportGate (max 2 generations at once)
        ▼
   Db (SQLite)   AuditService   NotificationService + ChangeTracker   DeskService   HousekeepingService (daily reminders)
```
- **Reuse:** audit (`AuditService.Log`), notifications (`INotificationService.Notify/NotifyWhere`), change markers (`ChangeTracker.TouchUser/Touch`), My Desk (`DeskService`), daily job (`HousekeepingService.ExtraSteps`, run by `BackupScheduler` after the daily backup), master lists (`MasterRepository`, Administration → Master lists), XLSX (`XlsxWriter`, extended), PDF fonts (extracted from `ENotePdf.cs` into a shared `PdfFonts` class), CSV parsing (`Csv.Parse`), FY (`LeadRepository.FinancialYear`), date/time (`Fmt`).
- **e-Note:** not used to carry compliance (see §11). R2 adds "Converted to e-Note".

### 3.3 State diagrams

**Action point** (`Status`):
```
            ┌───────────── accept update (Ongoing) ─────────────┐
 create ──► Ongoing ◄──────── reject closure proposal ──────── ProposedClosure
   │        │  ▲  accept update (PendingExternal)                     ▲
   │        ▼  │                                                      │ accept update (proposes closure)
   │     PendingExternal ─────────────────────────────────────────────┘
   │
   └─ (Ongoing | PendingExternal | ProposedClosure) ── Cell: close  ──► Completed
                                                    ── Cell: drop   ──► Dropped
                                                    ── Cell: merge  ──► Merged (MergedIntoId set)
   Completed | Dropped | Merged ── Cell: reopen (reason) ──► Ongoing
```
*Open* = Ongoing, PendingExternal, ProposedClosure. Overdue = open and `CurrentDueDate < today` (IST).

**Compliance update** (`ReviewState`): `Pending ─► Accepted | Returned`. One pending update per point at a time. Text columns are immutable from the moment of insertion (trigger); the review columns can be set once.

**Due-date revision** (`Status`): `Pending ─► Approved | Rejected`. One pending revision per point. The Cell can also revise directly, which inserts an already-approved row.

**Inward item and invoice:** see R2, §3.6.

### 3.4 Main flows (R1)
1. **Register event** (Cell): form → `ComplianceEvents` row → audit `Compliance.EventCreated`.
2. **Add points** one by one (form) or by **import** (§6.9): CSV upload → parse and validate in memory → preview page that carries the CSV text in a hidden field → *Confirm* re-parses and re-validates the posted text and inserts all rows in one transaction (all or nothing). Each new point gets the next number from `ComplianceSerials` for the FY of today (IST). Officers are notified.
3. **Officer update:** officer submits text, as-on date, proposed status, papers reference → update row (Pending); the point shows "update awaiting review"; Cell queue marker touched.
4. **Cell review:** *Accept* (optionally editing the ATR wording, default = officer text) → point `AtrText`, `AtrAsOn`, `Status` per the proposed status, `NextUpdateDue` recomputed; or *Return* with remarks → officer notified.
5. **Revision:** officer requests new date + reason → Cell approves (point `CurrentDueDate` changes; `Reminded*` markers reset) or rejects.
6. **Closure:** Cell closes, drops or merges with remarks + closure authority; reopen with reason.
7. **ATR:** Cell (or Viewer) picks template, events, as-on date and options → download DOCX/PDF/XLSX (streamed). Cell may then **Mark as final** (forum, submitted-on date) → snapshot rows + SHA-256 saved; later downloads of that snapshot regenerate identical output.
8. **Daily job:** reminders and Cell digest (§8).

### 3.5 Request lifecycle for a compliance page
Browser → existing middleware (CSP, headers) → **NoUploadGuard** (new, refuses multipart to module routes) → authentication → Razor page → `ComplianceAccess` check (404 for anything the user may not see, so existence is not revealed) → repository/workflow → page.

### 3.6 R2 overview (Inward Register + Invoice Inward)
```
Inward pages ─► InwardAccess ─► InwardRepository / InwardWorkflow ─► Db, Audit, Notifications, Desk, e-Note (ENoteWorkflow.Create for "Converted to e-Note")
                               InvoiceWorkflow (payments, auto-close, duplicate check, GSTIN check)
                               InwardImport (project codes CSV, ERP payment CSV; in memory)
```
Inward state: `Registered (unassigned) ─► WithHolder ─► (re-mark ⟲ / returned to registry ─► Registered) ─► Closed ─► (reopen on reminder) WithHolder`.
Invoice state: `Received ─► WithVerifier ─► Certified ─► SentToAccounts ─► PartPaid ─► Paid`, with side exits `OnHold ⇄ (previous)`, `ReturnedToVendor`, `Rejected` (each with reason). `Paid` is automatic when paid + deductions = total; `ClosedShort` needs a reason.

## 4. Data model (R1: migration 7, "Compliance management (v1.6.0)")

Added to `Schema.Migrations` in `Infrastructure/Schema.cs` as entry `(7, …)`; additive only (`CREATE … IF NOT EXISTS`, inserts guarded by `WHERE NOT EXISTS`). Older programs ignore these tables (roll-back safe). New roles are stored as text in `Users.Roles`; older programs drop unknown role names when parsing (`Roles.Parse`), so roll-back is safe.

### 4.1 Fixed codes (in code, `Data/Compliance.cs`)
| SourceType code | Label | Default template |
|---|---|---|
| Board | Board of Directors | BoardAtr |
| AuditCommittee | Audit Committee of the Board | BoardAtr |
| BoardCommittee | Other Board sub-committee (BD, HR, Risk, …) | BoardAtr |
| NabardInspection | NABARD inspection | ObservationReply |
| StatutoryAudit | Statutory / C&AG audit | ObservationReply |
| InternalAudit | Internal audit | InternalAudit |
| ReviewMeeting | Review meeting (subsidiaries, strategic, NABARD oversight) | BoardAtr |
| Other | Other | ObservationReply |

ReportingCycle: `NextMeeting`, `Quarterly`, `Monthly`, `OneTime`. Priority: `High`, `Medium`, `Low`. Point status, review state and revision status as in §3.3.

### 4.2 DDL
```sql
CREATE TABLE IF NOT EXISTS ComplianceSerials (FinancialYear TEXT PRIMARY KEY, LastSerial INTEGER NOT NULL);

CREATE TABLE IF NOT EXISTS ComplianceEvents (
  Id INTEGER PRIMARY KEY,
  SourceType TEXT NOT NULL,              -- §4.1 code
  Forum TEXT NOT NULL,                   -- name from master list ComplianceForum (stored as text)
  EventNo TEXT,                          -- "99th", "Q2 FY 2026-27"
  Title TEXT NOT NULL,
  HeldOn TEXT,                           -- yyyy-MM-dd (meeting date) …
  PeriodFrom TEXT, PeriodTo TEXT,        -- … or period covered (audits)
  ReportReceivedOn TEXT,
  ReportedTo TEXT,                       -- body to which compliance is reported
  ReportingCycle TEXT NOT NULL DEFAULT 'NextMeeting',
  NextReportingOn TEXT,                  -- date of the next meeting / submission, if known
  IsConfidential INTEGER NOT NULL DEFAULT 0,
  SourceKeptAt TEXT,                     -- where the minutes / report are kept
  Remarks TEXT,
  CreatedByUserId INTEGER NOT NULL, CreatedByName TEXT NOT NULL, CreatedAt TEXT NOT NULL,
  UpdatedAt TEXT NOT NULL, RowVersion INTEGER NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS IX_CEvents_Source ON ComplianceEvents(SourceType, HeldOn DESC);

CREATE TABLE IF NOT EXISTS CompliancePoints (
  Id INTEGER PRIMARY KEY,
  EventId INTEGER NOT NULL REFERENCES ComplianceEvents(Id),
  Number TEXT NOT NULL UNIQUE,           -- CMP/2026-27/00012
  FinancialYear TEXT NOT NULL,
  Seq INTEGER NOT NULL DEFAULT 0,        -- order within the event (printing order)
  RefNo TEXT,                            -- memorandum / agenda / para / observation no.
  RefSubject TEXT,                       -- e.g. memorandum subject, "Chairman's opening remarks"
  PointText TEXT NOT NULL,
  Category TEXT, Priority TEXT NOT NULL DEFAULT 'Medium',
  OwnerVertical TEXT,
  PrimaryOfficerUserId INTEGER NOT NULL REFERENCES Users(Id),
  OfficerCode TEXT,
  OriginalDueDate TEXT NOT NULL, CurrentDueDate TEXT NOT NULL,
  NextUpdateDue TEXT,
  Amount REAL,
  CarriedFromId INTEGER REFERENCES CompliancePoints(Id),
  Status TEXT NOT NULL DEFAULT 'Ongoing',
  AtrText TEXT, AtrAsOn TEXT,            -- vetted "action taken" wording and its as-on date
  PapersKeptAt TEXT,
  PendingUpdateId INTEGER, PendingRevisionId INTEGER,
  LastReturnedAt TEXT,                   -- set when an update is returned, cleared by the next submission
  ClosedAt TEXT, ClosedByName TEXT, ClosureRemarks TEXT, ClosureAuthority TEXT,
  MergedIntoId INTEGER REFERENCES CompliancePoints(Id),
  Reminded7For TEXT, Reminded1For TEXT, LastOverdueReminderOn TEXT, UpdateRemindedFor TEXT,
  CreatedByName TEXT NOT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL,
  RowVersion INTEGER NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS IX_CPoints_Event ON CompliancePoints(EventId, Seq, Id);
CREATE INDEX IF NOT EXISTS IX_CPoints_StatusDue ON CompliancePoints(Status, CurrentDueDate);
CREATE INDEX IF NOT EXISTS IX_CPoints_Officer ON CompliancePoints(PrimaryOfficerUserId, Status, CurrentDueDate);
CREATE INDEX IF NOT EXISTS IX_CPoints_Vertical ON CompliancePoints(OwnerVertical, Status);
CREATE INDEX IF NOT EXISTS IX_CPoints_Closed ON CompliancePoints(ClosedAt) WHERE ClosedAt IS NOT NULL;
CREATE INDEX IF NOT EXISTS IX_CPoints_Pending ON CompliancePoints(PendingUpdateId) WHERE PendingUpdateId IS NOT NULL;

CREATE TABLE IF NOT EXISTS ComplianceSupportOfficers (
  PointId INTEGER NOT NULL REFERENCES CompliancePoints(Id), UserId INTEGER NOT NULL REFERENCES Users(Id),
  PRIMARY KEY (PointId, UserId)
);  -- current assignment (changes are recorded in ComplianceHistory)
CREATE INDEX IF NOT EXISTS IX_CSupport_User ON ComplianceSupportOfficers(UserId, PointId);

CREATE TABLE IF NOT EXISTS ComplianceUpdates (
  Id INTEGER PRIMARY KEY,
  PointId INTEGER NOT NULL REFERENCES CompliancePoints(Id),
  SubmittedByUserId INTEGER NOT NULL, SubmittedByName TEXT NOT NULL, SubmittedAt TEXT NOT NULL,
  AsOn TEXT NOT NULL, UpdateText TEXT NOT NULL, ProposedStatus TEXT NOT NULL, PapersKeptAt TEXT,
  ReviewState TEXT NOT NULL DEFAULT 'Pending',
  ReviewedByName TEXT, ReviewedAt TEXT, ReviewRemarks TEXT, AtrText TEXT
);
CREATE INDEX IF NOT EXISTS IX_CUpdates_Point ON ComplianceUpdates(PointId, Id DESC);
CREATE INDEX IF NOT EXISTS IX_CUpdates_Pending ON ComplianceUpdates(ReviewState) WHERE ReviewState = 'Pending';
-- the officer's text never changes; the review is recorded once
CREATE TRIGGER IF NOT EXISTS ComplianceUpdates_guard BEFORE UPDATE ON ComplianceUpdates
WHEN old.ReviewState <> 'Pending' OR new.PointId IS NOT old.PointId OR new.SubmittedByUserId IS NOT old.SubmittedByUserId
  OR new.SubmittedAt IS NOT old.SubmittedAt OR new.AsOn IS NOT old.AsOn OR new.UpdateText IS NOT old.UpdateText
  OR new.ProposedStatus IS NOT old.ProposedStatus OR new.PapersKeptAt IS NOT old.PapersKeptAt
BEGIN SELECT RAISE(ABORT, 'Compliance updates are append-only'); END;
CREATE TRIGGER IF NOT EXISTS ComplianceUpdates_no_delete BEFORE DELETE ON ComplianceUpdates
BEGIN SELECT RAISE(ABORT, 'Compliance updates are append-only'); END;

CREATE TABLE IF NOT EXISTS ComplianceRevisions (
  Id INTEGER PRIMARY KEY,
  PointId INTEGER NOT NULL REFERENCES CompliancePoints(Id),
  RequestedByName TEXT NOT NULL, RequestedAt TEXT NOT NULL,
  OldDate TEXT NOT NULL, NewDate TEXT NOT NULL, Reason TEXT NOT NULL,
  Status TEXT NOT NULL DEFAULT 'Pending', DecidedByName TEXT, DecidedAt TEXT, DecisionRemarks TEXT
);
CREATE INDEX IF NOT EXISTS IX_CRevisions_Point ON ComplianceRevisions(PointId, Id DESC);
CREATE INDEX IF NOT EXISTS IX_CRevisions_Pending ON ComplianceRevisions(Status) WHERE Status = 'Pending';
CREATE TRIGGER IF NOT EXISTS ComplianceRevisions_guard BEFORE UPDATE ON ComplianceRevisions
WHEN old.Status <> 'Pending' OR new.PointId IS NOT old.PointId OR new.OldDate IS NOT old.OldDate
  OR new.NewDate IS NOT old.NewDate OR new.Reason IS NOT old.Reason OR new.RequestedAt IS NOT old.RequestedAt
BEGIN SELECT RAISE(ABORT, 'Due-date revisions are append-only'); END;
CREATE TRIGGER IF NOT EXISTS ComplianceRevisions_no_delete BEFORE DELETE ON ComplianceRevisions
BEGIN SELECT RAISE(ABORT, 'Due-date revisions are append-only'); END;

CREATE TABLE IF NOT EXISTS ComplianceHistory (
  Id INTEGER PRIMARY KEY,
  PointId INTEGER, EventId INTEGER,
  At TEXT NOT NULL, ActorName TEXT NOT NULL,
  Action TEXT NOT NULL, FromStatus TEXT, ToStatus TEXT, Detail TEXT
);
CREATE INDEX IF NOT EXISTS IX_CHistory_Point ON ComplianceHistory(PointId, Id);
CREATE TRIGGER IF NOT EXISTS ComplianceHistory_no_update BEFORE UPDATE ON ComplianceHistory
BEGIN SELECT RAISE(ABORT, 'Compliance history is append-only'); END;
CREATE TRIGGER IF NOT EXISTS ComplianceHistory_no_delete BEFORE DELETE ON ComplianceHistory
BEGIN SELECT RAISE(ABORT, 'Compliance history is append-only'); END;

CREATE TABLE IF NOT EXISTS ComplianceSnapshots (
  Id INTEGER PRIMARY KEY,
  Template TEXT NOT NULL,                -- BoardAtr | ObservationReply | InternalAudit
  Title TEXT NOT NULL,
  SubmittedTo TEXT NOT NULL, SubmittedOn TEXT NOT NULL, AsOn TEXT NOT NULL,
  OptionsJson TEXT NOT NULL,             -- generation options (event ids, flags) as canonical JSON
  HeaderJson TEXT NOT NULL,              -- title lines, annexure headings, summary rows exactly as printed
  ItemCount INTEGER NOT NULL, Sha256 TEXT NOT NULL,
  CreatedByName TEXT NOT NULL, CreatedAt TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS ComplianceSnapshotItems (
  SnapshotId INTEGER NOT NULL REFERENCES ComplianceSnapshots(Id),
  Seq INTEGER NOT NULL, Section INTEGER NOT NULL,   -- section = annexure / sheet index
  PointId INTEGER,                                   -- null for non-point rows
  Cells TEXT NOT NULL,                               -- JSON array of the printed cell texts, in column order
  ReportedText TEXT,                                 -- the "action taken" text given for this point (feeds the next ATR)
  Status TEXT,
  PRIMARY KEY (SnapshotId, Seq)
);
CREATE INDEX IF NOT EXISTS IX_CSnapItems_Point ON ComplianceSnapshotItems(PointId, SnapshotId DESC) WHERE PointId IS NOT NULL;
CREATE TRIGGER IF NOT EXISTS ComplianceSnapshots_no_update BEFORE UPDATE ON ComplianceSnapshots BEGIN SELECT RAISE(ABORT, 'ATR snapshots are immutable'); END;
CREATE TRIGGER IF NOT EXISTS ComplianceSnapshots_no_delete BEFORE DELETE ON ComplianceSnapshots BEGIN SELECT RAISE(ABORT, 'ATR snapshots are immutable'); END;
CREATE TRIGGER IF NOT EXISTS ComplianceSnapshotItems_no_update BEFORE UPDATE ON ComplianceSnapshotItems BEGIN SELECT RAISE(ABORT, 'ATR snapshots are immutable'); END;
CREATE TRIGGER IF NOT EXISTS ComplianceSnapshotItems_no_delete BEFORE DELETE ON ComplianceSnapshotItems BEGIN SELECT RAISE(ABORT, 'ATR snapshots are immutable'); END;

-- master lists (Administration → Master lists), seeded once
INSERT INTO MasterItems (Type, Name, SortOrder, IsActive) SELECT 'ComplianceForum', … WHERE NOT EXISTS (…);
INSERT INTO MasterItems (Type, Name, SortOrder, IsActive) SELECT 'ComplianceCategory', … WHERE NOT EXISTS (…);
```
Seeded forums: Board of Directors; Audit Committee; Business Development Committee; HR Committee; Risk Management Committee; NABARD (Inspection); C&AG; Statutory Auditor; Internal Auditor; Subsidiaries Review Meeting; Strategic Review Meeting; NABARD Oversight Review. Seeded categories: Governance; Finance & Accounts; Taxation; Human Resources; IT & Systems; Business Development; Legal & Statutory; Administration; Other.

**Why points store text (forum, category, vertical) and not master ids:** a master item can be renamed or retired without changing what was recorded or printed (as requests and leads do today).

**Concurrency:** every point change is `UPDATE … WHERE Id = @id AND RowVersion = @rv` inside `BEGIN IMMEDIATE`; 0 rows → "This action point was changed by someone else; please reload." Events likewise. Pending-update and pending-revision uniqueness is enforced by the `PendingUpdateId`/`PendingRevisionId` columns being null-checked in the same `UPDATE`.

**FY rollover:** the number uses the FY of the creation date (IST); `ComplianceSerials` keeps a counter per FY (upsert … `RETURNING`, as `LeadSerials`).

## 5. Permissions matrix (R1)

Roles: **CA** = ComplianceAdmin, **CV** = ComplianceViewer, **AU** = Auditor, **PO** = primary officer of the point, **SO** = supporting officer of the point, **X** = anyone else (including the intranet Administrator unless they also hold CA/CV).
"Restricted" = source type in `RestrictedSourceTypes` or event marked confidential.

| Action | CA | CV | AU (non-restricted) | AU (restricted) | PO | SO | X |
|---|---|---|---|---|---|---|---|
| See Compliance menu | ✔ | ✔ | ✔ | — | ✔ if any open/closed point assigned | ✔ same | — |
| Dashboard, Points list, Events list | ✔ | ✔ | ✔ (non-restricted rows only) | only if `AuditorSeesRestricted` | own points only (My points page) | own | 404 |
| View event header | ✔ | ✔ | ✔ | setting | ✔ (events of own points) | ✔ | 404 |
| View point, updates, revisions, history | ✔ | ✔ | ✔ | setting | ✔ | ✔ | 404 |
| Create/edit event; create/edit/import points | ✔ (open points only for edit) | — | — | — | — | — | — |
| Submit compliance update | — | — | — | — | ✔ (status Ongoing or PendingExternal, no pending update) | ✔ same | — |
| Request due-date revision | — | — | — | — | ✔ (open, none pending) | — | — |
| Review update; decide revision; revise directly | ✔ | — | — | — | — | — | — |
| Close / drop / merge / reopen / reassign / bulk transfer | ✔ | — | — | — | — | — | — |
| Generate ATR, export Excel | ✔ | ✔ | ✔ (non-restricted events only) | setting | — | — | — |
| Mark ATR as final (snapshot) | ✔ | — | — | — | — | — | — |
| View/regenerate snapshots | ✔ | ✔ | ✔ if all its events visible | setting | — | — | — |

Rules:
- Every page and handler checks `ComplianceAccess` server-side; anything not visible returns **404** (not 403), so URL guessing reveals nothing (same as circular drafts, `docs/TEST-SCRIPT.md` row 4).
- A CA who is also PO cannot accept their own update or approve their own revision (segregation): the Review action is hidden and refused when `SubmittedByUserId == current user`. A second CA must act.
- Points list filters, dashboard counts, Excel export, ATR generation, snapshot listing, notifications and My Desk all apply the same visibility predicate (`ComplianceAccess.VisibilitySql`), so restricted rows cannot leak through counts.
- **Never in text outside the module:** notification titles/messages and audit-log details carry only the point number, the source reference and the source-type label, never the point or update text. Compliance data is not added to site search (`Pages/Search.cshtml.cs`).
- The intranet Administrator manages roles (Users page) but has no compliance access through that role.

## 6. Screens (R1)

All under `/Compliance`, linked from a **Compliance** main-menu tab (with a badge = My Desk compliance count) shown to CA, CV, AU and to anyone with an assigned point. Dates shown as **dd.MM.yyyy**; amounts as ₹ with Indian grouping (`Fmt.Money`), with an "₹ lakh" toggle on the Points list and export.

| # | Route | Who | Content and actions |
|---|---|---|---|
| 6.1 | `/Compliance` | CA, CV, AU | **Dashboard:** tiles Open · Overdue · Due in 7 days · Due in 30 days · Awaiting review · Revision requests · Proposed for closure · Officer inactive. **Matrix** source type × status (each cell links to the filtered list). **Ageing** of open points since raised (0–30, 31–90, 91–180, 181–365, >365 days) and overdue days (1–30, 31–90, >90). **By vertical** and **by officer** (top 20, open/overdue). **Closed per month** for the last 12 months (CSS bars). Officers without a viewing role are redirected to `/Compliance/Mine`. |
| 6.2 | `/Compliance/Points` | CA, CV, AU | Drill-down list, 50 per page. Filters: source type, forum, event, status (open, overdue, due7, due30, awaiting review, proposed closure, completed, dropped, merged, all), vertical, officer, category, priority, text (number, reference, text). Handler `?handler=Export` → XLSX of the filtered rows (max 10,000; message if more). |
| 6.3 | `/Compliance/Mine` | anyone | My action points (primary or supporting): open first, overdue highlighted, returned updates flagged, then closed (last 90 days). |
| 6.4 | `/Compliance/Events` | CA, CV, AU | Events list (visible only), filters source type/forum/year; counts open/total per event; *New event* (CA). |
| 6.5 | `/Compliance/EventEdit/{id?}` | CA | Event form (all §4.2 event fields). Source type cannot change once points exist. |
| 6.6 | `/Compliance/Event/{id}` | CA, CV, AU, officers | Event header; its points (officers: only theirs); CA: *Add point*, *Import points*, *Edit event*, *Generate ATR for this event*. |
| 6.7 | `/Compliance/PointEdit/{id?}?eventId=` | CA | Point form: reference, subject, text, category, priority, owner vertical, primary officer (active users), supporting officers (multi-select), officer code, due date (original; read-only after creation — use revision), amount, carried-from (point number), papers kept at, initial ATR text and status (only at creation, for migrating existing open items). Edit allowed while open; history row with changed fields. |
| 6.8 | `/Compliance/Point/{id}` | all who can view | Details, current ATR text, status, due dates; tabs: Updates (each with officer text and vetted ATR text), Revisions, History. Action panels by role: officer — *Submit update*, *Request due-date revision*; CA — *Review pending update* (Accept with editable ATR text / Return with remarks), *Decide revision*, *Revise due date*, *Close*, *Drop*, *Merge into*, *Reopen*, *Reassign officers*. Every form posts `RowVersion`. |
| 6.9 | `/Compliance/Import?eventId=` | CA | Download template (`?handler=Template`, CSV with header + one synthetic row). Upload CSV (≤ `MaxImportKB`, ≤ `MaxImportRows`, `.csv` only, text check: must decode as UTF-8 and contain no NUL bytes) → preview table with per-row errors; the CSV text travels in a hidden field; *Confirm* re-validates and imports all rows or none. Columns: `RefNo, RefSubject, ActionPoint, Category, Priority, OwnerVertical, PrimaryOfficer (user name), SupportingOfficers (user names separated by ;), OfficerCode, DueDate (dd.MM.yyyy or yyyy-MM-dd), Amount, CarriedFrom (point number), Status (Ongoing/PendingExternal), ActionTakenSoFar, AsOn`. |
| 6.10 | `/Compliance/Review` | CA | Cell queue: pending updates (oldest first), pending revisions, proposed closures, overdue without update in 30 days. |
| 6.11 | `/Compliance/Atr` | CA, CV, AU | Generate form: template; events (multi-select of visible events, default = selected event and earlier events of the same forum with open points); as-on date (default today); include points completed since (date, default = last final snapshot of this template and forum, else none); officer code format (Code / Name / Name (Code)); format DOCX / PDF / XLSX → file download. CA: *Mark as final* (submitted to, submitted on) → creates the snapshot and redirects to it. |
| 6.12 | `/Compliance/Snapshots`, `/Compliance/Snapshot/{id}` | CA, CV, AU (visibility rule) | List; details (header, items, SHA-256 with "verified" check); download DOCX / PDF / XLSX regenerated from the snapshot. |
| 6.13 | `/Compliance/Transfer` | CA | Bulk reassignment: from officer → to officer (active), scope primary / supporting / both, open points only; preview count; confirm (one transaction). |

## 7. Outputs

### 7.1 Common model and libraries
`ReportDocument`: organisation line, title, subtitle lines ("Status as on dd.MM.yyyy"), sections (heading, column definitions with relative widths, rows of cell texts, optional bold rows), optional summary table, signature block, footer text, a fixed `GeneratedAt`. Three renderers:
- **DOCX** (`Infrastructure/Reports/DocxWriter.cs`): own WordprocessingML; A4 landscape (`w:pgSz w:w=16838 w:h=11906 w:orient=landscape`), 1.5 cm margins, tables with borders and repeated header rows, footer with "Page {PAGE} of {NUMPAGES}" fields and the snapshot SHA-256 when final. Calibri 10 pt. ZIP entries carry a fixed timestamp (`GeneratedAt`) so output is byte-stable.
- **PDF** (`Infrastructure/Reports/ReportPdfWriter.cs`): SkiaSharp + HarfBuzz, embedded Noto Sans/Devanagari (shared `PdfFonts` extracted from `ENotePdf.cs`), A4 landscape, wrapped table cells, header row repeated on each page, "Page x of y". Metadata dates = `GeneratedAt`.
- **XLSX** (`XlsxWriter.BuildBook`, new overload in `Infrastructure/XlsxWriter.cs`): one sheet per section, wrap-text style, fixed ZIP timestamps.
- All three write to a `Stream`; pages stream them to the response through `WriteOnlyAsyncStream` (`Infrastructure/Endpoints.cs`). At most **2** generations run at once (`ReportGate`, `SemaphoreSlim(2)`); a third waits up to 20 s, then sees "The report generator is busy; try again in a minute." Responses are attachments with `nosniff`.

### 7.2 Template BoardAtr (Board / committees / review meetings)
Title: "Follow-up Actions on the Minutes of the {EventNo} Meeting of the {Forum} held on {HeldOn}" (for a pack: the latest event's title, then "Annexure I … n" sections). Subtitle "Status as on {AsOn}".
- **Annexure I** (latest event by HeldOn): columns **Memorandum / Agenda No. (with subject) | Action Point | Action Taken | Status**. Rows: all points of the event except Dropped/Merged, ordered by Seq. Cell 1 = `RefNo` + newline + `RefSubject`. Action Taken = `AtrText` (or "—"). Status = **Completed** (Completed or ProposedClosure) / **Ongoing** (Ongoing, PendingExternal; PendingExternal adds " (guidance awaited)").
- **Annexure II, III …** (earlier events, newest first): columns **Agenda No. | Action Point | Action presented in previous {Forum} meeting | Further action taken / to be taken**. Rows: points open on the as-on date, plus points completed on or after "include completed since". Column 3 = `ReportedText` of the point's latest snapshot item (any final snapshot), else "—". Column 4 = current `AtrText` + " [Completed]" when completed.
- **Summary** after each annexure: Completed n / Ongoing n / Total n.
- Signature block at the end.

Synthetic example (Annexure I row): `Memo No. 7 — Review of IT security policy | Place the revised IT security policy before the Board after vetting by the Risk Committee | Policy vetted by the Risk Committee on 02.10.2026; placed at item 4 today | Completed`.

### 7.3 Template ObservationReply (statutory / C&AG / NABARD inspection / other)
One section per event: heading "{Forum} — {Title} ({period or date})"; columns **S.No. | Observation | Proposed Action / Reply**. S.No. = `RefNo` or running number. Observation = `PointText`. Reply = `AtrText`; for PendingExternal points the reply is prefixed "Guidance awaited — ". Rows: not Dropped/Merged; closed points only if completed on/after "include completed since".

### 7.4 Template InternalAudit
One section (sheet) for the selection, heading "Internal Audit — compliance status as on {AsOn}"; columns **S.No. | Audit Observation | Dealing Person | Compliance | Status as on {AsOn} | New/Old**. Dealing Person = officer code per the chosen format. Status = Complied (Completed, ProposedClosure) / Ongoing / Pending (PendingExternal). New/Old = **New** if the point belongs to the latest selected event and has no `CarriedFromId`, else **Old**. Rows: open points of the selected events plus points completed since the chosen date. XLSX is the primary format for this template.

### 7.5 Snapshot mechanism
1. *Mark as final* runs the same `AtrBuilder.Build(options)` as the download, producing a `ReportDocument`.
2. In one transaction: insert `ComplianceSnapshots` (template, title, submitted to/on, as-on, `OptionsJson`, `HeaderJson` = everything in the document that is not an item row, item count, SHA-256, creator, time) and one `ComplianceSnapshotItems` row per printed row (`Cells` JSON, `PointId`, `ReportedText`, `Status`).
3. SHA-256 = hash of a canonical text: `NABCONS ATR snapshot v1` + header JSON + every item's `Seq|Section|PointId|Cells|ReportedText|Status` line, UTF-8. Stored and shown; the snapshot page recomputes it and shows **Verified** or **MISMATCH**.
4. **Regeneration** builds the `ReportDocument` from the stored header and items only (no live data), with `GeneratedAt` = the snapshot's `CreatedAt`. The live document rendered at step 1 and the document rebuilt from the snapshot are equal, so the DOCX and XLSX are **byte-identical** and the PDF is identical apart from fields the PDF library randomises, if any (the test pins this; see §13).
5. The footer of a snapshot output reads "Final — submitted to {SubmittedTo} on {SubmittedOn} · Snapshot #{Id} · SHA-256 {first 16 hex}…".
6. Reopening or editing a point never changes a snapshot (triggers). The next ATR reads "previous" text from the newest snapshot item for the point.

### 7.6 Points list Excel export
Columns: Number, Source type, Forum, Event, Ref no., Subject, Action point, Category, Priority, Vertical, Primary officer, Officer code, Original due, Current due, Status, ATR text, As on, Amount (₹ or ₹ lakh), Days overdue, Papers kept at, Closed on, Closure authority.

## 8. Notifications, reminders, My Desk

Notification type prefix `Compliance.` (module label "Compliance" added to `NotificationModules.Labels`). Link always `/Compliance/Point/{id}` (or the Review page). Text: number, reference and source label only (§5).

| Event | Recipients | Type |
|---|---|---|
| Point created / imported, or officer (re)assigned | new primary and supporting officers | `Compliance.Assigned` |
| Update submitted | all active CA (except the submitter) | `Compliance.UpdateSubmitted` |
| Update accepted / returned | submitter (+ primary if different) | `Compliance.UpdateAccepted` / `.UpdateReturned` |
| Revision requested | all active CA | `Compliance.RevisionRequested` |
| Revision approved / rejected; due date revised directly | primary officer | `Compliance.RevisionDecided` |
| Closed / dropped / merged / reopened | primary and supporting officers | `Compliance.Closed` etc. |
| Daily: due in 7 days, due in 1 day, overdue (repeats every `OverdueRepeatDays`), next update due | primary officer | `Compliance.Reminder` |
| Daily digest (only when anything is awaiting review or overdue) | all active CA | `Compliance.Digest` |

**Daily job** (`ComplianceWorkflow.DailyReminders`, added to `HousekeepingService.ExtraSteps` when `Compliance:Enabled`; runs once a day after the backup at `Intranet:DailyBackupTime`). For each open point of an active officer, with today = IST date:
- due-in-7: `today+1 < CurrentDueDate ≤ today+DueSoonDays` and `Reminded7For ≠ CurrentDueDate` → notify, set `Reminded7For`.
- due-in-1: `today ≤ CurrentDueDate ≤ today+1` and `Reminded1For ≠ CurrentDueDate`.
- overdue: `CurrentDueDate < today` and (`LastOverdueReminderOn` is null or ≤ today − `OverdueRepeatDays`).
- update due: `NextUpdateDue ≤ today`, no pending update, `UpdateRemindedFor ≠ NextUpdateDue`.
Using windows (not exact days) means a missed day (PC off) still sends the reminder the next run. Batches of 200 points per transaction.

**NextUpdateDue** = at creation and after each accepted update: Monthly → as-on + 1 month; Quarterly → as-on + 3 months; NextMeeting → event `NextReportingOn` − `UpdateLeadDays` (if set and in the future), else none; OneTime → none. Never later than `CurrentDueDate`.

**My Desk** (`Data/Desk.cs`): two new counts in the existing single count query:
- `ComplianceMine` = open points where the user is primary or supporting officer and (overdue, or due within `DueSoonDays`, or `LastReturnedAt` set, or `NextUpdateDue ≤ today` with no pending update).
- `ComplianceCell` (CA only) = pending updates (not submitted by the user) + pending revisions + proposed closures.
Both are added to `DeskCounts.Total`, to the poll JSON (`compliance`) and to the menu badge. New sections on the My Desk page: "Compliance — my action points" and "Compliance Cell". Change markers: officers via `TouchUser`; the Cell via a new queue key `t:compliance` included in `DeskService.Keys` for CA users.

## 9. Settings (`appsettings.json`, section `Compliance`; all optional — the owner's existing file needs no change)

| Key | Default | Effect |
|---|---|---|
| `Enabled` | `true` | `false` hides the menu, pages (404), My Desk sections and reminders. Data kept. |
| `RestrictedSourceTypes` | `["Board","AuditCommittee"]` | Source type codes treated as restricted (unknown codes ignored). |
| `AuditorSeesRestricted` | `false` | Auditor role may read restricted events. |
| `DueSoonDays` | `7` | "Due soon" window for dashboard, My Desk, reminders. |
| `OverdueRepeatDays` | `7` | Overdue reminder repeat interval. |
| `UpdateLeadDays` | `15` | For "next meeting" cycles: update due this many days before `NextReportingOn`. |
| `SignatureBlock` | `"Company Secretary"` | Printed at the end of ATRs (use `\n` for several lines). |
| `MaxImportRows` | `500` | Rows per import file. |
| `MaxImportKB` | `1024` | Import file size limit. |
| `DefaultOfficerCodeFormat` | `"Code"` | `Code`, `Name` or `NameAndCode`. |

Class `ComplianceOptions` in `Data/Compliance.cs`, registered like `ENoteOptions` in `Program.cs`. Roles `ComplianceAdmin` and `ComplianceViewer` are added to `Roles.All` (Users page shows them; `Intranet:WindowsGroupRoles` can map AD groups to them).

## 10. Storage and backup

Design volumes (5 years): 6,000 points × ~1.5 KB = 9 MB; 40,000 updates × ~1.2 KB = 48 MB; 30,000 history rows × 0.3 KB = 9 MB; 2,000 snapshots × 40 items × ~1.5 KB = 120 MB; indexes ~20 %. **Compliance ≈ 225 MB.** R2: 250,000 inward items × ~0.8 KB = 200 MB, 25,000 invoices × 0.5 KB = 13 MB, movements 750,000 × 0.25 KB = 190 MB → **R2 ≈ 480 MB**. Total database after 5 years ≈ 0.8 GB on top of today's size.

Backup: `BackupService.Create` copies the database with `VACUUM INTO` and zips it (text compresses ~4:1), so a 1 GB database gives a ~250 MB ZIP in about a minute on the reference PC; 30-day retention ≈ 7.5 GB disk. **No new folder** is created by either module (no files), so backup and restore cover the new data unchanged. The existing integrity check, `.partial` handling and restore staging (v1.5.0) apply as they are.

## 11. Alternatives considered and rejected

| Alternative | Why rejected |
|---|---|
| Build compliance on top of e-Note | e-Note is a free-moving note with one holder and an approve/reject outcome; action points need due dates, periodic updates, vetted wording, closure authority and ATR printing. Forcing that into notes would distort both. Integration is through My Desk and notifications instead. |
| Rich-text / table editor for action points | Heavy JS, CSP friction, risk of HTML injection, and Word output would not round-trip. Multi-line plain text plus an amount field and "papers kept at" covers the observed formats. |
| E-mail integration (Outlook / Microsoft 365) | Not reachable from the NABARD network; outbound connections are forbidden. |
| Storing attachments or scanned copies (either module) | **Decided by the COO.** Reasons recorded: Board and audit papers are already held in controlled paper files; duplicating them on a desktop server adds leakage risk, malware exposure through uploads, and storage/backup growth (scans would add tens of GB); the register's job is to track action, not to be a document store. The "kept at" fields give traceability instead. |
| Storing generated ATR files | Files can drift from the data, need storage and integrity control; rows + SHA-256 regenerate the identical report on demand. |
| Chart library | Not allowed (no JS libraries); CSS bars and tables are enough and print well. |
| Separate invoice register (R2) | One number series and one movement trail for all inward items is what registry staff use; invoice fields and lifecycle are a sub-type (extra table keyed by inward id). |
| DocumentFormat.OpenXml / NPOI / ClosedXML packages | Licence-clean, but large and unnecessary: the ATR needs paragraphs and tables only. Hand-written OpenXML is ~300 lines and already proven by `XlsxWriter`. |
| XLSX import | Needs a reader for shared strings, number formats and dates; CSV from Excel is unambiguous. Can be added later. |

## 12. Detailed Implementation

### R1 = 1.6.0 Compliance Management (in this order)
| # | File | Change and reason |
|---|---|---|
| 1 | `Infrastructure/Schema.cs` | Migration 7 (§4.2). |
| 2 | `Infrastructure/Security.cs` | Roles `ComplianceAdmin`, `ComplianceViewer` + descriptions; extensions `IsComplianceAdmin`, `IsComplianceViewer`. |
| 3 | `Data/Models.cs` | `MasterTypes.ComplianceForum`, `ComplianceCategory` + labels (so Administration → Master lists shows them). |
| 4 | `Data/Compliance.cs` (new) | Constants (§4.1), `ComplianceOptions`, entity classes, `ComplianceAccess` (visibility predicate, per-action checks), `ComplianceRepository` (queries: dashboard, lists, mine, events, details, desk rows), `ComplianceWorkflow` (every state change, numbering, notifications, history, audit, daily reminders). |
| 5 | `Data/ComplianceImport.cs` (new) | CSV template, parse and validate rows in memory. |
| 6 | `Data/AtrBuilder.cs` (new) | Build `ReportDocument` for the three templates from live data; snapshot save, load, canonical hash, rebuild. |
| 7 | `Infrastructure/Reports/ReportDocument.cs`, `DocxWriter.cs`, `ReportPdfWriter.cs`, `ReportGate.cs` (new); `Infrastructure/PdfFonts.cs` (new, extracted from `ENotePdf.cs`); `Infrastructure/XlsxWriter.cs` (multi-sheet `BuildBook` to a stream, wrap style, fixed time). |
| 8 | `Infrastructure/Notifications.cs` | Module label; `ChangeTracker.ComplianceQueue`. |
| 9 | `Data/Desk.cs`, `Infrastructure/Endpoints.cs`, `Pages/Desk/Index.cshtml`, `Pages/Shared/_Layout.cshtml`, `wwwroot/js/site.js` (badge key only if needed) | Counts, sections, menu tab and badge. |
| 10 | `Infrastructure/UploadGuard.cs` (new) + `Program.cs` | Refuse multipart to `/Compliance` except `/Compliance/Import`; register services, options, daily step. |
| 11 | `Pages/Compliance/*` (new) | Pages of §6. |
| 12 | `wwwroot/css/site.css` | Classes for tiles, matrix, bars (no inline styles). |
| 13 | `Infrastructure/Seeder.cs` | Demo (new empty databases only): user `cs` (ComplianceAdmin), `coo` also ComplianceViewer, two synthetic events with points. Never touches a non-empty database. |
| 14 | `tests/NabconsIntranet.Tests/ComplianceTests.cs` (new), `tests/e2e/compliance.e2e.js` (new, added to `run-all.sh`), `PerformanceTests` compliance budget, `BenchSeed` compliance volumes. |
| 15 | Docs: `CHANGELOG.md`, `docs/ADMIN-GUIDE.md`, `docs/STAFF-GUIDE.md`, `docs/IT-SECURITY-NOTE.md`, `docs/DEPLOYMENT-GUIDE.md` (settings), `docs/TEST-SCRIPT.md` (rows + p95), `docs/SBOM.md` (no new package), `SETUP_GUIDE.md`, `TESTING_CHECKLIST.md`, `README.md`, `docs/designs/SITEMAP.md` + `PLATFORM.md` (roles, migration 7). |
| 16 | `NabconsIntranet.csproj` version 1.6.0; package rebuilt; previous ZIP removed. |

### R2 = 1.7.0 Inward and Invoice Inward (after the R2 interview)
Migration 8: `InwardSerials`, `InwardItems` (number, office, type, mode, received at, sender, sender ref/date, subject, summary, category, priority, confidential, action required, reply due, kept at, pages, status, holder, closure fields, reminder count, linked-to id, e-Note id, row version), `InwardMovements` (append-only), `InwardCopies`, `InwardNotes` (append-only), `InvoiceDetails` (1:1 with item: vendor, GSTIN, MSME, invoice no/date/FY, project code, PO, taxable, GST, total, terms, payment due, invoice status), `InvoicePayments` (append-only; reversal = negative row with reason), `ProjectCodes`. Files: `Data/Inward.cs`, `Data/Invoices.cs`, `Data/Gstin.cs` (offline checksum), `Pages/Inward/*`, `Pages/Invoices/*`, UploadGuard covers `/Inward` and `/Invoices` except the two CSV imports, Desk sections, reports, prints (acknowledgement slip PDF, daily register PDF/XLSX) reusing the R1 report writers.

## 13. Test plan

**xUnit (`ComplianceTests`)**
1. Migration 7 applies once; `Initialize` twice changes nothing; schema 7; roll-back: a 1.5.0 program starts on the migrated database (manual/upgrade check).
2. Numbering per FY: two points in March and April get `…/2025-26/00001` and `…/2026-27/00001`.
3. Action-point state machine: every allowed transition succeeds; every disallowed one throws (update on Completed point, close without authority, officer closing, own-update review).
4. Update review: accept copies vetted wording; officer text unchanged; `UPDATE ComplianceUpdates SET UpdateText=…` after review fails (trigger); delete fails.
5. Revisions: original due date never changes; second pending request refused; approve resets reminder markers.
6. Concurrency: stale `RowVersion` → `WorkflowException`.
7. Permissions matrix: officer sees only own points (404 on others); X gets 404 on every route; Administrator without CA gets 404; Auditor sees non-restricted only unless the setting is on; restricted points absent from dashboard counts, list, export, ATR event list for Auditor; notification texts contain no point text.
8. Snapshot: immutable (update/delete fail); hash verifies; **ATR regenerated from a snapshot is byte-identical** (DOCX, XLSX; PDF per §7.5); next ATR's "previous" column equals the snapshot text; reopening a point after a snapshot leaves the snapshot unchanged.
9. Import: header and row errors reported; all-or-nothing; a file with an error imports nothing; **nothing is written to disk** (file counts of `data`, `documents`, temp folders before/after).
10. **No file upload accepted:** a multipart POST to every `/Compliance` page except Import returns 415, and the Import page accepts only one `.csv` ≤ limit.
11. Reminders: due-7/due-1/overdue/update-due sent once per window, overdue repeats after 7 days, inactive officers skipped, digest only when needed; restricted text absent.
12. Merge/drop/reopen; bulk transfer moves only open points and notifies.
13. Backup includes the new tables (row count in a restored copy).
14. Generation semaphore: never more than two concurrent renders.

**Playwright (`tests/e2e/compliance.e2e.js`)**: CA registers an event and imports 3 points from CSV; officer sees My Desk item, submits update; CA returns it, officer resubmits, CA accepts with edited wording; officer requests revision, CA approves; CA generates Board ATR DOCX and PDF (downloads have the right type and size), marks final, regenerates; dashboard tiles show expected numbers; staff user gets 404 on the point URL; phone width has no horizontal scroll.

**Performance** (`PerformanceTests`, `NABCONS_BENCH=1`): seed 6,000 points, 40,000 updates, 2,000 snapshots × 40 items across 500 users; budgets p95: dashboard < 300 ms, points list (filtered, page 1) < 300 ms, text search < 400 ms, ATR for 3 meetings × 40 points < 5 s (DOCX and PDF). Results recorded in `docs/TEST-SCRIPT.md`.

**Upgrade check**: copy of a 1.5.0 database → `--migrate` twice → row counts of existing tables unchanged, schema 7 → `--verify-audit` passes → 1.5.0 program still starts on it.

## 14. Acceptance criteria — R1 (1.6.0)
1. Migration 7 is additive, applied once, roll-back safe; existing row counts unchanged on upgrade; audit verifies.
2. Roles ComplianceAdmin / ComplianceViewer assignable on the Users page.
3. CA can register events and add points singly and by CSV import (preview → confirm, all or nothing, nothing written to disk).
4. Officers see only their points, submit updates, and request revisions; CA reviews, decides, closes (authority required), drops, merges, reopens and reassigns, including bulk transfer.
5. Officer text and the history are immutable (triggers); the original due date is never overwritten.
6. Dashboard, matrix, ageing, by-vertical, by-officer and monthly trend display correctly and respect visibility.
7. Board ATR (multi-meeting annexures + summaries), Observation–Reply and Internal-Audit outputs in DOCX, PDF and XLSX, A4 landscape, header, "Status as on", page x of y.
8. Mark as final stores an immutable snapshot with SHA-256; regeneration is identical; the next ATR fills "previous" from it.
9. Reminders and digest arrive in-app and on My Desk; badges update through the existing poll only.
10. Restricted and confidential events never leak to unauthorised users through pages, counts, exports, notifications, My Desk or audit details.
11. No upload is possible on any compliance route other than the CSV import; no generated file is stored.
12. Performance budgets met at design volumes and recorded.
13. All existing tests and the new xUnit and Playwright tests pass; documents updated; package built with checksum.

## 15. Risks and open items
| # | Item | Plan |
|---|---|---|
| R-1 | Decisions D1–D5, D9, D11, D12 taken without the interview | Owner confirms or changes before merge; most are settings or small code changes. |
| R-2 | R2 interview questions D6–D8, D10 (central vs per-office registry and numbering; reply-due defaults and working vs calendar days; invoice certifier, payment terms, ERP export formats; self-inward from day one) | Ask at the start of the R2 Elephant session. Proposed defaults: one number series per office `NCS/IN/<OfficeCode>/<FY>/<serial>`; reply due 7/3/1 calendar days (Normal/Urgent/Immediate); certifier = officer owning the project code; payment terms 30 days (MSME capped at 45); self-inward on from day one. |
| R-3 | Exact house formatting of ATRs (logo, fonts, numbering) | DOCX is editable; a `.dotx` can be applied in Word; a later change can embed a template. |
| R-4 | PDF byte-identity depends on SkiaSharp metadata | Test establishes it; if Skia adds a random document ID, identity is asserted on the extracted model hash and on DOCX/XLSX bytes, and the doc records it. |
| R-5 | Officers transferred or deactivated | Dashboard "officer inactive" tile + bulk transfer; reminders skip inactive users. |
| R-6 | Users with both CA and officer roles | Segregation rule (§5): cannot review their own update or revision. |
| R-7 | SQLite write contention at month-end ATR time | Writes are single short transactions; ATR generation is read-only and limited to two at a time. |
