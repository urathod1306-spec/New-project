# Message Board

## Problem Statement
Staff had no shared place to post news, greetings and photos; the notice board is for official notices by Publishers only.

## Goals & Non-Goals
**Goals:** every signed-in user posts a message with up to 4 photos; separate tab; administrators pin up to 3 posts and remove posts; authors remove their own; home-page card; "new since last visit" badge.
**Non-Goals:** comments, likes, editing, private messages, video.

Terminology: **remove** = the post is hidden from everyone and its photo files are deleted; the database row stays (a trigger blocks deleting it) with who removed it, when and why. Removed posts are not shown in the application to anyone, including administrators and auditors; the audit log keeps the record.

## Architecture (as built)
- **Tables:** `BoardPosts` (author snapshot, body, pinned, removed fields; delete blocked by trigger), `BoardPhotos`; `Users.BoardSeenAt`.
- **Repository:** `BoardRepository` (`Data/Board.cs`): `Create` validates text (≤ `MaxChars`), photo count, flood guard (`PostsPerHour` per user, in memory); photos decoded and re-encoded (1,600 px and 480 px) under `data/uploads/board/<year>/<guid>`; rows written in one transaction; files removed if anything fails. `Delete` hides the post and deletes its photo files. `SetPinned` enforces `MaxPinned` inside a `BEGIN IMMEDIATE` transaction.
- **Endpoints/pages:** `Pages/Board/Index` (composer, pinned, latest, pager), `_Post` partial (also on the home page), `GET /board/photo/{id}` for signed-in users, never for removed posts. Badge count in `DeskService` (`BoardNew`).

### Behaviour details (confirmed in the v1.5.0 review)
- **Removal deletes the photo files** on purpose: a removed post is usually removed because a photo should not be shown; the post record, its text and the removal reason stay (append-only). This differs from other modules, which keep files.
- Photo addresses are re-checked on every view (`no-cache`), so a removed post's photos stop showing at once (v1.5.0; previously browsers could keep them for a day).
- The hourly limit counts only posts that were saved; a post refused for a bad photo does not use it up (v1.5.0). A photo that fails part-way is removed again.
- "New since last visit": posts by others in the last 30 days created after the person last opened any board page; the home-page card does not mark them seen.
- Two photo-size settings exist: `Intranet:MaxPhotoMB` (profile photos, 2 MB) and `MessageBoard:MaxPhotoMB` (board photos, 5 MB).

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
