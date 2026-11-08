# DD-07 — Compliance Management (R1 = 1.6.0) and Inward Register with Invoice Inward (R2 = 1.7.0)

**Status:** v2.1 — R1 revised after Goldfish round 1 and the round-2 readiness pass (G1 comprehension, G2 critic, G3 readiness; resolutions in `docs/designs/reviews/DD-07_consolidated_review_report.md`). R2 is an **outline** that needs its own interview and Elephant pass before it can be built (§16).
**Author:** Elephant session · **Date:** 08.11.2026 · **Baseline:** release 1.5.0, schema 6.

This document is the single source of truth for R1. An engineer, or a fresh AI session, should be able to build R1 from it without the conversation. Existing behaviour is cited by file. Paths are relative to `src/NabconsIntranet/` unless they start with `docs/` or `tests/`.

### 0.1 Decisions taken for the owner (interview stand-ins)
The owner asked for 1.6.0 to be built in one pass, so each interview question was answered with the recommended option. Each is easy to change before the 1.6.0 pull request is merged. ⚙ marks a setting.

| # | Question | Decision for R1 | Why |
|---|---|---|---|
| D1 | Who forms the Compliance Cell? | **One cell for all sources:** members with the *Compliance Admin* grant (the Company Secretary's office; F&A staff can be added). | One queue and one ATR process. Per-source cells can be added later as a filter. |
| D2 | Is ownership by person or by vertical? | **By person:** one primary dealing officer plus optional supporting officers. The owner vertical is recorded for reports. A bulk reassignment page handles transfers. | Reminders and accountability need a named person. |
| D3 | How are Board items closed? | **Only the Cell closes.** Every closure needs a *closure authority* reference. The officer can only propose closure. | A point closes when the forum takes it on record, and the reference makes this auditable. |
| D4 | ATR headings, signature block and house template | Headings as in §7. The organisation name goes at the top. The signature block is ⚙ `SignatureBlock` (default "Company Secretary"). There is no `.dotx` in R1. | The DOCX can be edited in Word for final touches. |
| D5 | What can officers see of C&AG and NABARD reports? | The **event header and their own points** only. | Least privilege. |
| D9 | How long are records kept? | **For the life of the system.** Points and events cannot be deleted (triggers). | They are permanent governance records, and the volume is small (§10). |
| D11 | Bulk import format | **CSV** (in Excel: *Save as CSV UTF-8*), parsed in memory. | `Infrastructure/Csv.cs` already exists. |
| D12 | Who grants compliance access, and who sees restricted sources? | Compliance access is a **compliance grant** held in the module's own table, **not** an intranet role. Only a Compliance Admin can grant or revoke it, so the intranet Administrator cannot give it to anyone, including themselves. The first Compliance Admin is created with the server command `--grant-compliance-admin <user>`, which is audited. Restricted sources (Board, Audit Committee, or any event marked confidential) are visible to Compliance Admins, Compliance Viewers and the assigned officers (their own points only). The *Auditor* role sees them only if ⚙ `AuditorSeesRestricted` = true (default false). | This keeps IT administrators out of Board matters by design. Administrator password resets of compliance members notify the Cell. Anyone who can copy the database file on the server can still read the data: this is recorded as a residual risk in `docs/IT-SECURITY-NOTE.md`. |
| D13 | Who maintains the forum and category lists? | The intranet Administrator, under Administration → Master lists (the existing page). | No new admin screen is needed. The Cell asks IT to add a forum. |
| D14 | Date format | The whole compliance module and its printouts use **dd.MM.yyyy**. The rest of the intranet is unchanged. | This follows the brief. |

---

## 1. The Problem

**Compliance Management (R1).** NABCONS receives action points from several sources: Board and committee meetings, NABARD inspections, statutory and C&AG audits, internal audits, and review meetings. Today they are tracked in Word and Excel files kept by different people. As a result:
- nobody can say at a glance what is open or overdue;
- officers are chased by phone;
- every Action Taken Report (ATR) is retyped from the previous one.

The module gives each point an owner, a due date and a dated history of compliance updates, which the Compliance Cell vets. It prints the ATR in the formats already used for the Board, the auditors and internal audit. It also keeps a tamper-evident record of exactly what each forum was told.

**Inward Register (R2).** Letters and e-mails go unanswered because they are never inwarded, assigned or tracked. Invoices are received, but nobody can say how many are unpaid or for how long.

R2 gives every item:
- an inward number;
- a current holder;
- an ageing clock;
- a closure record.

Invoices also get a payment lifecycle with MSME 45-day monitoring.

Both modules are **registers only**: no file is ever uploaded or stored.

## 2. Glossary

| Term | Meaning |
|---|---|
| **Event** | A meeting, audit or inspection that produces action points. Example: the "99th Board meeting held on 12.09.2026". |
| **Source type** | One of the fixed codes in §4.1. It sets the default ATR template and decides whether the event is *restricted*. |
| **Forum** | The body that held the event or issued the report. Chosen from the master list `ComplianceForum` and stored as both its id and its name. |
| **Restricted** | An event whose source type is listed in ⚙ `RestrictedSourceTypes`, **or** that is marked confidential. Visibility is worked out when the data is read, so changing the setting applies to existing data as well. |
| **Compliance grant** | *Compliance Admin* (CA) or *Compliance Viewer* (CV), held in `ComplianceMembers`. |
| **Compliance Cell** | The active users with a CA grant. |
| **Action point** (point) | One thing to be done, under one event. It has a number `CMP/<FY>/<nnnnn>`, a source reference, text, a due date and a primary officer. |
| **Raised on** | The date the point arose. Ageing is counted from this date. It defaults to the event's held-on date, otherwise the report-received date, otherwise the creation date. |
| **Primary / supporting officer** | The primary officer is answerable for the point. Supporting officers can also see it and submit updates. The **officer code** is a short code printed in the internal-audit sheet, for example "AC(AA)". |
| **Compliance update** | A dated statement by an officer. It cannot be changed once submitted. |
| **ATR text** | The vetted "Action Taken" wording that is printed. The officer's own text is kept unchanged alongside it. |
| **Own point** | A point on which the user is the primary or a supporting officer. A Cell member may not take Cell actions on their own points (segregation of duties). |
| **Closure authority** | The reference that justifies a closure, drop or merge. |
| **ATR** | Action Taken Report: the printed statement for a forum, in one of three templates (§7). |
| **Content hash** | The SHA-256 of the printed content of an ATR (header and rows, without the footer). It is shown on screen and in the draft footer. |
| **Snapshot** | The database copy of an ATR marked *Final — submitted to {forum} on {date}*. It cannot be changed. |
| **Supporting papers kept at** | A text reference to where the evidence is kept. No file is ever uploaded. |
| **FY** | The Indian financial year, April to March, written `2026-27`. Uses `LeadRepository.FinancialYear` in `Data/Leads.cs`. |
| **Today** | The office date: `Fmt.Today` in `Infrastructure/Helpers.cs`, which is UTC plus `DisplayUtcOffsetMinutes` (IST by default). |

## 3. Technical Plan

### 3.1 Principles
- **Same architecture as every module** (`docs/designs/PLATFORM.md`):
  - Razor Pages and singleton repository/workflow classes over `Infrastructure/Db.cs`.
  - Parameterised SQL.
  - One short `BEGIN IMMEDIATE` transaction per state change, with `RowVersion` checks.
  - Notifications are written inside the transaction (`Infrastructure/Notifications.cs`).
  - The hash-chained audit entry is written after the commit (`Infrastructure/AuditService.cs`).
  - My Desk counts come from `Data/Desk.cs`.
- **No files.**
  - The only file input in the module is the CSV import. It is read in memory and then discarded.
  - `UploadGuard` (§3.5) refuses every other upload.
  - Nothing is written to `documents/`, `data/tmp`, the system temporary folder or anywhere else (§3.5 explains how ASP.NET buffering is kept in memory).
- **Bounded text.** Every text field has a maximum length, enforced both by the server code and by `CHECK` constraints. No single word may be longer than 200 characters, which stops encoded files being pasted in as text. The request body on module routes is capped at 256 KB (2 MB on the import page).
- **Generated outputs are never stored.** A snapshot stores rows, not files.
- **No new NuGet package.**
  - DOCX: `Infrastructure/Reports/DocxWriter.cs`.
  - PDF: `Infrastructure/Reports/ReportPdfWriter.cs` (SkiaSharp and HarfBuzz, already bundled).
  - XLSX: `Infrastructure/Reports/XlsxBookWriter.cs`.

### 3.2 Components (R1)
```
Pages/Compliance/*  ──►  ComplianceAccess      who may see/do what (membership cache + visibility SQL)
        │                ComplianceRepository  read queries (dashboard, lists, mine, details, desk rows)
        │                ComplianceWorkflow    every state change (one transaction each), numbering, history, notifications
        │                ComplianceImport      CSV ─► validated rows (in memory)
        │                AtrBuilder            live data or snapshot ─► ReportDocument; content hash; snapshot save/load
        ▼
Infrastructure/Reports:  ReportDocument ─► DocxWriter | ReportPdfWriter | XlsxBookWriter ; ReportGate (2 at a time)
        ▼
Db · AuditService · INotificationService.Notify + ChangeTracker · DeskService · ComplianceScheduler (hosted, daily reminders)
```
- **Reused components:**
  - **Notifications.** Recipients are always resolved to user ids first and passed to `INotificationService.Notify`. The Cell is resolved from `ComplianceMembers`, so it works the same with local and Windows sign-in.
  - **Master lists** come from `MasterRepository`.
  - **Financial year and dates** come from `LeadRepository.FinancialYear` and `Fmt`.
  - **CSV parsing** uses `Csv.Parse`.
  - **Change markers:** `ChangeTracker.TouchUser`, plus the new queue key `t:compliance` for the Cell.
  - **PDF fonts** come from `Infrastructure/PdfFonts.cs` (extracted from `ENotePdf.cs`).
- **e-Note is not used** to carry compliance work (see §11).

### 3.3 Transitions

**Action point.** *Open* means Ongoing, PendingExternal or ProposedClosure.

| From | Action | Who | Required | To / effect |
|---|---|---|---|---|
| — | Create / import | CA | event, text, primary officer, due date | **Ongoing**. Number assigned; officers notified. |
| Ongoing, PendingExternal | Submit update | PO or SO, with no pending update | as-on (≤ today), text, proposed status ∈ {Ongoing, PendingExternal, ProposedClosure} | Update is Pending. Point `PendingUpdateId` is set and `LastReturnedAt` cleared. |
| Ongoing, PendingExternal | Withdraw own pending update | its submitter | — | Update becomes Withdrawn. `PendingUpdateId` is cleared. |
| any open, with a pending update | Accept update | CA, not on an own point and not the submitter | ATR text (default: officer text) | Update Accepted. Point gets `AtrText`, `AtrAsOn` = update as-on, Status = proposed status, `NextUpdateDue` recomputed (§8). |
| any open, with a pending update | Return update | CA (same rule) | remarks | Update Returned. `LastReturnedAt` = now. Status unchanged. |
| ProposedClosure | Reject closure proposal | CA (same rule) | remarks | **Ongoing**. `AtrText` unchanged. |
| any open | Request due-date revision | PO, with no pending revision | new date (≥ today), reason | Revision is Pending. |
| any open | Withdraw own revision | its requester | — | Revision becomes Withdrawn. |
| any open, with a pending revision | Approve / reject revision | CA (same rule) | remarks for reject | On approve, `CurrentDueDate` = new date, the reminder markers are reset and `NextUpdateDue` is recomputed. |
| any open | Revise due date directly | CA (same rule) | new date, reason | An already-approved revision row is inserted, with the same effect as approve. A pending revision request becomes Superseded. |
| any open | Edit point | CA (same rule) | — | Fields are updated. History records the old and new value of each changed field. `Number`, `EventId` and `OriginalDueDate` can never change (trigger). |
| any open | **Close** | CA (same rule) | remarks, closure authority | **Completed**, with closure fields set. Pending update and revision become **Superseded**. |
| any open | **Drop** | CA (same rule) | reason, authority | **Dropped**, same effect. |
| any open | **Merge into** another point | CA (same rule) | target number, reason | **Merged** and `MergedIntoId` set. The target must be open, a different point, in the same restriction class, not merged, and visible to the actor. Chains are not possible because a merged point is not open. |
| Completed, Dropped, Merged | Reopen | CA (same rule) | reason | **Ongoing**. The closure fields and `MergedIntoId` are cleared (history keeps the old values). |
| any open | Reassign officers / bulk transfer | CA (same rule) | new officers | Officers changed and reminder markers reset. Pending items stay for the Cell. Old and new officers are notified. |

Every row writes one `ComplianceHistory` row and one audit entry. Each action posts the point's `RowVersion`; if it is stale, the user sees "This action point was changed by someone else; please reload." Partial unique indexes make sure there is at most one Pending update and one Pending revision per point.

**Compliance update:** `Pending → Accepted | Returned | Withdrawn | Superseded`. **Revision:** `Pending → Approved | Rejected | Withdrawn | Superseded`. Once a row leaves Pending it is frozen by a trigger.

### 3.4 ATR flow (preview, then final)
1. The user opens `/Compliance/Atr` and chooses a template, events, an as-on date and options. **Preview** shows the rows on screen, the **content hash**, and download buttons for DOCX, PDF and XLSX. The draft footer reads "Draft — content hash ABCD…".
2. To **mark it as final**, the CA submits "submitted to", "submitted on" and the hash shown on the preview. The server rebuilds the document. If the content hash differs, it refuses: "The data has changed since your preview; preview again." Otherwise it saves the snapshot in one transaction, writes the audit entry `Compliance.AtrFinalised` with the hash (which puts the hash into the audit chain), and opens the snapshot.
3. The final copy for the forum is downloaded from the snapshot page. Its footer reads "Final — submitted to … on … · Snapshot #n · SHA-256 …".

### 3.5 Request pipeline for module routes
1. The existing header middleware runs first.
2. **UploadGuard** runs next, before authentication. For `POST`, `PUT` and `PATCH` to `/Compliance*` (paths compared without regard to case):
   - The body limit is set to 256 KB, or 2 MB for `/Compliance/Import`, through `IHttpMaxRequestBodySizeFeature`. A larger body gets 413.
   - The content type must be `application/x-www-form-urlencoded`. The only exception is exactly `/Compliance/Import`, which also accepts `multipart/form-data`. Anything else gets **415**.
3. Authentication and the Razor page follow, with `ComplianceAccess` checked on every handler. Anything the user may not see returns **404**.
4. **Keeping the import off disk.** ASP.NET Core writes a multipart body larger than `FormOptions.MemoryBufferThreshold` to a temporary file. `Program.cs` therefore sets `MemoryBufferThreshold` to 2.1 MB, larger than the import page's 2 MB body limit, so an import is always buffered in memory. On other pages this only means uploads under 2 MB stay in memory, which is negligible. A test checks that the temporary folders are unchanged (§13).

### 3.6 R2 overview
R2 is an outline only; see §16.

## 4. Data model (R1: migration 7, "Compliance management (v1.6.0)")

Migration 7 is added to `Schema.Migrations` in `Infrastructure/Schema.cs`. It is additive only:
- `CREATE … IF NOT EXISTS`;
- seed inserts guarded by `WHERE NOT EXISTS`.

Programs before 1.6.0 ignore the new tables, so rolling back is safe. Compliance grants live in the module's own table, so a 1.5.0 program that edits a user cannot remove them.

### 4.1 Fixed codes (`Data/Compliance.cs`)
| SourceType | Label | Default template |
|---|---|---|
| Board | Board of Directors | BoardAtr |
| AuditCommittee | Audit Committee of the Board | BoardAtr |
| BoardCommittee | Other Board sub-committee (BD, HR, Risk, …) | BoardAtr |
| NabardInspection | NABARD inspection | ObservationReply |
| StatutoryAudit | Statutory / C&AG audit | ObservationReply |
| InternalAudit | Internal audit | InternalAudit |
| ReviewMeeting | Review meeting (subsidiaries, strategic, NABARD oversight) | BoardAtr |
| Other | Other | ObservationReply |

| Field | Allowed values |
|---|---|
| ReportingCycle | NextMeeting, Quarterly, Monthly, OneTime |
| Priority | High, Medium, Low |
| Grants | ComplianceAdmin, ComplianceViewer |

### 4.2 DDL (complete)
```sql
CREATE TABLE IF NOT EXISTS ComplianceSerials (FinancialYear TEXT PRIMARY KEY, LastSerial INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS ComplianceState (Key TEXT PRIMARY KEY, Value TEXT);          -- e.g. LastReminderRun

CREATE TABLE IF NOT EXISTS ComplianceMembers (
  UserId INTEGER PRIMARY KEY REFERENCES Users(Id),
  Role TEXT NOT NULL CHECK (Role IN ('ComplianceAdmin','ComplianceViewer')),
  GrantedByName TEXT NOT NULL, GrantedAt TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS ComplianceMemberHistory (
  Id INTEGER PRIMARY KEY, UserId INTEGER NOT NULL, Action TEXT NOT NULL, Role TEXT, ActorName TEXT NOT NULL, At TEXT NOT NULL);
-- + no-update / no-delete triggers on ComplianceMemberHistory

CREATE TABLE IF NOT EXISTS ComplianceEvents (
  Id INTEGER PRIMARY KEY,
  SourceType TEXT NOT NULL, ForumId INTEGER, Forum TEXT NOT NULL CHECK (length(Forum) <= 150),
  EventNo TEXT CHECK (length(EventNo) <= 60), Title TEXT NOT NULL CHECK (length(Title) <= 300),
  HeldOn TEXT, PeriodFrom TEXT, PeriodTo TEXT, ReportReceivedOn TEXT,
  ReportedTo TEXT CHECK (length(ReportedTo) <= 150),
  ReportingCycle TEXT NOT NULL DEFAULT 'NextMeeting', NextReportingOn TEXT,
  IsConfidential INTEGER NOT NULL DEFAULT 0,
  SourceKeptAt TEXT CHECK (length(SourceKeptAt) <= 300), Remarks TEXT CHECK (length(Remarks) <= 1000),
  CreatedByUserId INTEGER NOT NULL, CreatedByName TEXT NOT NULL, CreatedAt TEXT NOT NULL,
  UpdatedAt TEXT NOT NULL, RowVersion INTEGER NOT NULL DEFAULT 1);
CREATE INDEX IF NOT EXISTS IX_CEvents_Source ON ComplianceEvents(SourceType, HeldOn DESC);
CREATE INDEX IF NOT EXISTS IX_CEvents_Forum ON ComplianceEvents(ForumId, HeldOn DESC);
-- + no-delete trigger

CREATE TABLE IF NOT EXISTS CompliancePoints (
  Id INTEGER PRIMARY KEY, EventId INTEGER NOT NULL REFERENCES ComplianceEvents(Id),
  Number TEXT NOT NULL UNIQUE, FinancialYear TEXT NOT NULL, Seq INTEGER NOT NULL DEFAULT 0,
  RefNo TEXT CHECK (length(RefNo) <= 60), RefSubject TEXT CHECK (length(RefSubject) <= 300),
  PointText TEXT NOT NULL CHECK (length(PointText) <= 4000),
  Category TEXT, Priority TEXT NOT NULL DEFAULT 'Medium', OwnerVertical TEXT,
  PrimaryOfficerUserId INTEGER NOT NULL REFERENCES Users(Id), OfficerCode TEXT CHECK (length(OfficerCode) <= 30),
  RaisedOn TEXT NOT NULL, OriginalDueDate TEXT NOT NULL, CurrentDueDate TEXT NOT NULL, NextUpdateDue TEXT,
  Amount REAL, CarriedFromId INTEGER REFERENCES CompliancePoints(Id),
  Status TEXT NOT NULL DEFAULT 'Ongoing'
    CHECK (Status IN ('Ongoing','PendingExternal','ProposedClosure','Completed','Dropped','Merged')),
  AtrText TEXT CHECK (length(AtrText) <= 4000), AtrAsOn TEXT, PapersKeptAt TEXT CHECK (length(PapersKeptAt) <= 300),
  PendingUpdateId INTEGER, PendingRevisionId INTEGER, LastReturnedAt TEXT,
  ClosedAt TEXT, ClosedByName TEXT, ClosureRemarks TEXT CHECK (length(ClosureRemarks) <= 1000),
  ClosureAuthority TEXT CHECK (length(ClosureAuthority) <= 300), MergedIntoId INTEGER REFERENCES CompliancePoints(Id),
  Reminded7For TEXT, Reminded1For TEXT, LastOverdueReminderOn TEXT, UpdateRemindedFor TEXT,
  CreatedByName TEXT NOT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL, RowVersion INTEGER NOT NULL DEFAULT 1);
-- indexes: (EventId, Seq, Id); (Status, CurrentDueDate); (PrimaryOfficerUserId, Status, CurrentDueDate); (OwnerVertical, Status);
--          (ClosedAt) WHERE ClosedAt IS NOT NULL; (CarriedFromId) WHERE CarriedFromId IS NOT NULL
-- triggers: BEFORE UPDATE OF Number, FinancialYear, EventId, OriginalDueDate → abort; BEFORE DELETE → abort

CREATE TABLE IF NOT EXISTS ComplianceSupportOfficers (
  PointId INTEGER NOT NULL REFERENCES CompliancePoints(Id), UserId INTEGER NOT NULL REFERENCES Users(Id),
  PRIMARY KEY (PointId, UserId));
CREATE INDEX IF NOT EXISTS IX_CSupport_User ON ComplianceSupportOfficers(UserId, PointId);

CREATE TABLE IF NOT EXISTS ComplianceUpdates (
  Id INTEGER PRIMARY KEY, PointId INTEGER NOT NULL REFERENCES CompliancePoints(Id),
  SubmittedByUserId INTEGER NOT NULL, SubmittedByName TEXT NOT NULL, SubmittedAt TEXT NOT NULL,
  AsOn TEXT NOT NULL, UpdateText TEXT NOT NULL CHECK (length(UpdateText) <= 4000),
  ProposedStatus TEXT NOT NULL CHECK (ProposedStatus IN ('Ongoing','PendingExternal','ProposedClosure')),
  PapersKeptAt TEXT CHECK (length(PapersKeptAt) <= 300),
  ReviewState TEXT NOT NULL DEFAULT 'Pending' CHECK (ReviewState IN ('Pending','Accepted','Returned','Withdrawn','Superseded')),
  ReviewedByUserId INTEGER, ReviewedByName TEXT, ReviewedAt TEXT,
  ReviewRemarks TEXT CHECK (length(ReviewRemarks) <= 1000), AtrText TEXT CHECK (length(AtrText) <= 4000));
CREATE INDEX IF NOT EXISTS IX_CUpdates_Point ON ComplianceUpdates(PointId, Id DESC);
CREATE UNIQUE INDEX IF NOT EXISTS UX_CUpdates_Pending ON ComplianceUpdates(PointId) WHERE ReviewState = 'Pending';
-- guard: BEFORE UPDATE when old.ReviewState <> 'Pending' or any submitted column (incl. SubmittedByName) changes → abort; BEFORE DELETE → abort

CREATE TABLE IF NOT EXISTS ComplianceRevisions (
  Id INTEGER PRIMARY KEY, PointId INTEGER NOT NULL REFERENCES CompliancePoints(Id),
  RequestedByUserId INTEGER NOT NULL, RequestedByName TEXT NOT NULL, RequestedAt TEXT NOT NULL,
  OldDate TEXT NOT NULL, NewDate TEXT NOT NULL, Reason TEXT NOT NULL CHECK (length(Reason) <= 1000),
  Status TEXT NOT NULL DEFAULT 'Pending' CHECK (Status IN ('Pending','Approved','Rejected','Withdrawn','Superseded')),
  DecidedByUserId INTEGER, DecidedByName TEXT, DecidedAt TEXT, DecisionRemarks TEXT CHECK (length(DecisionRemarks) <= 1000));
CREATE INDEX IF NOT EXISTS IX_CRevisions_Point ON ComplianceRevisions(PointId, Id DESC);
CREATE UNIQUE INDEX IF NOT EXISTS UX_CRevisions_Pending ON ComplianceRevisions(PointId) WHERE Status = 'Pending';
-- guard and no-delete triggers as for updates

CREATE TABLE IF NOT EXISTS ComplianceHistory (
  Id INTEGER PRIMARY KEY, PointId INTEGER, EventId INTEGER, At TEXT NOT NULL,
  ActorUserId INTEGER, ActorName TEXT NOT NULL, Action TEXT NOT NULL, FromStatus TEXT, ToStatus TEXT, Detail TEXT);
CREATE INDEX IF NOT EXISTS IX_CHistory_Point ON ComplianceHistory(PointId, Id);
CREATE INDEX IF NOT EXISTS IX_CHistory_Event ON ComplianceHistory(EventId, At);
-- no-update / no-delete triggers

CREATE TABLE IF NOT EXISTS ComplianceImports (
  Id INTEGER PRIMARY KEY, EventId INTEGER NOT NULL, Sha256 TEXT NOT NULL, Rows INTEGER NOT NULL,
  ImportedByName TEXT NOT NULL, ImportedAt TEXT NOT NULL, UNIQUE (EventId, Sha256));

CREATE TABLE IF NOT EXISTS ComplianceSnapshots (
  Id INTEGER PRIMARY KEY, Template TEXT NOT NULL, ForumId INTEGER, Forum TEXT NOT NULL, Title TEXT NOT NULL,
  SubmittedTo TEXT NOT NULL, SubmittedOn TEXT NOT NULL, AsOn TEXT NOT NULL,
  OptionsJson TEXT NOT NULL, HeaderJson TEXT NOT NULL, ItemCount INTEGER NOT NULL, Sha256 TEXT NOT NULL,
  CreatedByUserId INTEGER NOT NULL, CreatedByName TEXT NOT NULL, CreatedAt TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS IX_CSnap_Forum ON ComplianceSnapshots(Template, ForumId, AsOn DESC);
CREATE TABLE IF NOT EXISTS ComplianceSnapshotEvents (SnapshotId INTEGER NOT NULL, EventId INTEGER NOT NULL, PRIMARY KEY (SnapshotId, EventId));
CREATE INDEX IF NOT EXISTS IX_CSnapEvents_Event ON ComplianceSnapshotEvents(EventId);
CREATE TABLE IF NOT EXISTS ComplianceSnapshotItems (
  SnapshotId INTEGER NOT NULL, Seq INTEGER NOT NULL, Section INTEGER NOT NULL, PointId INTEGER,
  Cells TEXT NOT NULL, Bold INTEGER NOT NULL DEFAULT 0, ReportedText TEXT, Status TEXT, PRIMARY KEY (SnapshotId, Seq));
CREATE INDEX IF NOT EXISTS IX_CSnapItems_Point ON ComplianceSnapshotItems(PointId, SnapshotId DESC) WHERE PointId IS NOT NULL;
CREATE TABLE IF NOT EXISTS ComplianceSnapshotVoids (
  SnapshotId INTEGER PRIMARY KEY, Reason TEXT NOT NULL, VoidedByName TEXT NOT NULL, VoidedAt TEXT NOT NULL);
-- no-update / no-delete triggers on Snapshots, SnapshotEvents, SnapshotItems, SnapshotVoids

-- master lists, seeded once: ComplianceForum and ComplianceCategory (names below)
```
Write the full SQL, with every trigger written out, in `Schema.cs` migration 7. Free-text columns not listed with a CHECK above are limited in code (`ComplianceText`) to 150 characters (names, category, vertical) or 4,000 (history detail).

**Seeded master lists:**
- **Forums:** Board of Directors; Audit Committee; Business Development Committee; HR Committee; Risk Management Committee; NABARD (Inspection); C&AG; Statutory Auditor; Internal Auditor; Subsidiaries Review Meeting; Strategic Review Meeting; NABARD Oversight Review.
- **Categories:** Governance; Finance & Accounts; Taxation; Human Resources; IT & Systems; Business Development; Legal & Statutory; Administration; Other.

**Design notes:**
- **Text plus id.** Points and events store the forum, category and vertical as text, so renaming or retiring a master item never changes a record. Events also store `ForumId`, so ATR continuity (earlier meetings, previous snapshots) survives a rename. An edit form accepts the current value even if it has since been retired.
- **Numbering.** The point number uses the FY of the creation date. `ComplianceSerials` keeps one counter per FY, using the same `INSERT … ON CONFLICT DO UPDATE … RETURNING` pattern as `LeadSerials`. Migrated legacy points get a current-FY number; their `RaisedOn` keeps their real age.

## 5. Permissions

| Code | Meaning |
|---|---|
| **CA** | Compliance Admin grant |
| **CV** | Compliance Viewer grant |
| **AU** | the intranet Auditor role |
| **PO** | primary officer of the point |
| **SO** | supporting officer of the point |
| **X** | anyone else, including the intranet Administrator |

"Restricted" is defined in §2. AU† means: AU sees restricted events only when `AuditorSeesRestricted` is true.

| Action | CA | CV | AU | PO | SO | X |
|---|---|---|---|---|---|---|
| Compliance menu tab | ✔ | ✔ | ✔ | ✔ if any point is assigned | ✔ same | — |
| Dashboard, Points list, Events, Excel export | ✔ | ✔ | non-restricted (+AU†) | — (uses My points) | — | 404 |
| View event header | ✔ | ✔ | non-restricted (+AU†) | events of own points | same | 404 |
| View point, updates, revisions, history | ✔ | ✔ | non-restricted (+AU†) | ✔ | ✔ | 404 |
| Create / edit events; create, edit or import points | ✔ (not on own points) | — | — | — | — | — |
| Submit or withdraw an update | — | — | — | ✔ | ✔ | — |
| Request or withdraw a revision | — | — | — | ✔ | — | — |
| Cell actions (review, decide, revise, close, drop, merge, reopen, reassign, transfer) | ✔ except on own points | — | — | — | — | — |
| Generate an ATR (preview and draft download) | ✔ | ✔ | non-restricted events only (+AU†) | — | — | — |
| Mark an ATR final; void a snapshot | ✔ | — | — | — | — | — |
| View or download a snapshot | ✔ | ✔ | only if every one of its events is visible | — | — | — |
| Manage grants (`/Compliance/Members`) | ✔ (cannot revoke their own grant) | — | — | — | — | — |

**Rules that apply throughout:**
1. **404 for anything not visible.** This applies to every page and handler, so guessing a URL reveals nothing.
2. **One visibility rule everywhere.** Every list, count, export, ATR event choice, snapshot list and desk count uses the same SQL predicate, `ComplianceAccess.VisibleSql`.
3. **Segregation of duties.** A CA may not take Cell actions on their own points, and may not review an update they submitted. The dashboard warns when fewer than two active CAs exist.
4. **No compliance text outside the module.**
   - Notifications carry only the point number, the source reference, and the source-type label (or "Restricted" for restricted events).
   - Audit details carry only ids, numbers, the source-type label (or "restricted"), states and counts. Reasons, remarks, authority and all other free text stay in `ComplianceHistory`.
   - Compliance data is not added to site search (`Pages/Search.cshtml.cs`).
   - The daily job writes its own audit entry `Compliance.Reminders` containing counts only. Downloads and exports are audited as `Compliance.AtrDownloaded` / `Compliance.Exported` with ids and counts only.
5. **Grants are visible to the Cell.** Every grant or revoke, and every administrator password reset of a compliance member, notifies all active CAs and is audited.

## 6. Screens (R1)
All routes are under `/Compliance`. A **Compliance** menu tab appears for the people in §5. Its badge is the compliance part of My Desk. Dates are shown as dd.MM.yyyy. Amounts are shown as ₹ with Indian digit grouping (`Fmt.Money`), and the Points page and its export have a "₹ lakh" option.

| Route | Who | Content and actions |
|---|---|---|
| `/Compliance` | CA, CV, AU | **Dashboard.** Officers with no viewing grant are redirected to `/Compliance/Mine`.<br>**Tiles:** Open; Overdue; Due within `DueSoonDays`; Due within 30 days; Awaiting review; Revision requests; Proposed for closure; *Officer inactive* (open points whose primary officer's account is deactivated).<br>**Breakdowns:**<ul><li>a source type × status matrix, each cell linking to the Points list;</li><li>ageing since `RaisedOn` (0–30, 31–90, 91–180, 181–365, >365 days);</li><li>overdue ageing (1–30, 31–90, >90 days);</li><li>by vertical;</li><li>by officer (top 20);</li><li>closed per month over the last 12 months, drawn as bars with CSS width classes in 5 % steps (no inline styles).</li></ul>**Warning** when fewer than two CAs are active. |
| `/Compliance/Points` | CA, CV, AU | 50 rows per page.<br>**Filters:** source type, forum, event, status (open, overdue, due-soon, due30, awaiting, proposed, completed, dropped, merged, all), vertical, primary officer, category, priority, text (number, reference or point text, via `LIKE`).<br>**Export** (`?handler=Export`): XLSX of the filtered rows, at most 10,000; if there are more, the user is asked to narrow the filter. |
| `/Compliance/Mine` | anyone with points | Own points: open first (overdue, then due soon, then returned updates flagged), then points closed in the last 90 days. |
| `/Compliance/Events` | CA, CV, AU | Visible events, filtered by source type, forum and year, with open/total counts. CA sees a *New event* button. |
| `/Compliance/EventEdit/{id?}` | CA | Event form. The source type is locked once the event has points. Saving recomputes `NextUpdateDue` for the event's open points. |
| `/Compliance/Event/{id}` | CA, CV, AU, officers | Event header and its points (officers see only their own). CA can add a point, import points, edit the event, and generate an ATR. |
| `/Compliance/PointEdit/{id?}?eventId=` | CA | Point form: reference, subject, text, category, priority, vertical, primary and supporting officers (active users; the primary is removed from the supporting list), officer code, raised on, due date (create only), amount, carried-from (a point number the user can see), papers kept at. On create only, an initial status and ATR text with as-on date, for migrating items that are already open. |
| `/Compliance/Point/{id}` | anyone who can view | Point details, current ATR text, status and dates. **Panels:** updates (officer text with the vetted text), revisions, history, linked points (shown only if visible).<br>**Officer forms:** submit update, withdraw update, request revision, withdraw revision.<br>**CA forms:** review (accept with editable ATR text, or return), reject closure proposal, decide revision, revise directly, close, drop, merge, reopen, reassign. Each form posts `RowVersion`. |
| `/Compliance/Import?eventId=` | CA | Template download (`?handler=Template`), upload, preview and confirm. Details below the table. |
| `/Compliance/Review` | CA | The Cell queue: pending updates, pending revisions, proposed closures, and overdue points with no update in 30 days. Items on the CA's own points are shown as "needs another CA". |
| `/Compliance/Atr` | CA, CV, AU | ATR form, preview and draft downloads. Details below the table. |
| `/Compliance/Snapshots` and `/Compliance/Snapshot/{id}` | CA, CV, AU (visibility rule) | **List:** each snapshot with a "voided" flag.<br>**Detail:** header, rows, and the stored SHA-256 with a recomputed check (**Verified** or **MISMATCH**).<br>**Downloads:** DOCX, PDF or XLSX, regenerated from the snapshot.<br>**CA only:** void the snapshot (reason required; appends a row; the snapshot stays). |
| `/Compliance/Transfer` | CA | Bulk transfer from officer A to officer B (B must be active and different from A). Scope: primary, supporting or both, open points only. Shows a preview count before confirming. Excludes points where the acting CA is an officer, and runs in one transaction.<br>When B becomes primary on a point, B is removed from that point's supporting officers. Supporting rows are added with insert-or-ignore. Reminder markers are reset. A and B are notified. |
| `/Compliance/Members` | CA | Lists the grants and their history. CA can grant or revoke CA and CV (active users only; a CA cannot revoke their own grant). |

**Import page (`/Compliance/Import`):**
- **Limits:** at most `MaxImportKB` and `MaxImportRows`, `.csv` only. The file must decode as UTF-8 with no NUL bytes; otherwise the user is told to save it as "CSV UTF-8".
- **Headers** are matched without regard to case and can be in any order: `RefNo, RefSubject, ActionPoint*, Category, Priority, OwnerVertical, PrimaryOfficer*, SupportingOfficers, OfficerCode, RaisedOn, DueDate*, Amount, CarriedFrom, Status, ActionTakenSoFar, AsOn` (* required).
- **Field rules:**
  - Officers are given by **login user name** (`Users.Username`, without regard to case; `;` separates supporting officers) and must be active.
  - Category and vertical must be blank or match an active master item.
  - Priority defaults to Medium.
  - Dates may be written dd.MM.yyyy, dd/MM/yyyy, dd-MM-yyyy, d/M/yyyy or yyyy-MM-dd. Past due dates are allowed.
  - Status is Ongoing or PendingExternal. ActionTakenSoFar needs AsOn.
  - Text length rules are the same as on the forms.
- **Preview** carries the CSV text in a hidden field; the confirm form is posted as `multipart/form-data` (allowed only on this page), so the text is not percent-encoded. The SHA-256 is computed on the text after removing a BOM and turning CRLF into LF.
- Rows naming the acting CA as primary or supporting officer are refused (segregation). Officers get **one** notification per import listing how many points were assigned.
- **Confirm** re-checks the size and row limits and re-validates every row. It then refuses a file already imported into the event (`ComplianceImports` holds the SHA-256 per event). Finally it inserts every row in one transaction, numbered in file order, with `Seq` continuing from the event's maximum.

**ATR page (`/Compliance/Atr`):**
- **Options:**
  - Template: defaults to the source type of the first event.
  - Events: visible events. **BoardAtr and InternalAudit: all selected events must belong to one forum** (`ForumId`); ObservationReply may mix forums and stores the newest event's forum, which only drives the defaults. A server-side *Pack* button (`?handler=Pack&forumId=`) ticks that forum's newest event plus its earlier events with open points.
  - As-on date (§7.6).
  - Include completed since: defaults to the as-on date of the previous snapshot for the same template and forum.
  - Officer code format: Code, Name, or Name (Code).
- **Actions:**
  - Preview: shows the rows on screen with the content hash.
  - Draft downloads: DOCX, PDF and XLSX.
  - Mark as final (CA): §3.4.

## 7. Outputs

### 7.1 Model and writers (already built and tested)
`ReportDocument` (`Infrastructure/Reports/ReportDocument.cs`) contains:
- the organisation line, title and subtitles;
- the sections, each with a heading, relative column widths and rows (each row has its cells and a bold flag), and an `IsSummary` flag;
- the signature block and footer;
- a fixed `GeneratedAt` and the file name.

The writers render only from this model:

| Format | Writer | Notes |
|---|---|---|
| DOCX | `DocxWriter` | A4 landscape, bordered tables with the heading row repeated on each page, footer "Page {PAGE} of {NUMPAGES}" |
| PDF | `ReportPdfWriter` | A4 landscape, wrapped cells, long rows split across pages, page x of y |
| XLSX | `XlsxBookWriter` | One sheet per non-summary section, title rows, frozen heading row, wrapped cells, no formulas |

**Determinism.** ZIP entry times and PDF metadata dates are both set to `GeneratedAt`, and the metadata author is "NABCONS Intranet". As a result, **the same model gives the same bytes in all three formats**; this is tested in `tests/NabconsIntranet.Tests/ReportWriterTests.cs`.

**Limits.**
- `ReportGate` allows at most 2 renders at a time across ATRs, exports and e-Note PDFs; `ENotePdfService` uses the same gate. A third request waits up to 20 s and then sees "busy".
- Output is rendered into a `MemoryStream` capped at 50 MB and sent only when complete, so a failure never produces a truncated download.
- Responses are sent as attachments with `nosniff`.

### 7.2 BoardAtr (Board, committees, review meetings)
- **Title:** "Follow-up Actions on the Minutes of the {EventNo} Meeting of the {Forum} held on {HeldOn}", taken from the newest selected event.
- **Subtitle:** "Status as on {AsOn}".

**Annexure I** is the newest selected event, by HeldOn.
- **Columns:** Memorandum / Agenda No. (with subject) | Action Point | Action Taken | Status.
- **Rows:** all of the event's points, ordered by Seq, except Dropped and Merged points. Completed points are always printed, as this is the meeting being reported.
- **Cell contents:**
  - Column 1: `RefNo`, a line break, then `RefSubject`.
  - Action Taken: `AtrText`, or "—".
  - Status: **Completed** for Completed and ProposedClosure points; **Ongoing** for Ongoing points; **Ongoing (guidance awaited)** for PendingExternal points.

**Annexure II, III, …** are the earlier selected events, newest first.
- **Columns:** Agenda No. | Action Point | Action presented in previous {Forum} meeting | Further action taken / to be taken.
- **Rows:** points that are open, plus points completed (`ClosedAt` date) on or after the "include completed since" date.
- **Column 3:** the `ReportedText` of the point's item in the **previous snapshot**: the newest non-voided snapshot of the same template and `ForumId` with `AsOn` < the current as-on date. If there is none, "—".
- **Column 4:** the current `AtrText`, followed by " [Completed]" when completed.

**After each annexure:** a summary table showing Completed n / Ongoing n / Total n.

**At the end:** the signature block.

**Synthetic example:** `Memo No. 7 — Review of IT security policy | Place the revised IT security policy before the Board after vetting by the Risk Committee | Policy vetted by the Risk Committee on 02.10.2026; placed at item 4 today | Completed`.

### 7.3 ObservationReply (statutory, C&AG, NABARD inspection, other)
Each event is one section, with the heading "{Forum} — {Title} ({period or date})".
- **Columns:** S.No. | Observation | Proposed Action / Reply.
- **S.No.:** `RefNo`, or a running number.
- **Reply:** `AtrText`. For PendingExternal points it is prefixed "Guidance awaited — ".
- **Rows:** all points except Dropped and Merged. Completed points are included only if completed since the chosen date.

### 7.4 InternalAudit
A single section and sheet, with the heading "Internal Audit — compliance status as on {AsOn}".
- **Columns:** S.No. | Audit Observation | Dealing Person | Compliance | Status as on {AsOn} | New/Old.
- **Dealing Person:** the officer code in the chosen format. If the point has no code, the officer's name.
- **Status:**
  - **Complied** for Completed and ProposedClosure points;
  - **Ongoing** for Ongoing points;
  - **Pending** for PendingExternal points.
- **New/Old:** **New** if the point belongs to the newest selected event and has no `CarriedFromId`; otherwise **Old**.
- **Rows:** open points, plus points completed since the chosen date.

### 7.5 Snapshot mechanism
1. **What the snapshot stores.**
   - `ComplianceSnapshotItems` holds the printed table rows of non-summary sections: section index, cells as a JSON array, bold flag, `PointId`, `ReportedText` = the raw `AtrText` printed for that point (no " [Completed]" suffix), and `Status` = the point's status code. In the hash line a null value is written as an empty string.
   - Mark as final rebuilds, compares the hash and inserts the snapshot inside **one** `BEGIN IMMEDIATE` transaction. The live hash is computed through the same "document → snapshot rows" function used for saving.
   - `HeaderJson` holds everything else as JSON: organisation line, title, subtitles, signature, the definition of each section (heading, columns and widths, summary flag, sheet name) and the summary rows.
   - JSON is written with `System.Text.Json` using one fixed options object: default encoder, no indentation, properties in declaration order.
2. **Content hash.** It is the SHA-256 of the UTF-8 text made of the line `NABCONS ATR v1`, the `HeaderJson`, and one line per item: `Seq|Section|PointId|Bold|Cells|ReportedText|Status`. It is the same for a live preview and for the snapshot made from it.
3. **Footers.** The footer is not part of the content. It is added at render time:
   - draft: "Draft — content hash {16 hex}";
   - final: "Final — submitted to {SubmittedTo} on {SubmittedOn} · Snapshot #{Id} · SHA-256 {16 hex}".
4. **Regeneration.** The document is rebuilt only from the snapshot rows, with `GeneratedAt` = the snapshot's `CreatedAt`. Two regenerations give byte-identical DOCX, XLSX and PDF. The document rebuilt from a snapshot is equal to the live document that was saved (same content hash), and rendering that live document with the final footer gives the same bytes as the regeneration.
5. **Integrity.** The stored SHA-256 is also written into the hash-chained audit log (`Compliance.AtrFinalised`), so the snapshot page's "Verified" check is anchored by `--verify-audit`.
6. **Immutability and voiding.** A snapshot cannot be updated or deleted. A snapshot marked final by mistake is **voided**: a row is appended to `ComplianceSnapshotVoids`, and a voided snapshot is ignored as a "previous" source. Reopening or editing a point never changes a snapshot.

### 7.6 As-on date rule
- The as-on date is a **label** for the current state of the data, so it cannot be earlier than any change the report contains. It must be on or after the latest of the following, across the points the report will print:
  - their `AtrAsOn`;
  - their `ClosedAt` date;
  - the date of their latest `ComplianceHistory` entry.
- The as-on date must also be on or before today.
- The form shows the earliest date it will accept, and refuses an earlier one with that reason.

### 7.7 Points list export
The export has the following columns:

| Column | Notes |
|---|---|
| Number | |
| Source type | |
| Forum | |
| Event | |
| Ref no. | |
| Subject | |
| Action point | |
| Category | |
| Priority | |
| Vertical | |
| Primary officer | |
| Officer code | |
| Raised on | |
| Original due | |
| Current due | |
| Status | |
| ATR text | |
| As on | |
| Amount | in ₹, or ₹ lakh when that option is chosen |
| Days overdue | |
| Papers kept at | |
| Closed on | |
| Closure authority | |

## 8. Notifications, reminders and My Desk

**Notifications.** Notification types start with `Compliance.`, and "Compliance" is added to `NotificationModules.Labels`.
- Each notification links to `/Compliance/Point/{id}`, or to the Review or Mine page.
- The text contains only the point number, the reference and the source label ("Restricted" for restricted events).

| Event | Recipients |
|---|---|
| Point created, imported, reassigned or transferred | new primary and supporting officers (old officers too on transfer) |
| Update submitted | active CAs except the submitter |
| Update accepted or returned | the submitter, and the primary officer if different |
| Revision requested or withdrawn | active CAs |
| Revision approved, rejected or directly revised | the primary officer |
| Closure proposal rejected | primary and supporting officers |
| Closed, dropped, merged or reopened | primary and supporting officers |
| Grant changed; administrator reset the password of a member | active CAs |
| Daily reminder | each officer gets **one** notification per run summarising their points: "n overdue, n due within 7 days, n due tomorrow, n updates due" |
| Daily digest | active CAs, only when something is awaiting review or overdue |

**Daily job: `ComplianceScheduler`.**
- **Timing.** It is a hosted service. It checks every 30 minutes and, the first time it finds the office time (`Fmt`) at or after 09:00, runs `ComplianceWorkflow.DailyReminders(fmt.Today)` once that day. It records the run date in `ComplianceState('LastReminderRun')`, so the job runs once a day whenever the PC is on, independent of the backup time. If the PC was off at 09:00, the job runs when the PC is next switched on.
- **Per point.** For each open point whose primary officer is active:
  - **Due soon:** `today+1 < CurrentDueDate ≤ today+DueSoonDays` and `Reminded7For ≠ CurrentDueDate`.
  - **Due tomorrow or today:** `today ≤ CurrentDueDate ≤ today+1` and `Reminded1For ≠ CurrentDueDate`.
  - **Overdue:** `CurrentDueDate < today` and (`LastOverdueReminderOn` is null, or ≤ today − `OverdueRepeatDays`).
  - **Update due:** `NextUpdateDue ≤ today`, there is no pending update, and `UpdateRemindedFor ≠ NextUpdateDue`.

  The point's markers are set in the same transaction as the notification. Points are processed in batches of 200 per transaction.
- **Example.** With today = 10.11 and due = 11.11, the point is "due tomorrow" (not "due soon"). On 12.11 it is "overdue".
- **Return value.** The job returns counts only.

**NextUpdateDue.**
- **When it is recomputed:** when a point is created, when an update is accepted, when a revision is approved or the date is revised directly, when the point is reopened, and when the event is edited. Reminders go to the **primary** officer only.
- **Base date:** the as-on date of the latest accepted update, otherwise the point's `RaisedOn`.
- **By reporting cycle:**

  | Cycle | NextUpdateDue |
  |---|---|
  | Monthly | base + 1 month |
  | Quarterly | base + 3 months |
  | NextMeeting | if `NextReportingOn` is after today: the later of today and `NextReportingOn` − `UpdateLeadDays`; otherwise none |
  | OneTime | none |

- **Cap.** It is capped at `CurrentDueDate`. If it would then fall before today, it is set to today, so that an overdue point with no recent update asks for one.

**My Desk (`Data/Desk.cs`).**
- **New `DeskCounts` members.** They are optional named parameters with default 0, added at the end, so existing calls keep working:
  - `ComplianceMine`: open own points (primary or supporting, counted once) that are overdue, due within `DueSoonDays`, have a returned update, or have an update due with none pending.
  - `ComplianceCell`: for CAs only, the pending updates, pending revisions and proposed closures, excluding own points.
  - `ComplianceAssigned`: 1 if the user has any point, open or closed. Used only to show the menu tab.
- **Totals and badges.** `Total` includes `ComplianceMine` + `ComplianceCell`. The poll JSON gains `compliance`; the token hash includes both counts; `site.js` adds `compliance` to its badge list.
- **Cache keys.** `DeskService.Keys` adds `t:compliance` for CAs. `RoleKey` adds the user's compliance grant, read from the `ComplianceAccess` cache.
- **Sections.** My Desk gets two new sections: "Compliance — my action points" and "Compliance Cell".
- **Change markers.** Officers are touched per user; the Cell queue is touched whenever an update or revision is submitted, withdrawn or decided, and on any closure-related change.

## 9. Settings (`appsettings.json` section `Compliance`)
All settings are optional: the owner's existing file needs no change. This follows the existing pattern for `ENote` and `MessageBoard` (CLAUDE.md asks only that new settings have safe defaults).

| Key | Default | Effect |
|---|---|---|
| `Enabled` | `true` | `false` hides the tab, pages (404), desk sections and reminders. Data is kept. |
| `RestrictedSourceTypes` | `["Board","AuditCommittee"]` | Codes matched case-insensitively. Unknown codes are logged at start-up and ignored. Write `["None"]` for no restricted types. A list given in the file replaces the default (it is not added to it). |
| `AuditorSeesRestricted` | `false` | When true, the Auditor role can read restricted events. |
| `DueSoonDays` | `7` | Window used by the dashboard, desk and reminders. |
| `OverdueRepeatDays` | `7` | How often an overdue point is reminded again. |
| `UpdateLeadDays` | `15` | For the NextMeeting cycle: how many days before the meeting an update is due. |
| `ReminderHour` | `9` | Office hour after which the daily job runs. |
| `SignatureBlock` | `"Company Secretary"` | Text printed at the end of an ATR. `\n` starts a new line. |
| `MaxImportRows` / `MaxImportKB` | `500` / `1024` | Import limits. |
| `DefaultOfficerCodeFormat` | `"Code"` | `Code`, `Name` or `NameAndCode`. |

## 10. Storage and backup
- **Volume after 5 years (design volumes):** about 225 MB for compliance:

  | Data | Rows × size | Total |
  |---|---|---|
  | Points | 6,000 × 1.5 KB | 9 MB |
  | Updates | 40,000 × 1.2 KB | 48 MB |
  | History | 30,000 × 0.3 KB | 9 MB |
  | Snapshots | 2,000 × 40 items × 1.5 KB | 120 MB |
  | Indexes | about +20 % | |

- **R2 (estimate):** about 480 MB more.
- **Backup.** `BackupService` copies the database with `VACUUM INTO` and zips it. Text compresses about 4:1, so a 1 GB database becomes a ZIP of about 250 MB, taking about a minute on the reference PC (7th-gen Core i5, 16 GB, SSD).
- **No new folder.** Neither module creates a folder, so the backup and restore cover the new tables unchanged. The integrity check, `.partial` handling and restore staging added in v1.5.0 apply.
- **Upgrade copies.** Each upgrade also leaves a `pre-upgrade-*.db` copy next to the database. These are not pruned automatically. At about 1 GB each, delete old copies after a successful upgrade (`docs/UPGRADE-GUIDE.md`, and the monthly check in `PLATFORM.md`).

## 11. Alternatives considered and rejected
| Alternative | Why rejected |
|---|---|
| Building compliance on top of e-Note | e-Note is a free-moving note with one holder and an approve/reject outcome. Action points need due dates, periodic vetted updates, closure authority and ATR printing. Integration is through My Desk and notifications instead. |
| Compliance roles on the intranet Users page | The intranet Administrator could grant them to themselves (finding G2-5), and a 1.5.0 roll-back would strip them (G2-22, G3-8). A separate grant table managed by the Cell avoids both. |
| Rich-text or table editor | Needs heavy JavaScript, conflicts with the CSP and risks HTML injection. Multi-line plain text plus the amount field and "papers kept at" is enough. |
| E-mail integration | The network cannot reach it, and outbound connections are forbidden. |
| Storing attachments or scans (either module) | **Decided by the COO.** The papers are already held in controlled paper files. Copies on a desktop server would add leakage risk, malware exposure through uploads, and tens of GB of storage and backup. The register tracks action; the "kept at" fields give traceability. |
| Storing the generated ATR files | Files can drift from the data and need their own storage and integrity checks. Rows plus a SHA-256 anchored in the audit chain regenerate an identical report. |
| Chart library | JavaScript libraries are not allowed. CSS width classes and tables are enough. |
| OpenXml SDK, NPOI or ClosedXML | Licence-clean but large. Paragraphs and tables need about 300 lines of code, already written and tested. |
| XLSX import | CSV from Excel is unambiguous, and the parser already exists. |
| Running reminders from the backup job | A PC switched off before 19:30 would never send them (finding G2-10). |
| Rebuilding past states for back-dated ATRs | Replaying history is complex and error-prone. The as-on rule in §7.6 prevents misstatement instead. |
| Inline per-value bar widths | Blocked by the CSP (`style-src 'self'`). |

## 12. Detailed implementation, R1 (in this order)
| # | File | Change |
|---|---|---|
| 1 | `Infrastructure/Schema.cs` | Migration 7 (§4.2). |
| 2 | `Infrastructure/Notifications.cs` | Add the "Compliance" label and `ChangeTracker.ComplianceQueue = "t:compliance"`. |
| 3 | `Data/Models.cs` | Add `MasterTypes.ComplianceForum` and `ComplianceCategory`, with labels. |
| 4 | `Infrastructure/Helpers.cs` | Add `Fmt.DateDot(iso)` (dd.MM.yyyy) and `Fmt.Lakh(amount)`. |
| 5 | `Infrastructure/Reports/*`, `Infrastructure/PdfFonts.cs`, `Infrastructure/ENotePdf.cs` | **Done:** writers, gate, shared fonts. Remaining: `ENotePdfService` uses `ReportGate`. |
| 6 | `Data/Compliance.cs` (new) | Codes, `ComplianceOptions` (with `Resolve()`), entities, `ComplianceAccess` (membership cache reloaded on change; `VisibleSql`; checks per action), `ComplianceRepository`, `ComplianceWorkflow` (§3.3, notifications, history, audit, `DailyReminders`). |
| 7 | `Data/ComplianceImport.cs` (new) | Template, parsing and validation. |
| 8 | `Data/AtrBuilder.cs` (new) | The three templates, the as-on rule, previous-snapshot lookup, content hash, snapshot save, load, regenerate and void. |
| 9 | `Infrastructure/ComplianceScheduler.cs` (new) | Hosted daily job. |
| 10 | `Infrastructure/UploadGuard.cs` (new; partial: content-type allowlist ignoring `; charset=`, body caps and the call in `Program.cs` right after the header middleware still to add) + `Program.cs` | Content-type allowlist, body caps, `MemoryBufferThreshold`. Register the options, services and hosted service. |
| 11 | `Infrastructure/Tools.cs`, `deploy/windows/Grant-Compliance-Admin.bat` | `--grant-compliance-admin <user>`: always allowed (recovery when every CA has left), only for an existing active user (in Windows mode the user must have signed in once), audited, notifies the CAs; listed in the usage text. |
| 12 | `Data/Desk.cs`, `Infrastructure/Endpoints.cs`, `Pages/Desk/Index.cshtml`, `Pages/Shared/_Layout.cshtml`, `wwwroot/js/site.js` | Counts, cache keys, token, poll JSON, desk sections, tab and badge list. |
| 13 | `Pages/Admin/UserEdit.cshtml.cs` | When an administrator resets a compliance member's password, or gives anyone the Auditor role, notify the CAs (the notification opens its own transaction). |
| 14 | `Pages/Compliance/*` (new) | The pages in §6, plus a shared `_ComplianceTabs.cshtml`. |
| 15 | `wwwroot/css/site.css` | Tiles, matrix, bar width classes `w-0` to `w-100` in steps of 5. |
| 16 | `Infrastructure/Seeder.cs` | Demo data on **new empty databases only**: user `cs` (CA), `coo` given a CV grant, two synthetic events with points. |
| 17 | Tests | `ComplianceTests.cs` (new), `ReportWriterTests.cs` (**written**), `BoardTests.cs` (schema assertion becomes `>= 6`), `tests/e2e/compliance.e2e.js` + `run-all.sh`, compliance budgets in `PerformanceTests`/`BenchSeed`. |
| 18 | Docs | `CHANGELOG.md`, `README.md`, `SETUP_GUIDE.md`, `TESTING_CHECKLIST.md`, `docs/ADMIN-GUIDE.md`, `docs/STAFF-GUIDE.md`, `docs/IT-SECURITY-NOTE.md` (grants, residual database-file risk), `docs/DEPLOYMENT-GUIDE.md` (settings, grant command), `docs/UPGRADE-GUIDE.md`, `docs/TEST-SCRIPT.md` (rows and p95 results), `docs/SBOM.md` (no new package), `docs/designs/SITEMAP.md`, `PLATFORM.md` (migration 7), `DESK-NOTIFICATIONS.md`. |
| 19 | `NabconsIntranet.csproj` | Version 1.6.0. Rebuild the package and remove the 1.5.0 ZIP. |

## 13. Test plan (R1)
**xUnit (`ComplianceTests`):**
1. **Migration.** Migration 7 is applied once and the schema reaches 7. All triggers and CHECK constraints are active.
2. **Numbering.** Numbers run per FY: March and April give `…/2025-26/00001` and `…/2026-27/00001`.
3. **Transitions.** Every allowed transition in §3.3 succeeds, and representative forbidden ones fail. These include:
   - updating a Completed point;
   - closing without an authority;
   - an officer trying to close;
   - a CA acting on an own point;
   - merging into a closed point or one in a different restriction class;
   - a second pending update.
4. **Updates.** Accepting stores the vetted text. The officer's text is unchanged. Updating a reviewed row through SQL fails, and so does a delete. Closing a point supersedes its pending update and revision. Withdrawing works.
5. **Revisions.** The original due date can never change (the trigger is checked by SQL too). Approving a revision resets the reminder markers.
6. **Concurrency.** A stale `RowVersion` is refused.
7. **Permissions.**
   - An officer sees only their own points, and gets 404 on other points.
   - X gets 404 everywhere. An intranet Administrator without a grant gets 404.
   - The Auditor sees no restricted events unless `AuditorSeesRestricted` is on: not on the dashboard, the list, the export, the ATR event list or the snapshot list.
   - Notification and audit texts contain no point text.
8. **Grants.** Only a CA can grant. A CA cannot revoke their own grant. Every change is notified and audited. The `--grant-compliance-admin` command works.
9. **Snapshots.**
   - Updating or deleting a snapshot fails.
   - The hash verifies.
   - Regeneration is byte-identical (DOCX, XLSX and PDF).
   - The live document with the final footer renders the same bytes as the regeneration.
   - Marking final with a stale preview hash is refused.
   - The next ATR's "previous" column shows the snapshot text, and ignores voided snapshots, other forums and other templates.
   - Reopening a point does not change its snapshot.
10. **As-on rule.** An as-on date earlier than the latest change is refused.
11. **Import.**
    - Header errors and row errors are reported.
    - Import is all or nothing.
    - A repeat file is refused.
    - Limits are re-checked on confirm.
    - Nothing is written to disk: each test app gets its own `ASPNETCORE_TEMP` folder, and that folder, `data` and `documents` have the same files before and after.
12. **No upload accepted.**
    - A multipart POST to every other compliance page gets 415.
    - Other content types get 415.
    - An over-long body gets 413.
    - Over-long text fields are refused, and so is a word longer than 200 characters.
13. **Reminders.**
    - Each window fires once per officer and gives one aggregated notification.
    - An overdue point is reminded again after `OverdueRepeatDays`.
    - Inactive officers are skipped.
    - The digest is sent only when needed.
    - The scheduler marker stops a second run on the same day.
14. **Bulk transfer.** Covers the edge cases in §6 (target already a supporting officer, self-transfer refused).
15. **My Desk.** Counts, token and cache invalidation are correct for officers and CAs.
16. **Backups.** A restored backup contains the compliance tables.

**Playwright (`tests/e2e/compliance.e2e.js`).** The test runs one end-to-end path:
1. The CA registers an event and imports 3 points from CSV.
2. The officer sees the point on My Desk and submits an update.
3. The CA returns it; the officer resubmits; the CA accepts with edited wording.
4. The officer requests a revision and the CA approves it.
5. The CA previews a Board ATR, downloads the DOCX and PDF, marks it final, and downloads it from the snapshot.
6. The dashboard tiles are checked.
7. A staff user gets 404 on the point URL.
8. At phone width there is no horizontal scroll.

**Performance (`NABCONS_BENCH=1`).**
- **Seed data:**
  - 6,000 points;
  - 40,000 updates;
  - 2,000 snapshots × 40 items;
  - 500 users.
- **Budgets (p95):**
  - dashboard < 300 ms;
  - points list (filtered, page 1) < 300 ms;
  - text search < 400 ms;
  - ATR for 3 meetings × 40 points < 5 s, in both DOCX and PDF.
- **Recording:** results go in `docs/TEST-SCRIPT.md`.

**Upgrade.**
1. Take a copy of a 1.5.0 database (built from `main` in a git worktree).
2. Run `--migrate` twice.
3. Check that the existing tables' row counts are unchanged and that `--verify-audit` passes.
4. Check that the 1.5.0 program still starts on the upgraded database.

## 14. Acceptance criteria (R1, 1.6.0)
1. Migration 7 is additive, applied once and safe to roll back. Existing row counts are unchanged and the audit log verifies.
2. Compliance access is granted only by the Cell (the first CA with the server command). Grant changes are notified and audited.
3. The Cell can register events and add points one at a time or by CSV import: preview, then confirm; all or nothing; no repeat imports; nothing written to disk.
4. Officers see only their own points. They can submit and withdraw updates, and request and withdraw revisions.
5. The Cell can review and decide, close (authority required), drop, merge, reopen, reassign and bulk-transfer, but never on its own points.
6. Officer text, history, revisions, snapshots and grant history cannot be changed, and points and events cannot be deleted (triggers). The original due date never changes.
7. The dashboard (tiles, matrix, ageing, by vertical, by officer, trend) and the Points list and export are correct and respect visibility.
8. All three templates are produced in DOCX, PDF and XLSX: A4 landscape, organisation header, "Status as on", page x of y.
9. The ATR flow is preview, then mark final with the content hash. The snapshot cannot be changed, its hash is anchored in the audit chain, regeneration is identical, and the next ATR fills "previous" from the right snapshot. Voiding is possible.
10. Reminders arrive once a day whenever the PC is on, one per officer, plus the Cell digest. Badges update only through the existing poll.
11. Restricted and confidential events never leak through pages, counts, exports, notifications, My Desk, the audit log or snapshots.
12. No upload is possible except the CSV import. Text and body sizes are bounded. No generated file is stored.
13. The performance budgets are met at design volumes and recorded.
14. All existing and new tests pass. The documents are updated. The package is built with its checksum.

## 15. Risks and open items (R1)
| # | Item | Plan |
|---|---|---|
| R-1 | Decisions D1–D14 were taken without the interview. | The owner confirms them before merge. Most are settings or small changes. |
| R-2 | Anyone who can copy the database file or a backup ZIP on the server can read Board matters. | Residual risk, recorded in `IT-SECURITY-NOTE`. Restrict folder access (NTFS) to the service account and the custodian. Encryption at rest (BitLocker) is recommended. |
| R-3 | House formatting of the ATR (logo, fonts). | The DOCX can be edited. A `.dotx` can be supported later. |
| R-4 | The Cell may have only one CA. | The dashboard warns. That CA's own points wait for a second CA. |
| R-6 | The intranet Administrator can give anyone, including themselves, the **Auditor** role, which reads non-restricted compliance. | Residual risk: the CAs are notified whenever someone gains the Auditor role (§12 #13); recorded in IT-SECURITY-NOTE. |
| R-7 | ProposedClosure points print as Completed / Complied before the Cell closes them (D3 practice: the forum is told the action is complete and takes it on record). | Owner to confirm; otherwise print "Proposed for closure". |
| R-5 | Write contention at month-end ATR time. | Writes are single short transactions. ATR generation is read-only and limited to 2 at a time. |

## 16. R2 (1.7.0) — outline only; must not be built from this document
R2 needs its own interview and a revised version of this document, written in a fresh Elephant session. The outline and the points already raised are kept here.

**Outline.** Migration 8 adds:
- `InwardSerials`, `InwardItems`, `InwardMovements` (append-only), `InwardCopies`, `InwardNotes` (append-only);
- `InvoiceDetails` (1:1 with an item);
- `InvoicePayments` (append-only);
- `ProjectCodes`.

The same report writers and `UploadGuard` are reused, with exactly two CSV imports allowed: project codes and ERP payments.

**Questions for the R2 interview:**
1. Should registry and numbering be central or per office? Office codes are needed: `Location` has no code today.
2. Reply-due days, and whether working or calendar days are counted (there is no holiday calendar).
3. Who certifies invoices, and what are the standard payment terms?
4. Sample ERP payment export and project-code export.
5. Should self-inward be open to all staff from day one?

**Points already raised by the Goldfish review:**
- **Money and payments.**
  - Store money as **integer paise** (G2-11).
  - A payment reversal must reference the original payment, cannot exceed it, and moves the status back (G2-11).
  - Add deduction and TDS columns per payment and a hold-from-status column (G3-R5).
- **Duplicates.**
  - Duplicate key: GSTIN (or PAN or vendor id) + normalised invoice number + FY of the invoice date (G2-12).
  - ERP voucher numbers are unique, so a re-import is idempotent (G2-12).
- **MSME.** The 45-day clock runs from acceptance or deemed acceptance (MSMED Act s.15). Record the Udyam number and the enterprise class, and produce an FY-end s.43B(h) report (G2-29).
- **"Converted to e-Note".** The brief says documents are attached in the e-Note itself. Confirm that the COO accepts this, or block attachments on converted notes (G2-4). Also define the field mapping and the behaviour when e-Note is off (G3-R8).
- **Search.** Use FTS5 for inward search (G2-29).
- **Validation and prints.** GSTIN mod-36 check, and layouts for the acknowledgement slip and the daily register (G3-R9).
- **Still to write.** Roles, permissions matrix, screens, settings, tests and budgets (G3-R1, R3, R4).
