# NABCONS Intranet — Administrator Guide

For the named **technical custodian** and **administrators**. Every action described here is recorded in the audit log.

## Roles

| Role | Can do |
|---|---|
| **Staff** (everyone) | Browse published content, search, submit requests, track own requests |
| **Publisher** | Create circular metadata, upload files and new versions, publish, withdraw, supersede; post notices |
| **Approver** | Act as Reviewing Officer or Sanctioning Authority on requests routed to them |
| **Administrator** | Users and roles, staff directory, CSV import, master lists, backups, all requests |
| **Auditor** | Read-only: audit log (search, export, verify), all requests, all circulars; cannot change anything |

A user can hold several roles. Nobody can approve their own request, and the two approval levels must be different people.

## Going live (switching from pilot data to real use)

1. Agree the go-live with NABARD IT (see `IT-SECURITY-NOTE.md`).
2. Stop the intranet.
3. In `appsettings.json` set `"SeedDemoData": false` and `"ShowDemoAccountsOnLogin": false`. Optionally change `"InitialAdminPassword"`.
4. Move the `data` and `documents` folders out of the application folder (keep them until you are satisfied).
5. Start the intranet. A new empty database is created with one account, `admin`, and the `InitialAdminPassword`. Sign in. You are forced to choose a new password.
6. Under *Administration*:
   - **Master lists** — check verticals, locations, circular categories and request categories;
   - **Import staff CSV** — load the approved directory extract;
   - **Users & roles** — create named accounts for publishers, approvers and auditors (local mode), or assign roles after first sign-in (Windows mode). Create a personal administrator account for each custodian. Then deactivate the shared `admin` account or keep it only for emergencies.
7. Set `SecondaryBackupFolder` to an approved LAN share and take a first backup.

## Users (local accounts)

- **Add user:** *Administration → Users & roles → Add user*. Set a temporary password, which the user must change at first sign-in. Passwords need at least 10 characters, with upper and lower case, a number and a symbol.
- **Reset password:** open the user and type a new temporary password.
- **Unlock:** accounts lock for 15 minutes after 5 wrong passwords. Use *Unlock account* to release one earlier.
- **Leaver:** untick *Account active*. The account cannot sign in, and its history stays intact. Accounts are never deleted.
- Changing roles or deactivating an account signs that user out of open sessions.
- **Locked out of the only admin account?** On the server, double-click `Reset-Admin-Password.bat`.

## Self-registration (from v1.1.0)

Staff can register themselves from the sign-in page (**New user? Register here**). Every registration needs an administrator's approval.

1. **The person enters their 10-digit mobile number.** It becomes their sign-in name.
   - **Number found in the staff directory:** their name, designation, vertical and office are filled in for them to confirm or correct. The e-mail on record is shown only partly (`a******a@…`), so someone guessing numbers learns little.
   - **Number not in the directory:** they fill in their details as a new person.
   - *Vertical* may be left blank (**Not applicable / leave blank**).
   - *Office / location* lists all state and union-territory capitals plus your offices, alphabetically. **Others** lets them type a location that is not listed; the typed text goes into the directory on approval. If it should become a regular choice, add it under *Administration → Master lists → Offices / Locations*.
   - They choose their own password, using the usual password rules.
2. **The request waits for approval.** The *Administration* menu shows a count, and *Administration → Self-registrations* lists the requests. If the person tries to sign in meanwhile, they are told the registration is awaiting approval.
3. **The administrator opens the request.** For existing staff, a side-by-side comparison highlights anything the person changed.
   - **Approve:** choose any extra roles; Staff is always given. This creates the sign-in account and **always fills in the staff directory**: the existing entry is updated with the confirmed details, or a new entry is added. For new people, enter their employee code. If none is known, a provisional code `REG-<mobile>` is suggested; correct it later under *Administration → Staff directory*.
   - **Reject:** a reason is required. The person is told the registration was not approved.
4. The person signs in with their mobile number and chosen password.

Safeguards:
- One pending registration per number, and no registration for a number that already has an account.
- Look-ups are limited to 10 per computer every 10 minutes.
- Every look-up, submission, approval and rejection is in the audit log.
- Mobile numbers are added to the directory with *Show mobile* switched **off**; switch it on only where disclosure is approved.

To switch self-registration off, set `"SelfRegistrationEnabled": false` in `appsettings.json` and restart. It is not used in Windows-authentication mode.

## Staff directory

