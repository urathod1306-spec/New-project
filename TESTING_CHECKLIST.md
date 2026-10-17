# NABCONS Intranet 1.2.0 — Manual testing checklist (office machine, before merging)

Test on a **copy** first (see `docs/UPGRADE-GUIDE.md` → "Trying a new version safely"), or outside office hours. Tick each line.

**Accounts to use.** Prepare these before you start:

| Who | Set-up |
|---|---|
| **A** – ordinary staff | Any staff account, linked to their directory entry |
| **B** – BDU member | Vertical **BDU** |
| **C** – COO or MD | Role **LeadViewer** (*Users & roles*), *not* in BDU |
| **D** – administrator | Your admin account, *not* in BDU and *without* LeadViewer |

## Upgrade
- [ ] `NabconsIntranet.exe --migrate` prints a backup path, then "schema version 4". Running it again says "already up to date".
- [ ] *Administration → System information*: version 1.2.0; Business Leads line says BDU.
- [ ] Existing users sign in, circulars open, directory search and filters work, *Change password* works, admin pages load.

## My Profile (as A)
- [ ] User menu shows **My Profile** above **Change password**.
- [ ] Edit → change extension, e-mail and location → Save → "Profile updated". *Recent changes* lists each field with old and new values. The Staff Directory shows the new values.
- [ ] A mobile with 9 digits is refused. Another active user's mobile is refused ("already used").
- [ ] If A signs in with a mobile number, change it → sign out → sign in with the **new** number.
- [ ] Name, employee code, designation and roles are read-only. No date of birth appears anywhere.
- [ ] Vertical → choose BDU → *Request change* → "pending approval". A still has no Business Leads pipeline.

## Vertical approval (as D, then A)
- [ ] *Administration* shows "1 vertical change waiting" → *Vertical changes* → Approve.
- [ ] A refreshes: vertical is BDU and the *Pipeline* tab appears. Change it back with another request, or keep it for the lead tests.
- [ ] Rejecting without remarks is refused.

## Photos (as A)
- [ ] Upload a phone photo (JPEG). The preview shows, then Save → the photo appears in the header, the menu avatar and the directory row.
- [ ] Upload a sideways phone photo: it is shown upright.
- [ ] A 5 MB photo is refused. A `.exe` renamed to `.jpg` is refused. A PDF is refused.
- [ ] Remove photo → the initials circle returns.
- [ ] As D: *Users & roles → A → Remove photo* works.

## Contact card (any user, desktop and phone)
- [ ] Staff Directory → click a name → the card opens with photo, designation, vertical, office, extension and e-mail. Mobile appears only if the directory shows it.
- [ ] *Copy* buttons say "Copied". The e-mail link opens Outlook. *Save contact* downloads a `.vcf` that imports into Outlook or a phone.
- [ ] Esc closes the card and the keyboard focus returns to the row. Tab cycles inside the card.
- [ ] On a phone the card fills the screen.

## Business Leads
- [ ] As A: *Business Leads → Submit a lead*, filling all fields and adding a PDF → you get a reference like `NCS/BD/2026-27/0001`.
- [ ] As A: submit a second lead for the same client with a similar title → the warning shows the first reference → *Submit anyway* works.
- [ ] As A: *My leads* lists both. Opening one shows the details and attachment, and you can add a note.
- [ ] As A and as D: type the address `/Leads` → "Access denied" (403).
- [ ] As another staff user: open A's lead address `/Leads/Details/<number>` → 403. The same for its attachment link.
- [ ] As B: the *Business Leads* nav shows a badge. The *Pipeline* lists the leads; one due within 7 days is highlighted.
- [ ] As B: change status without a remark → refused. Choose *Lost* without a reason → refused. *Under Review* with a remark → OK. Assign an owner. Add a BDU file.
- [ ] As B: *Won* without value and date → refused. With them → OK.
- [ ] As A: the lead now shows the new status. Notes can no longer be added once it is past *Under Review*.
- [ ] As C (COO/MD): *Pipeline*, *Dashboard* (conversion %, by source, by vertical) and *Export to Excel* all work. The export opens in Excel.
- [ ] Timeline: every change shows who and when, with nothing editable.

## Backup
- [ ] *Administration → Backup → Create backup now*. The ZIP contains `data/uploads/photos/…`.
- [ ] *Audit → Verify integrity* passes.
