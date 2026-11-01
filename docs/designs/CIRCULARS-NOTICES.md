# Circulars, Notice Board and Site Search

## Problem Statement
Circulars and office orders are scattered in e-mail and shared drives. Staff need one authoritative, searchable place, with control over drafts, who may see restricted documents, and which version is current.

## Goals & Non-Goals
**Goals:** circular metadata with versioned files; draft → published; visibility *All staff / Vertical only / Restricted*; full-text search; supersession and withdrawal kept visible; notice board on the home page; one search box for people and documents.
**Non-Goals:** editing documents in the browser; e-mail distribution; document-level digital signatures.

## Architecture (as built)
- **Tables:** `Circulars` (number unique, title, category, issuing vertical, issue date, keywords, synopsis, status, visibility, supersession/withdrawal fields), `CircularFiles` (versions with SHA-256), FTS5 table `CircularSearch` maintained by triggers.
- **Access:** `CircularRepository.Search/Latest/GetVisible` apply visibility in SQL: drafts and restricted circulars only for Publishers, Administrators and Auditors (`SeesAllCirculars`); vertical-only circulars for staff of that vertical. Downloads go through `Pages/Circulars/Download` with the same check and are audited.
- **Publishing:** Publishers create metadata, upload files (FileStore checks), publish; a later version adds a file row; supersession links to the new circular; withdrawal records a reason. Superseded/withdrawn circulars are hidden from staff search unless "Include withdrawn" is ticked.
- **Search:** user text is converted to a safe FTS5 query by `SearchText.ToFtsQuery` (letters/digits only, quoted, prefix-matched, max 10 terms), so malformed input cannot break the query. `Pages/Search` combines directory and circular results.
- **Notice board:** table `Notices` (title, body, important flag, start/end dates). Publishers manage notices (`Pages/Notices`); the home page lists current ones.

## Alternatives Considered
- **Shared folder with Windows permissions:** rejected — no metadata, no search, no audit, no supersession.
- **Search with SQL `LIKE`:** rejected for circulars — slow and poor ranking; FTS5 ships inside `e_sqlite3.dll`.

## Risks & Mitigations
| Risk | Mitigation |
|---|---|
| Restricted circular fetched by URL guessing | Visibility re-checked on details and download; returns 404 |
| Uploaded file is not what its extension claims | Magic-byte check; random stored names; served as attachment with `nosniff` |

## Tests
`ExistingBehaviourTests`; e2e `core.e2e.js` (visibility, URL guessing, invalid files, FTS search, malformed search).
