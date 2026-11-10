# Changelog — NABCONS Intranet

## 1.7.0 — 2026-11-09
**Upgrade note:** database schema 7 → 8 (additive only: new inward and invoice tables, one new column `ENotes.SourceInwardId`, eight inward categories added to the master lists; `NabconsIntranet.exe --migrate` takes a backup first). The new `Inward` settings section is optional. No new program files, packages or DLLs.

Built with the *Elephant–Goldfish Model*: design document `docs/designs/DD-08-inward-and-invoices.md` first, reviewed by fresh readers in two rounds (comprehension, critic, readiness; 94 points, all resolved — `docs/designs/reviews/DD-08_consolidated_review_report.md`), then built from the document, benchmarked at 250,000 items and given a hostile code review.

### Added — Inward register with invoice inward and tracking (new **Inward** tab)
- **Inward numbers per office:** `NCS/IN/<office code>/<FY>/00001`, restarting each financial year. Office codes are set on *Master lists → Offices / Locations*; offices without a code share `GEN`.
- **Registry and self-inward.** The Inward Operator of an office registers letters, e-mails, government orders, legal notices, invoices and credit/debit notes (type, mode, received time up to 90 days back, sender and reference, subject, summary, category, priority, action required, reply due, where the papers are kept). Any user can also inward an item they received directly (setting `SelfInwardEnabled`).
- **Movement.** Mark to an officer, accept, re-mark, return to the registry, copy to others, notes, close (replied with outward reference, filed, forwarded, duplicate), cancel (entered in error) and reopen. Every step is kept in an append-only movement trail. Reply-due defaults: 7 / 3 / 1 calendar days (Normal / Urgent / Immediate).
- **Reminder letters** are inwarded with their own number, linked to the original (whose holder is notified and which reopens if it was closed).
- **Convert to e-Note** (where e-Note is on): creates a draft e-Note linked to the inward item; files cannot be attached to it (setting `ConvertedNotesAllowFiles`).
- **Acknowledgement slip** (A4 PDF) for the sender; **register** with filters, search, Excel export and a printed register (PDF, up to 31 days).
- **Dashboard:** open, overdue, due soon, unassigned, held by inactive users, received today; by holder, vertical and office; ageing; received vs closed per month.
- **Invoices:** vendor, GSTIN (check digit verified), MSME / Udyam and class, invoice no. and date, project code, PO, taxable + GST + other = total, terms. Duplicate invoices (same vendor/GSTIN, number and FY) are blocked unless marked "not a duplicate" with a reason, and the first payment of such an invoice needs a "checked" confirmation. Lifecycle: received → with verifier → certified (never by the person who inwarded it) → with Accounts → part-paid / paid, with hold, return to vendor, reject, close short and credit/debit notes. Payments (net, TDS, other deductions, voucher/UTR) are append-only; mistakes are reversed, never edited. Amounts are kept in paise.
- **MSME:** due date = invoice date + the lower of the terms and 45 days; flags for "past 45 days" and "due within 5 days"; **MSME / section 43B(h) report** (Excel) as on any date, reproducible with an "entries up to" date.
- **Accounts:** invoice dashboard (unpaid and ₹ outstanding, ageing, by project / vendor / holder, received vs paid), invoice list export, **ERP payment import** from CSV with a preview (matched, already recorded, unmatched, not payable, would overpay, enter manually, invalid), and **project codes** by CSV.
- **Access:** Inward Operator (per office), Inward Viewer and Invoice Accounts grants, managed by the Administrator on *Inward → Access* (never for themselves), with history. Confidential items show only their number, dates and status to anyone without full access, and their text is not in the search index. Bulk transfer of a person's open items.
- **My Desk and reminders:** "Inward — with me", "Registry — unassigned" and "Accounts — invoices to pay" sections with a live badge; a daily summary per holder, a digest per registry and one for Accounts.
- **Register only:** no file upload or storage; CSV imports are read in memory.

## 1.6.0 — 2026-11-08
**Upgrade note:** database schema 6 → 7 (additive only: new compliance tables; `NabconsIntranet.exe --migrate` takes a backup first). The new `Compliance` settings section is optional. No new program files or DLLs except `Grant-Compliance-Admin.bat`.

