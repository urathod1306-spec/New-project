# e-Note (Digital Note-Sheet)

Status at v1.4.0: built and tested; **switched off on the live server** by the owner (`ENote:Enabled = false`). Data tables exist; pages return 404 while disabled.

## Problem Statement
Routine office notes move on paper note-sheets ("green sheets"); tracking where a file is, and reconstructing who said what, is slow.

## Goals & Non-Goals
**Goals:** no fixed hierarchy (holder marks to any active user); one holder at a time; comments, attachments, send back, return to initiator, approve/reject (never by the initiator); numbered text versions; append-only note-sheet; approved notes visible to all staff unless confidential; metadata-only register for administrators and auditors; archival PDF (English and Hindi).
**Non-Goals:** disciplinary, vigilance and complaint matters (stay on paper); digital signatures; rich-text editing.

## Architecture (as built)
- **Tables:** `ENotes`, `ENoteSerials` (per vertical code and FY), `ENoteBodyVersions`, `ENoteMovements`, `ENoteComments` (append-only by triggers), `ENoteAttachments` (delete only before first submission; one-time "superseded by" update), `ENoteParticipants`, contentless FTS5 `ENoteSearch`; `MasterItems.Code` for vertical codes.
- **Workflow:** `ENoteWorkflow` — every action is one `BEGIN IMMEDIATE` transaction with `RowVersion` and state check, movement/comment append, participants, search index and notifications; audit after commit. Number `NCS/EN/<code|GEN>/<FY>/<serial>` at first submission.
- **Visibility:** `ENoteRepository.CanView` — initiator; participants; everyone for approved non-confidential notes (setting `ApprovedVisibility`). Register (`Pages/ENotes/Register`) is metadata only.
- **PDF:** `Infrastructure/ENotePdf.cs` — SkiaSharp + HarfBuzz with embedded Noto fonts; record SHA-256 in the footer; stored once per final state; at most 2 generated at once.

## Alternatives Considered
- **Fixed approval hierarchy:** rejected by the COO — real routing follows the offline hierarchy.
- **PDFsharp / QuestPDF:** PDFsharp lacks Devanagari shaping; QuestPDF licence has a revenue limit.

## Risks & Mitigations
| Risk | Mitigation |
|---|---|
| Confidential content leak | Server-side checks on every page/download/ZIP/PDF; 404 for others; visibility matrix test |
| Disabled module still referenced | Pages 404; nav and desk sections hidden; old notification links lead to 404 until retention removes them |

## Tests
`ENoteWorkflowTests`, `ENoteVisibilityTests`, `ENoteUploadTests`, `ENotePdfTests`, `ENoteMigrationTests`; e2e `enote.e2e.js`; `PerformanceTests`; `tests/LoadTest`.