- Holds only official information: employee code, name, designation, vertical, location, official e-mail, extension and (where disclosure is approved) official mobile. **Do not** store home addresses, personal numbers, dates of birth, payroll or appraisal data.
- **CSV import:** *Administration → Import staff CSV*. Download the template, fill it in Excel, then *Save As → CSV UTF-8*. Upload it and review the preview (new / update / error per row). Nothing changes until you confirm. Rows are matched on `EmployeeCode`. Tick *Mark staff not in this file as inactive* only when the file is a complete list.
- A sample file is in `sample-data\staff-import-sample.csv`.
- To remove someone, set *Active to*. Their entry disappears from the directory but stays in the history.

## Circulars

- Publishers create a circular with its number, title, category, issuing vertical, dates, keywords, synopsis, visibility and file. It starts as a **Draft**, which is invisible to staff.
- **Publish** makes it visible. **Visibility:** *All staff*; *Issuing vertical only* (staff whose user record has the same vertical); *Restricted* (publishers, administrators and auditors only).
- **New version:** upload a replacement file on the circular page. Earlier versions are kept, each with its SHA-256 checksum.
- **Supersede:** when creating the new circular, choose the old one under *Supersedes*. Publishing the new one marks the old one *Superseded*, with a link to the new one.
- **Withdraw:** a reason is mandatory. Staff still find a withdrawn circular when they tick *Include withdrawn*, clearly marked.
- Circular numbers must be unique. Search covers number, title, category, vertical, keywords and synopsis. Full-text search inside PDFs is deliberately not included in phase 1.

## Requests (General Administrative Approval Request)

`Draft → Submitted → Under Review → Approved / Returned / Rejected → Closed`

- The submitter chooses a **Reviewing Officer** (level 1) and a **Sanctioning Authority** (level 2). Both must be active users with the Approver role.
- The Reviewing Officer can *Recommend & forward*, *Return* or *Reject*. The Sanctioning Authority can *Approve*, *Return* or *Reject*. Returning and rejecting require remarks.
- A returned request goes back to the submitter, who edits it and submits it again.
- Every step is stored permanently, with the previous and new state, the person, the role, the time and the remarks. History cannot be edited or deleted, even directly in the database (protected by database triggers).
- If two people act on the same request at the same moment, the second is told that it changed and is shown the latest version.
- *All requests* (administrators and auditors) shows every request with its ageing.

## Profiles, photos and Business Leads (v1.2)

- **Vertical changes:** staff request them on *My Profile*. Approve or reject under *Administration → Vertical changes* (a remark is required to reject). Turn approval off with `"VerticalChangeRequiresApproval": false`.
- **Linking accounts:** *Users & roles* shows whether each account is linked to a staff-directory entry. Open a **Not linked** account → *Staff directory entry* → choose → *Save link*. Unlinked users see "ask the administrator" on My Profile.
- **Photos:** remove an unsuitable photo from *Users & roles → Edit → Remove photo*.
- **Business Leads access:**
  - BDU members are users whose vertical is **BDU** (or the vertical set in `BusinessDevelopmentVerticalName` / `BusinessDevelopmentVerticalId`).
  - Give the COO and MD the **LeadViewer** role.
  - The Administrator role alone does **not** open the pipeline.
- **Master lists:** lead client types, sectors, sources and lost/dropped reasons are edited under *Master lists*, like the other lists.

## My Desk and notifications (v1.3)

- **My Desk** (`/Desk`) shows each user what is waiting for them. Administrators also see pending self-registrations and vertical/profile changes, and BDU members and LeadViewers see new leads and their assigned leads that are due. Sections appear only for people with the matching role or items.
- **Notifications** are in-app only: no e-mail, SMS or other outside service.
  - They are created with the action that causes them, and nobody is notified about their own action.
  - Administrators are notified about registrations and vertical changes. With Windows sign-in, only administrators whose intranet user record holds the Administrator role are notified; people who get it only from `WindowsAdministrators` or an AD group mapping are not.
- **Housekeeping** runs every day straight after the scheduled backup. It removes old notifications (settings `Notifications:RetainReadDays` and `RetainUnreadDays`) and writes a `System.Housekeeping` entry to the audit log. If `DailyBackupTime` is switched off, housekeeping does not run either.
- Notifications are a convenience. The **audit log remains the record** of who did what.

## e-Note (v1.3)

