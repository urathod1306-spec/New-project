# NABCONS Intranet — Structured Test Script

Run on the isolated test PC with the demo data (password for all demo accounts: `Nabcons@2026`). Record Pass/Fail, the tester and the date. Items marked ✔ were also verified by the automated browser test run during development.

| # | Area | Steps | Expected result | Result |
|---|---|---|---|---|
| 1 | Unauthorised access | Open `http://<host>:8080/Circulars` in a private window without signing in | Redirected to sign-in; no content shown ✔ | |
| 2 | Role segregation | Sign in as `staff`; open `/Admin`, `/Audit`, `/Circulars/Edit`, `/Notices` | "Access denied" for each ✔ | |
| 3 | Role segregation | Sign in as `auditor`; try `/Circulars/Edit` and any Admin page | Denied; audit and all requests are viewable read-only ✔ | |
| 4 | Direct URL guessing | As `publisher`, create a draft circular and note its download link `/Circulars/Download/<n>`. Sign in as `staff` and open that link, and the details page | "Page not found" (404). Drafts and restricted files cannot be fetched by URL ✔ | |
| 5 | Direct URL guessing | As `staff`, open `/Requests/Details/1`, `/Requests/Attachment/1` for a request that is not theirs (create one as another user first) | 404 | |
| 6 | Visibility | As `staff` (Rural Infrastructure) search "approval matrix"; as `approver1` (HR) search the same | Staff sees the vertical-only circular; approver1 does not ✔ | |
| 7 | Invalid files | As `publisher`, upload a `.exe`; a text file renamed to `.pdf` | Both rejected with a clear message; `Upload.Rejected` in audit ✔ | |
| 8 | Oversized uploads | Upload a file larger than 20 MB | Rejected (browser warns; server refuses) | |
| 9 | Duplicate circular numbers | Create a circular with number `NABCONS/HR/<year>/01` | "already exists" ✔ | |
| 10 | Malformed search | Search for `"*)(OR AND NEAR( ' ; DROP TABLE` in circulars and directory | Page loads normally, no error ✔ | |
| 11 | Submission | As `staff`, New request → fill in → choose Rajesh Iyer / Anita Deshmukh → attach a PDF → Save & submit | Status *Submitted*, with approver1 ✔ | |
| 12 | Sequence | As `approver2`, open the request before approver1 acts | No action buttons ✔ | |
| 13 | Approval | As `approver1` *Recommend*; as `approver2` *Approve*; as `staff` *Close* | States change in order; history shows each step with role, time and remarks ✔ | |
| 14 | Rejection / return | Repeat with *Return* (remarks required), resubmit, then *Reject* | Remarks enforced; history preserved ✔ | |
| 15 | Concurrent approvals | As `approver1`, open the same submitted request in two tabs; act in tab 1, then in tab 2 | Tab 2 is refused: "updated by someone else" ✔ | |
| 16 | Self-approval | As `approver1`, raise a request and try to choose yourself | Not offered and refused by the server | |
| 17 | Withdrawn / superseded | Open `NABCONS/HR/<year>/01` and `NABCONS/ADM/<year>/03` | Superseded banner linking to HR/04 (the superseded circular still appears in search); withdrawn banner with reason, and the withdrawn circular is hidden from staff search unless *Include withdrawn* is ticked | |
| 18 | Audit completeness | As `auditor`, filter the audit log for each action above | Each action is present with user, time and IP | |
| 19 | Audit integrity | *Audit → Verify integrity*; run `Verify-Audit-Log.bat` | "intact" ✔ | |
| 20 | Lockout | Enter a wrong password 5 times for `auditor` | Account locked for 15 minutes; `Login.LockedOut` in audit; admin can unlock ✔ | |
| 21 | Backup | *Admin → Backup → Create backup now*; *Verify* | ZIP and checksum created; verify OK ✔ | |
| 22 | Full restore | Copy the application folder and a backup to another PC → `Restore-Backup.bat` → start → sign in | All data, documents and history present; integrity check passes ✔ (command-line restore tested) | |
| 23 | CSV import | Import `sample-data\staff-import-sample.csv` | Preview shows 2 new, 1 update; confirm applies them; audit entry written | |
| 24 | Edge/Chrome rendering | Check the home page, directory, circular, request and admin pages in Edge and Chrome, at desktop and phone width | Layout correct; no horizontal scrolling on a phone ✔ | |
| 25 | Restart | Close the console window and start again; reboot the PC with the service installed | Data intact; service starts automatically after reboot | |
| 27 | Self-registration (existing) | Sign-in page → Register → enter the mobile of a directory entry → change the designation → choose a password → submit | Details pre-filled; e-mail masked; "Registration submitted" ✔ | |
| 28 | Self-registration (new) | Register a number that is not in the directory | Blank form; submitted ✔ | |
| 29 | Approval | Admin → Self-registrations → review → approve | Changed fields highlighted; account created; directory entry updated or added (provisional REG-code if none) ✔ | |
| 30 | Sign-in | Sign in with the mobile number (also try "+91 98…") before and after approval | Before: "awaiting approval"; after: signed in ✔ | |
| 31 | Rejection / duplicates | Reject one with a reason; register the same number twice | Rejected applicant told not approved; duplicate blocked ✔ | |
| 32 | Notifications (v1.3) | As `staff`, submit a request to approver1. As `approver1`, keep any page open for up to a minute | Bell count rises without reloading; Approval Inbox badge too; the drop-down shows the new item as unread ✔ | |
| 33 | Notification privacy | As `staff`, open `/Notifications/Open/<n>` with the number of one of approver1's notifications | "Page not found"; approver1's notification stays unread ✔ | |
| 34 | Mark all read | Bell → *Mark all read*, and on the Notifications page | Badge and list cleared ✔ | |
| 35 | My Desk by role | Sign in as `staff`, `approver1`, `admin`, `bdu` and open *My Desk* | Each sees only the sections for their role or items; sign-in opens My Desk unless switched off in *My Profile → Preferences* ✔ | |
| 36 | Session timeout with polling | Sign in, leave the page open and untouched for 35 minutes | Signed out as before; the poll does not keep the session alive ✔ (automated with a 1-minute session) | |
| 37 | Keyboard | Tab to the bell, Enter, arrow keys, Esc | Opens, moves through items, closes and returns focus to the bell ✔ | |
| 38 | e-Note chain (v1.3) | `staff` drafts with two attachments → marks to `approver1` → comment, attach, mark to `approver2` → *Send back* → `approver1` marks to `coo` → *Return to initiator* → `staff` revises (version 2) and marks to `coo` → *Approve* | Every step on the note-sheet with name, designation and time; number assigned at submission; versions 1 and 2 kept; notifications to each recipient ✔ | |
| 39 | Only the holder acts | Open a note as someone in the chain who does not hold it now; try the action URLs with a copied form | No action buttons; any POST is refused ✔ | |
| 40 | Initiator cannot decide | Return a note to its initiator; try *Approve* as the initiator | Refused ✔ | |
| 41 | Concurrency | Same note in two tabs as the holder; act in tab 1, then tab 2 | Tab 2: "This note has changed" ✔ | |
| 42 | Visibility / URL guessing | As an unrelated user and as `auditor`/`admin`, open `/ENotes/<n>`, `/ENotes/Attachment/<n>`, `/ENotes/<n>/Pdf`, `/ENotes/<n>/Attachments.zip` for notes in every state, confidential and not | Only approved, non-confidential notes open for them; everything else "Page not found"; the PDF only for the initiator; the register shows metadata only ✔ (automated matrix: 8 states × 2 × 5 roles × 5 addresses) | |
| 43 | Append-only | In a copy of the database, try `UPDATE`/`DELETE` on `ENoteMovements`, `ENoteComments`, `ENoteBodyVersions`, and `DELETE` on attachments of a submitted note | Refused by triggers ✔ | |
| 44 | Uploads | Attach a 12 MB file, a renamed `.exe`, a 51st file | Each refused with a clear message; nothing left on disk ✔ | |
| 45 | Archival PDF | Initiator → *Download PDF for archival* on an approved note; open in a PDF reader; check Hindi text | Fonts embedded; numbered paragraphs; note-sheet; decision; attachment schedule with SHA-256; footer "not digitally signed", page x of y, record SHA-256 ✔ | |
| 46 | Phone / keyboard | Use a note with the keyboard only (type-ahead with arrows + Enter), and at phone width | Works; no horizontal scrolling ✔ | |
| 47 | Message Board (v1.4) | As `staff`, post a message with two photos (one taken sideways on a phone) | Post appears with both photos, upright; shown on the home page; other users see a badge on *Message Board* until they open it ✔ | |
| 48 | Board limits | Post an empty message; 2,001 characters; 5 photos; a 6 MB photo; a renamed `.exe` as `.jpg` | Each refused with a clear message; nothing stored ✔ | |
| 49 | Pin / remove | As `admin`, pin 3 posts, try a 4th; remove a post with a reason; as `staff`, try to pin | 4th refused ("Unpin one first"); removed post and its photos gone for everyone, `Board.Removed` in the audit log; staff see no Pin button ✔ | |
| 26 | No egress | With an outbound "block all" firewall rule for `NabconsIntranet.exe`, use every page | Everything works; no blocked-connection events | |

