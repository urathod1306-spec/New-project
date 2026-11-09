# DD-08: Inward Register with Invoice Inward and tracking (release 1.7.0)

**Status:** v1. Elephant draft for Goldfish review.
**Date:** 09.11.2026.
**Baseline:** release 1.6.0, schema 7.
**Scope:** this is R2 of `DD-07-compliance-and-inward.md`, which only outlined it (DD-07 §16). This document replaces that outline. It is the single source of truth for 1.7.0.

Paths are relative to `src/NabconsIntranet/` unless they start with `docs/` or `tests/`. The register-only rule, the offline and CSP rules, the audit and migration rules, and the patterns established in 1.6.0 all apply: grants held in the module, the upload guard, report writers, the scheduler, and desk counts (DD-07 §3, §7.1).

## 0. Decisions

### 0.1 Interview answers from the owner (09.11.2026)

| # | Question | Answer |
|---|---|---|
| D6 | Numbering | **One series per office**: `NCS/IN/<office code>/<FY>/<serial 5 digits>`. The serial restarts every April. |
| D7 | Reply-due default | **Calendar days**: Normal 7, Urgent 3, Immediate 1 (settings). The person inwarding can change the date for each item. |
| D8 | Invoice certification and terms | The **officer the invoice is marked to** certifies it. The registry is shown the project-code owner as a suggestion. Default terms are **30 days** from the invoice date. For MSME vendors, the payment due date can never be later than **45 days from acceptance**. |
| D10 | Self-inward | **Open to all staff from day one** (setting `SelfInwardEnabled`, default true). |

### 0.2 Decisions taken by the Elephant (owner may change before merge)

**E1. Office codes**
- Codes come from the existing **Offices / Locations** master list.
- The Master lists page gains a code column for offices: 2–8 letters or digits, the same rules as vertical codes.
- An office without a code numbers its items with `GEN`.
- No existing data is changed.

**E2. Item types**
- The types are a **fixed list in code**: Letter, E-mail, Invoice, Government order / notice, Legal notice, Other.
- This is not a master list as the brief suggests, because "Invoice" drives its own lifecycle and must not be renamable.
- Categories are a master list (`InwardCategory`).

**E3. Who manages access**
- Registry staff (*Inward Operator*, per office), *Inward Viewer* (COO, MD) and *Invoice Accounts* are **grants held in the module**, managed by the intranet **Administrator** on *Inward → Access*.
- This is unlike compliance: inward is an office-administration register, not a Board matter. Every change is audited.
- Grants live in their own table, so a roll-back to 1.6 does not strip them (finding G2-22 from DD-07).

**E4. Reminder letters**
- A reminder or follow-up letter is **inwarded with its own number** and immediately closed as *"Reminder of {original}"*.
- The original's reminder count goes up, and the original is reopened (to its last holder) if it was closed.
- Tracking therefore continues in one place.

**E5. Converted to e-Note**
- This option is **only offered when e-Note is enabled**. The owner's live server has e-Note off, so it is hidden there.
- It creates a **draft** e-Note for the holder. The draft carries the inward number and details as text.
- The new nullable column `ENotes.SourceInwardId` links the note back to the inward item.
- Documents may be attached in the e-Note, as the brief allows. **Owner to confirm** (risk R-3).

**E6. Money**
- All amounts are stored as **integer paise**, so the "paid + deductions = total" comparison is exact.

**E7. ERP payment CSV**
- There is **our own template**, because no ERP sample was available. The columns are listed in §6.11.
- Matching is on the inward number, or on vendor GSTIN/name plus invoice number plus invoice FY.
- The voucher/UTR reference is unique, so importing the same file again changes nothing.
- When an ERP sample arrives, a column-mapping change can be made.

**E8. Confidential items**
- In registers, lists, exports and prints, a confidential item shows **metadata only**: number, dates, type, mode, office, holder and status. The subject, summary, sender and sender's reference are replaced by "Confidential".
- Full details are shown only to participants (§5) and to Inward Viewers.
- The subject and summary of a confidential item are not put in the search index.

**E9. Holder for Accounts**
- When an invoice is *Sent to Accounts*, the item has **no personal holder**. It sits in the Accounts queue, which every Invoice Accounts user sees.

## 1. The Problem
Letters and e-mails go unanswered because nobody records receiving them, gives them to someone, or follows them up. Invoices arrive, but nobody can say how many are unpaid, for how long, or whether MSME vendors are being paid within the law (MSMED Act s.15; Income-tax s.43B(h)).

The Inward Register fixes this:
- **Every item** gets a number, a current holder, a reply-due date, an ageing clock and a closure record.
- **Invoices** also get a verification-to-payment lifecycle, duplicate protection and MSME monitoring.
- **No scans or files are stored** (decided by the COO). The paper stays in the file, the e-mail stays in the mailbox, and the register records where they are kept.

## 2. Glossary
| Term | Meaning |
|---|---|
| **Inward item** | One registered letter, e-mail, invoice or notice. |
| **Office** | The receiving office (a *Location* master item with a code). |
| **Inwarding** | Registering an item. It is done by an **Inward Operator** for their office, or by any user (**self-inward**) for an item they received directly. |
| **Holder** | The user who currently has to act on the item. |
| **Mark to** | Give the item to a holder. **Re-mark:** the holder passes it on. |
| **Copy-to** | Users told about the item for information. They can see it and add notes, but cannot act on it. |
| **Participant** | Anyone who inwarded the item, held it, or was copied on it. |
| **Return to registry** | The holder says the item was marked to them wrongly. It goes back to *Unassigned*. |
| **Closure type** | How the item ended: Replied, Action completed, Filed (no reply needed), Forwarded, Converted to e-Note, Duplicate, Reminder of another item, or Invoice settled / returned / rejected. |
| **Reply due** | The date by which a reply or action is due. Items marked "Information only" have none. |
| **Invoice status** | Received → With verifier → Certified → Sent to Accounts → Part-paid → Paid. Side states: On hold, Returned to vendor, Rejected, Closed short. |
| **Acceptance** | The date the verifier certifies the invoice. The MSME 45-day limit (MSMED Act s.15) runs from this date. |
| **Paise** | 1/100 rupee. All amounts are stored as whole paise. |
| **Project code** | Master list kept by Accounts, imported from an ERP CSV. It may name an owner officer, who is suggested as verifier. |

