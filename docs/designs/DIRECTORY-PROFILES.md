# Staff Directory, Profiles, Photos and Self-Registration

## Problem Statement
Staff need to find colleagues and their official contact details, keep their own details current without an administrator, and new staff need a way to get an account without paperwork.

## Goals & Non-Goals
**Goals:** searchable directory of official fields; contact card with vCard; self-service contact edits with an audit trail; staff photos with all metadata removed; self-registration by 10-digit mobile number, approved by an administrator, which also fills the directory.
**Non-Goals:** HR records (date of birth, salary, appraisal); organisation chart; editing name, code or designation by the user.

## Architecture (as built)
- **Directory:** table `Staff` (official fields; `ShowMobile` controls mobile disclosure; `ActiveTo` retires a person). `StaffRepository` (`Data/DirectoryRepositories.cs`): search by name/code/designation/e-mail/extension with vertical and location filters, paged; `GetForCard`. Administrators edit entries and import CSV with a preview (`Pages/Admin/Staff*`).
- **Contact card:** `GET /directory/card/{staffId}` returns JSON (mobile only if `ShowMobile`); `GET /directory/vcard/{staffId}` returns a vCard 3.0 (`Infrastructure/Endpoints.cs`). The directory page opens the card in a `<dialog>` with keyboard focus handling (`site.js`).
- **Account ↔ directory link:** `Users.StaffId`. Linked automatically at start-up only when unambiguous (registration record, mobile = user name, or unique official e-mail) by `ProfileRepository.LinkUnlinkedAccounts`; never by name; never re-linked. Administrators link manually (`Pages/Admin/UserEdit`).
- **My Profile** (`Pages/Profile/Index`, `ProfileRepository`): edits mobile, extension, official e-mail and location of the linked entry, validated (mobile 10 digits starting 6–9 and unique; optional official e-mail domain). Each changed field is written to `ProfileAudit` (append-only) and the audit log. Users who sign in with their mobile number have their user name changed with it. Vertical changes create a `ProfileChangeRequests` row for administrator approval (setting `VerticalChangeRequiresApproval`), because vertical controls access to Business Leads. Preference "Open My Desk after sign-in" (`Users.OpenDeskAfterSignIn`).
- **Photos** (`Infrastructure/PhotoService.cs`): JPEG/PNG/WebP up to `MaxPhotoMB` (2), decoded (real image check, ≤ 40 MP), oriented from EXIF, centre-cropped, re-encoded to 400 px and 96 px JPEG without metadata; stored as `data/uploads/photos/<userId>.jpg`; served by `GET /photo/{userId}` to signed-in users with a version number in the URL for caching (`Users.PhotoVersion`).
- **Self-registration** (`Pages/Account/Register`, `RegistrationRepository`): anonymous, rate-limited per IP; looks up the mobile in the directory and pre-fills (e-mail masked); creates a `RegistrationRequests` row with a password hash; administrators are notified; on approval an account (user name = mobile) is created and the directory entry is updated or added (provisional code if none), then linked.

## Alternatives Considered
- **Name-based auto-linking:** rejected — common names collide; a wrong link would expose or let someone edit another person's details.
- **Storing original photos:** rejected — original files carry GPS/EXIF; re-encoding removes them and bounds size.
- **Self-registration without approval:** rejected — anyone on the LAN could create an account.

## Risks & Mitigations
| Risk | Mitigation |
|---|---|
| Mobile numbers are personal data | Shown only where `ShowMobile`; masked e-mail during registration; registration lookups rate-limited |
| Wrong link between account and person | No name matching; administrator sees link status and can change it |
| Malicious image files | Decoded by SkiaSharp with pixel limits; only re-encoded output is stored/served |

## Tests
`ProfileTests`, `PhotoTests`, `ExistingBehaviourTests`; e2e `registration*.e2e.js`, `profile-photos-card.e2e.js`.