- **Vertical codes.** *Master lists → Verticals* has an **e-Note code** column (2–8 letters or digits, e.g. HR, RID). New notes are numbered `NCS/EN/<code>/<FY>/<serial>`; verticals without a code use GEN. A change applies only to notes numbered afterwards. Each code has its own serial, restarting every April.
- **Categories** are a master list (*Master lists → e-Note categories*).
- **The register** (*e-Notes → e-Note register*, for Administrators and Auditors) lists every submitted note with its current holder, days with that holder and total days. It can be filtered, searched and exported to Excel.
  - It shows **metadata only**. The text, note-sheet and attachments follow the normal rules: the people the note passed through, and all staff once it is approved (unless confidential).
  - The Administrator and Auditor roles give no extra access to note content. Subjects of confidential notes are hidden in the register.
- **Nobody can edit history.** Comments, movements, submitted text versions and submitted attachments cannot be changed or deleted, even directly in the database (triggers). Corrections are made by adding a comment or a newer attachment.
- **Out of scope:** disciplinary, vigilance and complaint matters stay on the physical file. Initiators confirm this when they create a note.
- **Storage:**
  - attachments: `documents\enotes\<year>\<random>.dat`;
  - archival PDFs: `documents\enotes\pdf`.
  - Both are in every backup ZIP, under `documents/`.
- **Daily housekeeping** (after the scheduled backup) sends ageing reminders and reports any stored e-Note file that has no database record. It never deletes files; see the log.
- **Switching off:** set `"ENote": { "Enabled": false }`. The data stays and returns when it is switched back on.

## Message Board (v1.4)

- **Everyone can post.** Signed-in staff post messages with up to 4 photos on the *Message Board* tab. The home page shows the pinned posts and the newest ones.
- **Pinning.** Administrators see **📌 Pin** on each post. At most 3 posts can be pinned at a time (`MessageBoard:MaxPinned`); unpin one to pin another. Pinned posts stay at the top until unpinned.
- **Removing.** Administrators can remove any post: *Remove*, optionally give a reason, then *Remove post*. Authors can remove their own.
  - The post disappears for everyone and its photos are deleted.
  - The text, author, time and reason stay in the database and the audit log (`Board.Removed`), so there is a record of what was taken down.
- **Photos** are re-encoded without location or camera data and stored under `data\uploads\board`, which is included in every backup.
- **Switching off:** `"MessageBoard": { "Enabled": false }` in `appsettings.json`, then restart. Posts are kept.

## Audit log

- Records sign-ins (success, failure, lockout), sign-outs, uploads and rejected uploads, publishing, withdrawal, supersession, downloads, every request action, user and role changes, password resets, master-data changes, staff imports and edits, backups and restores.
- **Append-only:** the database refuses updates or deletions.
- **Tamper-evident:** each entry contains the SHA-256 of the previous one. *Audit → Verify integrity* (or `Verify-Audit-Log.bat`) recomputes the chain and reports the first altered entry, if any.
- **Export CSV** produces a file for review in Excel.

## Backup and restore

- **Automatic:** every day at `DailyBackupTime` (default 19:30) while the intranet is running. **Manual:** *Administration → Backup & restore → Create backup now*, or `Backup-Now.bat`.
- Each `NABCONS-Intranet-Backup-<date>.zip` contains the database (a consistent copy), all documents, `appsettings.json` and an audit-log CSV, with a `.sha256` file next to it.
- Configure `SecondaryBackupFolder` so that a copy leaves the PC. A backup on the same disk does not protect against disk failure or theft.
- **Restore:** stop the intranet → `Restore-Backup.bat` → drag in the ZIP → Enter. The checksum is verified first. The current `data` and `documents` folders are renamed to `*.before-restore-<date>`, never deleted. Start again and run *Verify integrity*.
- **Test a full restore every quarter** on a spare PC: copy the application folder and a backup ZIP there, restore, start, sign in and open a circular.

## Logs

The `logs\` folder holds one file per day (`intranet-YYYYMMDD.log`) with start-up messages, errors and a copy of each audit event. Delete files older than your retention policy.

## Health checklist (monthly)

- [ ] Latest backup present in both the backup folder and the secondary folder; one backup *Verified*
- [ ] *Audit → Verify integrity* passes
- [ ] Review *Login.Failed* / *Login.LockedOut* / *Upload.Rejected* entries
- [ ] Leavers deactivated (users and directory)
- [ ] Disk space on the data drive
- [ ] Windows and antivirus updates applied per IT patch cycle
