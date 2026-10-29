# NABCONS Intranet — Software Bill of Materials (v1.4.0)

All components are from Microsoft, the SQLitePCLRaw project (the SQLite build used by Microsoft.Data.Sqlite) or the SkiaSharp project (maintained under Microsoft/.NET Foundation, MIT). Versions are pinned in `src/NabconsIntranet/NabconsIntranet.csproj`. No JavaScript or CSS frameworks and no web fonts are used; the site's own stylesheet and script are in `wwwroot/`. **Nothing is downloaded at runtime**: every DLL ships inside the ZIP.

| Component | Version | Licence | Purpose |
|---|---|---|---|
| .NET runtime + ASP.NET Core runtime (bundled, self-contained win-x64) | 10.0.13 (LTS) | MIT | Application platform and web server (Kestrel) |
| Microsoft.AspNetCore.Authentication.Negotiate | 10.0.13 | MIT | Windows Authentication (Kerberos/NTLM). 10.0.13 fixes advisory GHSA-vrrp-6v32-2p8g present in 10.0.12 |
| Microsoft.Data.Sqlite / Microsoft.Data.Sqlite.Core | 10.0.13 | MIT | SQLite data access |
| SQLitePCLRaw.bundle_e_sqlite3, .core, .lib.e_sqlite3, .provider.e_sqlite3 | 2.1.13 | Apache-2.0 | SQLite engine (`e_sqlite3.dll`, includes FTS5 full-text search) |
| Microsoft.Extensions.Hosting.WindowsServices | 10.0.13 | MIT | Run as a Windows service |
| System.ServiceProcess.ServiceController | 10.0.13 | MIT | (dependency of the above) |
| System.DirectoryServices.Protocols | 10.0.13 | MIT | (dependency of Negotiate; LDAP group lookup on non-Windows only) |
| SkiaSharp + SkiaSharp.NativeAssets.Win32 | 4.153.1 | MIT | Staff photos (decode/verify, orient, crop, re-encode without metadata) and the e-Note archival PDF (`libSkiaSharp.dll`) |
| SkiaSharp.HarfBuzz | 4.153.1 | MIT | e-Note PDF: text shaping so Hindi (Devanagari) and English print correctly (v1.3) |
| HarfBuzzSharp + HarfBuzzSharp.NativeAssets.Win32 | 14.2.1.301 | MIT | The HarfBuzz shaping engine (`libHarfBuzzSharp.dll`) (v1.3) |
| Noto Sans, Noto Sans Devanagari (Regular, Bold) — font files | 2022 release, reduced with fontTools (Latin set; hinting removed) | SIL Open Font License 1.1 | Embedded in the program and in each e-Note PDF, so no fonts installed on the server are needed (`src/NabconsIntranet/Resources/Fonts`, licence text included) (v1.3) |

Test-only (not in the package): HarfBuzzSharp.NativeAssets.Linux 14.2.1.301 (MIT; lets the tests run on Linux), xunit 2.9.3, xunit.runner.visualstudio 3.1.4, Microsoft.NET.Test.Sdk 17.14.1, Microsoft.AspNetCore.Mvc.Testing 10.0.13, SkiaSharp.NativeAssets.Linux.NoDependencies 4.153.1 (lets the tests run on the Linux build machine); Playwright for browser tests; `tests/LoadTest` (own code, uses Microsoft.Data.Sqlite) for the load check. No JavaScript libraries were added: e-Note, My Desk and notifications use the existing `site.js`.

Build: `.NET SDK 10`, `dotnet publish -c Release -r win-x64` (self-contained, single-file, not trimmed). The exact runtime patch level is shown on *Administration → System information*. Rebuild and redeploy when Microsoft publishes a .NET 10 security update (normally on the second Tuesday of the month): bump `RuntimeFrameworkVersion` and the Microsoft package versions together.
