# Goldfish Review — Stage 5: Zoom-Out (Skeptical Architect) (2026-11-01)

Reviewer: Zoom-Out Goldfish (fresh context, read-only; design docs + code).
Verdict (reviewer): the architecture fits one offline PC (SQLite WAL, server-rendered pages, ETag polling, no framework bloat; the owner's rules are visibly respected). The main weakness is horizontal reuse: uploads, number series, person snapshots, throttling, cache invalidation and module endpoints were each built per module, and their behaviour has drifted. Consolidate before adding modules; none of this needs a schema change.

| # | Sev | Scope | Finding | Direction |
|---|---|---|---|---|
| Z1 | Major | Leads/Requests pages, ENoteWorkflow OrphanFiles | Leads and Requests leave orphan files when a later file or the workflow call fails; only e-Note cleans up, and the orphan report scans only `documents/enotes` | FileStore save-all/rollback helper; orphan report over all of `documents/` |
| Z2 | Major | Leads, Requests, Circulars pages; ENoteWorkflow.AttachAsync | Upload-then-record logic duplicated and placed in different layers; limits scattered (static lists, options, hard-coded 5/10) | One attach helper with a per-module policy |
| Z3 | Major | ENotes.cs Person; Board.cs; Leads/Requests | `ENoteRepository.Person` used by the Board (e-Note module disabled); others snapshot from claims; Board passes `db.Open()` inline | Move `PersonSnapshot` to UserRepository; use it everywhere |
| Z4 | Major | Leads.FinancialYear, ENoteWorkflow, RequestWorkflow numbering | FY logic lives in Leads; serial upsert written twice; Request number uses the UTC calendar year | Shared Numbering helper; document the Request format as legacy |
| Z5 | Major | ChangeTracker use | Request desk counts kept fresh only as a side effect of Notify; `ENoteQueue` global key invalidates all users; some e-Note writes do not touch; disabled modules still in key set | Per-user explicit invalidation; drop `ENoteQueue` |
| Z6 | Major | PhotoService, Board | Image decode has no memory or concurrency limit (40 MP ≈ 160 MB + copies, up to 4 per post) | Semaphore around image work; sampled decode |
| Z7 | Major | AuditService | Audit written after commit in a separate transaction; a failure means action done, no audit, error page | `audit.Log(c, tx, …)` inside the business transaction where one exists |
| Z8 | Major | Endpoints.cs | Module endpoints in Infrastructure; `/api/users/lookup` (e-Note) ignores `ENote:Enabled` | Per-module endpoint maps with one switch guard |
| Z9 | Major | Board.cs vs Throttle | Board flood guard re-implements `Throttle.Allow`; neither prunes idle keys | Use `Throttle` with pruning |
| Z10 | Major | Audit page, Admin Index, AuditService.ExportCsv | Pages query the database directly; raw SQL `where` fragment passed between layers | Move into AuditService with parameterised filters |
| Z11 | Major | Leads.cs pipeline | Closed lead statuses hard-coded as literals in SQL | Build from `LeadStatus.Closed` |
| Z12 | Nit | DIRECTORY / MESSAGE-BOARD docs | Two `MaxPhotoMB` settings (2 and 5) in different sections; board one undocumented | Document both |
| Z13 | Nit | PhotoService | `WriteJpeg` and `WriteFitted` near-duplicates | Implement one with the other |
| Z14 | Nit | FileStore, ENoteWorkflow, Board | Path-containment check written three times | `FileStore.Resolve/Delete` |
| Z15 | Nit | Endpoints WriteOnlyAsyncStream | Sync-over-async wrapper for the ZIP; may be unnecessary in .NET 10 | Check; remove or document |
| Z16 | Nit | Housekeeping.ExtraSteps | Plug-in list with one consumer; orphan step registered when e-Note is disabled | Call steps directly with enable checks; orphan scan as a core step |
| Z17 | Nit | Leads Dashboard | Loads every lead twice and aggregates in C# | SQL GROUP BY; LIMIT for "upcoming" |
| Z18 | Nit | Leads.PossibleDuplicates | Loads 180 days of leads into memory on each check | Tender-number equality query first |
| Z19 | Nit | Desk.cs vs repositories | Desk SQL duplicates repository predicates | Share predicates |
| Z20 | Nit | MESSAGE-BOARD | Removal deletes photos, unlike the append-only stance of other modules; reason not documented | Document the deliberate choice |
