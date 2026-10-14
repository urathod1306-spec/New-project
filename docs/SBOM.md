# NABCONS Intranet — Software Bill of Materials (v1.0.1)

All components are from Microsoft or the SQLitePCLRaw project (the SQLite build used by Microsoft.Data.Sqlite). Versions are pinned in `src/NabconsIntranet/NabconsIntranet.csproj`. No JavaScript or CSS frameworks are used. The site's own stylesheet and script are in `wwwroot/`.

| Component | Version | Licence | Purpose |
|---|---|---|---|
| .NET runtime + ASP.NET Core runtime (bundled, self-contained win-x64) | 10.0 (LTS) | MIT | Application platform and web server (Kestrel) |
| Microsoft.AspNetCore.Authentication.Negotiate | 10.0.12 | MIT | Windows Authentication (Kerberos/NTLM) |
| Microsoft.Data.Sqlite / Microsoft.Data.Sqlite.Core | 10.0.12 | MIT | SQLite data access |
| SQLitePCLRaw.bundle_e_sqlite3, .core, .lib.e_sqlite3, .provider.e_sqlite3 | 2.1.12 | Apache-2.0 | SQLite engine (`e_sqlite3.dll`, includes FTS5 full-text search) |
| Microsoft.Extensions.Hosting.WindowsServices | 10.0.12 | MIT | Run as a Windows service |
| System.ServiceProcess.ServiceController | 10.0.12 | MIT | (dependency of the above) |
| System.DirectoryServices.Protocols | 10.0.12 | MIT | (dependency of Negotiate; LDAP group lookup on non-Windows only) |

Build: `.NET SDK 10`, `dotnet publish -c Release -r win-x64` (self-contained, single-file, not trimmed). The exact runtime patch level is shown on *Administration → System information*. Rebuild and redeploy when Microsoft publishes a .NET 10 security update (normally on the second Tuesday of the month).