Built with Google's *Elephant–Goldfish Model*: design document `docs/designs/DD-07-compliance-and-inward.md` written first (Elephant), tested by fresh reviewers in two rounds (comprehension, critic, implementation readiness; 108 points, all resolved — `docs/designs/reviews/DD-07_consolidated_review_report.md`), then built from the document and given a hostile code review.

### Added — Compliance Management (new **Compliance** tab)
- **Events and action points.** The Compliance Cell registers Board and committee meetings, NABARD inspections, statutory/C&AG audits, internal audits and review meetings, and records the action points under each: reference (memorandum, agenda, para), text, category, priority, owner vertical, primary and supporting dealing officers, due date and amount. Points are numbered `CMP/<FY>/00001`.
- **Bulk import** from a CSV template (Excel → *Save as CSV UTF-8*): preview with row-by-row checks, then all rows or none; the same file cannot be imported twice. The file is read in memory and never kept.
- **Periodic compliance.** Officers submit dated updates (ongoing, pending with an external party, or complied — propose closure). The Cell accepts them, editing the wording printed in the ATR if needed, or returns them with remarks. The officer's text is never changed.
- **Due dates.** Officers request a new date with a reason; the Cell decides. The original due date is never overwritten.
- **Closure.** Only the Cell closes, drops or merges a point, always with a closure authority (e.g. "taken on record by the Board"). Points can be reopened with a reason.
- **Monitoring.** Dashboard with open, overdue, due-soon and awaiting-review counts; source × status matrix; ageing; by vertical and by officer; points closed per month. Drill-down list with filters and Excel export (₹ or ₹ lakh).
- **Action Taken Reports** in Word (editable), PDF and Excel, A4 landscape, "Status as on", page x of y:
  - Board / committee ATR with Annexure I (latest meeting) and Annexures II, III… (earlier meetings with open points, including *"Action presented in previous meeting"*) and Completed / Ongoing / Total summaries;
  - Observation – reply (statutory, C&AG, NABARD inspection);
  - Internal-audit compliance sheet with dealing person and New / Old.
- **Final ATRs.** Preview, then *Mark as final — submitted to {forum} on {date}*: the exact content is kept as an unchangeable snapshot, with its SHA-256 recorded in the audit log. It can be downloaded again at any time and comes out identical; the next ATR takes "previous meeting" text from it. No report file is stored.
- **My Desk and reminders.** Officers see points that are overdue, due soon, returned or awaiting an update; the Cell sees its queue. Daily in-app reminders (one summary per officer) and a Cell digest; the Compliance tab has a live badge through the existing poll.
- **Access and confidentiality.** Compliance access is granted only by the Compliance Cell (the intranet Administrator cannot give it); the first Compliance Admin is created with `Grant-Compliance-Admin.bat`. Board and Audit Committee events, and events marked confidential, are visible only to the Cell, Compliance Viewers and the assigned officers. Cell members cannot act on their own points. Notifications and the audit log carry numbers, not text.
- **Register only.** No upload, attachment or file storage anywhere in the module; a guard refuses uploads on compliance pages.

### Also
- Shared report writers for Word, PDF and Excel (no new packages); the e-Note PDF now shares the same font code and generation limit.

## 1.5.0 — 2026-11-01
**Upgrade note:** no database change (schema stays 6) and no new settings. Program files only; roll back to 1.4.0 at any time.

