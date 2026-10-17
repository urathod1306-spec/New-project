# NABCONS Intranet — Architecture & Security Note for NABARD IT

*One-page design-clearance note (step 1 of the concept-to-demo path). Items marked **Confirm** need NABARD IT's decision; they are not assumed policy.*

## Summary

A small, purpose-built intranet: **one ASP.NET Core 10 application**, **one SQLite database file** and **one protected document folder**, running on a single approved Windows PC or server inside the NABARD LAN. It has no runtime dependency on the internet: no CDN, cloud API, AI service, online licence check, telemetry, container registry, package manager or Microsoft 365. All fonts, icons, styles and scripts are bundled locally. AI tools were used only to write code on a development machine outside the LAN, using synthetic data.

```
 Staff browsers (Edge/Chrome) ──HTTP(S) on LAN──►  NabconsIntranet.exe  (Kestrel web server, or IIS)
                                                   ├── data\intranet.db        SQLite (WAL), restricted NTFS
                                                   ├── documents\              uploaded files, outside web root
                                                   ├── logs\                   daily log files
                                                   └── backups\  ──copy──►     approved LAN backup share
 Optional: Windows Authentication (Kerberos/NTLM) against NABARD Active Directory
```

## Functional scope (MVP)

Branded home page and notice board · staff directory with controlled CSV import · circular/document repository with metadata search, categories, visibility rules, versions, supersession and withdrawal · one configurable two-level approval form with inbox and history · role-based authorisation (Staff, Publisher, Approver, Administrator, Auditor) · append-only audit log · backup and restore · administration pages.
**Excluded:** chatbot/RAG, OCR, mobile app, e-mail/SMS, e-signatures, HRMS/ECM/finance integration, workflow designer, external access, BI, Docker/Kubernetes, multiple databases.

## Validation checklist

