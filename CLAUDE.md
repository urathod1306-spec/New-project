# NABCONS Intranet — notes for future changes

ASP.NET Core 10 Razor Pages + SQLite intranet, shipped as a self-contained Windows package (`build/package.sh` → `release/*.zip`). It runs on an isolated LAN: no runtime internet, no CDNs, everything in `wwwroot/`, strict CSP (no inline scripts or styles; use classes in `wwwroot/css/site.css` and code in `wwwroot/js/site.js`).

## Change process (requested by the owner)

- Make every change on a **new branch** named `claude/v<next-version>-<topic>` created from `main`.
- **Never merge into `main` without the owner's explicit confirmation** in the conversation. Push the branch, report what changed, then wait.
- Bump `<Version>` in `src/NabconsIntranet/NabconsIntranet.csproj` (patch for fixes and small changes, minor for features). Rebuild the package and remove the previous ZIP from `release/`. Update version references in `README.md`, `docs/DEPLOYMENT-GUIDE.md` and `docs/SBOM.md`.
- Default port is **8080**, kept as the owner requested.

## Live data must survive every upgrade

The owner has a live installation with real users and circulars. Upgrades replace program files only (`deploy/windows/Upgrade-Existing-Intranet.bat`; see `docs/UPGRADE-GUIDE.md`).

- **Database changes go only in `Schema.Migrations`** (`src/NabconsIntranet/Infrastructure/Schema.cs`), as a new entry with the next version number. Additive only: `CREATE TABLE IF NOT EXISTS`, `ALTER TABLE … ADD COLUMN … DEFAULT …`, new indexes. Never drop, rename or rewrite tables or columns, and never edit a released migration. Older program versions must still work on the upgraded database (roll-back).
- Do not change `Schema.Sql` in ways that alter existing tables; it is the version-1 baseline.
- New settings must have safe defaults in `IntranetOptions`: the owner's existing `appsettings.json` is kept and does not get new keys.
- Never change seeding so that it can touch a non-empty database.
- Approval history, attachments and the audit log are append-only (database triggers); keep it that way.

## Testing before pushing

- `dotnet build src/NabconsIntranet`; run the published app and exercise the changed pages in a headless browser (Playwright is available).
- For any migration: start the new build on a copy of a database created by the previous version and check that row counts are unchanged, the migration is applied once, and `--verify-audit` passes.
