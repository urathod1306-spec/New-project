# Goldfish Review — Codebase Details: Directory/Profiles, Circulars/Notices, Requests, Business Leads (2026-11-01)

Reviewer: Codebase Details Goldfish (fresh context, read-only).
Verdict (reviewer):
- **Docs mostly accurate**, with two hard contradictions: superseded circulars are not hidden from search, and Returned requests cannot be Closed.
- **Main code risks:**
  - registration approval is not atomic;
  - Submit bypasses the lookup throttle;
  - administrator vertical edits bypass ProfileAudit and the master list;
  - the duplicate-check endpoint leaks lead data to all staff;
  - request numbers use the UTC year.
- **Checked and fine:**
  - no inline style or script anywhere (CSP);
  - all Leads/Details POST handlers check access;
  - request handlers use `CanView` + `AllowedActions`;
  - profile handlers take the user id from the cookie only;
  - circular downloads re-check visibility;
  - the lead serial is safe under concurrency;
  - request Execute is guarded by RowVersion and state;
  - vCard and card respect `ShowMobile`.

| # | Sev | Location | Finding | Suggested fix |
|---|---|---|---|---|
| M1 | Blocker | CircularRepository.cs:47-52 vs CIRCULARS-NOTICES | Doc: superseded and withdrawn are hidden from staff search; code hides only withdrawn (also Latest/home, Search) | Decide; align code or doc |
| M2 | Blocker | RequestWorkflow.cs:54-70 vs REQUESTS | Close only from Approved/Rejected; Returned and Draft can never be closed/withdrawn; doc omits admin Close and full re-review after resubmit | Correct doc; consider Withdraw for Draft/Returned |
| M3 | Major | RequestWorkflow.cs:129 | Request number year from `DateTime.UtcNow` (wrong year 00:00–05:30 IST on 1 Jan) | Use office date |
| M4 | Major | RegistrationRepository.Approve + RegistrationReview | Approval not atomic: user created and directory changed before the guarded status update; a concurrent reject leaves an active account | One transaction; claim the row first |
| M5 | Major | Register.cshtml.cs OnPostSubmit | Submit repeats the directory lookup and reveals "found / not found" + masked e-mail without the lookup throttle | Throttle Submit too; document limits |
| M6 | Major | Admin/UserEdit, Admin/StaffEdit | Admin vertical change bypasses ProfileAudit, master-list check and Staff sync (vertical decides BDU access); StaffEdit doesn't sync Users | Route through ApplyVertical; validate against masters |
| M7 | Major | Circulars Edit + Repository | `SupersedesId` editable after publishing; links never re-applied; Publish can overwrite another supersession | Lock after publish; check target in Publish |
| M8 | Major | CircularRepository Publish/Withdraw | Row counts ignored; audit entries written even when nothing changed; no RowVersion on circulars | Return/check counts; refuse when 0 |
| M9 | Major | Leads/New OnGetCheck | Any user can probe other people's leads (reference, status, date) by client/title/tender | Non-managers get a yes/no only |
| M10 | Major | RegistrationRepository.Approve / FindStaffByMobile | Matching falls back to a retired entry and reactivates/overwrites a former employee's row (recycled mobile) | Match active only, or warn and require confirmation |
| M11 | Major | RequestWorkflow AllowedActions | If the assigned officer loses the Approver role or is deactivated, the request is stuck; no reassignment | Admin reassign action, or document |
| M12 | Major | ProfileRepository mobile uniqueness; StaffEdit/import | Uniqueness only vs active linked users; StaffEdit/import do not validate mobiles | Document; validate everywhere |
| M13 | Major | Register vs Profile validation | Registration accepts any 10 digits, skips the e-mail domain, and does not check the vertical against masters (crafted POST could pick BDU) | One shared validator |
| M14 | Major | Leads.PossibleDuplicates vs doc | Extra subset rule, token rules and max 5 undocumented; unbounded 180-day load | Document; pre-filter in SQL |
| M15 | Major | Leads ChangeStatus | Any → any transitions; reopening keeps stale award fields; award date unchecked; LeadViewers cannot be owners; none documented | Document; clear award when leaving Won |
| M16 | Nit | Mine lists, Leads.All, Staff.All, export cap 2000 | Unbounded lists; export silently truncates at 2000 | Page; warn at cap |
| M17 | Nit | Requests/Circulars/Leads uploads | Orphan files on later failure (also Z1) | Shared helper |
| M18 | Nit | PhotoService / photo endpoint | Thumbnail, 32 px minimum, inactive users' photos served, `v` unchecked — undocumented | Document; decide on inactive users |
| M19 | Nit | RequestWorkflow.CanView | Chosen reviewer/approver can view a Draft before submission | Restrict or document |
| M20 | Nit | ProfileRepository LinkUnlinkedAccounts / SetLink | Considers inactive users; SetLink accepts non-existent/retired StaffId | Check existence; document |
| M21 | Nit | Notices Deactivate | Audits "Notice.Removed" even if nothing changed; notices cannot be edited; home shows max 10 — undocumented | Check count; document |
| M22 | Nit | Admin Staff export / import DeactivateMissing | Export includes hidden mobiles; DeactivateMissing one UPDATE per row, no transaction — undocumented | Document; one transaction |
| M23 | Nit | Circulars Details upload | New versions can be added to Superseded circulars, no remark | Document or restrict |
| M24 | Nit | Requests/Circulars validation | Request category and issuing vertical not checked against masters; money stored as REAL | Validate |