| Area | Implemented control | Confirm with IT |
|---|---|---|
| Server | Runs on Windows 10/11 or Server 2016+; self-contained, no runtime install; can run as a Windows service or under IIS | **Confirm:** dedicated VM, Windows Server or managed desktop for the pilot; owner and support |
| Network | Listens on one configurable port (default TCP 8080); firewall helper opens it for Domain/Private profiles only; host-name binding supported | **Confirm:** host name, static IP, VLAN, DNS entry, port (preferably 443) and permitted user segments |
| Authentication | *Local mode:* PBKDF2-SHA256 (210,000 iterations) salted hashes, 10-character complexity policy, lockout after 5 failures for 15 minutes, forced change of temporary passwords, administrator resets, 30-minute idle timeout. *Windows mode:* Negotiate (Kerberos/NTLM); roles from AD groups or the intranet user record; no second credential store | **Confirm:** AD group access, Windows Authentication, Kerberos SPN and service account |
| TLS | HTTPS with an internal-CA certificate configured in `appsettings.json` (Deployment Guide section 6); cookies are Secure over HTTPS | **Confirm:** internal certificate authority and certificate-renewal owner |
| Session/web | HttpOnly, SameSite=Strict cookies; anti-forgery tokens on every form; strict Content-Security-Policy (`default-src 'self'`, no inline script or style); X-Frame-Options DENY; nosniff; no-referrer; no Server header; fallback policy denies anonymous access to every page except sign-in | — |
| File access | Documents stored outside the web root under generated names (`<guid>.dat`), served only through authorisation-checked download pages; directory browsing impossible; path traversal blocked | Dedicated service/app-pool identity; least-privilege NTFS: Modify only on `data`, `documents`, `logs`, `backups` |
| Uploads | Extension allow-list; file-signature (magic-byte) validation; size limit (20 MB default); renamed to generated IDs; SHA-256 recorded; content type set by the server, never by the browser; always sent as an attachment with `nosniff` | **Confirm:** antivirus real-time scanning of the `documents` folder |
| Database | SQLite file in `data\`; parameterised SQL only; foreign keys enforced; approval history and audit log protected by database triggers against update and delete | **Confirm:** migration threshold to SQL Server (e.g. >200 concurrent users or multi-server) |
| Audit | Logs login success, failure and lockout, logout, upload, rejected upload, publish, withdrawal, supersession, download, request creation, submission, assignment, recommendation, approval, return, rejection and closure, role change, user creation and deactivation, password reset, master-data change, staff import and edit, backup and restore. Hash-chained (SHA-256) and verifiable in the application or from the command line; CSV export | — |
| Backup | Daily scheduled ZIP (database copy via `VACUUM INTO`, documents, configuration, audit export) with SHA-256 checksum; retention; optional copy to a LAN share; checksum-verified restore that moves current data aside | **Confirm:** frequency, retention, encryption at rest (e.g. BitLocker on the target share), off-machine destination, restore-testing owner |
| Patching | .NET runtime is bundled. Patch by replacing the application folder with a rebuilt package (Deployment Guide section 9); previous folder kept for rollback | **Confirm:** Windows/.NET/library patch cycle and rollback package |
| Allow-listing | Binaries in the package: `NabconsIntranet.exe`, `e_sqlite3.dll`, `libSkiaSharp.dll` (photos, PDF), `libHarfBuzzSharp.dll` (PDF text shaping, v1.3) and `aspnetcorev2_inprocess.dll` (IIS module, used only under IIS); `SHA256SUMS.txt` lists every file; nothing is extracted to `%TEMP%` | **Confirm:** executable/DLL/script approval (AppLocker/WDAC) and any antivirus exclusions |
| Transfers | Single ZIP with published SHA-256; built reproducibly from pinned packages (`docs/SBOM.md`) | Approved media or transfer gateway; malware scan, checksum, dependency manifest and change ticket |
| Vulnerability review | Structured test script in `docs/TEST-SCRIPT.md` (unauthorised access, role segregation, URL guessing, invalid uploads, concurrency, restore) | Threat model, dependency scan, authenticated testing, upload testing and remediation sign-off |
| Photos (v1.2) | JPEG/PNG/WebP ≤ 2 MB, verified by decoding (not extension/MIME); re-encoded 400×400 JPEG with all EXIF/GPS removed; stored in `data\uploads\photos` named by user ID; served only to signed-in users | **Confirm:** staff-photo consent basis |
| Business leads (v1.2) | Permissions enforced on every page, download, export and JSON endpoint; BDU = approved vertical, COO/MD = LeadViewer role; append-only timeline; attachments signature-checked, ≤ 10 MB each / 25 MB per lead, random stored names | **Confirm:** classification of client/tender information |
| Notifications & polling (v1.3) | In-app only; no e-mail/SMS/push and no outbound connection. Badges refresh with one small authenticated GET every 60 s (configurable, minimum 30) that returns `304 Not Modified` when nothing changed. The request pauses while the tab is hidden, backs off to 5 minutes after errors, and never runs twice at once. **No WebSockets, SignalR, Server-Sent Events or long-polling.** The poll is excluded from sliding expiration, so it never extends the 30-minute idle timeout, and returns 401 (not a redirect) once a session ends. Notifications are readable and markable only by their owner; links are restricted to internal paths (no open redirect). The in-memory caches assume a single server process (the exe, the Windows service, or IIS with its default single worker) | — |
| e-Note (v1.3) | Server-side authorisation on every page, download, ZIP, PDF and JSON endpoint. Unauthorised requests get "not found", so guessed numbers reveal nothing. Content is visible to the initiator and participants; approved notes to all staff unless confidential. Administrators and Auditors see metadata only. Comments, movements, text versions and submitted attachments are append-only (database triggers). Optimistic concurrency. Every action is audited (`ENote.*`; views sampled daily). Attachments use the same validator as circulars (extension allow-list, magic bytes, size/count/quota limits, random stored names, sent as attachments with `nosniff`); a file is written first and recorded in a short transaction, and deleted again if that fails | **Confirm:** classification of note content; that disciplinary/vigilance/complaint matters stay off-system |
| Archival PDF (v1.3) | Generated on the server, offline, with SkiaSharp + HarfBuzz (MIT). Noto fonts (SIL OFL 1.1) are embedded in the program and in each PDF, so no installed fonts are used. Footer: "not digitally signed", page x of y, note number and SHA-256 of the record. Stored once per final state; at most 2 generated at a time | **Confirm:** whether a digitally signed PDF is needed later |
| Binaries (v1.3) | Adds `libHarfBuzzSharp.dll` next to the exe (listed in `SHA256SUMS.txt`) | **Confirm:** allow-listing of the new DLL |
| Privacy | Directory limited to official fields; mobile shown only when disclosure is approved; no HR-sensitive data; demo data is synthetic | Data minimisation, staff-photo/contact consent basis, access rules and retention |
| Operations | — | Named business owner, technical custodian, publisher, backup operator and incident contact |

## Runtime network behaviour

The application **makes no outbound connections**. It only accepts inbound HTTP(S) connections on the configured port. In Windows mode it talks to the domain controller through the operating system's standard Kerberos/NTLM machinery. A copy to `SecondaryBackupFolder`, when configured, uses the standard Windows file-share (SMB) access of the service account. IT can verify this with a 30-day zero-egress observation (outbound firewall deny-all for the process).