## 3. Technical plan

### 3.1 Components
```
Pages/Inward/*  ──►  InwardAccess      (grants cache ≤30 s, visibility SQL, per-action checks)
                     InwardRepository  (queries: registers, dashboards, invoice reports, desk rows)
                     InwardWorkflow    (every state change; numbering; reminders link; e-Note conversion)
                     InvoiceWorkflow   (invoice lifecycle, duplicate key, payments, reversals, auto-close)
                     Gstin             (offline format + mod-36 check digit)
                     InwardImport      (project codes CSV, ERP payments CSV; in memory)
                     InwardReports     (acknowledgement slip, register PDF/XLSX, invoice exports → ReportDocument)
        ▼
Infrastructure: Db · AuditService · INotificationService · ChangeTracker · DeskService · UploadGuard · ReportGate/writers · InwardScheduler (daily)
```
- **Reused from 1.6.0:**
  - grants pattern (`ComplianceAccess`);
  - `UploadGuard` (the prefix list gains `/Inward`);
  - `ReportDocument` writers, extended with a **portrait** option for the acknowledgement slip;
  - the scheduler pattern (`ComplianceScheduler`);
  - text limits (`ComplianceText`);
  - desk count embedding;
  - FTS5 pattern (`ENoteSearch`);
  - `LeadRepository.FinancialYear`;
  - `Fmt`.
- **No new package.**

### 3.2 Item states (`InwardItems.Status`)
| From | Action | Who | Required | To / effect |
|---|---|---|---|---|
| — | Inward (operator) | Operator of the office | §6.3 fields | **Unassigned**, or **WithHolder** if a holder is chosen |
| — | Self-inward | Any user (setting) | §6.3 fields | **WithHolder**, held by the user unless another holder is chosen |
| Unassigned | Mark to | Operator of the office | holder (active user) | **WithHolder** |
| WithHolder | Accept | Holder | — | `AcceptedAt` set |
| WithHolder | Re-mark | Holder or operator of the office | holder, optional remarks | **WithHolder** (new holder; `AcceptedAt` cleared) |
| WithHolder | Return to registry | Holder | remarks | **Unassigned** |
| any not Closed | Add copy-to | Holder or operator | users | Copies added (append-only) |
| any | Add note | Participant or operator | text ≤ 2,000 | Note appended |
| WithHolder (non-invoice) | Close | Holder | closure type, remarks, plus the type's fields (§6.5) | **Closed** |
| Closed (non-invoice) | Reopen | Last holder, operator of the office, or Inward Viewer | reason | **WithHolder** (last holder; if inactive, **Unassigned**) |
| — | Inward as reminder of N | Operator / self | original number | New item **Closed** (type Reminder); original `ReminderCount + 1`; original reopened if closed |

Every action:
- writes one `InwardMovements` row (append-only);
- writes an audit entry (`Inward.*`, numbers only);
- checks `RowVersion`.

### 3.3 Invoice states (`InvoiceDetails.InvoiceStatus`)
| From | Action | Who | Required | To / item effect |
|---|---|---|---|---|
| — | Inward as Invoice | Operator or self | invoice fields (§6.3); duplicate check | **Received** (item Unassigned), or **WithVerifier** if marked |
| Received | Mark to verifier | Operator | holder | **WithVerifier** (item WithHolder) |
| WithVerifier | Re-mark | Holder | holder | **WithVerifier** (new holder) |
| WithVerifier | Certify | Holder | acceptance date (≤ today, ≥ invoice date) | **Certified**; `AcceptedOn` set; `PaymentDueOn` computed (§3.4) |
| Certified | Send to Accounts | Holder | — | **SentToAccounts**; item **WithAccounts** (no personal holder) |
| SentToAccounts, PartPaid | Record payment | Invoice Accounts | §6.10 | **PartPaid**; or **Paid** when paid + deductions = total, which **closes** the item (type InvoiceSettled) |
| SentToAccounts, PartPaid | Close short | Invoice Accounts | reason | **ClosedShort**; item **Closed** |
| Paid, PartPaid, ClosedShort | Reverse a payment | Invoice Accounts | payment, reason | Status recomputed (§3.4); item **reopened** (WithAccounts) if it was closed |
| Received, WithVerifier, Certified, SentToAccounts, PartPaid | Put on hold | Holder, or Accounts if WithAccounts | reason | **OnHold**; `HoldFromStatus` remembered |
| OnHold | Release | Same | — | Back to `HoldFromStatus` |
| Received, WithVerifier, Certified, SentToAccounts (no payment) | Return to vendor / Reject | Holder, Accounts, or operator (if Received) | reason | **ReturnedToVendor** / **Rejected**; item **Closed** |

Rules:
- The generic Close action is not available for invoice items. They close only through these transitions.
- Reopen of an invoice is only through payment reversal.
- A returned or rejected invoice that comes back is a **new** inward item. The duplicate check then needs an override reason (§3.5).

### 3.4 Invoice calculations
- **Totals.** `TotalPaise` is mandatory and greater than 0. Taxable and GST are optional. If both are given, taxable + GST must equal total (± 100 paise for rounding).
- **Payments.** For each payment row: `NetPaise` (amount paid) + `TdsPaise` + `OtherDeductionsPaise` = **settled**. The status is recomputed from Σ settled:
  - 0 → **SentToAccounts**;
  - between 0 and total → **PartPaid**;
  - equal to total → **Paid**;
  - more than total → **refused**, because an invoice cannot be overpaid.