## Inward register and invoices (v1.7)
| # | Area | Steps | Expected | Result |
|---|---|---|---|---|
| N1 | Office codes and access | Set codes `HO`, `DEL` on *Master lists → Offices / Locations*; as `admin` grant Inward Operator (HO), Inward Viewer, Invoice Accounts on *Inward → Access*; try to grant yourself | Grants listed with history; yourself not offered ✔ | |
| N2 | Inward and slip | As the HO operator inward a letter marked to an officer; download the acknowledgement slip | Number `NCS/IN/HO/<FY>/00001`; slip is A4 portrait PDF ✔ | |
| N3 | Holder | As the officer: accept; close as *Replied* with outward reference | Item closed; trail shows each step ✔ | |
| N4 | Reminder letter | Inward a letter as "reminder of" the closed item | New number closed as reminder; original reopened with the officer, reminder count 1 ✔ | |
| N5 | Confidential | Inward a confidential legal notice marked to an officer; open it as another user, as the Auditor, as the Inward Viewer | Other user: page not found; Auditor: "Confidential", no text; Viewer: full ✔ | |
| N6 | Self-inward | As an ordinary user use *Inward an item* | Number of their office; held by them ✔ | |
| N7 | Duplicate invoice | Inward an invoice twice (same GSTIN, number, FY); then tick "not a duplicate" with a reason | Second refused with the first number; override saved and flagged; first payment asks for "checked" ✔ | |
| N8 | Segregation | Inwarder tries to certify / mark the invoice to themselves | Refused ✔ | |
| N9 | Payment cycle | Verifier certifies and sends to Accounts; Accounts records a part payment, an overpayment, a reversal and the same voucher again | Part-paid; overpay refused; reversal reopens; voucher accepted after reversal; Paid when settled ✔ | |
| N10 | MSME report | *Invoices → MSME / 43B(h) report* as on 31 March | Excel with Micro & Small and "class not recorded" sheets ✔ | |
| N11 | ERP import | Upload the payment template with one good row, one overpaying row and one unknown number | Preview categories; only the good row recorded; same file refused again ✔ | |
| N12 | No files | Post a file to an inward item page | 415, nothing stored ✔ (xUnit `Files_are_refused_except_on_the_import_pages`) | |
| N13 | Upgrade | Upgrade a copy of a 1.6.0 database | Schema 8; existing row counts unchanged; no demo items added; `--verify-audit` passes; 1.6.0 still starts on it ✔ | |

