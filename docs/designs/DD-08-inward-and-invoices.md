# DD-08: Inward Register with Invoice Inward and tracking (release 1.7.0)

**Status:** v2.
- v1 was reviewed by fresh Goldfish reviewers (G1 comprehension, G2 critic, G3 readiness), and their findings are resolved here. The record of every finding is in `docs/designs/reviews/DD-08_consolidated_review_report.md`.
- The owner answered six questions (§0.1).
- This document is the single source of truth for 1.7.0 and describes what is built.

**Date:** 09.11.2026. **Baseline:** release 1.6.0, schema 7. **Supersedes:** DD-07 §16 (R2 outline).

Paths are relative to `src/NabconsIntranet/` unless they start with `docs/` or `tests/`. These 1.6.0 rules and patterns all apply (DD-07 §3, §7.1):
- **Rules:** register-only; offline; CSP; audit and migration rules.
- **Patterns:** grants held in the module, `UploadGuard`, report writers, scheduler, desk counts, `ComplianceText` limits.

## 0. Decisions

### 0.1 Owner's answers (09.11.2026)
| # | Question | Answer |
|---|---|---|
| D6 | Numbering | **One series per office:** `NCS/IN/<office code>/<FY>/<serial, 5 digits minimum>`. The serial restarts every April. Offices without a code share the series `GEN`. |
| D7 | Reply-due default | **Calendar days:** Normal 7, Urgent 3, Immediate 1. These are settings, and the date can be changed on each item. |
| D8 | Invoice certification and terms | **The officer the invoice is marked to certifies it.** The project-code owner is suggested as that officer. Default terms are **30 days** from the invoice date. |
| D10 | Self-inward | **Open to all staff from day one** (setting `SelfInwardEnabled`, default true). |
| D15 | MSME rule | **Simple rule:** an MSME invoice is due on the invoice date + the shorter of the terms and **45 days**. It is flagged when unpaid past invoice date + 45. The flag counts from receipt, not from certification. |
| D16 | Files on e-Notes created from inward items | **No files.** A note converted from an inward item can never carry attachments (a setting can allow them later). The note also takes the item's confidential flag. |

### 0.2 Elephant decisions (owner may change before merge)
- **E1 Office codes.** These are the codes of the **Offices / Locations** master list. They are edited on the Master lists page and are unique within the list. `GEN` is reserved. An office is always referred to by its **id**.
- **E2 Types and modes.** Both are fixed lists in code, without database CHECK constraints, so new values can be added without rewriting a table.
  - **Types:** Letter, E-mail, Invoice, Credit / debit note, Government order / notice, Legal notice, Other.
  - **Modes:** Post, Registered post, Speed Post, Courier, By hand, E-mail, Fax, Portal (GeM / e-procurement).
  - **Categories:** a master list, `InwardCategory`.
- **E3 Access grants.** Inward Operator (per office), Inward Viewer and Invoice Accounts are **grants held in the module**.
  - They are managed by the intranet **Administrator** on *Inward → Access*.
  - An Administrator cannot grant to themselves.
  - Every change is audited and notified to the Inward Viewers.
- **E4 Reminder letters.**
  - A reminder or follow-up letter is inwarded with its own number and closed at once as *Reminder of {original}*, held by the person who inwarded it.
  - The original's reminder count goes up, and the original's holder (or Accounts) is notified.
  - A **non-invoice** original is reopened (to its last holder) if it was closed. An **invoice** is never reopened this way.
  - Linking is allowed only to items the user may see (§5). For any other number the message is the same "not found". Links are followed to the root item.
  - A reminder takes the original's confidential flag.
- **E5 Converted to e-Note.**
  - Offered only while e-Note is enabled; the owner's live server has e-Note off, so the action is hidden there.
  - The holder chooses an e-Note category and accepts the usual declaration.
  - A **draft** e-Note is created with `SourceInwardId`. It takes the item's confidential flag. Its subject is "Inward {number}: {subject}"; for a confidential item, only "Inward {number}".
  - The inward item is then closed (`ConvertedToENote`, `ENoteId`). If the close fails, a retry reuses the existing draft found by `SourceInwardId`, so the step is idempotent.
  - `ENoteWorkflow.AttachAsync` refuses files on notes with `SourceInwardId` unless `Inward:ConvertedNotesAllowFiles` = true (default false; D16).
  - The e-Note page shows "Created from inward …" only to viewers who can open the item.
- **E6 Money.** Amounts are stored as **integer paise** (`Infrastructure/Gstin.cs`, class `Paise`).
- **E7 ERP payment CSV.** It uses our own template (§6.11) because no sample exists. `Amount` is the **net** amount paid. When a real ERP sample arrives, a column-mapping change can be made.
- **E8 Confidential items.**
  - **Metadata only.** In lists, exports, prints, aggregates and search results, a user without full access (§5) sees only the number, dates, type, mode, office, holder and status. Subject, summary, sender, sender's reference and vendor read "Confidential".
  - **Search.** The search index holds only the number and the invoice number of a confidential item.
  - **Copies.** Copying a confidential item to someone needs an extra confirmation and is audited.
- **E9 Accounts queue.** An invoice sent to Accounts has no personal holder. It sits in the Accounts queue (item status `WithAccounts`).
- **E10 Segregation.**
  - The person who inwarded an invoice cannot certify it.
  - A self-inwarded invoice goes to the registry as *Received*, or to another officer chosen on the form, never to the person inwarding it.
  - Every close-short is notified to the Inward Viewers and shown on the invoice dashboard.

## 1. The Problem
Letters and e-mails go unanswered because nobody records receiving them, gives them to someone, or follows them up. Invoices arrive, but nobody can say how many are unpaid, for how long, or whether MSME vendors are being paid on time.

The Inward Register gives every item:
- a number;
- a current holder;
- a reply-due date;
- an ageing clock;
- a closure record.

Invoices also get:
- a verification-to-payment lifecycle, including credit and debit notes;
- duplicate protection;
- MSME 45-day monitoring.

No scans or files are stored (COO decision). The register records where the paper or e-mail is kept.