- **Payment due date (`PaymentDueOn`).**
  - Standard: invoice date + terms days (default 30).
  - MSME: the earlier of the standard date and acceptance + 45 days.
  - This is set at certification and shown before then as "expected".
- **MSME flag.** An MSME invoice that is not Paid or ClosedShort is flagged:
  - **> 45 days**: `AcceptedOn` + 45 < today;
  - **due ≤ 5 days**: within the final stretch.
- **FY-end 43B(h) report.** As on 31 March of a chosen FY, it lists invoices from **micro and small** enterprises that are unpaid, or paid after the allowed period. Medium enterprises are excluded under s.43B(h).

### 3.5 Duplicate invoices
- **Key.** `DupKey` = (`GSTIN` in upper case, or `N:` + the normalised vendor name if there is no GSTIN) + `|` + the normalised invoice number + `|` + the invoice FY.
  - Normalised vendor name: lower case, letters and digits only, common suffixes (pvt, ltd, private, limited, llp) removed.
  - Normalised invoice number: upper case, letters and digits only.
- **Uniqueness.** A partial unique index on `InvoiceDetails(DupKey) WHERE OverrideReason IS NULL` blocks duplicates.
- **Override.** The person inwarding may tick *"This is not a duplicate"* and must give a reason. The reason is stored and audited, and the existing item's number is shown to them.
- **Warning.** The same GSTIN and invoice number in a **different FY** gives a warning only. Invoice numbers restart each FY under GST rule 46.

## 4. Data model: migration 8, "Inward register and invoices (v1.7.0)"
Additive only. Older programs ignore the new tables. The new column `ENotes.SourceInwardId` is nullable, and older programs ignore it.