## Performance (v1.7, inward)
Run with `NABCONS_BENCH=1 dotnet test tests/NabconsIntranet.Tests --filter InwardBenchTests`.
- **Data:** 250,000 items over 12 offices (10% invoices, 12% open), ~500 holders, full search index; operator of 3 offices.
- **Machine:** development container with 4 vCPU.

| Measure | p50 | p95 | Budget |
|---|---|---|---|
| Dashboard, Inward Viewer (all offices) | 204 ms | 244 ms | < 300 ms |
| Dashboard, operator of 3 offices | 141 ms | 162 ms | < 300 ms |
| Invoice dashboard (Accounts) | 177 ms | 184 ms | < 300 ms |
| Register page 1 (operator / participant only) | 5.1 / 6.0 ms | 5.8 / 6.7 ms | < 300 ms |
| Text search (viewer) | 31.7 ms | 44.6 ms | < 400 ms |
| My Desk | 2.1 ms | 4.3 ms | < 300 ms |
| Re-mark (write) | 1.1 ms | 1.8 ms | < 50 ms |
| Register PDF, one day | 28.6 ms | 33.7 ms | < 3 s |

## Compliance (v1.6)
| # | Area | Steps | Expected | Result |
|---|---|---|---|---|
| C1 | Access | As `admin` (no grant) open `/Compliance/Points` and `/Compliance/Members` | "Page not found" for both: the Administrator role gives no compliance access ✔ | |
| C2 | First Compliance Admin | On the server run `Grant-Compliance-Admin.bat` for a user | User becomes Compliance Admin; audit `Compliance.AccessGranted`; existing CAs notified ✔ | |
| C3 | Event and import | As `cs`: register a statutory-audit event; *Import from CSV* with the template (2 rows); then import the same file again | Preview shows rows OK; 2 points numbered `CMP/<FY>/nnnnn`; second import refused ✔ | |
| C4 | No files | Try to post a file to a compliance page (e.g. a crafted multipart request) | 415, nothing stored ✔ (xUnit `No_file_can_be_uploaded_except_the_csv_import_and_bodies_are_bounded`) | |
| C5 | Officer update | As the officer: submit compliance; as `cs`: return with remarks; officer resubmits proposing closure; `cs` accepts with edited wording | Officer text kept; ATR wording changed; status Proposed for closure ✔ | |
| C6 | Segregation | As a CA who is also the officer of a point: try to accept its update | Refused: "another Compliance Cell member must act" ✔ | |
| C7 | Closure | Close without an authority; then with one; reopen | First refused; closed; reopen keeps the old closure in history ✔ | |
| C8 | Revision | Officer requests a new date; `cs` approves | Current due date changes; original due date unchanged ✔ | |
| C9 | Restricted | As `auditor` open a Board meeting point URL | Not found; Board points absent from dashboard counts and lists ✔ | |
| C10 | ATR | *Print ATR → Prepare pack* for the Board; preview; download Word, PDF, Excel; *Mark as final* | A4 landscape, Annexures, summary, "Status as on", page x of y; snapshot page says **Verified**; final copy footer shows snapshot no. and SHA-256 ✔ | |
| C11 | Stale preview | Preview an ATR, change a point's update in another window, then *Mark as final* | Refused: "data has changed since your preview" ✔ | |
| C12 | Next ATR | After C10, accept a new update on an earlier meeting's point and preview again | "Action presented in previous meeting" shows the text from the final ATR ✔ | |
| C13 | Reminders | Leave a point overdue overnight | One reminder in the officer's bell; Cell digest; nothing repeated the same day ✔ | |
| C14 | Upgrade | Upgrade a copy of a 1.5.0 database | Schema 7; existing row counts unchanged; `--verify-audit` passes; 1.5.0 still starts on it ✔ | |