## 2. Glossary
| Term | Meaning |
|---|---|
| **Inward item** | One registered letter, e-mail, invoice, credit/debit note or notice. |
| **Office** | The receiving office: a *Location* master item, which may have a code. |
| **Inward date** | The office date (`Fmt.Today`) on which the item is registered. It decides the FY of the number. |
| **Received at** | When the item actually arrived. It can be up to 90 days earlier than the inward date, but never in the future. |
| **Holder** | The user who currently has to act. |
| **Mark to** / **re-mark** | Give the item to a holder. |
| **Copy-to** | Users told for information. They can see the item and add notes. |
| **Participant** | Someone with **full** access to an item: the person who inwarded it, the current holder, past holders (unless they *returned it to the registry*), and copy-to users. Kept in `InwardParticipants`. |
| **Return to registry** | The holder says the item was marked to them by mistake. It goes back to *Unassigned*, and that holder stops being a participant. |
| **Closure type** | How the item ended: `Replied`, `ActionCompleted`, `Filed`, `Forwarded`, `ConvertedToENote`, `Duplicate`, `Reminder`, `Cancelled` (entered in error), `InvoiceSettled`, `InvoiceClosedShort`, `InvoiceReturned`, `InvoiceRejected`. |
| **Invoice status** | `Received`, `WithVerifier`, `Certified`, `SentToAccounts`, `PartPaid`, `Paid`, `OnHold`, `ReturnedToVendor`, `Rejected`, `ClosedShort`, `Cancelled`. |
| **Effective total** | Invoice total + Σ debit notes − Σ credit notes. |
| **Settled** | Σ (net + TDS + other deductions) over all payment rows; reversals count negative. |
| **Outstanding** | Effective total − settled. |
| **MSME due** | Invoice date + min(terms, 45) days (D15). |
| **Paise** | 1/100 rupee. All amounts are stored in paise. |

## 3. Technical plan

### 3.1 Components
```
Pages/Inward/*  ──►  InwardAccess      grants (multi-valued: roles × offices, cache ≤30 s), visibility SQL, full-access SQL, checks
                     InwardRepository  queries: registers, dashboards, invoice reports, mine, desk rows
                     InwardWorkflow    item transitions (§3.2), numbering, participants, FTS upkeep, reminder link, e-Note conversion,
                                       edit / cancel, transfer, grants, daily reminders
                     InvoiceWorkflow   invoice transitions (§3.3), duplicate key and warnings, payments, adjustments, reversals
                     InwardImport      project codes CSV, ERP payments CSV (in memory)
                     InwardReports     acknowledgement slip, register PDF/XLSX, invoice list, MSME / 43B(h) exports
Infrastructure: Gstin + Paise (done) · Reports writers with Portrait (done) · UploadGuard · InwardScheduler · Desk · Notifications
```

### 3.2 Item transitions (non-invoice items)
| From | Action | Who | Required | Effect |
|---|---|---|---|---|
| — | Inward | Operator of the office | §6.3 fields | **Unassigned**, or **WithHolder** if marked |
| — | Self-inward | Any user (setting) | §6.3 fields; office = the user's own office (§6.3) | **WithHolder**; held by the user unless another holder is chosen |
| Unassigned | Mark to | Operator of the office, or Inward Viewer | active holder | **WithHolder** |
| WithHolder | Accept | Holder | — | `AcceptedAt` set |
| WithHolder | Re-mark | Holder, operator of the office, or Inward Viewer | active holder; remarks optional | New holder; `AcceptedAt` cleared |
| WithHolder | Return to registry | Holder | remarks | **Unassigned**; the holder stops being a participant |
| open | Edit details | Operator of the office, the person who inwarded it, or the holder | changed fields | Fields updated. A movement records **field names only**. The search index is re-indexed. |
| open | Copy to | Holder or operator of the office | users (confirmation if confidential) | Copies added; the copied users become participants |
| any | Add note | Participant or operator of the office | text ≤ 2,000 | Note appended |
| WithHolder | Close | Holder | closure type, remarks, plus that type's fields (§6.5) | **Closed** |
| open | Cancel (entered in error) | Operator of the office, or the person who inwarded it | reason | **Closed** (`Cancelled`) |
| Closed (not Cancelled or Reminder) | Reopen | Last holder, operator of the office, or Inward Viewer | reason | **WithHolder** (the last holder; **Unassigned** if they are inactive). The reply-due date is kept. |

Every action:
- runs in one `BEGIN IMMEDIATE` transaction;
- checks and bumps `RowVersion`;
- writes one movement;
- writes one audit entry (`Inward.*`, number and type only, never free text).

### 3.3 Invoice items: one combined transition table
Item status follows the invoice status:

| Invoice status | Item status |
|---|---|
| Received | Unassigned |
| WithVerifier, Certified | WithHolder (the verifier) |
| SentToAccounts, PartPaid | WithAccounts |
| Paid, ClosedShort, ReturnedToVendor, Rejected, Cancelled | Closed |
| OnHold | keeps the item status of `HoldFromStatus` |

For invoices, `ActionRequired` is *Action* and there is no reply-due date. Invoices have their own alerts (§8).

| From | Action | Who | Required | To |
|---|---|---|---|---|
| — | Inward as Invoice | Operator, or self-inward (holder never the person inwarding it) | invoice fields; duplicate check (§3.5) | **Received**, or **WithVerifier** if marked |
| Received | Mark to verifier | Operator or Inward Viewer | holder ≠ the person who inwarded it | **WithVerifier** |
| WithVerifier, Certified | Re-mark | Holder, operator, or Inward Viewer | holder ≠ the person who inwarded it | Same status, new holder (certification cleared if Certified) |
| WithVerifier | Accept | Holder | — | `AcceptedAt` |
| WithVerifier, Certified | Return to registry | Holder | remarks | **Received** (certification cleared) |
| WithVerifier | Certify | Holder (≠ the person who inwarded it) | certification date (between invoice date and today) | **Certified** |
| Certified | Send to Accounts | Holder | — | **SentToAccounts** |
| SentToAccounts (no payments) | Send back to verifier | Invoice Accounts | remarks | **WithVerifier** (to the certifying officer, or Received if they are inactive) |
| SentToAccounts, PartPaid | Record payment | Invoice Accounts | §6.10; first payment of an overridden duplicate needs *"checked: not a duplicate payment"* | **PartPaid**, or **Paid** when outstanding = 0 |
| SentToAccounts, PartPaid | Credit / debit note | Invoice Accounts | document number, date, amount (a credit note ≤ outstanding) | Status recomputed |
| SentToAccounts, PartPaid | Close short | Invoice Accounts | reason | **ClosedShort**; Inward Viewers notified |
| any status with payments (including OnHold, Paid, ClosedShort) | Reverse a payment | Invoice Accounts | payment, reason | Status recomputed. ClosedShort is undone; OnHold keeps OnHold with `HoldFromStatus` recomputed. Item reopened as **WithAccounts** if it was closed. |
| Received … PartPaid (not OnHold) | Put on hold | Holder; Accounts when WithAccounts; operator when Received | reason | **OnHold**. Nothing but release, notes and reversals is allowed while on hold. |
| OnHold | Release | Same people | — | `HoldFromStatus` |
| Received, WithVerifier, Certified, SentToAccounts (no payments) | Return to vendor / Reject | Holder; Accounts when WithAccounts; operator when Received | reason | **ReturnedToVendor** / **Rejected** |
| any open, no payments | Cancel (entered in error) | Operator or the person who inwarded it | reason | **Cancelled** |
| open | Edit details | Operator, the person who inwarded it, or the holder (before Certified); Invoice Accounts (amounts, until the first payment) | fields | Recompute `DupKey`, `InvoiceFy`, `PaymentDueOn`; duplicate check again; re-index |

