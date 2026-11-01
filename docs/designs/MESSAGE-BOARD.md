# Message Board

## Problem Statement
Staff had no shared place to post news, greetings and photos; the notice board is for official notices by Publishers only.

## Goals & Non-Goals
**Goals:** every signed-in user posts a message with up to 4 photos; separate tab; administrators pin up to 3 posts and remove posts; authors remove their own; home-page card; "new since last visit" badge.
**Non-Goals:** comments, likes, editing, private messages, video.

## Architecture (as built)
- **Tables:** `BoardPosts` (author snapshot, body, pinned, removed fields; delete blocked by trigger), `BoardPhotos`; `Users.BoardSeenAt`.
- **Repository:** `BoardRepository` (`Data/Board.cs`): `Create` validates text (≤ `MaxChars`), photo count, flood guard (`PostsPerHour` per user, in memory); photos decoded and re-encoded (1,600 px and 480 px) under `data/uploads/board/<year>/<guid>`; rows written in one transaction; files removed if anything fails. `Delete` hides the post and deletes its photo files. `SetPinned` enforces `MaxPinned` inside a `BEGIN IMMEDIATE` transaction.
- **Endpoints/pages:** `Pages/Board/Index` (composer, pinned, latest, pager), `_Post` partial (also on the home page), `GET /board/photo/{id}` for signed-in users, never for removed posts. Badge count in `DeskService` (`BoardNew`).

## Alternatives Considered
- **Reuse the notice board:** rejected — different audience (all staff vs Publishers), photos, pinning and moderation.

## Risks & Mitigations
| Risk | Mitigation |
|---|---|
| Inappropriate content | Administrators remove posts; removal audited with reason; acceptable-use text on the page |
| Photo metadata (GPS) | Re-encoding removes all metadata |
| Disk growth | 4 photos × ≤ 1,600 px per post; files deleted on removal |

## Tests
`BoardTests`, `BoardDisabledTests`; e2e `board.e2e.js`.
