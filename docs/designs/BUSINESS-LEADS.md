# Business Leads

## Problem Statement
Business opportunities seen by staff across verticals were not reaching the Business Development Unit (BDU) in a trackable way, and management had no pipeline view.

## Goals & Non-Goals
**Goals:** any user submits a lead; unique reference `NCS/BD/<FY>/<serial>`; soft duplicate warning; BDU members and LeadViewers (COO, MD) manage status, owner and files; pipeline, dashboard and Excel export; submitter follows their own leads.
**Non-Goals:** CRM features (contacts database, e-mail campaigns); proposal document management; revenue accounting.

## Architecture (as built)
- **Tables:** `Leads`, `LeadSerials` (per FY), `LeadEvents` (append-only timeline), `LeadAttachments` (no delete).
- **Access:** `LeadAccess` (`Data/Leads.cs`): BDU = current approved vertical equals the configured BDU vertical (`BusinessDevelopmentVerticalId` or `...Name`); LeadViewer role; administrators get no lead powers. Submitter sees and follows up own leads while Submitted/Under Review. Every page and download checks; others get 403.
- **Reference:** atomic `INSERT … ON CONFLICT DO UPDATE … RETURNING` on `LeadSerials` inside `BEGIN IMMEDIATE`.
- **Status rules:** remark required for every change; Lost/Dropped need a reason (and text for "Other"); Won needs award value and date; `RowVersion` concurrency.
- **Duplicates:** last 180 days, same tender number, or same client with similar title (Jaccard ≥ 0.5) — warning only.
- **Notifications:** BDU members on new leads; submitter on status/owner changes; new owner on assignment (same transaction).
- **Export:** dependency-free `Infrastructure/XlsxWriter.cs`.

## Alternatives Considered
- **Access by role only:** rejected — BDU membership changes with postings; vertical (approved by an administrator) is the source of truth.

## Risks & Mitigations
| Risk | Mitigation |
|---|---|
| Client/tender information is sensitive | Access limited to BDU/LeadViewer/submitter; audit of every action and export |
| Vertical change grants access | Vertical changes need administrator approval |

## Tests
`LeadTests`; e2e `leads.e2e.js`.