This release comes from a structured design review (Google's *Elephant–Goldfish Model*): design specifications were written for every module (`docs/designs/`) and then checked against the code by independent reviewers with no prior context. 108 findings were raised; 90 were fixed and 18 were kept or deferred with reasons. See `docs/designs/reviews/consolidated_review_report.md`.

### Security
- Small built-in addresses (photos, contact cards, badge counts, people search) now require exactly the same sign-in check as pages; in Windows mode they previously accepted any Windows user.
- Failed sign-ins are counted reliably even when many arrive at once, so the 5-attempt lock-out cannot be bypassed.
- Sign-in answers in the same time whether or not the user name exists, and a computer is limited to 30 failed sign-ins in 10 minutes.
- Self-registration: submissions are limited to 5 per hour per computer; the mobile must be a valid Indian mobile, the e-mail on the official domain (when set) and the vertical from the list.
- Business Leads: the duplicate warning no longer shows the reference or status of leads the person may not open.
- The e-Note people search is switched off together with e-Note.

### Backups and recovery
- Every backup is now checked before it is kept: the database copy passes an integrity check, the ZIP is written under a temporary name, read back, and only then given its final name. A failed backup no longer leaves a partial ZIP behind.
- Restore unpacks the backup to a staging folder first, and only then swaps it in; if anything fails, your current data is put back. Unsafe entries in a ZIP are listed.
- `Rollback-Upgrade.bat` waits for the service to stop, refuses to run while the program is open in a window, and reports a failed copy instead of always saying "restored".

### Fixes
- Requests: the number uses the Indian date (a request made just after midnight on 1 January no longer gets the old year); drafts are visible only to the submitter (and administrators/auditors) until submitted; the category must be from the list.
- Circulars: publishing, withdrawing and superseding check the circular's current state, so double clicks or two publishers cannot overwrite each other; the "supersedes" link is locked after publishing.
- No stray files are left when saving a request, circular or lead fails part-way; the nightly report of such files now covers all modules (it reports, never deletes).
- Business Leads: moving a lead away from *Won* clears the awarded value and date.
- Registration: two administrators approving the same request at once can no longer create two accounts; the directory entry of someone who has left is never matched or brought back.
- Administration: changing a user's vertical also updates their directory entry (and the other way round) and is recorded in the profile history; linking to a deleted directory entry, removing an already-removed notice, and a failed "deactivate missing" import are handled cleanly.
- Message Board: a post refused for a bad photo no longer uses up the hourly limit; a removed post cannot be pinned; photos of a removed post stop showing at once; a saved post can never lose its photos.
- Photos taken with orientation 7 (some phones, rotated and mirrored) now appear correctly instead of blank.
- The notification bell takes you to the sign-in page when your session has ended, instead of showing the sign-in page inside the menu.
- Photo processing is limited to two at a time, protecting a small server PC's memory.
- Several small efficiency fixes (home page counts, e-Note badge refresh, view sampling).

### Documentation
- Design specifications for every module in `docs/designs/`, with a glossary, roles matrix, settings table, migrations table and operations notes; the five review reports and the consolidated report in `docs/designs/reviews/`.

## 1.4.0 — 2026-10-29
**Upgrade note:** database schema 5 → 6 (additive only; `NabconsIntranet.exe --migrate` takes a backup first). The new `MessageBoard` settings section is optional; defaults apply when it is absent.

### Added
- **Message Board**: a new tab in the main menu.
  - Every signed-in user can post a message, with up to 4 photos, as a separate post.
  - **Administrators** can pin up to **3** posts to the top and remove any post. Authors can remove their own posts.
  - Messages are plain text (up to 2,000 characters; line breaks kept).
  - Photos: JPEG, PNG or WebP, up to 5 MB each. Each is checked to be a real image, turned upright, resized (1,600 px, with a 480 px preview) and saved again without location or camera data, under `data\uploads\board`. Photos are shown only to signed-in users.
  - The **home page** shows a *Message board* card (pinned posts first, then the newest). The **menu badge** counts new posts by others since your last visit and refreshes with the existing badge check.
  - Removing a post hides it and deletes its photos. The post record stays for the audit trail: a database trigger prevents deleting it.
  - Every post, pin, unpin and removal is in the audit log (`Board.*`).
  - A flood guard limits each person to 10 posts an hour (configurable).
  - Switch the whole board off with `"MessageBoard": { "Enabled": false }`.

## 1.3.0 — 2026-10-17
**Upgrade note:** database schema 4 → 5 (additive only; `NabconsIntranet.exe --migrate` takes a backup first). New settings sections `Notifications` and `ENote` are optional. Defaults apply when absent. One new DLL is shipped: `libHarfBuzzSharp.dll` (text shaping for the PDF; see the IT Security Note).

### Phase 2 — e-Note (digital note-sheet)
- **No fixed hierarchy.** The initiator drafts a note, then marks it to any active colleague, following the usual office hierarchy. Whoever holds the note can:
  - comment and attach files;
  - mark it onward to anyone, including someone earlier in the chain;
  - send it back to the person who sent it, or return it to the initiator;
  - as competent authority, **approve** or **reject** it. The initiator never decides.
- One holder at a time; only the holder acts. If two people act at once, the second sees "This note has changed".
- **States:**
  - Draft → In movement ⇄ Returned to initiator → Approved / Rejected / Withdrawn → Closed.
  - The initiator can withdraw any time before a decision. Closing files the note read-only for ever.
- **Numbers:**
  - `NCS/EN/<vertical code or GEN>/<FY>/<serial>`, given at the first submission.
  - Each vertical code has its own serial, restarting every April.
  - Set the codes under *Master lists → Verticals*.
- **Text:**
  - Plain text, paragraphs numbered automatically, up to 50,000 characters.
  - Once submitted, the text changes only after a return, and every submitted text is kept as a numbered version.
- **Record-keeping:**
  - The note-sheet (comments and movements, with each person's name and designation at that moment) is append-only, enforced by database triggers.
  - Attachments are append-only once submitted; an uploader can mark their own file "superseded".
- **Visibility:**
  - Until approval, only the initiator and the people the note passed through can see it.
  - Approved notes are visible to all staff unless marked **Confidential** (setting `ENote:ApprovedVisibility`).
  - Administrators and Auditors see **metadata only**, through the **e-Note register** (ageing, filters, Excel export).
  - A declaration at creation excludes disciplinary, vigilance and complaint matters.
- **Attachments:**
  - PDF, Word, Excel, PowerPoint, JPG/PNG, TXT, CSV; content signature checked.
  - Limits: 10 MB per file, 100 MB and 50 files per note, with a remaining-quota hint.
  - *Download all (ZIP)*, streamed.
- **Archival PDF**, for the initiator once a note is decided:
  - Generated on the server with bundled fonts (English and Hindi), with page x of y and the record's SHA-256 in every footer.
  - Generated once per final state and stored in `documents\enotes\pdf`.
  - There is also a print-friendly view.
- **e-Note notifications and My Desk sections:**
  - received, sent back, returned, approved/rejected (initiator and all participants), withdrawn, comments/attachments (initiator only);
  - a daily reminder to holders of notes pending more than 7 days.
- Every action is in the hash-chained audit log (`ENote.*`). Views are recorded once per user per note per day.
- The daily housekeeping also keeps the database's query statistics current (`PRAGMA optimize`) and reports, but never deletes, stored files with no record.

### Phase 1 — My Desk and in-app notifications
- **Notification bell** in the top bar:
  - shows the unread count (99+ above 99) and a drop-down of the latest 10, with *Mark all read* and *View all*;
  - fully keyboard-usable (Enter opens, arrow keys move, Esc closes);
  - full **Notifications** page (`/Notifications`), with unread/all and module filters and pagination. Opening a notification marks it read.
- **What triggers a notification:**
  - Requests: assigned to the reviewer or sanctioning authority; the submitter is told when the request is recommended, approved, returned or rejected.
  - Administrators: a self-registration or vertical change is waiting. The requester is told when a vertical change is decided.
  - Business Leads: BDU members are told of each new lead. The submitter is told of status changes and owner assignment, and the new owner is told the lead is theirs.
- Nobody is notified about their own action. Each notification is written in the same database transaction as the action that causes it.
- **My Desk** (`/Desk`, with a "My Desk (n)" badge) brings your work into one page:
  - requests awaiting your action and your requests returned to you;
  - for administrators: registrations and vertical/profile changes waiting;
  - for BDU members and LeadViewers: leads in *Submitted* and your assigned leads due within 7 days or overdue;
  - recent notifications.
- New preference on My Profile: **Open My Desk after sign-in** (on by default).
- **Live badges** refresh with one small request every 60 s (`Notifications:PollSeconds`, minimum 30):
  - the request pauses while the tab is hidden, backs off after errors, and gets "304 Not Modified" when nothing changed;
  - the poll never extends the 30-minute idle timeout;
  - no WebSockets, Server-Sent Events or other long-lived connections are used.
- **Daily housekeeping** runs after the daily backup. It removes read notifications older than 90 days and unread ones older than 365 days (configurable). The audit log is not affected.
- Badge counts (Approval Inbox, Administration, Business Leads) now come from one cached query instead of several queries on every page.

### Changed
- New packages for the PDF: SkiaSharp.HarfBuzz 4.153.1 and HarfBuzzSharp 14.2.1.301 (MIT). Noto Sans and Noto Sans Devanagari fonts (SIL OFL 1.1) are embedded.
- The upload request size limit now allows several e-Note attachments in one request.
- Backups taken within the same second (e.g. `--migrate` run twice quickly, or a double click on *Create backup now*) no longer fail; the second gets a `-2` suffix.
- Signing in now opens **My Desk** unless the preference is switched off. Before, it opened the home page.
- The internal notification hook for leads, empty since 1.2.0, is replaced by in-app notifications.

## 1.2.0 — 2026-10-17
**Upgrade note:** database schema 3 → 4 (additive only). See `SETUP_GUIDE.md` for the upgrade command. Existing data is not changed.

### Added
- **My Profile** (user menu, above *Change password*):
  - Staff update their own mobile, extension/landline, official e-mail and office/location. Changes go straight to the staff directory and every field change is audited (who, when, old value, new value).
  - People who sign in with their mobile number keep doing so after changing it: the sign-in name follows the new number.
  - Name, employee code, designation and roles are read-only.
- **Vertical changes need approval** (`VerticalChangeRequiresApproval`, on by default). The request waits under *Administration → Vertical changes*, and access (e.g. Business Leads) changes only once it is approved.
- **Staff photos**:
  - Upload, replace or remove a photo on My Profile, with a preview. JPEG, PNG or WebP, up to 2 MB.
  - Every photo is decoded to verify it is a real image, set upright, cropped to a square and re-encoded at 400×400 with a 96×96 thumbnail. All EXIF/GPS metadata is removed.
  - Photos are stored in `data\uploads\photos` and shown only to signed-in users. Administrators can remove a photo.
- **Directory contact card**:
  - Click a name to open a card with photo, designation, vertical, office, extension, mobile (only where shown in the directory) and e-mail.
  - Call/e-mail links, copy buttons and *Save contact* (.vcf, vCard 3.0). Full-screen on phones, and usable with the keyboard alone.
  - Thumbnails appear in the directory list.
- **Business Leads**:
  - Any user can submit an opportunity: client type, sector, source, value, due date, client contact, tender number and attachments. A soft duplicate warning appears before submitting.
  - Each lead gets a reference `NCS/BD/<FY>/<serial>`. The FY runs April–March and the serial restarts each FY.
  - *My Leads* for submitters. The submitter can add follow-up notes while the lead is *Submitted* or *Under Review*.
  - For BDU members and the new **LeadViewer** role (COO/MD):
    - **Pipeline** with filters, search, sort and highlighting of leads due within 7 days or overdue
    - **Dashboard** (status, value, conversion rate, source, submitting vertical, upcoming due dates) and **Excel export**
    - Status changes (a remark is required on every change; Lost/Dropped need a reason; Won needs the awarded value and date), owner assignment, BDU files
    - An append-only timeline
  - A nav badge counts leads still in *Submitted*.
- New master lists: lead client types, sectors, sources, lost/dropped reasons; the **BDU** vertical if missing.
- `NabconsIntranet.exe --migrate`: takes a full backup, then upgrades the database (safe to repeat).
- Automated tests: 24 xUnit tests against the real application, plus Playwright browser suites (`tests/`).

### Changed
- Security updates: Microsoft packages and the bundled .NET runtime 10.0.12 → **10.0.13** (fixes advisory GHSA-vrrp-6v32-2p8g in the Windows-authentication component).
- Backups now include `data\uploads` (photos).
- Sign-in claims refresh automatically after an approved vertical or sign-in-name change, without signing the user out.
- Text inputs without an explicit type now get the standard field style.

## 1.1.1 — 2026-10-14
- Registration: vertical may be left blank; all state and UT capitals in the location list; "Others" with free text.

## 1.1.0 — 2026-10-14
- Staff self-registration (10-digit mobile number) with administrator approval; the staff directory is updated on approval.

## 1.0.1 — 2026-10-14
- Safe in-place upgrades (`Upgrade-Existing-Intranet.bat`, `Rollback-Upgrade.bat`) and automatic additive database migrations.

## 1.0.0 — 2026-10-11
- First release: home page, staff directory, circulars, approval workflow, roles, audit log, backup/restore.
