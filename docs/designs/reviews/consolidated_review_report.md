# Consolidated review report — release 1.5.0

**Method:** Google's Elephant–Goldfish Model. The Elephant (the author, with full context) wrote the design specifications in `docs/designs/`. Fresh, zero-context Goldfish reviewers then read them and the code, read-only, in four passes: Comprehension, Zoom-Out, and two Codebase-Details passes (modules; platform; desk, e-Note and board). Each finding was classified by the Elephant as **PROPOSE FIX** (accepted: code or doc changed) or **CONTEST** (not changed, with the reason). Fixes are release 1.5.0. No schema change was needed (schema stays at version 6).

**Date:** 1 November 2026.

## Summary

- Findings: **108** (4 Blocker, 51 Major, 53 Nit).
- PROPOSE FIX: **90**; CONTEST (kept or deferred, with reason): **18**.
- All four Blockers (M1, M2, P1, P2) are resolved: P1 and P2 by code fixes, M2 by correcting the doc, M1 by the owner's decision with the doc corrected.
- New tests: `ReleaseFixTests` (12) and the 8-orientation photo test. All 81 unit tests pass.

## Reports
| Pass | Report | IDs |
|---|---|---|
| Comprehension | `2026-11-01_comprehension_report.md` | C1–C20 |
| Zoom-Out | `2026-11-01_zoom_out_report.md` | Z1–Z20 |
| Codebase details: modules | `2026-11-01_modules_codebase_details_report.md` | M1–M24 |
| Codebase details: platform | `2026-11-01_platform_codebase_details_report.md` | P1–P21 |
| Codebase details: desk, e-Note, board | `2026-11-01_desk_enote_board_codebase_details_report.md` | D1–D23 |