```sql
ALTER TABLE ENotes ADD COLUMN SourceInwardId INTEGER;

CREATE TABLE IF NOT EXISTS InwardSerials (OfficeCode TEXT NOT NULL, FinancialYear TEXT NOT NULL, LastSerial INTEGER NOT NULL, PRIMARY KEY (OfficeCode, FinancialYear));
CREATE TABLE IF NOT EXISTS InwardState (Key TEXT PRIMARY KEY, Value TEXT);

CREATE TABLE IF NOT EXISTS InwardMembers (
  Id INTEGER PRIMARY KEY, UserId INTEGER NOT NULL REFERENCES Users(Id),
  Role TEXT NOT NULL CHECK (Role IN ('InwardOperator','InwardViewer','InvoiceAccounts')),
  OfficeId INTEGER,                         -- operators only (Location master id)
  GrantedByName TEXT NOT NULL, GrantedAt TEXT NOT NULL, UNIQUE (UserId, Role, OfficeId));
CREATE TABLE IF NOT EXISTS InwardMemberHistory (Id INTEGER PRIMARY KEY, UserId INTEGER NOT NULL, Action TEXT NOT NULL, Role TEXT, OfficeId INTEGER, ActorName TEXT NOT NULL, At TEXT NOT NULL);
-- + no-update / no-delete triggers on InwardMemberHistory

CREATE TABLE IF NOT EXISTS InwardItems (
  Id INTEGER PRIMARY KEY,
  Number TEXT NOT NULL UNIQUE, OfficeId INTEGER NOT NULL, OfficeName TEXT NOT NULL, OfficeCode TEXT NOT NULL,
  FinancialYear TEXT NOT NULL, Serial INTEGER NOT NULL,
  ItemType TEXT NOT NULL CHECK (ItemType IN ('Letter','Email','Invoice','GovtOrder','LegalNotice','Other')),
  Mode TEXT NOT NULL CHECK (Mode IN ('Post','SpeedPost','Courier','Hand','Email','Fax')),
  ReceivedAt TEXT NOT NULL,                 -- ISO UTC
  SenderName TEXT NOT NULL CHECK (length(SenderName) <= 150), SenderOrg TEXT CHECK (length(SenderOrg) <= 200),
  SenderRef TEXT CHECK (length(SenderRef) <= 100), SenderRefDate TEXT,
  Subject TEXT NOT NULL CHECK (length(Subject) <= 300), Summary TEXT CHECK (length(Summary) <= 2000),
  Category TEXT CHECK (length(Category) <= 150),
  Priority TEXT NOT NULL DEFAULT 'Normal' CHECK (Priority IN ('Normal','Urgent','Immediate')),
  IsConfidential INTEGER NOT NULL DEFAULT 0,
  ActionRequired TEXT NOT NULL CHECK (ActionRequired IN ('Reply','Action','Information')),
  ReplyDueOn TEXT, KeptAt TEXT NOT NULL CHECK (length(KeptAt) <= 300), Pages INTEGER CHECK (Pages IS NULL OR Pages BETWEEN 0 AND 10000),
  Status TEXT NOT NULL CHECK (Status IN ('Unassigned','WithHolder','WithAccounts','Closed')),
  HolderUserId INTEGER REFERENCES Users(Id), HolderSince TEXT, AcceptedAt TEXT, LastHolderUserId INTEGER,
  InwardedByUserId INTEGER NOT NULL, InwardedByName TEXT NOT NULL, InwardedAt TEXT NOT NULL, SelfInward INTEGER NOT NULL DEFAULT 0,
  ClosureType TEXT, ClosureRemarks TEXT CHECK (length(ClosureRemarks) <= 1000), ReplyDate TEXT, OutwardRef TEXT CHECK (length(OutwardRef) <= 100),
  FiledFileNo TEXT CHECK (length(FiledFileNo) <= 100), ForwardedTo TEXT CHECK (length(ForwardedTo) <= 200),
  DuplicateOfId INTEGER, ENoteId INTEGER, ClosedAt TEXT, ClosedByName TEXT,
  LinkedToId INTEGER, ReminderCount INTEGER NOT NULL DEFAULT 0,
  LastDueReminderOn TEXT, LastOverdueReminderOn TEXT,
  RowVersion INTEGER NOT NULL DEFAULT 1, UpdatedAt TEXT NOT NULL);
-- indexes: (OfficeId, ReceivedAt DESC); (HolderUserId, Status, ReplyDueOn); (Status, ReplyDueOn); (Status, OfficeId, InwardedAt);
--          (ReceivedAt); (ClosedAt) WHERE ClosedAt IS NOT NULL; (LinkedToId) WHERE LinkedToId IS NOT NULL; (ItemType, Status)
-- triggers: Number, OfficeId, OfficeCode, FinancialYear, Serial, InwardedAt, InwardedByUserId never change; no delete

CREATE TABLE IF NOT EXISTS InwardMovements (
  Id INTEGER PRIMARY KEY, ItemId INTEGER NOT NULL, At TEXT NOT NULL, ActorUserId INTEGER, ActorName TEXT NOT NULL,
  Action TEXT NOT NULL, ToUserId INTEGER, ToName TEXT, Remarks TEXT CHECK (length(Remarks) <= 1000));
CREATE TABLE IF NOT EXISTS InwardCopies (ItemId INTEGER NOT NULL, UserId INTEGER NOT NULL, AddedAt TEXT NOT NULL, AddedByName TEXT NOT NULL, PRIMARY KEY (ItemId, UserId));
CREATE TABLE IF NOT EXISTS InwardNotes (Id INTEGER PRIMARY KEY, ItemId INTEGER NOT NULL, At TEXT NOT NULL, UserId INTEGER NOT NULL, Name TEXT NOT NULL, Text TEXT NOT NULL CHECK (length(Text) <= 2000));
-- indexes: InwardMovements(ItemId, Id); InwardMovements(ToUserId, ItemId) (participation); InwardCopies(UserId, ItemId); InwardNotes(ItemId, Id)
-- triggers: no update / no delete on all three

CREATE TABLE IF NOT EXISTS ProjectCodes (Code TEXT PRIMARY KEY, Name TEXT NOT NULL, OwnerUserId INTEGER, IsActive INTEGER NOT NULL DEFAULT 1, UpdatedAt TEXT NOT NULL, UpdatedBy TEXT NOT NULL);

CREATE TABLE IF NOT EXISTS InvoiceDetails (
  ItemId INTEGER PRIMARY KEY REFERENCES InwardItems(Id),
  VendorName TEXT NOT NULL CHECK (length(VendorName) <= 200), VendorGstin TEXT, VendorPan TEXT,
  IsMsme INTEGER NOT NULL DEFAULT 0, UdyamNo TEXT, MsmeClass TEXT CHECK (MsmeClass IS NULL OR MsmeClass IN ('Micro','Small','Medium')),
  InvoiceNo TEXT NOT NULL CHECK (length(InvoiceNo) <= 50), InvoiceDate TEXT NOT NULL, InvoiceFy TEXT NOT NULL,
  ProjectCode TEXT, PoRef TEXT CHECK (length(PoRef) <= 100),
  TaxablePaise INTEGER, GstPaise INTEGER, TotalPaise INTEGER NOT NULL CHECK (TotalPaise > 0),
  TermsDays INTEGER NOT NULL DEFAULT 30, AcceptedOn TEXT, PaymentDueOn TEXT,
  InvoiceStatus TEXT NOT NULL CHECK (InvoiceStatus IN ('Received','WithVerifier','Certified','SentToAccounts','PartPaid','Paid','OnHold','ReturnedToVendor','Rejected','ClosedShort')),
  HoldFromStatus TEXT, StatusReason TEXT CHECK (length(StatusReason) <= 1000),
  SettledPaise INTEGER NOT NULL DEFAULT 0, CertifiedByName TEXT, CertifiedAt TEXT, SentToAccountsAt TEXT, SettledAt TEXT,
  DupKey TEXT NOT NULL, OverrideReason TEXT CHECK (length(OverrideReason) <= 500));
CREATE UNIQUE INDEX IF NOT EXISTS UX_Invoice_Dup ON InvoiceDetails(DupKey) WHERE OverrideReason IS NULL;
-- indexes: (InvoiceStatus, InvoiceDate); (ProjectCode, InvoiceStatus); (VendorGstin); (IsMsme, InvoiceStatus, AcceptedOn)

CREATE TABLE IF NOT EXISTS InvoicePayments (
  Id INTEGER PRIMARY KEY, ItemId INTEGER NOT NULL, PaidOn TEXT NOT NULL,
  NetPaise INTEGER NOT NULL, TdsPaise INTEGER NOT NULL DEFAULT 0, OtherDeductionsPaise INTEGER NOT NULL DEFAULT 0,
  VoucherRef TEXT NOT NULL UNIQUE CHECK (length(VoucherRef) <= 60), ReversesId INTEGER UNIQUE, Remarks TEXT CHECK (length(Remarks) <= 500),
  Source TEXT NOT NULL CHECK (Source IN ('Manual','ErpImport','Reversal')), EnteredByUserId INTEGER NOT NULL, EnteredByName TEXT NOT NULL, EnteredAt TEXT NOT NULL);
-- index (ItemId, Id); triggers: no update / no delete. A reversal is a new row with negative amounts and ReversesId set (at most one reversal per payment).

CREATE TABLE IF NOT EXISTS InwardImports (Id INTEGER PRIMARY KEY, Kind TEXT NOT NULL, Sha256 TEXT NOT NULL, Rows INTEGER NOT NULL, ByName TEXT NOT NULL, At TEXT NOT NULL, UNIQUE (Kind, Sha256));
-- triggers: no update / no delete

CREATE VIRTUAL TABLE IF NOT EXISTS InwardSearch USING fts5(Number, Sender, SenderRef, Subject, Summary, Vendor, InvoiceNo, content='', contentless_delete=1, tokenize='unicode61');

-- master list InwardCategory, seeded once: General correspondence; Client / project; Government / regulator; Legal; Finance & accounts;
-- Human resources; Tenders & procurement; Other
```
- **Writing the SQL.** Write the full SQL, with every trigger, in `Schema.cs` migration 8.
- **Text limits.** Free text without a CHECK above is limited in code: names 150, notes 2,000.
- **Search index.** It is maintained in the same transaction as the item: insert, then delete and re-insert on edit. A confidential item indexes only the number, sender reference, vendor and invoice number.