The generic Close action is not offered for invoices. A returned or rejected invoice that comes back is a new item, and its duplicate check needs an override reason.

### 3.4 Invoice calculations
- **Amounts.**
  - Total is mandatory and > 0.
  - If taxable or GST is entered: taxable + GST + other charges (TCS, cess, round-off) = total, within ₹ 1.
- **Payment rows.**
  - Ordinary rows: net ≥ 0, TDS ≥ 0, deductions ≥ 0, and net + TDS + deductions > 0. Paid-on date between the invoice date and today.
  - Reversal rows: every amount ≤ 0, exactly equal to minus the original row. The paid-on date is the date of the reversal.
- **Status after any payment or adjustment.** Computed inside the write transaction from `SUM()`:
  - outstanding = 0 → **Paid**;
  - outstanding > 0 with something settled → **PartPaid**;
  - nothing settled → **SentToAccounts**;
  - outstanding < 0 is **refused**, so an invoice can never be overpaid.
- **Due dates (D15).**
  - Standard: `PaymentDueOn` = invoice date + terms.
  - MSME: invoice date + min(terms, `MsmeMaxDays` = 45).
- **MSME flags.** For an MSME invoice that is not Paid, ClosedShort, Returned, Rejected or Cancelled:
  - **past 45 days** when invoice date + 45 < today;
  - **due soon** when the MSME due date − today ≤ `MsmeDueSoonDays` (5).

  The flags apply from receipt, whatever the status.
- **43B(h) / MSME-1 report, as on a chosen date** (default 31 March of a chosen FY). It lists Micro and Small invoices (`IsMsme`, class Micro or Small, not trader) with invoice date ≤ the as-on date where:
  - the invoice is outstanding on the as-on date (counting payments and reversals dated on or before it), **or**
  - it was paid after its MSME due date.

  Invoices marked MSME without a class are listed separately as "class not recorded". The report also offers an "entered on or before" date, so it can be reproduced later.

### 3.5 Duplicate invoices
- **Key.** `DupKey` = (GSTIN in upper case, or `N:` + normalised vendor name) + `|` + normalised invoice number + `|` + invoice FY.
  - Vendor name normalisation: lower case, letters and digits only; the suffixes pvt / private / ltd / limited / llp / co are removed.
  - Invoice number normalisation: upper case, letters and digits only.
- **Blocking.** A partial unique index on `DupKey`, excluding overridden and cancelled rows, blocks a duplicate. The existing inward number is shown, and an override needs *"This is not a duplicate"* plus a reason.
  - The reason is stored on the invoice. The audit log records only "override of #N".
  - An overridden invoice is flagged on the Accounts dashboard. Its first payment needs a *checked* confirmation.
- **Warnings only** (the user confirms and saves again):
  - the same GSTIN and invoice number in another FY;
  - the same GSTIN and total with an invoice date within 30 days;
  - for any item, the same sender reference and date.
- **MSME hint.** If an earlier invoice from the same GSTIN was MSME, its Udyam number and class are offered as a warning.

## 4. Data model: migration 8, "Inward register and invoices (v1.7.0)"
Additive only. Nothing existing changes, except one nullable column on `ENotes` (default NULL), which older programs ignore. Enum-like columns are validated in code (E2); CHECKs cover lengths, amounts and signs.

