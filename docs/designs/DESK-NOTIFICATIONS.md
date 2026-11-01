# My Desk and In-App Notifications

## Problem Statement
Work items are spread across modules; staff miss what is waiting for them, and there is no e-mail on the isolated LAN.

## Goals & Non-Goals
**Goals:** in-app notifications written in the same transaction as the action; never notify the actor; bell with unread count and latest 10; My Desk page with one section per kind of pending work; live badges without WebSockets; idle timeout unaffected by polling.
**Non-Goals:** e-mail/SMS/push; real-time chat; per-user notification preferences.

## Architecture (as built)
- **Table:** `Notifications` (user, type `Module.Event`, title, message, internal link, entity, actor, created, read). Indexes on `(UserId, ReadAt, CreatedAt DESC)`, `(UserId, CreatedAt DESC)`, `(CreatedAt)`.
- **Service:** `INotificationService` (`Infrastructure/Notifications.cs`): `Notify`, `NotifyAdministrators`, `NotifyVertical` take the caller's connection and transaction, skip the actor and inactive users, validate links (`IsSafeLink`: internal paths only). Owner-only list, open (marks read) and mark-all-read.
- **Change tracking:** `ChangeTracker` keeps in-memory sequence numbers per user and per shared queue (admin, leads, e-notes, board). `DeskService.Counts` caches each user's counts and re-reads them when a relevant marker changes (or after 10 minutes); counts read within 1 s of a change are re-read on the next poll.
- **Poll:** `GET /api/notifications/summary` returns counts with an ETag; `304` when unchanged. `site.js` polls every `Notifications:PollSeconds` (60, minimum 30), pauses while the tab is hidden, backs off to 5 minutes after errors, one request at a time. The cookie handler excludes this path from sliding expiration and answers `/api/*` with 401 instead of a redirect.
- **My Desk** (`Pages/Desk`): sections by role — e-Notes pending, my e-Notes, requests awaiting action, returned requests, admin approvals, BDU leads, due leads, recent notifications; each one indexed query, 10 items.
- **Housekeeping:** read notifications deleted after 90 days, unread after 365, in batches of 5,000.

## Alternatives Considered
- **WebSockets / SignalR / Server-Sent Events:** rejected by the owner's network rules; a 60-second poll answered from memory costs ~1 ms.
- **E-mail:** no permitted mail relay; would add an outbound connection.

## Risks & Mitigations
| Risk | Mitigation |
|---|---|
| Polling keeps sessions alive | Excluded from sliding expiration (tested with a 1-minute session) |
| Open redirect through notification links | Links validated on write and on open |
| Cache shows stale counts | Invalidation on writes; 1-second re-read rule; 10-minute maximum age |

## Tests
`NotificationTests`, `PollSessionTests`, `PerformanceTests` (opt-in); e2e `desk-notifications.e2e.js`.
