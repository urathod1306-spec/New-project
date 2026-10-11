# NABCONS Intranet — Structured Test Script

Run on the isolated test PC with the demo data (password for all demo accounts: `Nabcons@2026`). Record Pass/Fail, the tester and the date. Items marked ✔ were also verified by the automated browser test run during development.

| # | Area | Steps | Expected result | Result |
|---|---|---|---|---|
| 1 | Unauthorised access | Open `http://<host>:8080/Circulars` in a private window without signing in | Redirected to sign-in; no content shown ✔ | |
| 2 | Role segregation | Sign in as `staff`; open `/Admin`, `/Audit`, `/Circulars/Edit`, `/Notices` | "Access denied" for each ✔ | |
| 3 | Role segregation | Sign in as `auditor`; try `/Circulars/Edit` and any Admin page | Denied; audit and all requests are viewable read-only ✔ | |
| 4 | Direct URL guessing | As `publisher`, create a draft circular and note its download link `/Circulars/Download/<n>`. Sign in as `staff` and open that link, and the details page | "Page not found" (404). Drafts and restricted files cannot be fetched by URL ✔ | |
| 5 | Direct URL guessing | As `staff`, open `/Requests/Details/1`, `/Requests/Attachment/1` for a request that is not theirs (create one as another user first) | 404 | |
| 6 | Visibility | As `staff` (Rural Infrastructure) search "approval matrix"; as `approver1` (HR) search the same | Staff sees the vertical-only circular; approver1 does not ✔ | |
| 7 | Invalid files | As `publisher`, upload a `.exe`; a text file renamed to `.pdf` | Both rejected with a clear message; `Upload.Rejected` in audit ✔ | |
| 8 | Oversized uploads | Upload a file larger than 20 MB | Rejected (browser warns; server refuses) | |
| 9 | Duplicate circular numbers | Create a circular with number `NABCONS/HR/<year>/01` | "already exists" ✔ | |
| 10 | Malformed search | Search for `"*)(OR AND NEAR( ' ; DROP TABLE` in circulars and directory | Page loads normally, no error ✔ | |
| 11 | Submission | As `staff`, New request → fill in → choose Rajesh Iyer / Anita Deshmukh → attach a PDF → Save & submit | Status *Submitted*, with approver1 ✔ | |
| 12 | Sequence | As `approver2`, open the request before approver1 acts | No action buttons ✔ | |
| 13 | Approval | As `approver1` *Recommend*; as `approver2` *Approve*; as `staff` *Close* | States change in order; history shows each step with role, time and remarks ✔ | |
| 14 | Rejection / return | Repeat with *Return* (remarks required), resubmit, then *Reject* | Remarks enforced; history preserved ✔ | |
| 15 | Concurrent approvals | As `approver1`, open the same submitted request in two tabs; act in tab 1, then in tab 2 | Tab 2 is refused: "updated by someone else" ✔ | |
| 16 | Self-approval | As `approver1`, raise a request and try to choose yourself | Not offered and refused by the server | |
| 17 | Withdrawn / superseded | Open `NABCONS/HR/<year>/01` and `NABCONS/ADM/<year>/03` | Superseded banner linking to HR/04; withdrawn banner with reason. Hidden from staff search unless *Include withdrawn* is ticked | |
| 18 | Audit completeness | As `auditor`, filter the audit log for each action above | Each action is present with user, time and IP | |
| 19 | Audit integrity | *Audit → Verify integrity*; run `Verify-Audit-Log.bat` | "intact" ✔ | |
| 20 | Lockout | Enter a wrong password 5 times for `auditor` | Account locked for 15 minutes; `Login.LockedOut` in audit; admin can unlock ✔ | |
| 21 | Backup | *Admin → Backup → Create backup now*; *Verify* | ZIP and checksum created; verify OK ✔ | |
| 22 | Full restore | Copy the application folder and a backup to another PC → `Restore-Backup.bat` → start → sign in | All data, documents and history present; integrity check passes ✔ (command-line restore tested) | |
| 23 | CSV import | Import `sample-data\staff-import-sample.csv` | Preview shows 2 new, 1 update; confirm applies them; audit entry written | |
| 24 | Edge/Chrome rendering | Check the home page, directory, circular, request and admin pages in Edge and Chrome, at desktop and phone width | Layout correct; no horizontal scrolling on a phone ✔ | |
| 25 | Restart | Close the console window and start again; reboot the PC with the service installed | Data intact; service starts automatically after reboot | |
| 26 | No egress | With an outbound "block all" firewall rule for `NabconsIntranet.exe`, use every page | Everything works; no blocked-connection events | |