## 5. Permissions
| Code | Meaning |
|---|---|
| **OP** | Inward Operator (per office) |
| **IV** | Inward Viewer |
| **IA** | Invoice Accounts |
| **AU** | the Auditor role |
| **AD** | the Administrator role |
| **P** | participant: inwarded it, held it (past or current), or copied on it |
| **X** | anyone else |

| Action | OP (own office) | IV | IA | AU | AD | P | X |
|---|---|---|---|---|---|---|---|
| Inward menu tab | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ if `SelfInwardEnabled` |
| Inward (register) an item | own offices | — | — | — | — | — | self-inward (any office) |
| Register list, dashboard, exports, register PDF | own offices | all | invoices only | all (read) | — | — | 404 |
| Open an item | own offices (confidential: metadata) | all, full | invoices, full | all (confidential: metadata) | — | full | 404 |
| Mark / re-mark / return / close / reopen | §3.2 | reopen | — | — | — | holder | — |
| Add note / copy-to | ✔ (own office) | — | — | — | — | note: ✔; copy-to: holder | — |
| Invoice: certify, send, hold, return or reject | if Received | — | when WithAccounts | — | — | holder | — |
| Payments, close short, reversals, ERP import, project codes | — | — | ✔ | — | — | — | — |
| Invoice reports, MSME list | — | ✔ | ✔ | ✔ | — | — | 404 |
| Acknowledgement slip | own offices | ✔ | — | — | — | inwarded-by | 404 |
| Inward access (grants) | — | — | — | — | ✔ | — | 404 |

Rules:
- **404 everywhere** for anything the user may not see.
- **One visibility rule.** Every list, count, export, print and desk count uses the same SQL predicate (`InwardAccess.VisibleSql`).
- **Confidential items** show metadata only to anyone who is not IV, IA (invoice) or a participant (E8).
- **No free text outside the module.** Notifications and audit details carry the inward number and type only, never subject or sender.
- **No site search.** Inward is not added to the site search (`Pages/Search.cshtml.cs`).

## 6. Screens (all under `/Inward`; dates dd.MM.yyyy; amounts ₹ with Indian grouping)
| # | Route | Who | Content |
|---|---|---|---|
| 6.1 | `/Inward` | OP, IV, IA, AU | Dashboard (detail below). Others are sent to `/Inward/Mine`. |
| 6.2 | `/Inward/Register` | OP, IV, IA (invoices), AU | Register list (detail below). |
| 6.3 | `/Inward/New` | OP, any user (self) | Inward form (detail below). |
| 6.4 | `/Inward/Mine` | anyone | Tabs: with me (open), copied to me, inwarded by me, closed in the last 90 days. |
| 6.5 | `/Inward/Item/{id}` | per §5 | Item page (detail below). |
| 6.6 | `/Inward/Ack/{id}` | per §5 | Acknowledgement slip, PDF (A4 portrait, half page). |
| 6.7 | `/Inward/Invoices` | IV, IA, AU | Invoice dashboard and list (detail below). |
| 6.8 | `/Inward/ProjectCodes` | IA | List and CSV import (detail below). |
| 6.9 | `/Inward/Access` | AD | Grants (OP per office, IV, IA), with a history of changes. |
| 6.10 | (on the item page) | IA | Payment form (detail below). |
| 6.11 | `/Inward/Payments` | IA | ERP payment import (detail below). |

**6.1 Dashboard**
- **Tiles:** open; overdue (reply due passed); due today or tomorrow; unassigned for more than 1 working day; received today.
- **Pending tables:** by holder (top 20), by holder's vertical, by office.
- **Ageing of open items since received:** 0–7, 8–15, 16–30, > 30 days.
- **Trend:** received versus closed per month for the last 12 months (`<progress>` bars).
- **Filter:** office (operators see only their own offices).

**6.2 Register list**
- **Filters:** office, date range (received), type, mode, status, holder, priority, overdue only, and a text search (FTS5 over number, sender, sender reference, subject, summary, vendor, invoice number).
- **Paging:** 50 rows per page.
- **Export:** Excel (at most 20,000 rows; otherwise the user is asked to narrow the filter).
- **Print:** register PDF for a date range (§7.2).

**6.3 Inward form**
- **Office:** defaults to the user's operator office, else the office of the user's directory entry, else `DefaultOfficeName`.
- **Fields:** type; mode; received date and time (default now); sender name and organisation; sender's reference and date; subject; summary; category; priority; confidential; action required; reply due (defaulted from priority; changeable); kept at (required); pages.
- **Reminder link:** optional "reminder or follow-up of inward no."
- **Mark to:** optional; self-inward defaults to the user. Copy-to users are optional.
- **Invoice section** (only when Type = Invoice; shown or hidden by `site.js`, and checked on the server anyway):
  - vendor name, GSTIN, MSME (yes/no) with Udyam number and class;
  - invoice number and date; project code (active codes); PO reference;
  - taxable, GST, total (₹); terms (days; default 30).
  - A duplicate warning or block, with the existing number, comes back from the server. *"Not a duplicate"* needs a reason.
