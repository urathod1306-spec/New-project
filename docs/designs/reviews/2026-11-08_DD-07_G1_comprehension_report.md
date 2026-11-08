# G1 Comprehension report — DD-07 (fresh reviewer, read-only)

**Restatement (summary).** The reviewer correctly restated both modules' purpose and users; the Board action-point path (event → points → officer updates → Cell review → revisions → reminders → ATR → mark as final → snapshot → closure with authority); the invoice path (R2 proposal); the reused services (Db, AuditService, INotificationService, ChangeTracker, DeskService, HousekeepingService, MasterRepository, XlsxWriter, Noto fonts, Csv.Parse, FinancialYear, Fmt, WriteOnlyAsyncStream, Roles, options pattern, Schema.Migrations, BackupService, ENoteWorkflow.Create); and the never-do list (no network, no new packages or JS libraries, no files, append-only history, no compliance text outside the module, load limits and p95 budgets).

## Ambiguities and assumptions
| ID | Section | What is ambiguous or assumed | Suggested clarification |
|---|---|---|---|
| G1-1 | §0.1 | Cites "brief §B1/§C"; brief not in the repository | Quote headings inline |
| G1-2 | §3.2, §8 | `NotifyWhere` is private; interface has only Notify/NotifyAdministrators/NotifyVertical | Say how role recipients are resolved |
| G1-3 | §3.3 | State diagram hard to read (ProposedClosure from Ongoing? PendingExternal → Ongoing?) | From/to/actor transition table |
| G1-4 | §3.3, §5 | Merge/reopen rules (MergedIntoId on reopen; merge into closed / other event / self) | State them |
| G1-5 | §5 | Supporting officers cannot request revisions; no updates during ProposedClosure — deliberate? | Confirm |
| G1-6 | §5 | Menu cell for Auditor on restricted is per-event | Menu shows if any event visible |
| G1-7 | §7.2, §7.5 | "Newest snapshot item (any final snapshot)" — different template/forum or after as-on? | Restrict and say so |
| G1-8 | §7.2 | Annexure I includes long-completed points; ProposedClosure printed as Completed | Confirm |
| G1-9 | §6.11, §7.5 | Data may change between download and Mark as final | Snapshot then download from snapshot, or warn |
| G1-10 | §5, §7.5, §9 | Restriction computed at read time; changing settings changes visibility of old data | Decide |
| G1-11 | §3.4, §4.2 | Migrated historical items get current-FY numbers | Confirm |
| G1-12 | §6.1 | "Officer inactive" tile undefined | Define |
| G1-13 | §8 | Reminder names vs `DueSoonDays`; due-1/overdue boundaries | Rename; examples |
| G1-14 | §8 | NextUpdateDue base date and cap when overdue | Formula per case |
| G1-15 | §9 | Separate `ComplianceOptions` vs CLAUDE.md "defaults in IntranetOptions" | Confirm |
| G1-16 | §7.1 | XLSX byte identity needs fixed entry times | State requirement |
| G1-17 | §8 | `DeskCounts` positional record; cache keys | List changes |
| G1-18 | §13 | 415 vs "refuses" | Fix status code |
| G1-19 | §12 | Demo user changes affect only new DBs | Confirm |
| G1-20–26 | R2 | Inward vs invoice state mapping; duplicate key; MSME clock; office codes; ERP formats; e-Note attachments; reference PC | R2 interview |
