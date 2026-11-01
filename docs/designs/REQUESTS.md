# General Administrative Approval Request

## Problem Statement
Routine administrative approvals (procurement below delegated limits, tours, nominations, IT equipment) move on paper with no record of who decided what and when.

## Goals & Non-Goals
**Goals:** one form type with two approval levels (Reviewing Officer, then Sanctioning Authority); permanent step history; attachments; inbox for approvers; protection against two people acting at once; notifications to the next actor and the submitter.
**Non-Goals:** configurable multi-level workflows; amount-based routing rules; delegation of powers.

## Architecture (as built)
- **Tables:** `Requests` (`FormType`, subject, category, amount, required-by, details, submitter, reviewer, approver, state, `AssignedToUserId`, `RowVersion`), `ApprovalActions` (append-only by trigger), `Attachments` (no delete by trigger).
- **States:** Draft → Submitted (with reviewer) → Under Review (with approver) → Approved / Returned / Rejected → Closed. `RequestWorkflow.AllowedActions` decides by state, role and assignment; `Execute` updates with `WHERE RowVersion = @expected AND State = @from`, appends the action and writes notifications in the same transaction; audit after commit.
- **Transitions:**

  | From | Action | Who | To |
  |---|---|---|---|
  | Draft or Returned | Submit | Submitter | Submitted (assigned to reviewer) |
  | Submitted | Recommend | Reviewing Officer | Under Review (assigned to approver) |
  | Submitted | Return / Reject (remarks required) | Reviewing Officer | Returned (to submitter) / Rejected |
  | Under Review | Approve / Return / Reject | Sanctioning Authority | Approved / Returned / Rejected |
  | Approved or Rejected | Close | Submitter or an administrator | Closed |

  A returned request is edited by the submitter and submitted again, starting at the reviewer. Drafts cannot be deleted. They keep their number, so numbers can have gaps; this is accepted, because the number is a reference, not a register serial.
- **Routing rules:** submitter chooses reviewer and approver from active users with the Approver role; neither may be the submitter; they must differ (`ValidateRouting`).
- **Numbering:** `NABCONS/REQ/<calendar year of the office date (IST)>/<id:D5>` assigned at creation (UTC before v1.5.0, so a request made just after midnight on 1 January could carry the old year).
- **Pages:** `Pages/Requests/Index` (mine), `Edit`, `Details`, `Inbox` (Approver policy), `All` (administrators and auditors), `Attachment`.

### Behaviour details (confirmed in the v1.5.0 review)
- Only Approved or Rejected requests can be closed. A Draft or Returned request can be neither closed nor withdrawn; it stays with the submitter. After resubmission the request goes through the full review again, starting at the reviewer.
- A Draft is visible only to its submitter, administrators and auditors; the chosen reviewer and approver see it once submitted (v1.5.0).
- If the assigned officer loses the Approver role or is deactivated, the request waits; there is no reassignment action yet. Work-around: the administrator restores the role temporarily, or the officer returns the request so the submitter can choose another. (Deferred: see the consolidated review report, M11.)
- The category must be one from the master list (an existing request keeps its old category if it was later removed from the list). Amounts are stored as SQLite REAL; they are shown rounded to rupees and are for reference only.
- Files saved for a request that then fails to save are deleted again (v1.5.0).

## Alternatives Considered
- **Generic workflow engine:** rejected for one form type (YAGNI); `FormType` column leaves room for more forms.

## Risks & Mitigations
| Risk | Mitigation |
|---|---|
| Two approvers act on stale pages | `RowVersion` + state check; second action refused |
| Self-approval | Routing validation refuses the submitter as reviewer/approver |

## Tests
`NotificationTests` (workflow notifications); e2e `core.e2e.js` (submission, sequence, approval, return, rejection, concurrency).
