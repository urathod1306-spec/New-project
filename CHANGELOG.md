# Changelog — NABCONS Intranet

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