```sql
ALTER TABLE ENotes ADD COLUMN SourceInwardId INTEGER DEFAULT NULL;

CREATE TABLE InwardSerials (OfficeCode TEXT NOT NULL, FinancialYear TEXT NOT NULL, LastSerial INTEGER NOT NULL, PRIMARY KEY (OfficeCode, FinancialYear));
CREATE TABLE InwardState (Key TEXT PRIMARY KEY, Value TEXT);

CREATE TABLE InwardMembers (
  Id INTEGER PRIMARY KEY, UserId INTEGER NOT NULL REFERENCES Users(Id), Role TEXT NOT NULL,   -- InwardOperator | InwardViewer | InvoiceAccounts
  OfficeId INTEGER NOT NULL DEFAULT 0,                                                          -- operator: Location id; others: 0
  GrantedByName TEXT NOT NULL, GrantedAt TEXT NOT NULL,
  CHECK ((Role = 'InwardOperator') = (OfficeId > 0)), UNIQUE (UserId, Role, OfficeId));
CREATE TABLE InwardMemberHistory (Id INTEGER PRIMARY KEY, UserId INTEGER NOT NULL, Action TEXT NOT NULL, Role TEXT, OfficeId INTEGER, ActorName TEXT NOT NULL, At TEXT NOT NULL);

CREATE TABLE InwardItems (
  Id INTEGER PRIMARY KEY, Number TEXT NOT NULL UNIQUE,
  OfficeId INTEGER NOT NULL, OfficeName TEXT NOT NULL, OfficeCode TEXT NOT NULL, FinancialYear TEXT NOT NULL, Serial INTEGER NOT NULL,
  ItemType TEXT NOT NULL, Mode TEXT NOT NULL, ReceivedAt TEXT NOT NULL,
  SenderName TEXT NOT NULL CHECK (length(SenderName) <= 150), SenderOrg TEXT CHECK (length(SenderOrg) <= 200),
  SenderRef TEXT CHECK (length(SenderRef) <= 100), SenderRefDate TEXT,
  Subject TEXT NOT NULL CHECK (length(Subject) <= 300), Summary TEXT CHECK (length(Summary) <= 2000),
  Category TEXT CHECK (length(Category) <= 150), Priority TEXT NOT NULL DEFAULT 'Normal', IsConfidential INTEGER NOT NULL DEFAULT 0,
  ActionRequired TEXT NOT NULL, ReplyDueOn TEXT, KeptAt TEXT NOT NULL CHECK (length(KeptAt) <= 300),
  Pages INTEGER CHECK (Pages IS NULL OR Pages BETWEEN 0 AND 10000), Irn TEXT CHECK (length(Irn) <= 64),
  Status TEXT NOT NULL,                                  -- Unassigned | WithHolder | WithAccounts | Closed
  HolderUserId INTEGER, HolderSince TEXT, AcceptedAt TEXT, LastHolderUserId INTEGER,
  InwardedByUserId INTEGER NOT NULL, InwardedByName TEXT NOT NULL, InwardedAt TEXT NOT NULL, SelfInward INTEGER NOT NULL DEFAULT 0,
  ClosureType TEXT, ClosureRemarks TEXT CHECK (length(ClosureRemarks) <= 1000), ReplyDate TEXT, OutwardRef TEXT CHECK (length(OutwardRef) <= 100),
  FiledFileNo TEXT CHECK (length(FiledFileNo) <= 100), ForwardedTo TEXT CHECK (length(ForwardedTo) <= 200),
  DuplicateOfId INTEGER, ENoteId INTEGER, ClosedAt TEXT, ClosedByName TEXT, LinkedToId INTEGER, ReminderCount INTEGER NOT NULL DEFAULT 0,
  LastDueReminderOn TEXT, LastOverdueReminderOn TEXT, RowVersion INTEGER NOT NULL DEFAULT 1, UpdatedAt TEXT NOT NULL);
-- indexes: (OfficeId, ReceivedAt DESC); (HolderUserId, Status); (Status, ReplyDueOn); (Status, OfficeId, InwardedAt); (InwardedAt);
--          (InwardedByUserId, Id); (ClosedAt) WHERE ClosedAt IS NOT NULL; (LinkedToId) WHERE LinkedToId IS NOT NULL; (ItemType, Status)
-- triggers: Number, OfficeId, OfficeCode, FinancialYear, Serial, InwardedAt, InwardedByUserId never change (WHEN new.X IS NOT old.X); no delete

CREATE TABLE InwardParticipants (ItemId INTEGER NOT NULL, UserId INTEGER NOT NULL, PRIMARY KEY (ItemId, UserId)) WITHOUT ROWID;   -- current full access
CREATE INDEX IX_InwardParticipants_User ON InwardParticipants(UserId, ItemId);
CREATE TABLE InwardMovements (Id INTEGER PRIMARY KEY, ItemId INTEGER NOT NULL, At TEXT NOT NULL, ActorUserId INTEGER, ActorName TEXT NOT NULL,
  Action TEXT NOT NULL, ToUserId INTEGER, ToName TEXT, Remarks TEXT CHECK (length(Remarks) <= 1000));
CREATE TABLE InwardCopies (ItemId INTEGER NOT NULL, UserId INTEGER NOT NULL, AddedAt TEXT NOT NULL, AddedByName TEXT NOT NULL, PRIMARY KEY (ItemId, UserId));
CREATE TABLE InwardNotes (Id INTEGER PRIMARY KEY, ItemId INTEGER NOT NULL, At TEXT NOT NULL, UserId INTEGER NOT NULL, Name TEXT NOT NULL, Text TEXT NOT NULL CHECK (length(Text) <= 2000));
-- indexes on ItemId; append-only triggers on Movements, Copies, Notes and MemberHistory

CREATE TABLE ProjectCodes (Code TEXT PRIMARY KEY COLLATE NOCASE, Name TEXT NOT NULL CHECK (length(Name) <= 200), OwnerUserId INTEGER, IsActive INTEGER NOT NULL DEFAULT 1, UpdatedAt TEXT NOT NULL, UpdatedBy TEXT NOT NULL);

CREATE TABLE InvoiceDetails (
  ItemId INTEGER PRIMARY KEY REFERENCES InwardItems(Id),
  VendorName TEXT NOT NULL CHECK (length(VendorName) <= 200), VendorGstin TEXT, IsMsme INTEGER NOT NULL DEFAULT 0, IsTrader INTEGER NOT NULL DEFAULT 0,
  UdyamNo TEXT CHECK (length(UdyamNo) <= 30), MsmeClass TEXT,
  InvoiceNo TEXT NOT NULL CHECK (length(InvoiceNo) <= 50), InvoiceDate TEXT NOT NULL, InvoiceFy TEXT NOT NULL,
  ProjectCode TEXT, PoRef TEXT CHECK (length(PoRef) <= 100),
  TaxablePaise INTEGER CHECK (TaxablePaise IS NULL OR TaxablePaise >= 0), GstPaise INTEGER CHECK (GstPaise IS NULL OR GstPaise >= 0),
  OtherPaise INTEGER CHECK (OtherPaise IS NULL OR OtherPaise >= 0), TotalPaise INTEGER NOT NULL CHECK (TotalPaise > 0),
  AdjustPaise INTEGER NOT NULL DEFAULT 0,                -- Σ debit − Σ credit notes
  SettledPaise INTEGER NOT NULL DEFAULT 0, TermsDays INTEGER NOT NULL DEFAULT 30 CHECK (TermsDays BETWEEN 0 AND 365), PaymentDueOn TEXT NOT NULL,
  InvoiceStatus TEXT NOT NULL, HoldFromStatus TEXT, StatusReason TEXT CHECK (length(StatusReason) <= 1000),
  CertifiedByUserId INTEGER, CertifiedByName TEXT, CertifiedOn TEXT, SentToAccountsAt TEXT, SettledAt TEXT,
  DupKey TEXT NOT NULL, OverrideReason TEXT CHECK (length(OverrideReason) <= 500), DuplicateCheckedBy TEXT);
CREATE UNIQUE INDEX UX_Invoice_Dup ON InvoiceDetails(DupKey) WHERE OverrideReason IS NULL AND InvoiceStatus <> 'Cancelled';
-- indexes: (InvoiceStatus, InvoiceDate); (ProjectCode, InvoiceStatus); (VendorGstin, InvoiceDate); (IsMsme, InvoiceStatus, InvoiceDate); (DupKey)

CREATE TABLE InvoicePayments (
  Id INTEGER PRIMARY KEY, ItemId INTEGER NOT NULL, PaidOn TEXT NOT NULL,
  NetPaise INTEGER NOT NULL, TdsPaise INTEGER NOT NULL DEFAULT 0, OtherDeductionsPaise INTEGER NOT NULL DEFAULT 0,
  VoucherRef TEXT NOT NULL CHECK (length(VoucherRef) <= 60), ReversesId INTEGER UNIQUE, Remarks TEXT CHECK (length(Remarks) <= 500),
  Source TEXT NOT NULL, EnteredByUserId INTEGER NOT NULL, EnteredByName TEXT NOT NULL, EnteredAt TEXT NOT NULL,
  UNIQUE (ItemId, VoucherRef),                           -- one UTR may pay several invoices
  CHECK ((Source = 'Reversal') = (ReversesId IS NOT NULL)),
  CHECK ((ReversesId IS NULL AND NetPaise >= 0 AND TdsPaise >= 0 AND OtherDeductionsPaise >= 0 AND NetPaise + TdsPaise + OtherDeductionsPaise > 0)
      OR (ReversesId IS NOT NULL AND NetPaise <= 0 AND TdsPaise <= 0 AND OtherDeductionsPaise <= 0)));
CREATE TABLE InvoiceAdjustments (Id INTEGER PRIMARY KEY, ItemId INTEGER NOT NULL, Kind TEXT NOT NULL,     -- CreditNote | DebitNote
  DocNo TEXT NOT NULL CHECK (length(DocNo) <= 50), DocDate TEXT NOT NULL, AmountPaise INTEGER NOT NULL CHECK (AmountPaise > 0),
  Remarks TEXT CHECK (length(Remarks) <= 500), EnteredByUserId INTEGER NOT NULL, EnteredByName TEXT NOT NULL, EnteredAt TEXT NOT NULL);
-- indexes on ItemId; append-only triggers on Payments and Adjustments; reversal voucher = 'REV-' || original Id

CREATE TABLE InwardImports (Id INTEGER PRIMARY KEY, Kind TEXT NOT NULL, Sha256 TEXT NOT NULL, Rows INTEGER NOT NULL, ByName TEXT NOT NULL, At TEXT NOT NULL);
-- append-only; project-code imports refuse a repeated SHA; payment imports record it (idempotency comes from the (item, voucher) key)

CREATE VIRTUAL TABLE InwardSearch USING fts5(Number, Sender, SenderRef, Subject, Summary, Vendor, InvoiceNo, content='', contentless_delete=1, tokenize='unicode61');
-- the same contentless pattern as ENoteSearch (migration 5); a confidential item indexes Number and InvoiceNo only

-- master list InwardCategory, inserted with WHERE NOT EXISTS (the only data insert): General correspondence; Client / project;
-- Government / regulator; Legal; Finance & accounts; Human resources; Tenders & procurement; Other
```