## Traceability matrix
| ID | Severity | Finding (short) | Class | Resolution | Where |
|---|---|---|---|---|---|
| C1 | Major | "No fixed hierarchy" vs README "following the office hierarchy" | PROPOSE FIX | Fixed in the design docs | SITEMAP glossary; PLATFORM sections; module docs |
| C2 | Major | "Vertical" (main access key), "masters", "location/office" are never defined | PROPOSE FIX | Fixed in the design docs | SITEMAP glossary; PLATFORM sections; module docs |
| C3 | Major | Acronyms not expanded: NABARD, FY (April–March? when serials reset), WAL, FTS5, PBKDF2, COO, MD, YAGNI, MP,… | PROPOSE FIX | Fixed in the design docs | SITEMAP glossary; PLATFORM sections; module docs |
| C4 | Major | Time handling never described: UTC or IST storage | PROPOSE FIX | Fixed in the design docs | SITEMAP glossary; PLATFORM sections; module docs |
| C5 | Major | README lists 5 roles, PLATFORM 6 | PROPOSE FIX | Fixed in the design docs | SITEMAP glossary; PLATFORM sections; module docs |
| C6 | Major | Settings named without section, default or effect | PROPOSE FIX | Fixed in the design docs | SITEMAP glossary; PLATFORM sections; module docs |
| C7 | Major | No transition table: actors, reject at level 1?, what happens after Returned, who closes, draft deletion, n… | PROPOSE FIX | Fixed in the design docs | SITEMAP glossary; PLATFORM sections; module docs |
| C8 | Major | "Failure is logged and raised": what the user sees and who is alerted are unclear | PROPOSE FIX | Fixed in the design docs | SITEMAP glossary; PLATFORM sections; module docs |
| C9 | Major | Default time, retention, backup-failure signal, whether restore needs the service stopped, whether the chec… | PROPOSE FIX | Fixed in the design docs | SITEMAP glossary; PLATFORM sections; module docs |
| C10 | Major | Schema versions vs program versions unmapped | PROPOSE FIX | Fixed in the design docs | SITEMAP glossary; PLATFORM sections; module docs |
| C11 | Major | How an AD user gets a user record | PROPOSE FIX | Fixed in the design docs | SITEMAP glossary; PLATFORM sections; module docs |
| C12 | Major | e-Note is off but presented as a feature | PROPOSE FIX | Fixed in the design docs | SITEMAP glossary; PLATFORM sections; module docs |
| C13 | Nit | "Remove", "delete" and "hide" used for the same thing | PROPOSE FIX | Fixed in the design docs | SITEMAP glossary; PLATFORM sections; module docs |
| C14 | Nit | Reference formats differ (prefix, year type, when assigned) without rationale | PROPOSE FIX | Fixed in the design docs | SITEMAP glossary; PLATFORM sections; module docs |
| C15 | Major | No list of notification events and recipients | PROPOSE FIX | Fixed in the design docs | SITEMAP glossary; PLATFORM sections; module docs |
| C16 | Nit | What site search covers (e-Notes? leads? notices? board?) | PROPOSE FIX | Fixed in the design docs | SITEMAP glossary; PLATFORM sections; module docs |
| C17 | Nit | Rate-limit values, rejection path (password hash removed?), provisional code format, who is notified | PROPOSE FIX | Fixed in the design docs | SITEMAP glossary; PLATFORM sections; module docs |
| C18 | Nit | Load figures and test conditions do not line up | PROPOSE FIX | Fixed in the design docs | SITEMAP glossary; PLATFORM sections; module docs |
| C19 | Major | Is the live server on HTTPS? Cookie Secure flag? Why was HTTP accepted for the pilot? | PROPOSE FIX | Fixed in the design docs | SITEMAP glossary; PLATFORM sections; module docs |
| C20 | Nit | No request-lifecycle overview | PROPOSE FIX | Fixed in the design docs | SITEMAP glossary; PLATFORM sections; module docs |
| Z1 | Major | Leads and Requests leave orphan files when a later file or the workflow call fails | PROPOSE FIX | Fixed: orphan report is a core step over all documents | HousekeepingService.OrphanFiles; ReleaseFixTests.Orphan_report_… |
| Z2 | Major | Upload-then-record logic duplicated and placed in different layers | PROPOSE FIX | Partly fixed: one save-all helper; full upload service deferred | FileStore.SaveAllAsync |
| Z3 | Major | `ENoteRepository.Person` used by the Board (e-Note module disabled) | CONTEST | Deferred: works when e-Note is off (static SQL); refactor without user benefit | — |
| Z4 | Major | FY logic lives in Leads | PROPOSE FIX | Partly fixed (request year); shared numbering helper deferred | RequestWorkflow |
| Z5 | Major | Request desk counts kept fresh only as a side effect of Notify | PROPOSE FIX | Fixed: shared e-Note marker removed; per-user refresh | ENoteWorkflow; Desk; Notifications |
| Z6 | Major | Image decode has no memory or concurrency limit (40 MP ≈ 160 MB + copies, up to 4 per post) | PROPOSE FIX | Fixed: at most two photos processed at a time | PhotoService.Gate; Board |
| Z7 | Major | Audit written after commit in a separate transaction | CONTEST | Deferred: large refactor of every workflow; failure is logged and visible; documented | PLATFORM risks |
| Z8 | Major | Module endpoints in Infrastructure | PROPOSE FIX | Partly fixed: switch guard on the lookup; per-module endpoint maps deferred | Endpoints.cs |
| Z9 | Major | Board flood guard re-implements `Throttle.Allow` | CONTEST | Kept per repository on purpose (separate app instances in tests); shared Throttle now prunes idle keys | Board.cs; Helpers.Throttle |
| Z10 | Major | Pages query the database directly | CONTEST | Deferred: refactor or optimisation without a current defect | — |
| Z11 | Major | Closed lead statuses hard-coded as literals in SQL | PROPOSE FIX | Fixed: SQL built from LeadStatus.Closed | LeadRepository.ClosedSql; ReleaseFixTests.Closed_lead_statuses_… |
| Z12 | Nit | Two `MaxPhotoMB` settings (2 and 5) in different sections | PROPOSE FIX | Documented | MESSAGE-BOARD |
| Z13 | Nit | `WriteJpeg` and `WriteFitted` near-duplicates | CONTEST | Deferred: refactor or optimisation without a current defect | — |
| Z14 | Nit | Path-containment check written three times | PROPOSE FIX | Partly fixed: FileStore.Resolve/Delete added | FileStore |
| Z15 | Nit | Sync-over-async wrapper for the ZIP | CONTEST | Kept: ZipArchive writes synchronously; the wrapper is needed with synchronous IO off | Endpoints.cs |
| Z16 | Nit | Plug-in list with one consumer | PROPOSE FIX | Fixed: orphan report is a core step over all documents | HousekeepingService.OrphanFiles; ReleaseFixTests.Orphan_report_… |
| Z17 | Nit | Loads every lead twice and aggregates in C# | CONTEST | Deferred: refactor or optimisation without a current defect | — |
| Z18 | Nit | Loads 180 days of leads into memory on each check | CONTEST | Deferred: refactor or optimisation without a current defect | — |
| Z19 | Nit | Desk SQL duplicates repository predicates | CONTEST | Deferred: refactor or optimisation without a current defect | — |
| Z20 | Nit | Removal deletes photos, unlike the append-only stance of other modules | PROPOSE FIX | Documented | MESSAGE-BOARD |
| M1 | Blocker | Doc: superseded and withdrawn are hidden from staff search | CONTEST | Owner decision: superseded circulars stay searchable (staff search old numbers); doc and TEST-SCRIPT row 17 corrected | CIRCULARS-NOTICES; TEST-SCRIPT |
| M2 | Blocker | Close only from Approved/Rejected | PROPOSE FIX | Fixed: doc states Close/Withdraw rules and full re-review | REQUESTS |
| M3 | Major | Request number year from `DateTime.UtcNow` (wrong year 00:00–05:30 IST on 1 Jan) | PROPOSE FIX | Fixed: office-date year | RequestWorkflow |
| M4 | Major | Approval not atomic: user created and directory changed before the guarded status update | PROPOSE FIX | Fixed: approval claims the request first; rolled back if the account cannot be made | RegistrationRepository.Approve; ReleaseFixTests.Two_approvals_… |
| M5 | Major | Submit repeats the directory lookup and reveals "found / not found" + masked e-mail without the lookup thro… | PROPOSE FIX | Fixed: submit throttled (5 per hour per computer) | Register.cshtml.cs |
| M6 | Major | Admin vertical change bypasses ProfileAudit, master-list check and Staff sync (vertical decides BDU access) | PROPOSE FIX | Fixed: vertical changes go through the profile history and sync directory ↔ account; master-list check | UserEdit; StaffEdit; ProfileRepository |
| M7 | Major | `SupersedesId` editable after publishing | PROPOSE FIX | Fixed: state-checked publish/withdraw/supersede; supersession locked after publish | CircularRepository; Circulars pages |
| M8 | Major | Row counts ignored | PROPOSE FIX | Fixed: state-checked publish/withdraw/supersede; supersession locked after publish | CircularRepository; Circulars pages |
| M9 | Major | Any user can probe other people's leads (reference, status, date) by client/title/tender | PROPOSE FIX | Fixed: details only for leads the person may open | Leads/New; site.js; ReleaseFixTests.Duplicate_check_… |
| M10 | Major | Matching falls back to a retired entry and reactivates/overwrites a former employee's row (recycled mobile) | PROPOSE FIX | Fixed: only current directory entries matched or updated | RegistrationRepository; ReleaseFixTests.Retired_directory_entries_… |
| M11 | Major | If the assigned officer loses the Approver role or is deactivated, the request is stuck | CONTEST | Deferred (new feature): limitation and work-around documented | REQUESTS behaviour details |
| M12 | Major | Uniqueness only vs active linked users | PROPOSE FIX | Documented; mobile validation on staff edit/import deferred | DIRECTORY-PROFILES |
| M13 | Major | Registration accepts any 10 digits, skips the e-mail domain, and does not check the vertical against master… | PROPOSE FIX | Fixed: mobile 6–9, e-mail domain, vertical from master list | Register.cshtml.cs |
| M14 | Major | Extra subset rule, token rules and max 5 undocumented | PROPOSE FIX | Documented; SQL pre-filter deferred with Z18 | BUSINESS-LEADS |
| M15 | Major | Any → any transitions | PROPOSE FIX | Fixed: leaving Won clears the award; transitions documented | Leads.ChangeStatus; BUSINESS-LEADS |
| M16 | Nit | Unbounded lists | CONTEST | Deferred: lists are small at current volumes; export cap documented | BUSINESS-LEADS |
| M17 | Nit | Orphan files on later failure (also Z1) | PROPOSE FIX | Fixed: shared FileStore.SaveAllAsync; files removed when the record fails | FileStore; Requests/Leads/Circulars pages |
| M18 | Nit | Thumbnail, 32 px minimum, inactive users' photos served, `v` unchecked — undocumented | PROPOSE FIX | Documented (inactive users' photos kept on purpose) | DIRECTORY-PROFILES |
| M19 | Nit | Chosen reviewer/approver can view a Draft before submission | PROPOSE FIX | Fixed: drafts visible only to submitter, administrators and auditors | RequestWorkflow.CanView |
| M20 | Nit | Considers inactive users | PROPOSE FIX | Fixed: link checks the entry exists | ProfileRepository.SetLink |
| M21 | Nit | Audits "Notice.Removed" even if nothing changed | PROPOSE FIX | Fixed: removal checks the row changed; documented | NoticeRepository; Notices page |
| M22 | Nit | Export includes hidden mobiles | PROPOSE FIX | Fixed: one transaction; export documented | StaffRepository.DeactivateMissing |
| M23 | Nit | New versions can be added to Superseded circulars, no remark | CONTEST | Kept: corrections to superseded circulars are useful; documented | CIRCULARS-NOTICES |
| M24 | Nit | Request category and issuing vertical not checked against masters | PROPOSE FIX | Fixed for request category; circular issuing vertical and REAL amounts documented/deferred | Requests/Edit; REQUESTS |
| P1 | Blocker | Minimal-API endpoints use the DefaultPolicy (authenticated only), not the fallback (`nab:uid` claim). In Wi… | PROPOSE FIX | Fixed: endpoints use the same policy as pages | Program.cs |
| P2 | Blocker | Backups are checksummed, never verified (no integrity check of the DB copy, ZIP not re-read) | PROPOSE FIX | Fixed: quick_check, .partial file, re-read before rename | BackupService.Create; ReleaseFixTests.Backup_is_checked_… |
| P3 | Major | ZIP written directly to its final name | PROPOSE FIX | Fixed: quick_check, .partial file, re-read before rename | BackupService.Create; ReleaseFixTests.Backup_is_checked_… |
| P4 | Major | A restore failing mid-way leaves `data` empty | PROPOSE FIX | Fixed: staged restore with roll-back; skipped entries listed; config note | BackupService.Restore; ReleaseFixTests.Restore_… |
| P5 | Major | Failed-login count read-then-write, not atomic | PROPOSE FIX | Fixed: one atomic UPDATE … RETURNING | UserRepository; ReleaseFixTests.Failed_sign_ins_… |
| P6 | Major | Unknown/deactivated user names return before the slow hash, so timing reveals valid names | PROPOSE FIX | Fixed: dummy hash for unknown names; 30 failed sign-ins per computer per 10 minutes | Login.cshtml.cs; ReleaseFixTests.Unknown_user_name_… |
| P7 | Major | The chain is unkeyed and does not detect tail truncation | CONTEST | Kept unkeyed (no secret store on a single PC); limits stated plainly in the doc | PLATFORM |
| P8 | Major | Stops only the service | PROPOSE FIX | Fixed: waits for STOPPED, checks console exe and robocopy result | Rollback-Upgrade.bat |
| P9 | Major | One attempt per day even on failure | PROPOSE FIX | Documented | PLATFORM operations |
| P10 | Major | Secondary folder copies, pre-upgrade snapshots, logs, `*.before-restore-*` folders and extra `--migrate` ZI… | PROPOSE FIX | Documented; automatic pruning of other folders deferred | PLATFORM operations |
| P11 | Major | Backup ZIPs (database, appsettings with the initial admin password, audit CSV) and plain-text logs hold sen… | PROPOSE FIX | Documented | PLATFORM operations |
| P12 | Nit | Transform maps the image outside the bitmap | PROPOSE FIX | Fixed: orientation 7 upright | PhotoService; PhotoTests (8 orientations) |
| P13 | Nit | Schema version read outside a transaction | PROPOSE FIX | Fixed: version re-checked inside the migration transaction | Db.Initialize |
| P14 | Nit | First-sign-in auto-provisioning race gives a 500 | PROPOSE FIX | Fixed: unique-violation re-reads the user | Identity.cs |
| P15 | Nit | Parameterless `BeginTransaction()` is already IMMEDIATE | PROPOSE FIX | Documented | PLATFORM operations |
| P16 | Nit | "Database newer than program" note logged as `System.Upgraded` on every start | PROPOSE FIX | Fixed: System.SchemaNewer action | Program.cs; Tools.cs |
| P17 | Nit | Settings names, counter reset, role change signs the user out, cookie not Secure over HTTP — undocumented | PROPOSE FIX | Documented | PLATFORM operations |
| P18 | Nit | Photo naming/limits, txt/csv check depth undocumented | PROPOSE FIX | Documented (photo sizes); text-file checks unchanged | DIRECTORY-PROFILES |
| P19 | Nit | Header list incomplete | CONTEST | Deferred: header list in DEPLOYMENT-GUIDE is sufficient for IT | — |
| P20 | Nit | Configuration not restored | PROPOSE FIX | Fixed: staged restore with roll-back; skipped entries listed; config note | BackupService.Restore; ReleaseFixTests.Restore_… |
| P21 | Nit | Blocking `SemaphoreSlim.Wait()` on request threads with the 30-second busy timeout | PROPOSE FIX | Documented | PLATFORM operations |
| D1 | Major | Only e-Note endpoint without the `ENote:Enabled` check | PROPOSE FIX | Fixed: lookup returns 404 when e-Note is off | Endpoints.cs; ReleaseFixTests.People_lookup_is_off_when_e_note_is_off |
| D2 | Major | The cleanup catch also wraps the tracker touch and audit after commit | PROPOSE FIX | Fixed: photos removed only if the post was not committed | Board.Create |
| D3 | Major | `/Notifications?handler=Menu` redirects to sign-in when the session has ended | PROPOSE FIX | Fixed: bell goes to the sign-in page | site.js |
| D4 | Major | Local mode: every poll still runs `ValidateCookie` → one Users primary-key lookup | PROPOSE FIX | Fixed: design doc corrected | DESK-NOTIFICATIONS / ENOTE / MESSAGE-BOARD behaviour details |
| D5 | Nit | Markers bumped before commit | CONTEST | Kept: the 1-second re-read rule covers markers bumped before commit; documented | DESK-NOTIFICATIONS |
| D6 | Nit | Deleting unread notifications does not bump the tracker | CONTEST | Kept: unread notifications deleted after 365 days; counts self-correct within the 10-minute maximum age | — |
| D7 | Nit | Read notifications selected by CreatedAt, not ReadAt | PROPOSE FIX | Fixed: design doc corrected | DESK-NOTIFICATIONS / ENOTE / MESSAGE-BOARD behaviour details |
| D8 | Nit | Shows 5, doc says 10 | PROPOSE FIX | Fixed: design doc corrected | DESK-NOTIFICATIONS / ENOTE / MESSAGE-BOARD behaviour details |
| D9 | Nit | Nav badge counts InMovement + Returned | PROPOSE FIX | Documented (badge vs sections); "10+" display kept | DESK-NOTIFICATIONS |
| D10 | Nit | Back-off after 3 errors | PROPOSE FIX | Fixed: design doc corrected | DESK-NOTIFICATIONS / ENOTE / MESSAGE-BOARD behaviour details |
| D11 | Nit | Sliding-expiration exclusion and the /api 401 apply to local accounts only (Windows has no idle timeout) — … | PROPOSE FIX | Fixed: design doc corrected | DESK-NOTIFICATIONS / ENOTE / MESSAGE-BOARD behaviour details |
| D12 | Nit | A rejected post still uses a quota slot | PROPOSE FIX | Fixed: failed posts keep the quota and leave no files | Board.Create; ReleaseFixTests.Failed_posts_… |
| D13 | Nit | `max-age=86400`: browsers keep a removed post's photo for up to 24 h | PROPOSE FIX | Fixed: board photos served with no-cache | Endpoints.cs |
| D14 | Nit | Rules undocumented (30 days, own posts excluded, any board page marks all seen, home card does not, MarkSee… | PROPOSE FIX | Fixed: design doc corrected | DESK-NOTIFICATIONS / ENOTE / MESSAGE-BOARD behaviour details |
| D15 | Nit | Dead code duplicating the Desk query | PROPOSE FIX | Fixed: dead code removed | Board.cs |
| D16 | Nit | A file written before size/hash computation is not in the cleanup list if that step throws | PROPOSE FIX | Fixed: failed posts keep the quota and leave no files | Board.Create; ReleaseFixTests.Failed_posts_… |
| D17 | Nit | IsDeleted read outside the transaction | PROPOSE FIX | Fixed: UPDATE checks IsDeleted inside the transaction | Board.SetPinned; ReleaseFixTests.A_removed_post_cannot_be_pinned |
| D18 | Nit | Single statements without RowVersion | PROPOSE FIX | Fixed: design doc corrected | DESK-NOTIFICATIONS / ENOTE / MESSAGE-BOARD behaviour details |
| D19 | Nit | Who may download, regeneration per state, old PDF kept and excluded from orphan report — undocumented | PROPOSE FIX | Fixed: design doc corrected | DESK-NOTIFICATIONS / ENOTE / MESSAGE-BOARD behaviour details |
| D20 | Nit | `used` HashSet can never trigger | CONTEST | Deferred: harmless; the streaming wrapper is required (see Z15) | — |
| D21 | Nit | Above 100,000 same-day entries every view scans the dictionary | PROPOSE FIX | Fixed: list cleared when the day changes | ENotes.FirstViewToday |
| D22 | Nit | Loads every request the user has ever submitted to count open ones | PROPOSE FIX | Fixed: COUNT query | RequestWorkflow.OpenCount; Index |
| D23 | Nit | ETag parts, RoleKey re-read, board/e-Note keys in every user's set — undocumented | PROPOSE FIX | Fixed: design doc corrected | DESK-NOTIFICATIONS / ENOTE / MESSAGE-BOARD behaviour details |

## Deferred work (candidates for a later release)
- **Z7** audit entries inside each business transaction.
- **M11** administrator reassignment of a stuck request.
- **Z2/Z8/Z14** one upload service and per-module endpoint maps.
- **Z4** a shared numbering helper (financial-year serials for leads and e-Notes).
- **Z17/Z18/M14/M16** SQL-side aggregation, duplicate pre-filter and paging for large volumes.
- **P10** automatic pruning of secondary copies, snapshots and logs.

## Keeping docs and code aligned (weekly drift check)
EGM is not a one-off. Each week, or before each release, the Elephant:
1. lists commits since the last check (`git log --since="1 week ago" --stat`);
2. for each changed module, re-reads its design doc and corrects any statement the change made untrue;
3. for a release, runs one fresh Goldfish Codebase-Details pass over the changed modules and adds its report here;
4. records findings and resolutions in this file (new rows, same classes).