- **After saving:** the item page, with an *Acknowledgement slip* button.

**6.5 Item page**
- **Shows:** details (respecting E8); movement trail; notes; copies; linked reminders.
- **Invoice panel:** status, amounts, payments, due date, and the MSME flag.
- **Actions:** buttons by role and state, per §3.2 and §3.3. Every form posts `RowVersion`.
- **Closure form:** the type, then the type's fields:
  - Replied: reply date and outward reference;
  - Filed: file number;
  - Forwarded: to whom;
  - Duplicate: inward number;
  - Converted to e-Note: no extra fields.

  Remarks are always required.

**6.7 Invoices**
- **Tiles:** received but unpaid (count and ₹); ageing by invoice date (0–30, 31–45, 46–60, 61–90, > 90); MSME over 45 days; MSME due within 5 days; on hold.
- **Tables:** by project code, by vendor (top 20), by holder or Accounts.
- **Trend:** received versus paid per month (₹ and count).
- **List:** filterable and exportable to Excel.
- **MSME report:** FY-end 43B(h) list, in Excel.

**6.8 Project codes**
- **List:** every code, with its owner and whether it is active.
- **CSV import:** columns `Code, Name, OwnerUserName, Active`; upsert by code. A code missing from the file is **not** deactivated unless *"Deactivate codes not in the file"* is ticked.
- **Limits:** 5,000 rows and 1,900 KB. The file is held in memory only and imported all or nothing; the same file twice is refused.

**6.10 Payment form**
- **Fields:** paid on; net amount; TDS; other deductions; voucher or UTR reference (unique); remarks.
- **Close short:** needs a reason.
- **Reverse:** done per payment row, with a reason.

**6.11 ERP payment import**
- **CSV template:** `InwardNo, VendorGstin, VendorName, InvoiceNo, InvoiceDate, PaidOn, Amount, TDS, OtherDeductions, VoucherRef, Remarks`.
- **Matching:**
  1. `InwardNo` if given;
  2. otherwise the duplicate key built from GSTIN or vendor name, invoice number and invoice FY.
- **Preview** shows matched, unmatched, already imported (voucher exists) and invalid rows.
- **Confirm** imports the matched new rows in one transaction and skips the rest. Rows that would overpay are refused.

## 7. Outputs
Outputs are built as `ReportDocument` (DD-07 §7.1) and streamed, never stored. `ReportDocument` gains `Portrait`, which is supported by the PDF and DOCX writers.

**7.1 Acknowledgement slip (PDF, portrait).**
- **Content:**
  - organisation name;
  - "Acknowledgement of receipt";
  - a two-column table of inward number, received date and time, office, from (sender, organisation), sender's reference and date, subject ("Confidential correspondence" if confidential), mode, pages, received by;
  - footer: "System-generated acknowledgement — no signature required · {number}".
- **Example:** `NCS/IN/HO/2026-27/00042 · 09.11.2026 11:20 · From: State Rural Livelihood Mission (Ref SRLM/2026/88 dated 05.11.2026) · Subject: Request for proposal — impact evaluation · By hand · 12 pages · Received by Asha Verma`.

**7.2 Inward register (PDF, landscape, and XLSX).**
- **Content:** office and date range in the title. Columns: Inward No · Received · Type / Mode · From · Sender ref & date · Subject · Marked to · Status / reply due.
- **Confidential items:** E8 applies.

**7.3 Invoice exports (XLSX).**
- **List columns:** Inward No, Vendor, GSTIN, MSME, Invoice no, Invoice date, Project, PO, Taxable, GST, Total, Settled, Outstanding, Status, Holder, Accepted on, Payment due, Days since invoice, MSME > 45.
- **43B(h) report columns:** Vendor, Udyam, Class, Invoice, Date, Accepted, Allowed by (acceptance + 45, or the agreed period if shorter), Total, Paid by 31 March, Outstanding on 31 March, Paid late (date).

## 8. Notifications, reminders, My Desk
**Notifications.** Module label "Inward"; types `Inward.*`.

| Event | Recipients |
|---|---|
| Marked or re-marked to someone | new holder |
| Copy-to added | the copied users |
| Returned to registry | operators of the office |
| Closed or reopened | inwarded-by user, and copies |
| Invoice certified, sent to Accounts, put on hold or returned | Invoice Accounts users (when sent); the holder (when returned from Accounts) |
| Invoice paid or closed short | the certifying officer |
| Grant changed | the user concerned |
| Daily reminder | each holder gets one aggregated notification: "n overdue, n due today or tomorrow, n not yet accepted for 2 days, n invoices with you > 7 days" |
| Daily digest to operators | "n unassigned for more than 1 working day" |
| Daily digest to Accounts | "n MSME invoices due within 5 days, n past 45 days" |

**Daily job (`InwardScheduler`).**
- **Timing:** same pattern as `ComplianceScheduler`: every 30 minutes; runs once per office day at or after `ReminderHour`, with the marker stored in `InwardState`.
- **Batching:** markers are set in batches of 200; one final transaction writes the summaries.
- **Working days:** Monday to Friday. There is no holiday calendar.
- **Overdue repeats:** an overdue item is reminded again after `OverdueRepeatDays`.

**My Desk.** New counts in the single query (optional named members of `DeskCounts`):

| Count | Who | What it counts |
|---|---|---|
| `InwardMine` | the holder | open items held by the user that are overdue, due within 1 day, or not yet accepted |
| `InwardUnassigned` | operators | Unassigned items in the operator's offices |
| `InvoiceQueue` | Invoice Accounts | items WithAccounts |