## 5. Permissions
| Code | Who |
|---|---|
| **OP** | Inward Operator of the item's office |
| **IV** | Inward Viewer |
| **IA** | Invoice Accounts |
| **AU** | the Auditor role |
| **AD** | the Administrator role |
| **P** | participant (§2) |
| **X** | anyone else |

- **Full access** = P, IV, or IA for invoice items.
- **Visible** = full access, OP, or AU. Everyone else gets 404.

| Action | OP | IV | IA | AU | AD | P | X |
|---|---|---|---|---|---|---|---|
| Inward menu tab | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ if self-inward is on |
| Inward an item | own offices | — | — | — | — | — | self-inward (own office) |
| Register, dashboard, exports, register PDF | own offices | all | invoice items | all | — | — | — |
| Open an item | metadata if confidential | full | invoices: full | metadata if confidential | — | full | 404 |
| Mark, re-mark, reopen | ✔ (own office) | ✔ | — | — | — | holder: re-mark; last holder: reopen | — |
| Edit / cancel | ✔ | — | amounts (invoices) | — | — | inwarder (edit, cancel); holder (edit) | — |
| Invoice: certify, send, hold, return or reject | Received only (hold, return, reject) | — | when WithAccounts | — | — | holder | — |
| Payments, notes, close short, reversals, ERP import, project codes | — | — | ✔ | — | — | — | — |
| Invoice reports, MSME / 43B(h) | — | ✔ | ✔ | ✔ | — | — | — |
| Acknowledgement slip | ✔ | ✔ | — | — | — | inwarder | — |
| Bulk transfer of a user's open items | — | ✔ | — | — | ✔ (metadata only) | — | — |
| Access grants | — | — | — | — | ✔ (not to self) | — | — |

Rules:
- **One visibility rule.** Every list, count, search, export, print, aggregate and desk count applies the visibility predicate and the E8 masking (`InwardAccess.VisibleSql` / `FullSql`).
- **Query shape.** The SQL is written so SQLite can use the indexes (office branch, participant branch), and it is benchmarked.
- **No free text outside the module.** Notifications and audit details carry the inward number and type only.
- **No site search.** Inward is not added to site search.
- **No bank details** are stored.
- **No PAN column** is kept. The GSTIN is shown in full only to full-access users.

## 6. Screens (under `/Inward`; dates dd.MM.yyyy; amounts ₹ with Indian grouping)
| # | Route | Who | Content |
|---|---|---|---|
| 6.1 | `/Inward` | OP, IV, IA, AU | Dashboard (below). Others go to `/Inward/Mine`. |
| 6.2 | `/Inward/Register` | OP, IV, IA (invoices), AU | Searchable, filterable register list (below). |
| 6.3 | `/Inward/New` | OP, any user (self) | The inward form (below). |
| 6.4 | `/Inward/Mine` | anyone | Tabs: with me (open); copied to me; inwarded by me; closed in the last 90 days. |
| 6.5 | `/Inward/Item/{id}` | per §5 | The item page (below). |
| 6.6 | `/Inward/Ack/{id}` | per §5 | Acknowledgement slip: A4 portrait PDF, short content. |
| 6.7 | `/Inward/Invoices` | IV, IA, AU | Invoice dashboard, list and reports (below). |
| 6.8 | `/Inward/ProjectCodes` | IA | Project codes: list and CSV import (below). |
| 6.9 | `/Inward/Access` | AD | Grants (OP per office, IV, IA) with history. Warnings: offices with no active operator; no active Invoice Accounts user. |
| 6.10 | (on the item page) | IA | Payments, adjustments and reversals (below). |
| 6.11 | `/Inward/Payments` | IA | ERP payment import (below). |
| 6.12 | `/Inward/Transfer` | IV, AD | Move every open item held by user A to user B (active, ≠ A). Preview counts, then one transaction with one movement per item. |

**6.1 Dashboard**
- **Tiles:** open; overdue (reply due passed); due today or tomorrow; unassigned for more than 1 working day; held by inactive users; received today.
- **Breakdowns:**
  - pending by holder (top 20), by holder's vertical, by office;
  - ageing since received: 0–7, 8–15, 16–30, > 30 days;
  - received vs closed per month for 12 months (`<progress>` bars).
- **Office filter.** Operators see only their own offices.

**6.2 Register**
- **Filters:** office, received from / to, type, mode, status, holder, priority, overdue only.
- **Search:** text search over the search index (§4).
- **Paging and export:** 50 rows a page. Excel export up to 20,000 rows.
- **Register PDF:** at most 31 days or 5,000 rows.

**6.3 Inward form**
- **Office.** Chosen from the user's operator offices. For self-inward it is the user's own office, worked out in this order:
  1. an operator grant;
  2. a directory entry whose location matches an office name (case-insensitive);
  3. `DefaultOfficeName`.

  If none of these gives an office, the user picks any active office.
