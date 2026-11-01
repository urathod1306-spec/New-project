# General Administrative Approval Request

## Problem Statement
Routine administrative approvals (procurement below delegated limits, tours, nominations, IT equipment) move on paper with no record of who decided what and when.

## Goals & Non-Goals
**Goals:** one form type with two approval levels (Reviewing Officer, then Sanctioning Authority); permanent step history; attachments; inbox for approvers; protection against two people acting at once; notifications to the next actor and the submitter.
**Non-Goals:** configurable multi-level workflows; amount-based routing rules; delegation of powers.

## Architecture (as built)
- **Tables:** `Requests` (`FormType`, subject, category, amount, required-by, details, submitter, reviewer, approver, state, `AssignedToUserId`, `RowVersion`), `ApprovalActions` (append-only by trigger), `Attachments` (no delete by trigger).
- **States:** Draft → Submitted (with reviewer) → Under Review (with approver) → Approved / Returned / Rejected → Closed. `RequestWorkflow.AllowedActions` decides by state, role and assignment; `Execute` updates with `WHERE RowVersion = @expected AND State = @from`, appends the action and writes notifications in the same transaction; audit after commit.
- **Routing rules:** submitter chooses reviewer and approver from active users with the Approver role; neither may be the submitter; they must differ (`ValidateRouting`).
- **Numbering:** `NABCONS/REQ/<calendar year>/<id:D5>` assigned at creation.
- **Pages:** `Pages/Requests/Index` (mine), `Edit`, `Details`, `Inbox` (Approver policy), `All` (administrators and auditors), `Attachment`.

## Alternatives Considered
- **Generic workflow engine:** rejected for one form type (YAGNI); `FormType` column leaves room for more forms.

## Risks & Mitigations
| Risk | Mitigation |
|---|---|
| Two approvers act on stale pages | `RowVersion` + state check; second action refused |
| Self-approval | Routing validation refuses the submitter as reviewer/approver |

## Tests
`NotificationTests` (workflow notifications); e2e `core.e2e.js` (submission, sequence, approval, return, rejection, concurrency).
