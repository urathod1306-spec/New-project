# Goldfish Review — Codebase Details: Desk/Notifications, e-Note, Message Board (2026-11-01)

Reviewer: Codebase Details Goldfish (fresh context, read-only). Totals: 0 Blockers, 4 Majors, 19 Nits.

Verdict (reviewer):
- **Accuracy:** the docs mostly match the code; the drift is in details.
- **Fix first:**
  - the ungated `/api/users/lookup` while e-Note is off;
  - the board catch that deletes photos of saved posts;
  - the bell injecting the sign-in page.
- **Cache invalidation** is sound in practice, but it depends on marker bumps made before commit.
- **Checked and correct:**
  - notification ownership checks;
  - `IsSafeLink` on write and on open;
  - LocalRedirect;
  - the bell partial is HTML-encoded;
  - no `Html.Raw`, inline style or inline script;
  - `CanView` on the attachment, ZIP, print and PDF routes;
  - the register is limited to Administrators and Auditors;
  - the board switch covers every board path;
  - triggers as documented;
  - indexes match;
  - no connection leak in `Person(..., dispose: true)`.

| # | Sev | Location | Finding | Suggested fix |
|---|---|---|---|---|
| D1 | Major | Endpoints `/api/users/lookup` | Only e-Note endpoint without the `ENote:Enabled` check; on the live server any user can search all active users | Gate it; test |
| D2 | Major | Board.Create catch | The cleanup catch also wraps the tracker touch and audit after commit; an audit failure deletes the photos of a saved post | Clean up only if not committed |
| D3 | Major | site.js bell `load()` | `/Notifications?handler=Menu` redirects to sign-in when the session has ended; fetch follows it and the sign-in page is injected into the drop-down | Detect the redirect; go to sign-in |
| D4 | Major | DESK-NOTIFICATIONS "answered from memory" | Local mode: every poll still runs `ValidateCookie` → one Users primary-key lookup | Correct the doc |
| D5 | Nit | Notifications.Notify | Markers bumped before commit; correctness relies on the commit landing within 1 s (several workflows bump nothing after commit; AgeingAlerts bumps 200 users then commits) | Bump after commit, or document |
| D6 | Nit | Housekeeping | Deleting unread notifications does not bump the tracker | Bump |
| D7 | Nit | Housekeeping vs doc | Read notifications selected by CreatedAt, not ReadAt | Correct the doc |
| D8 | Nit | Desk Recent | Shows 5, doc says 10 | Correct the doc |
| D9 | Nit | Desk e-Note section badge | Nav badge counts InMovement + Returned; the section lists InMovement (not initiator) and shows the list count capped, "10+" at exactly 10 | Real counts; document |
| D10 | Nit | site.js poll | Back-off after 3 errors; stops on 401/403; a 302 to change-password counts as an error — undocumented | Document |
| D11 | Nit | Program cookie events | Sliding-expiration exclusion and the /api 401 apply to local accounts only (Windows has no idle timeout) — undocumented | Document |
| D12 | Nit | Board.AllowPost | A rejected post still uses a quota slot | Count only successful posts |
| D13 | Nit | /board/photo cache | `max-age=86400`: browsers keep a removed post's photo for up to 24 h | `no-cache`, or document |
| D14 | Nit | Board "new since last visit" | Rules undocumented (30 days, own posts excluded, any board page marks all seen, home card does not, MarkSeen after Load) | Document |
| D15 | Nit | Board.NewSince | Dead code duplicating the Desk query | Remove |
| D16 | Nit | Board.Create | A file written before size/hash computation is not in the cleanup list if that step throws | Record before computing |
| D17 | Nit | Board.SetPinned | IsDeleted read outside the transaction; UPDATE lacks `AND IsDeleted = 0` | Add the condition |
| D18 | Nit | ENoteWorkflow Supersede/RemoveDraftAttachment | Single statements without RowVersion; Supersede allowed for any viewer-uploader — undocumented | Document |
| D19 | Nit | ENOTE PDF | Who may download, regeneration per state, old PDF kept and excluded from orphan report — undocumented | Document |
| D20 | Nit | ZIP endpoint | `used` HashSet can never trigger; `WriteOnlyAsyncStream.Write` blocks per 64 KB | Remove the set; review the blocking |
| D21 | Nit | ENotes.FirstViewToday | Above 100,000 same-day entries every view scans the dictionary | Clear on day change |
| D22 | Nit | Index page | Loads every request the user has ever submitted to count open ones | COUNT query |
| D23 | Nit | Desk change tracking doc | ETag parts, RoleKey re-read, board/e-Note keys in every user's set — undocumented | Document |
