# DD-07 review — consolidated report (Goldfish round 1)

Three fresh, zero-context reviewers read DD-07 v1 and the code read-only: **G1 Comprehension** (`2026-11-08_DD-07_G1_comprehension_report.md`), **G2 Critic** (33 findings) and **G3 Implementation readiness** (27 R1 items, 7 blocking; 9 R2 items). The Elephant classified every finding and revised the document to **v2**. A second readiness pass (round 2) was then run on v2; see the end of this file.
**Totals:** 81 rows — PROPOSE FIX 72, CONTEST 3, DEFER to R2 6. All 7 blocking R1 gaps (G3-1…G3-7) are resolved in v2.


| ID | Finding (short) | Class | Resolution in DD-07 v2 |
|---|---|---|---|
| G1-1 | Brief cited but not in repo | PROPOSE FIX | References to the brief removed; §7 states the headings |
| G1-2 | NotifyWhere is private | PROPOSE FIX | Recipients resolved to ids and passed to Notify (§3.2) |
| G1-3 | State diagram unclear | PROPOSE FIX | Transition table §3.3 |
| G1-4 | Merge/reopen rules | PROPOSE FIX | §3.3 merge target rules; reopen clears closure fields |
| G1-5 | SO cannot request revision; no updates in ProposedClosure | PROPOSE FIX | Confirmed deliberate in §3.3/§5 |
| G1-6 | Menu rule for Auditor | PROPOSE FIX | §5: tab for AU always; content filtered |
| G1-7 | 'Previous' from any snapshot | PROPOSE FIX | Same template + ForumId, AsOn < current, not voided (§7.2) |
| G1-8 | Annexure I includes completed; ProposedClosure printed Completed | PROPOSE FIX | Confirmed and stated (§7.2) |
| G1-9 | Data may change between download and final | PROPOSE FIX | Preview + content hash; final refuses on mismatch (§3.4) |
| G1-10 | Restriction computed at read time | PROPOSE FIX | Stated as deliberate (§2); notifications carry no text |
| G1-11 | Migrated points get current-FY numbers | PROPOSE FIX | Confirmed; RaisedOn keeps age (§4.2) |
| G1-12 | Officer inactive tile undefined | PROPOSE FIX | Defined (§6) |
| G1-13 | Reminder naming/boundaries | PROPOSE FIX | Renamed windows + worked example (§8) |
| G1-14 | NextUpdateDue formula | PROPOSE FIX | Base, cycle and cap defined (§8) |
| G1-15 | ComplianceOptions vs IntranetOptions | CONTEST | Separate section with safe defaults follows ENote/MessageBoard precedent; CLAUDE.md requirement (no new keys needed) is met |
| G1-16 | XLSX byte identity | PROPOSE FIX | XlsxBookWriter fixes entry times; tested |
| G1-17 | DeskCounts record changes | PROPOSE FIX | Optional named members; cache key/token changes listed (§8) |
| G1-18 | 415 vs refuses | PROPOSE FIX | 415/413 specified (§3.5) |
| G1-19 | Demo seed only on new DBs | PROPOSE FIX | Confirmed (§12 #16) |
| G1-20..26 | R2 questions | DEFER (R2) | §16 |
| G2-1 | IFormFile may spill to temp file | PROPOSE FIX | MemoryBufferThreshold 2.1 MB > 2 MB import body cap; test checks temp folders (§3.5, §13-11) |
| G2-2 | Unbounded text lets files be pasted | PROPOSE FIX | Length limits in code and CHECK constraints; 200-char word limit; 256 KB body cap |
| G2-3 | Guard denylist; matching rules | PROPOSE FIX | Content-type allowlist; exact import path; case-insensitive |
| G2-4 | e-Note attachments bypass no-files (R2) | DEFER (R2) | Brief allows documents in the e-Note; COO to confirm in R2 interview (§16) |
| G2-5 | Administrator can grant self compliance roles | PROPOSE FIX | Grants in ComplianceMembers, managed by CA only; first CA by server command; resets notify Cell; residual DB-file risk documented |
| G2-6 | Segregation only on submitter | PROPOSE FIX | No Cell action on own points; <2 CA warning |
| G2-7 | Pending items orphaned on close/reopen | PROPOSE FIX | Withdrawn/Superseded states; auto-supersede; partial unique indexes |
| G2-8 | Final differs from checked download | PROPOSE FIX | Content hash preview → final |
| G2-9 | Back-dated as-on misstates | PROPOSE FIX | As-on rule §7.6 (no earlier than latest change) |
| G2-10 | Reminders tied to backup time | PROPOSE FIX | ComplianceScheduler hosted job with daily marker |
| G2-11 | REAL money; reversals (R2) | DEFER (R2) | Integer paise, linked reversals (§16) |
| G2-12 | Invoice duplicate key; ERP re-import (R2) | DEFER (R2) | §16 |
| G2-13 | Restricted details in audit log | PROPOSE FIX | Audit carries ids/labels only; text stays in ComplianceHistory |
| G2-14 | Snapshot visibility from JSON | PROPOSE FIX | ComplianceSnapshotEvents table |
| G2-15 | Cross-link leakage via merge/carried-from | PROPOSE FIX | Same restriction class; links shown only if visible |
| G2-16 | Unkeyed snapshot hash | PROPOSE FIX | Hash written into audit chain; canonical JSON fixed |
| G2-17 | Previous column / reopen / since date / voids | PROPOSE FIX | Same template+forum; reopen clears closure; since = previous AsOn inclusive; voids table |
| G2-18 | No triggers on points/events | PROPOSE FIX | Immutable columns + no-delete triggers; history keeps old/new values |
| G2-19 | Bulk transfer edge cases | PROPOSE FIX | §6 Transfer rules |
| G2-20 | CA from AD groups not notified | PROPOSE FIX | Grants table independent of roles/claims |
| G2-21 | RoleKey/token miss compliance | PROPOSE FIX | §8 |
| G2-22 | Roll-back strips roles | PROPOSE FIX | Grants table not touched by older programs |
| G2-23 | Inline bar widths blocked by CSP | PROPOSE FIX | w-0..w-100 classes |
| G2-24 | Two gates; streaming truncation; XLSX memory | PROPOSE FIX | ENotePdf uses ReportGate; render to capped MemoryStream then send; export capped at 10,000 rows |
| G2-25 | Import double confirm / user name / dates / limits | PROPOSE FIX | SHA per event; login name; Indian date formats; limits re-checked |
| G2-26 | Ageing base undefined | PROPOSE FIX | RaisedOn column |
| G2-27 | Forum rename breaks continuity | PROPOSE FIX | ForumId stored and used for grouping |
| G2-28 | Reminder flood; stale NextUpdateDue | PROPOSE FIX | One aggregated reminder per officer; recompute on event edit |
| G2-29 | MSME clock etc. (R2) | DEFER (R2) | §16 |
| G2-30 | Options binder appends | PROPOSE FIX | Resolve(): case-insensitive, unknown logged, file list replaces default, ['None'] |
| G2-31 | Trigger guard gaps | PROPOSE FIX | Names added to guards; CHECK on states |
| G2-32 | Metadata / identity definition | PROPOSE FIX | Metadata fixed; identity defined as regeneration vs regeneration and live+final footer |
| G2-33 | pre-upgrade copies retention | PROPOSE FIX | Documented manual clean-up (§10) |
| G3-1 | No user ids for segregation | PROPOSE FIX | User id columns added |
| G3-2 | NotifyWhere private / AD roles | PROPOSE FIX | Same as G1-2, G2-20 |
| G3-3 | Transition table | PROPOSE FIX | §3.3 |
| G3-4 | Snapshot item/header split; canonical JSON | PROPOSE FIX | §7.5 |
| G3-5 | Previous snapshot selection | PROPOSE FIX | §7.2 |
| G3-6 | Past as-on not reconstructable | PROPOSE FIX | §7.6 |
| G3-7 | No Forum on snapshots | PROPOSE FIX | ForumId, Forum, SnapshotEvents |
| G3-8 | Roll-back strips roles | PROPOSE FIX | Grants table |
| G3-9 | Admin can read Board matters | PROPOSE FIX | Grants table + residual DB-file risk recorded |
| G3-10 | Masters maintained by Admin only | CONTEST | Kept: Administrator maintains forum/category lists (D13); low change rate |
| G3-11 | Build order | PROPOSE FIX | §12 reordered |
| G3-12 | Forgotten files | PROPOSE FIX | BoardTests, Helpers, ENotePdf, site.js, UPGRADE-GUIDE, DESK-NOTIFICATIONS added to §12 |
| G3-13 | Date format | PROPOSE FIX | D14: dd.MM.yyyy across the module |
| G3-14 | Desk changes | PROPOSE FIX | §8 |
| G3-15 | Guard placement; import size | PROPOSE FIX | §3.5 |
| G3-16 | Import details | PROPOSE FIX | §6 import rules |
| G3-17 | Event text in audit | PROPOSE FIX | §5 rule 4 covers events |
| G3-18 | IST vs server time | PROPOSE FIX | DailyReminders(fmt.Today) |
| G3-19 | NextUpdateDue recompute | PROPOSE FIX | §8 |
| G3-20 | XLSX writer | PROPOSE FIX | XlsxBookWriter |
| G3-21 | Two gates | PROPOSE FIX | Shared ReportGate |
| G3-22 | Segregation scope | PROPOSE FIX | §5 rule 3 |
| G3-23 | Transfer edge cases | PROPOSE FIX | §6 |
| G3-24 | Options precedent | CONTEST | As G1-15 |
| G3-25 | Trigger state values | PROPOSE FIX | CHECK + guard |
| G3-26 | Brief reference | PROPOSE FIX | Removed |
| G3-27 | 1.5.0 Linux build for roll-back | PROPOSE FIX | Worktree build (§13) |
| G3-R1..R9 | R2 not buildable | DEFER (R2) | §16: R2 needs its own interview and Elephant pass |

## Round 2 — fresh G3 readiness pass on v2

Verdict: R1 almost buildable in one pass; 2 blocking gaps, 19 clarifications. All resolved in **v2.1** (or recorded as owner confirmations):

| ID | Finding | Class | Resolution |
|---|---|---|---|
| R2G3-1 | Import confirm urlencoded inflates text; CRLF changes the hash (blocking) | PROPOSE FIX | Confirm posted as multipart; hash on BOM-stripped, LF text |
| R2G3-2 | Which forum a multi-event ATR belongs to (blocking) | PROPOSE FIX | BoardAtr/InternalAudit: one forum; ObservationReply: newest event's forum |
| R2G3-3 | UploadGuard incomplete | PROPOSE FIX | Allowlist, caps, placement |
| R2G3-4 | Reminder audit wording | PROPOSE FIX | Own `Compliance.Reminders` entry |
| R2G3-5 | NextMeeting cap contradiction | PROPOSE FIX | max(today, NRO − lead) when NRO > today |
| R2G3-6 | Recompute points; who is reminded | PROPOSE FIX | Reopen and direct revision recompute; primary only |
| R2G3-7 | Officers on the edit form vs Reassign | PROPOSE FIX | Same SetOfficers path (history, notifications, markers) |
| R2G3-8 | Direct revision with a pending request | PROPOSE FIX | Pending becomes Superseded |
| R2G3-9 | ReportedText/Status definitions; final in one transaction | PROPOSE FIX | §7.5 |
| R2G3-10 | CHECK coverage; "full SQL is in" | PROPOSE FIX | Reworded; code limits stated |
| R2G3-11 | Done markers | PROPOSE FIX | Updated |
| R2G3-12 | CLI grant rules, .bat | PROPOSE FIX | §12 #11 |
| R2G3-13 | Notify needs a transaction | PROPOSE FIX | Helper opens its own |
| R2G3-14 | Admin can grant Auditor | PROPOSE FIX | CAs notified; residual risk R-6 |
| R2G3-15 | Temp-folder test flaky | PROPOSE FIX | Private ASPNETCORE_TEMP per test app |
| R2G3-16 | Numbering test needs a date | PROPOSE FIX | Optional date parameter for tests |
| R2G3-17 | CA as officer on import; notification flood | PROPOSE FIX | Refused; one notification per officer per import |
| R2G3-18 | ProposedClosure printed as Completed | CONTEST | Kept per D3 practice; owner confirms (R-7) |
| R2G3-19 | Transfer when B is already primary | PROPOSE FIX | Handled (supporting row removed only) |
| R2G3-20 | Pack helper JS; download audit | PROPOSE FIX | Server-side Pack handler; download/export audit |
| R2G3-21 | R2 fenced | — | Confirmed |

No blocking questions remain for R1. Implementation proceeds from v2.1.