## Performance (v1.6, compliance)
Run with `NABCONS_BENCH=1 dotnet test tests/NabconsIntranet.Tests --filter ComplianceBenchTests`.
- **Data:** 500 users, 300 events, 6,000 action points, 40,000 updates, 30,000 history rows, 2,000 snapshots × 40 rows (database 103 MB), plus 3 Board meetings × 40 points created through the workflow.
- **Machine:** development container with 4 vCPU (as for v1.3).

| Measure | p50 | p95 | Budget |
|---|---|---|---|
| Dashboard (6,000 points) | 55.4 ms | 85.5 ms | < 300 ms |
| Points list, overdue filter, page 1 | 14.4 ms | 24.2 ms | < 300 ms |
| Points list, text search (all statuses) | 29.0 ms | 38.0 ms | < 400 ms |
| Events list | 13.9 ms | 20.1 ms | < 300 ms |
| Officer: My action points / My Desk | 6.4 / 9.8 ms | 8.0 / 11.7 ms | < 300 / 250 ms |
| ATR 3 meetings × 40 points — DOCX | 21.6 ms | 27.1 ms | < 5 s |
| ATR 3 meetings × 40 points — PDF | 108.6 ms | 114.4 ms | < 5 s |
| Points Excel export, all 6,120 rows | 0.64 s | | — |
| Daily reminders over 6,000 points | 69 ms | | — |