- **Fields:** type; mode; received at (default now; up to 90 days earlier); sender name and organisation; sender's reference and date; subject; summary; category; priority; confidential; action required; reply due (defaulted from priority); kept at (required); pages; IRN (optional).
- **Reminder link:** "Reminder / follow-up of inward no." (optional).
- **Routing:** mark to (optional; for self-inward the default is the user, but for an invoice never the person inwarding it); copy-to.
- **Invoice section** (Type = Invoice; shown or hidden by `site.js`; always checked on the server):
  - vendor name; GSTIN; MSME (yes/no) with Udyam number and class (Micro, Small, Medium); trader;
  - invoice number and date; project code (active codes; the owner is suggested as verifier); PO reference;
  - taxable, GST, other charges, total; terms (days).
- **Warnings** (§3.5): shown with a *Confirm and save* button. Blocks show the existing number, and override needs a reason.
- **After saving:** the item page opens, with *Acknowledgement slip*.

**6.5 Item page**
- **Content:**
  - details, masked per E8;
  - movement trail; notes; copies; linked reminders;
  - invoice panel: status; amounts (total, adjustments, effective, settled, outstanding); due date; MSME flag; payments; adjustments.
- **Actions:** buttons by role and state (§3.2, §3.3). Every form posts `RowVersion`.
- **Closure fields:**
  - Replied: reply date and outward reference;
  - Filed: file number;
  - Forwarded: to whom;
  - Duplicate: inward number (a visible item only);
  - Converted to e-Note: category and declaration.

  Remarks are always required.

**6.7 Invoices**
- **Tiles:** unpaid count and ₹ outstanding; ageing by invoice date (0–30, 31–45, 46–60, 61–90, > 90); MSME past 45 days; MSME due within 5 days; on hold; duplicate overrides awaiting the *checked* step; closed short this month.
- **Breakdowns:**
  - by project code, by vendor (top 20, masked per E8), by holder or Accounts;
  - received vs paid per month.
- **Exports:** the list, filterable, to Excel; the MSME / 43B(h) report (§3.4) to Excel.

**6.8 Project codes**
- **Import:** a CSV file with columns `Code, Name, OwnerUserName, Active`.
  - Rows are upserted by code; codes are compared without regard to case. `OwnerUserName` is the sign-in name, and the user must be active.
  - *"Deactivate codes not in the file"* is optional.
  - Limits: 5,000 rows and 1,900 KB. The file is read in memory and imported all or nothing; the same file is refused a second time.

**6.10 Payments (on the item page)**
- **Payment:** paid on; net; TDS; other deductions; voucher or UTR reference; remarks.
- **Credit / debit note:** document number, date, amount, remarks.
- **Reverse:** per payment row, with a reason.
- **Close short:** with a reason.

**6.11 ERP payment import**
- **Template columns:** `InwardNo, VendorGstin, VendorName, InvoiceNo, InvoiceDate, PaidOn, Amount (net), TDS, OtherDeductions, VoucherRef, Remarks`.
  - Dates may be dd.MM.yyyy, dd/MM/yyyy or yyyy-MM-dd.
  - Amounts may use Indian grouping.
- **Matching:**
  1. by `InwardNo`;
  2. otherwise by the duplicate key.

  Ambiguous matches (overridden duplicates) are treated as unmatched.
- **Preview categories:**
  - matched new;
  - already recorded (same item and voucher);
  - unmatched;
  - not payable (status not SentToAccounts or PartPaid, or on hold);
  - would overpay;
  - invalid.
- **Confirm:** imports the matched new rows in one transaction.

## 7. Outputs (as `ReportDocument`; streamed, never stored; two renders at a time; 50 MB cap)
**7.1 Acknowledgement slip (PDF, portrait).**
- **Header:** organisation name; "Acknowledgement of receipt".
- **Table** (two columns):
  - inward number; received date and time; office;
  - from (sender, organisation); sender's reference and date;
  - subject (masked to "Confidential correspondence" if confidential);
  - mode; pages; received by.
- **Footer:** "System-generated acknowledgement — no signature required · {number}".

**7.2 Inward register (PDF landscape and XLSX).**
- **Title:** office and date range.
- **Columns:** Inward No · Received · Type / Mode · From · Sender ref & date · Subject · Marked to · Status / reply due.
- **Masking:** E8.

**7.3 Invoice list (XLSX).**
- **Columns:** Inward No, Vendor, GSTIN, MSME, Class, Invoice no., Invoice date, Project, PO, Total, Adjustments, Settled, Outstanding, Status, Holder, Certified on, Payment due, Days since invoice, MSME past 45 days.

**7.4 MSME / 43B(h) report (XLSX).**
- **Columns:** Vendor, GSTIN, Udyam, Class, Invoice no., Invoice date, Due date (MSME), Total (effective), Paid by the as-on date, Outstanding on the as-on date, Paid late on.
- **Sections:** Micro/Small; class not recorded.

## 8. Notifications, reminders, My Desk
**Notifications** have the module label "Inward". Their text is number and type only.

| Event | Recipients |
|---|---|
| Marked or re-marked | the new holder |
| Copy-to | the copied users |
| Returned to registry | operators of the office |
| Closed, cancelled or reopened | the person who inwarded it, and the copies |
| Reminder received | the holder of the original, or Accounts |
| Sent to Accounts | Invoice Accounts users |
| Sent back to verifier, returned or rejected by Accounts | the certifying officer |
| Hold, release | the holder, or Accounts |
| Paid, closed short | the certifying officer; for close short, also the Inward Viewers |
| Grant changed | the user concerned and the Inward Viewers |

**Daily job (`InwardScheduler`).** It runs every day (pattern as `ComplianceScheduler`; marker in `InwardState`).
- **Holders:** one summary each, covering:
  - overdue (repeating every `OverdueRepeatDays`);
  - due today or tomorrow;
  - not accepted for 2 days;
  - invoices with them for more than 7 days.
- **Operators:** "n unassigned for more than 1 working day". Working days are Monday to Friday; this is the only place they are used.
- **Accounts:** "n MSME invoices due within 5 days, n past 45 days, n duplicate overrides to check".

Markers are set in batches of 200, and the summaries are written in one final transaction.

**My Desk.** New optional members on `DeskCounts`:

| Member | Counts |
|---|---|
| `InwardMine` | open items held by the user that are overdue, due within 1 day or not accepted, plus invoices held by the user |
| `InwardUnassigned` | operators: Unassigned items in their offices |
| `InvoiceQueue` | Accounts: items WithAccounts |
| `InwardAssigned` | the user is a participant in any item |

- **Poll and badges:** the counts feed `Total`, the poll value `inward`, the Inward tab badge and the badge list in `site.js`.
- **Cache keys:** `RoleKey` adds the user's sorted inward grants; the queue key is `t:inward`.
- **Sections:** "Inward — with me", "Registry — unassigned", "Accounts — invoices to pay".