- They are added to `Total`, to the poll JSON (`inward`), to the Inward tab badge and to `site.js`.
- **Cache keys:** the queue key `t:inward` (for operators and Accounts), and the inward grants in `RoleKey`.
- **My Desk sections:** "Inward — with me", "Registry — unassigned", "Accounts — invoices to pay".

## 9. Settings (section `Inward`; all optional)
| Key | Default | Effect |
|---|---|---|
| `Enabled` | `true` | `false` hides the module. Data is kept. |
| `NumberPrefix` | `NCS/IN` | Prefix of inward numbers |
| `DefaultOfficeName` | `Mumbai (Head Office)` | Default office when the user has none |
| `SelfInwardEnabled` | `true` | Any user may inward items they received |
| `ReplyDueDaysNormal` / `Urgent` / `Immediate` | `7` / `3` / `1` | Default reply-due period (calendar days) |
| `UnassignedAlertWorkingDays` | `1` | Operators' digest threshold |
| `OverdueRepeatDays` | `3` | Overdue reminder repeat |
| `InvoiceTermsDays` | `30` | Default payment terms |
| `MsmeMaxDays` | `45` | MSME limit from acceptance |
| `InvoiceHolderAlertDays` | `7` | Reminder to verifiers holding an invoice |
| `ReminderHour` | `9` | Daily job hour (office time) |
| `MaxImportRows` / `MaxImportKB` | `5000` / `1900` | CSV import limits |

## 10. Storage and backup
**Design volume (5 years):**

| Data | Rows × size | Total |
|---|---|---|
| Inward items | 250,000 × 0.9 KB | 225 MB |
| Movements | 750,000 × 0.25 KB | 190 MB |
| Notes | 100,000 × 0.4 KB | 40 MB |
| Invoices | 25,000 × 0.5 KB | 13 MB |
| Payments | 40,000 × 0.2 KB | 8 MB |
| FTS index | | ~60 MB |
| **Total** | | **≈ 540 MB** |

- **With compliance** (≈ 225 MB) the whole database is ≈ 0.8–0.9 GB after 5 years.
- **Backup:** a full backup (`VACUUM INTO` + ZIP) is ≈ 250 MB and takes about a minute.
- **No new folders**, because no files are stored. The existing backup and restore cover everything unchanged.

## 11. Alternatives considered and rejected
| Alternative | Why rejected |
|---|---|
| Scans / e-mail files stored with items | **Decided by the COO.** Paper stays in the file and e-mail in the mailbox. A "kept at" field gives traceability, avoids malware and leakage risk, and avoids tens of GB of storage. |
| Separate invoice register and number series | Registry staff use one inward diary. An invoice is an inward item with extra fields (`InvoiceDetails` 1:1) and its own lifecycle. |
| Building on e-Note | e-Note is approval movement. Inward needs diary numbering, reply-due dates, closure types and payment tracking. Conversion to e-Note covers the cases needing a decision. |
| Money as REAL | Rounding makes "paid = total" unreliable (finding G2-11). |
| Mailbox / ERP integration | No outbound connections; there is no API on the LAN. CSV imports instead. |
| Item types as a master list | "Invoice" drives logic, and renaming it would break the lifecycle (E2). |
| Holiday calendar for working days | Maintenance burden. Reply-due is in calendar days (D7); only the operator digest uses Monday–Friday. |
| LIKE search over 250,000 rows | Too slow for the 400 ms budget; FTS5 is already used for circulars and e-Notes. |

## 12. Detailed implementation (in order)
| # | File | Change |
|---|---|---|
| 1 | `Infrastructure/Schema.cs` | Migration 8 (§4). |
| 2 | `Infrastructure/Notifications.cs`, `Data/Models.cs` | "Inward" label; `ChangeTracker.InwardQueue`; `MasterTypes.InwardCategory`. |
| 3 | `Pages/Admin/Masters.cshtml(.cs)`, `Data/ENotes.cs` (`ENoteMaster.CleanCode` reused) | Office code column for Locations (unique per type). |
| 4 | `Infrastructure/Reports/ReportDocument.cs`, `ReportPdfWriter.cs`, `DocxWriter.cs` | `Portrait` option. |
| 5 | `Data/Inward.cs` (new) | Codes, `InwardOptions`, entities, `InwardAccess`, `Gstin`, `Money` (paise parse and format). |
| 6 | `Data/InwardRepository.cs` (new) | Queries (§6), desk SQL. |
| 7 | `Data/InwardWorkflow.cs` (new) | §3.2 transitions, numbering, FTS upkeep, reminder link, e-Note conversion, daily reminders, grants. |
| 8 | `Data/InvoiceWorkflow.cs` (new) | §3.3–3.5, payments, reversals. |
| 9 | `Data/InwardImport.cs` (new) | Project codes and ERP payment CSV (in memory). |
| 10 | `Data/InwardReports.cs` (new) | Acknowledgement slip, register, invoice and 43B(h) exports. |
| 11 | `Infrastructure/InwardScheduler.cs` (new); `Infrastructure/UploadGuard.cs` | Daily job; `/Inward` prefix with multipart allowed only on `/Inward/ProjectCodes` and `/Inward/Payments`. |
| 12 | `Program.cs` | Options, services, hosted service. |
| 13 | `Data/Desk.cs`, `Infrastructure/Endpoints.cs`, `Pages/Desk/Index.cshtml`, `Pages/Shared/_Layout.cshtml`, `wwwroot/js/site.js` | Counts, sections, tab, badge; invoice-section toggle on the inward form. |
| 14 | `Data/ENoteWorkflow.cs`, `Pages/ENotes/View.cshtml` | `SourceInwardId` set on conversion; "Created from inward …" link. |
| 15 | `Pages/Inward/*` (new) | Pages of §6. |
| 16 | `wwwroot/css/site.css` | A few classes. |
| 17 | `Infrastructure/Seeder.cs` | Demo (new empty databases only): office codes, an operator grant, Accounts grant, project codes, a few letters and invoices. |
| 18 | Tests | `InwardTests.cs`, `InvoiceTests.cs`, `GstinTests` (in `InvoiceTests`), `InwardBenchTests.cs`, `tests/e2e/inward.e2e.js` + `run-all.sh`. |
| 19 | Docs | CHANGELOG, README, SETUP_GUIDE, TESTING_CHECKLIST, ADMIN-GUIDE, STAFF-GUIDE, IT-SECURITY-NOTE, DEPLOYMENT-GUIDE (settings), UPGRADE-GUIDE, TEST-SCRIPT (rows + p95), SBOM, `docs/designs/SITEMAP.md`, `PLATFORM.md` (migration 8, roles), `DESK-NOTIFICATIONS.md`, DD-07 §16 (points here). |
| 20 | `NabconsIntranet.csproj` | 1.7.0; package rebuilt; 1.6.0 ZIP removed. |