## Performance (v1.3)

### Budgets at the brief's volume
Run with `NABCONS_BENCH=1 dotnet test tests/NabconsIntranet.Tests --filter PerformanceTests`.
- **Data:** the run seeds 500 users, 20,000 e-Notes, 200,000 movements, 40,000 comments and 300,000 notifications into a throw-away database (217 MB), then runs the nightly housekeeping once.
- **What is timed:** each page, as a full HTTP round trip inside the server process.
- **Machine:** development container with 4 vCPU. A 7th-gen Core i5 will be somewhat slower, but the margins are large.

| Measure | p50 | p95 | Budget |
|---|---|---|---|
| Badge poll, nothing changed (`304`) | 0.9 ms | 1.4 ms | < 20 ms |
| Badge counts after a change (database read) | 0.1 ms | 0.2 ms | < 20 ms |
| My Desk | 2.8 ms | 5.0 ms | < 250 ms |
| Note view (10 movements, 2 comments) | 2.8 ms | 8.0 ms | < 300 ms |
| Archival PDF, 29 pages, Hindi + English (`Thirty_page_note_renders_within_budget`) | 0.28 s (first generation) | | < 3 s |
| My e-Notes: Pending with me / Seen by me (page 2) | 3.2 / 4.5 ms | 5.7 / 5.3 ms | — |
| Approved notes, full-text search | 28.8 ms | 41.0 ms | — |
| Register: all 20,000 (page 1) / filtered + search | 3.6 / 24.9 ms | 5.2 / 32.9 ms | — |
| Register Excel export, 20,000 rows | 1.2 s | | — |
| Mark to (write, including audit entry) | 1.3 ms | 32.7 ms | — |

### Load check: 50 virtual users for 10 minutes
Run with `bash tests/LoadTest/run-load.sh 10 50`.
- **Setup:** a fresh copy of the published app with the benchmark volume above.
- **Each user:** signs in, then mixes My Desk, note views, *Mark to* posts, full-text searches and the Notifications page, with 1–4 s think time, plus the 60 s badge poll.
- **Machine:** the same 4-vCPU container, with the load generator running on the same machine.

| Operation | Requests | p50 | p95 | Max | Errors |
|---|---|---|---|---|---|
| My Desk | 4,235 | 2.9 ms | 5.1 ms | 1.2 s* | 0 |
| Note view | 4,491 | 2.5 ms | 4.9 ms | 0.3 s* | 0 |
| List: Seen by me | 3,552 | 4.9 ms | 7.7 ms | 1.2 s* | 0 |
| Mark to (POST) | 939 | 3.1 ms | 14.6 ms | 0.6 s* | 0 |
| Search approved notes | 1,140 | 30.8 ms | 42.3 ms | 1.2 s* | 0 |
| Notifications page | 1,161 | 2.8 ms | 4.6 ms | 0.3 s* | 0 |
| Badge poll | 483 | 1.4 ms | 2.6 ms | 0.1 s | 0 |
| Sign-in (password hashing, 50 at once) | 50 | 1.19 s | 1.43 s | 1.44 s | 0 |

**Totals:** 16,051 requests (26.6 per second), no errors. The server used 180 MB of memory and averaged 17 % CPU across 4 cores.

\* The maximum values all fall in the first seconds, when all 50 users sign in at once and the slow password hash (210,000 rounds, by design) holds the CPU. After that, every operation stays within its p95.