## 9. Settings (section `Inward`; all optional)
| Key | Default | Effect |
|---|---|---|
| `Enabled` | `true` | Shows or hides the whole module. |
| `NumberPrefix` | `NCS/IN` | Start of every inward number. |
| `DefaultOfficeName` | `Mumbai (Head Office)` | Office used when the user has none (§6.3). |
| `SelfInwardEnabled` | `true` | Lets any user inward items they received (D10). |
| `ReplyDueDaysNormal` / `Urgent` / `Immediate` | `7` / `3` / `1` | Default reply-due period (D7). |
| `MaxBackdateDays` | `90` | How far back "received at" may go. |
| `UnassignedAlertWorkingDays` | `1` | Operators' digest threshold. |
| `NotAcceptedAlertDays` | `2` | Reminder for items not yet accepted. |
| `OverdueRepeatDays` | `3` | How often overdue items are reminded again. |
| `InvoiceTermsDays` | `30` | Default payment terms. |
| `MsmeMaxDays` | `45` | MSME limit (D15). |
| `MsmeDueSoonDays` | `5` | "MSME due soon" window. |
| `InvoiceHolderAlertDays` | `7` | Reminder to verifiers holding an invoice. |
| `ReminderHour` | `9` | Hour the daily job runs. |
| `ConvertedNotesAllowFiles` | `false` | Allows files on e-Notes created from inward items (D16). |
| `MaxImportRows` / `MaxImportKB` | `5000` / `1900` | CSV import limits. `MaxImportKB` is clamped to 1,900, the upload guard's limit. |

## 10. Storage and backup
- **Expected size after 5 years:** about 0.55 GB for inward, and about 0.8–0.9 GB for the whole database with compliance.

| Table | Rows | Approx. size |
|---|---|---|
| Inward items | 250,000 | 225 MB |
| Movements | 750,000 | 190 MB |
| Notes | 100,000 | 40 MB |
| Participants | 600,000 | 15 MB |
| Invoices | 25,000 | 13 MB |
| Payments and adjustments | 45,000 | 9 MB |
| Search index | — | 60 MB |

- **Backup:** one full backup takes about a minute.
- **No new folders**, so the existing backup and restore cover everything.

## 11. Alternatives considered and rejected
| Alternative | Why rejected |
|---|---|
| Storing scans or e-mail files | COO decision. |
| Separate invoice register | One diary, with invoice details stored 1:1. |
| Building on e-Note | e-Note is a decision trail; the conversion covers the cases that need one. |
| Money as REAL | Rounding errors. |
| Mailbox or ERP integration | No outbound connections. |
| Item types as a master list | The Invoice type drives logic. |
| Enum CHECK constraints | They could never be extended without a table rewrite (G2-7). |
| Holiday calendar | Maintenance burden. |
| LIKE search | Too slow at 250,000 items; FTS5 instead. |
| Statutory acceptance-date MSME model | Owner chose the simpler 45-days-from-invoice rule (D15). |
| s.16 interest calculation | Deferred: it needs the bank-rate history; the report gives the inputs. |

## 12. Detailed implementation (in order)
**Already done on the branch:** office codes on the Master lists page (GEN reserved); `Portrait` in the PDF and DOCX writers; `Infrastructure/Gstin.cs` (`Gstin`, `Paise`).

| # | File | Change |
|---|---|---|
| 1 | `Infrastructure/Schema.cs` | Migration 8 (§4) |
| 2 | `Infrastructure/Notifications.cs`, `Data/Models.cs` | The "Inward" label; `InwardQueue`; `MasterTypes.InwardCategory` |
| 3 | `Data/ENoteWorkflow.cs`, `Data/ENotes.cs`, `Pages/ENotes/View.cshtml(.cs)` | `Create(..., sourceInwardId)`; `ENote.SourceInwardId`; attachment refusal; "Created from inward" link |
| 4 | `Data/Inward.cs` | Codes, `InwardOptions`, records, `InwardAccess` |
| 5 | `Data/InwardRepository.cs` | Queries and desk SQL |
| 6 | `Data/InwardWorkflow.cs` | §3.2, numbering, participants, search index, reminder link, conversion, edit/cancel, transfer, grants, reminders |
| 7 | `Data/InvoiceWorkflow.cs` | §3.3–3.5 |
| 8 | `Data/InwardImport.cs` | Project codes and ERP payment imports |
| 9 | `Data/InwardReports.cs` | §7 |
| 10 | `Infrastructure/InwardScheduler.cs`; `Infrastructure/UploadGuard.cs` | Daily job; `/Inward` prefix, multipart only on `/Inward/ProjectCodes` and `/Inward/Payments` |
| 11 | `Program.cs` | Options, services, hosted job |
| 12 | `Data/Desk.cs`, `Pages/Desk/Index.cshtml`, `Pages/Shared/_Layout.cshtml`, `Infrastructure/Endpoints.cs`, `wwwroot/js/site.js` | Counts, sections, tab, badge, invoice-section toggle |
| 13 | `Pages/Inward/*` | The pages in §6 |
| 14 | `wwwroot/css/site.css` | Styles |
| 15 | `Infrastructure/Seeder.cs` | Demo data, on new empty databases only |
| 16 | Tests | `InwardTests.cs`, `InvoiceTests.cs` (including GSTIN and paise), `InwardBenchTests.cs`, `tests/e2e/inward.e2e.js`, `run-all.sh`, `ReportWriterTests.cs` (portrait) |
| 17 | Docs | Same list as DD-07 §12 #18, plus DD-07 §16 pointing here |
| 18 | `NabconsIntranet.csproj` | 1.7.0; rebuild the package; remove the 1.6.0 ZIP |

## 13. Test plan
**xUnit:**
1. **Migration:** migration 8 runs once; the triggers and CHECKs hold.
2. **Numbering:** separate series per office and per FY, including GEN.
3. **Item transitions:** every §3.2 transition, including edit, cancel and transfer; return to registry removes participation.
4. **Invoice transitions:** every §3.3 transition; the item status always matches the invoice status.
5. **Payments:**
   - paise arithmetic, with Paid, PartPaid, and overpayment refused;
   - credit and debit notes; reversal (once only, including after ClosedShort and while OnHold);
   - one voucher across two invoices.
6. **Duplicates:** blocked, override, the *checked* confirmation, and the warnings.
7. **GSTIN:** valid and invalid check digits.
8. **MSME:** due dates, both flags, and the 43B(h) inclusion rules.
9. **Permissions:**
   - the §5 matrix, 404s, and E8 masking in the register, search, export, print and aggregates;
   - no free text in notifications or the audit log;
   - the Administrator cannot grant to themselves.