## 13. Test plan
**xUnit:**
1. **Migration.** Migration 8 applies once. Triggers and CHECKs work. 1.6.0 data is untouched.
2. **Numbering.** Numbers run per office and per FY; an office without a code gets `GEN`.
3. **Item transitions.** Every allowed item transition in §3.2 works, and forbidden ones fail.
4. **Invoice transitions.** Every invoice transition in §3.3 works; generic Close is refused for invoices.
5. **Payments.** Paise arithmetic: auto-Paid, PartPaid, overpayment refused, reversal (once, recomputes, reopens), close short.
6. **Duplicates.** Same key blocked; override with a reason allowed; same number in a different FY only warns.
7. **GSTIN.** Valid and invalid check digits; PAN extracted.
8. **MSME.** Due-date computation, the > 45 flag, and the 43B(h) list excludes Medium.
9. **Permissions.** The §5 matrix, 404s, confidential metadata-only in register, export and print, and no free text in notifications or audit.
10. **Reminder link.** The new item is closed and the original is reopened with `ReminderCount + 1`.
11. **e-Note conversion.** A draft e-Note is created with `SourceInwardId`; the option is hidden or refused when e-Note is off.
12. **Imports.** Project codes and ERP payments: preview, all or nothing, idempotent (same file refused; existing vouchers skipped), nothing on disk.
13. **No uploads.** Multipart to every other `/Inward` route gets 415; text limits hold.
14. **Daily reminders.** Aggregated, once per day, inactive users skipped.
15. **My Desk.** Counts and cache invalidation.
16. **Backup.** The backup includes the new tables.

**Playwright (`inward.e2e.js`):**
1. An operator inwards a letter (hand delivery) and prints the acknowledgement slip.
2. The operator marks it; the holder accepts it, adds a note, and replies and closes it.
3. Self-inward of an e-mail.
4. An invoice: inward, mark, certify, send to Accounts, part-pay, pay; the item closes automatically.
5. A duplicate invoice is blocked.
6. The register is printed to PDF and Excel.
7. A non-participant gets 404.
8. The page fits a phone screen.

**Performance (`NABCONS_BENCH=1`):**
- **Volume:** 250,000 items, 750,000 movements, 25,000 invoices and 40,000 payments.
- **Budgets (p95):**
  - dashboard < 300 ms;
  - register page 1 (office filter) < 300 ms;
  - FTS search < 400 ms;
  - invoice dashboard < 300 ms;
  - inward write (with FTS) < 50 ms;
  - register PDF for one day (300 items) < 3 s.

**Upgrade:**
1. Take a copy of a 1.6.0 database and run `--migrate` twice.
2. Check that row counts are unchanged, except for the new master items and the schema row.
3. Run `--verify-audit`.
4. Check that 1.6.0 still starts on the upgraded database.

## 14. Acceptance criteria (1.7.0)
1. Migration 8 is additive and roll-back safe. Existing data is unchanged; the audit log verifies.
2. Operators inward items for their offices; any user can self-inward. Numbers are `NCS/IN/<office>/<FY>/nnnnn`.
3. Marking, accepting, re-marking, returning to the registry, copy-to, notes, closure with the type's fields, reopen, and reminder linking all work. The movement trail and notes are append-only.
4. Invoices follow the §3.3 lifecycle:
   - payments are in paise;
   - the item closes automatically when paid;
   - reversals work;
   - close short needs a reason;
   - duplicate protection with an audited override;
   - GSTIN is checked offline.
5. The MSME 45-day monitoring and the 43B(h) list are correct.
6. Dashboards, registers, exports, the acknowledgement slip and the register PDF respect visibility and the confidential rule.
7. Reminders and digests arrive in-app; My Desk and the badge update through the existing poll only.
8. No upload anywhere except the two CSV imports (in memory). No file is stored. No bank details are stored.
9. The performance budgets are met at design volume and recorded.
10. All existing and new tests pass. The documents are updated. The package is built with its checksum.

## 15. Risks and open items
| # | Item | Plan |
|---|---|---|
| R-1 | The ERP payment export format is unknown (E7) | Our template now. A mapping change is made once a sample arrives. |
| R-2 | Elephant decisions E1–E9 | Owner confirms before merge. |
| R-3 | e-Note attachments after "Converted to e-Note" | The brief allows them. The owner's live server has e-Note off, so conversion is hidden there. |
| R-4 | Without a holiday calendar, "1 working day" counts gazetted holidays as working days | Accepted. The digest is advisory only. |
| R-5 | 300 items/day means about 1,500 writes/day plus FTS upkeep | Single short transactions; benchmarked. |
| R-6 | Operators may be at offices that have no code yet | `GEN` numbering; the Administrator adds codes on the Master lists page. |
