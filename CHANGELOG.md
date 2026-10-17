# Changelog — NABCONS Intranet

## 1.3.0 — in development (do not install yet)
Database schema 4 → 5 (additive). Built in two phases; the 1.3.0 package is produced only when both are complete.

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
