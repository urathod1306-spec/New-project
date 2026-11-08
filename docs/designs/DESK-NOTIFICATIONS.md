# My Desk and In-App Notifications

## Problem Statement
Work items are spread across modules; staff miss what is waiting for them, and there is no e-mail on the isolated LAN.

## Goals & Non-Goals
**Goals:** in-app notifications written in the same transaction as the action; never notify the actor; bell with unread count and latest 10; My Desk page with one section per kind of pending work; live badges without WebSockets; idle timeout unaffected by polling.
**Non-Goals:** e-mail/SMS/push; real-time chat; per-user notification preferences.

## Architecture (as built)
- **Table:** `Notifications` (user, type `Module.Event`, title, message, internal link, entity, actor, created, read). Indexes on `(UserId, ReadAt, CreatedAt DESC)`, `(UserId, CreatedAt DESC)`, `(CreatedAt)`.
- **Service:** `INotificationService` (`Infrastructure/Notifications.cs`): `Notify`, `NotifyAdministrators`, `NotifyVertical` take the caller's connection and transaction, skip the actor and inactive users, validate links (`IsSafeLink`: internal paths only). Owner-only list, open (marks read) and mark-all-read.
- **Change tracking:** `ChangeTracker` keeps in-memory sequence numbers per user and per shared queue (admin, leads, board). e-Note changes refresh only the people involved (holder, sender, initiator, participants) since v1.5.0; the former shared e-Note marker refreshed everyone. `DeskService.Counts` caches each user's counts and re-reads them when a relevant marker changes (or after 10 minutes); counts read within 1 s of a change are re-read on the next poll.
- **Poll:** `GET /api/notifications/summary` returns counts with an ETag; `304` when unchanged. `site.js` polls every `Notifications:PollSeconds` (60, minimum 30), pauses while the tab is hidden, backs off to 5 minutes after errors, one request at a time. The cookie handler excludes this path from sliding expiration and answers `/api/*` with 401 instead of a redirect.
- **My Desk** (`Pages/Desk`): sections by role — e-Notes pending, my e-Notes, requests awaiting action, returned requests, admin approvals, BDU leads, due leads, recent notifications; each one indexed query, 10 items (recent notifications: 5).
- **Events and recipients** (the actor is never notified):

  | Type | Recipient | My Desk section |
  |---|---|---|
  | Request.Assigned | Reviewer on submit; approver on recommend | Requests awaiting my action |
  | Request.Recommended / Approved / Returned / Rejected | Submitter | My requests returned to me (Returned) |
  | Admin.RegistrationPending / Admin.VerticalChangePending | All active administrators | Self-registrations / vertical changes to approve |
  | Profile.VerticalChangeApproved / Rejected | Requester | — |
  | Lead.Submitted | Active members of the BDU vertical | Leads in "Submitted" |
  | Lead.StatusChanged / OwnerAssigned | Submitter | — |
  | Lead.AssignedToYou | New owner | My assigned leads due within 7 days (by lead due date) |
  | ENote.Received / SentBack / Returned / Approved / Rejected / Withdrawn / Commented / AttachmentAdded / Ageing | See ENOTE.md | e-Notes pending with me; My e-Notes |
  | Message board | No notifications (badge only) | — |

- **Housekeeping:** notifications that have been read are deleted when they were *created* more than 90 days ago, unread ones after 365 days, in batches of 5,000.

### Behaviour details (confirmed in the v1.5.0 review)
- The poll backs off to 5 minutes after 3 errors and stops on 401/403; a redirect (for example to Change Password) counts as an error. When the bell menu is opened after the session has ended, the page goes to the sign-in page instead of showing it inside the menu (v1.5.0).
- Sliding expiration and the `/api` 401 answer apply to local accounts; Windows sign-in has no idle time-out.
- The ETag combines a per-start value, the user and every count; role or vertical changes are picked up because the cached entry also records them.
- Markers are bumped as part of each write, just before its commit; a count read within 1 second of a marker change is read again on the next poll, which covers that gap.
- The e-Note badge counts notes held by the user that are in movement or returned; the My Desk sections split these by role (pending for me / returned to me as initiator), each showing up to 10.

### Compliance (v1.6, DD-07 §8)
- Two more counts in the single count query: `ComplianceMine` (own action points overdue, due soon, returned or with an update due) and `ComplianceCell` (for Compliance Admins: pending updates, revision requests and closure proposals, excluding their own points). Both are part of *My Desk (n)* and of the poll's `compliance` value, which drives the Compliance tab badge. The Cell queue uses the change marker `t:compliance`; the user's compliance grant is part of the cache key.
- Daily compliance reminders run from their own scheduler (not the backup job): one aggregated notification per officer per day, plus a digest to the Cell.

## Alternatives Considered
- **WebSockets / SignalR / Server-Sent Events:** rejected by the owner's network rules; a 60-second poll costs ~1 ms: the counts come from memory; with local accounts the sign-in cookie is still checked against the user record (one primary-key lookup) on each poll.
- **E-mail:** no permitted mail relay; would add an outbound connection.

## Risks & Mitigations
| Risk | Mitigation |
|---|---|
| Polling keeps sessions alive | Excluded from sliding expiration (tested with a 1-minute session) |
| Open redirect through notification links | Links validated on write and on open |
| Cache shows stale counts | Invalidation on writes; 1-second re-read rule; 10-minute maximum age |

## Tests
`NotificationTests`, `PollSessionTests`, `PerformanceTests` (opt-in); e2e `desk-notifications.e2e.js`.