10. **Segregation:** the person who inwarded an invoice cannot certify it, and a self-inwarded invoice is never theirs to verify.
11. **Reminder link:** visible items only; a non-invoice original is reopened; an invoice is not.
12. **e-Note conversion:** a draft with the confidential flag and `SourceInwardId`; attachments refused; retry is idempotent; hidden when e-Note is off.
13. **Imports:** preview categories, all or nothing, idempotent, nothing written to disk.
14. **Uploads:** 415 on every other `/Inward` route; text limits.
15. **Daily reminders:** aggregated, run once a day, inactive holders skipped.
16. **My Desk:** counts and `RoleKey`.
17. **Backup:** the new tables are included.

**Playwright (`inward.e2e.js`):**
1. Operator inwards a letter and prints the slip.
2. Mark, accept, note, reply and close.
3. Self-inward of an e-mail.
4. An invoice from inward through certify, send, part payment, full payment and automatic close.
5. A duplicate is blocked.
6. Register exported to PDF and Excel.
7. A non-participant gets 404.
8. The pages fit a phone screen.

**Performance (`NABCONS_BENCH=1`):** at 250,000 items, 750,000 movements and 25,000 invoices, with an operator of 3 offices, the p95 budgets are:

| Measure | Budget |
|---|---|
| Dashboard | < 300 ms |
| Register page 1 | < 300 ms |
| Search | < 400 ms |
| Invoice dashboard | < 300 ms |
| Inward write | < 50 ms |
| One-day register PDF | < 3 s |

**Upgrade:** start from a copy of a 1.6.0 database. Run `--migrate` twice, check row counts and `--verify-audit`, then roll back to 1.6.0.

## 14. Acceptance criteria (1.7.0)
1. Migration 8 is additive and safe to roll back; the audit log verifies.
2. Operators and self-inward users get numbers `NCS/IN/<office>/<FY>/nnnnn`.
3. All item transitions work as specified (§3.2), with an append-only trail.
4. Invoices follow §3.3:
   - payments and adjustments are kept in paise;
   - the item closes automatically when paid;
   - reversals work;
   - close-short is visible to the Inward Viewers;
   - duplicate protection and segregation of duties hold.
5. The MSME flags and the 43B(h) report follow D15.
6. Every screen and output respects visibility and confidential masking.
7. Reminders and digests arrive in-app; the badges update through the existing poll only.
8. Nothing can be uploaded except the two CSV imports, which are held in memory. No files and no bank details are stored. e-Notes created from inward items cannot carry files.
9. The performance budgets are met and recorded.
10. All tests pass, the documents are updated, and the package is built with its checksum.

## 15. Risks and open items
| # | Item | Plan |
|---|---|---|
| R-1 | The ERP payment format is unknown | Use our template now; map columns when a sample arrives. |
| R-2 | Elephant decisions E1–E10 | The owner confirms before merge. |
| R-3 | Working days ignore holidays | Accepted: the digest is advisory only. |
| R-4 | 43B(h) and s.15 are simplified (D15) | The owner chose this. The statutory acceptance-date model can be added later. |
| R-5 | A single person holding both operator and Accounts grants | Accepted: segregation applies at certification, and close-short is visible to the Inward Viewers. |

## 15. Round-2 readiness resolutions (binding; override earlier text where they differ)

| ID | Resolution |
|---|---|
| R2-1 | A *Credit / debit note* item must name a visible invoice ("against invoice"), stored in `LinkedToId`. Invoice Accounts have full access to such items. Recording the adjustment against the invoice closes the note item as `ActionCompleted`. An adjustment can also be recorded without an inwarded note. |
| R2-2 | `ActionRequired` values are fixed in code: `Reply`, `Action`, `Information`. Only `Reply` and `Action` get `ReplyDueOn`; `Information` items never count as overdue or due soon. |
| R2-3 | No table-level uniqueness on (ItemId, VoucherRef). Code refuses a voucher already used on the item by a payment that has not been reversed; a plain index supports the check. |
| R2-4 | Re-mark of a Certified invoice moves it to `WithVerifier` and clears certification. |
| R2-5 | On return to registry the holder's participant row is kept if they are the inwarder, a copy-to user, or an earlier holder who never returned the item. |
| R2-6 | Type and office are never editable (cancel and re-inward). Confidential is editable and re-indexed. Non-Accounts invoice edits stop at Certified; Invoice Accounts may edit amounts until the first payment, audited as "amounts edited", with no re-certification. |
| R2-7 | Copy-to and Add note apply to invoices. Add note needs full access, or operator of the office on a non-confidential item. |
| R2-8 | Converted e-Note body = summary, or "See inward {number}" when there is none or the item is confidential. Unique index on `ENotes(SourceInwardId)`. `ConvertedToENote` items cannot be reopened. |
| R2-9 | Build order: options and access before the e-Note link (done). |
| R2-10 | Transfer skips invoices whose inwarder is the new holder (count shown in preview); Certified invoices move to `WithVerifier`; one audit entry with the item count. |
| R2-11 | A credit note that brings the outstanding to zero settles the invoice: status `Paid`, closure `InvoiceSettled`. |
| R2-12 | ERP import: overridden duplicates not yet checked go to the preview category "enter manually"; header is `Amount` (net); rows for the same invoice are added up for the overpayment test; Source is `Manual`, `Import` or `Reversal`. |
| R2-13 | Register dates filter by `ReceivedAt` (stored UTC ISO, shown and filtered in office time). Reply due = received date + D7 days. |
| R2-14 | The acknowledgement slip masks the subject only (E8 exception); it shows the sender because the sender is who receives the slip. |
| R2-15 | On a confidential item an operator without full access may edit metadata fields only and cannot add notes. |
| R2-16 | Working days are used only by the unassigned digest and the matching dashboard tile. |
| R2-17 | A reminder for an Unassigned item notifies the office operators; for a closed invoice, Invoice Accounts. |
| R2-18 | Scheduler markers never bump `RowVersion` or `UpdatedAt`. |
| R2-19 | MSME flags apply to every `IsMsme` invoice; the 43B(h) report excludes Medium and traders and counts adjustments dated on or before the as-on date. Future invoice dates are refused. |
| R2-20 | Reversal reopening a Paid / ClosedShort invoice clears `ClosureType`, `ClosedAt`, `SettledAt`. Reversal rows cannot be reversed. |
| R2-21 | Project codes by CSV only in 1.7.0; never preselect the inwarder as certifier; project code optional. |
| R2-22 | Tests added to the plan: voucher re-entry after reversal, transfer segregation, reopen of a converted item refused, overridden duplicate in import, scheduler markers vs RowVersion, roll-back with `SourceInwardId`; Playwright covers invoice dashboard, 43B(h), ERP import preview and Access page. |
