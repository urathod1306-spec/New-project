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

### Behaviour details (confirmed in the v1.5.0 review)
- **Duplicate check:** matches the same tender number (letters and digits only), or the same client with a similar title (shared words, or one title's words a subset of the other's), among leads of the last 180 days; at most 5 shown. A person who may not open a matching lead is told only that a colleague has submitted a similar lead, without reference or status (v1.5.0).
- **Status changes:** any open or closed status can be set by the BDU/COO (re-opening is allowed). Leaving *Won* clears the awarded value and award date (v1.5.0). The closed statuses (Won, Lost, Dropped) are defined once in code.
- Lead viewers cannot be owners; owners must be BDU members. The Excel export is limited to 2,000 rows.
- Files saved for a lead that then fails to save are deleted again (v1.5.0).

## Alternatives Considered
- **Access by role only:** rejected — BDU membership changes with postings; vertical (approved by an administrator) is the source of truth.

## Risks & Mitigations
| Risk | Mitigation |
|---|---|
| Client/tender information is sensitive | Access limited to BDU/LeadViewer/submitter; audit of every action and export |
| Vertical change grants access | Vertical changes need administrator approval |

## Tests
`LeadTests`; e2e `leads.e2e.js`.
