const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const BASE = process.env.BASE_URL || 'http://127.0.0.1:8080', SH = process.argv[2];
let failures = 0; const check = (c, m) => { console.log((c ? 'PASS ' : 'FAIL ') + m); if (!c) failures++; };
const PW = 'Arjun@Pass2026';
(async () => {
  const b = await chromium.launch(); const errs = [];
  const fresh = async (vw) => { const ctx = await b.newContext({ viewport: vw || { width: 1366, height: 900 } }); const p = await ctx.newPage();
    p.on('dialog', d => d.accept()); p.on('console', m => { if (m.type() === 'error' && !m.text().includes('404')) errs.push(m.text()); }); return p; };
  const signin = async (p, u, pw) => { await p.goto(BASE + '/Account/Login'); await p.fill('#Username', u); await p.fill('#Password', pw); await p.click('button[type=submit]:has-text("Sign in")'); await p.waitForLoadState('networkidle'); };
  let p = await fresh();
  await p.goto(BASE + '/Account/Login');
  check(await p.locator('a:has-text("New user? Register here")').count() === 1, 'login page shows Register link');
  await p.click('text=New user? Register here');
  await p.fill('#Mobile', '12345'); await p.evaluate(() => document.querySelector('#Mobile').removeAttribute('minlength'));
  await p.evaluate(() => document.querySelector('#Mobile').removeAttribute('pattern'));
  await p.click('button:has-text("Continue")'); await p.waitForLoadState('networkidle');
  check((await p.content()).includes('10-digit mobile'), 'short number rejected by server');
  await p.fill('#Mobile', '+91 90000 01008'); check(await p.inputValue('#Mobile') === '9000001008', 'pasted +91/spaces cleaned to 10 digits (got ' + await p.inputValue('#Mobile') + ')');
  await p.click('button:has-text("Continue")'); await p.waitForLoadState('networkidle');
  check(await p.inputValue('#Input_FullName') === 'Arjun Mehta', 'existing staff found by mobile and name pre-filled');
  check((await p.content()).includes('a**********a@nabcons.local') || /a\*+a@nabcons\.local/.test(await p.content()), 'e-mail shown masked only');
  check(!(await p.content()).includes('arjun.mehta@nabcons.local'), 'full e-mail not disclosed');
  await p.screenshot({ path: SH + '/r1-prefilled.png', fullPage: true });
  await p.fill('#Input_Designation', 'Senior Consultant');
  await p.fill('#Input_Password', 'short'); await p.fill('#Input_ConfirmPassword', 'short');
  await p.click('button:has-text("Submit registration")'); await p.waitForLoadState('networkidle');
  check((await p.content()).includes('at least 10 characters'), 'weak password rejected');
  await p.fill('#Input_Password', PW); await p.fill('#Input_ConfirmPassword', PW);
  await p.click('button:has-text("Submit registration")'); await p.waitForLoadState('networkidle');
  check((await p.content()).includes('Registration submitted'), 'registration submitted');
  await p.goto(BASE + '/Account/Register'); await p.fill('#Mobile', '9000001008'); await p.click('button:has-text("Continue")'); await p.waitForLoadState('networkidle');
  check((await p.content()).includes('already awaiting'), 'duplicate pending registration blocked');
  await signin(p, '9000001008', PW);
  check((await p.content()).includes('awaiting approval'), 'sign-in before approval explains it is pending');

  // brand-new person
  await p.goto(BASE + '/Account/Register'); await p.fill('#Mobile', '9123456789'); await p.click('button:has-text("Continue")'); await p.waitForLoadState('networkidle');
  check((await p.content()).includes('not in the staff directory'), 'unknown number allowed as new registration');
  check(await p.inputValue('#Input_FullName') === '', 'new registration form is blank');
  await p.fill('#Input_FullName', 'Nikhil Bose'); await p.fill('#Input_Designation', 'Project Associate');
  await p.selectOption('#Input_Vertical', 'IT & Digital'); await p.selectOption('#Input_Location', 'Pune');
  await p.fill('#Input_OfficialEmail', 'nikhil.bose@nabcons.local'); await p.fill('#Input_EmployeeCode', 'NC2050');
  await p.fill('#Input_Password', 'Nikhil@Pass2026'); await p.fill('#Input_ConfirmPassword', 'Nikhil@Pass2026');
  await p.screenshot({ path: SH + '/r2-new.png', fullPage: true });
  await p.click('button:has-text("Submit registration")'); await p.waitForLoadState('networkidle');
  check((await p.content()).includes('Registration submitted'), 'new person registration submitted');
  // new person WITHOUT an employee code
  await p.goto(BASE + '/Account/Register'); await p.fill('#Mobile', '9988776655'); await p.click('button:has-text("Continue")'); await p.waitForLoadState('networkidle');
  await p.fill('#Input_FullName', 'Sara Ali'); await p.fill('#Input_OfficialEmail', 'sara.ali@nabcons.local');
  await p.selectOption('#Input_Vertical', 'Agri & Allied'); await p.selectOption('#Input_Location', 'Jaipur');
  await p.fill('#Input_Password', 'Sara@Pass20266'); await p.fill('#Input_ConfirmPassword', 'Sara@Pass20266');
  await p.click('button:has-text("Submit registration")'); await p.waitForLoadState('networkidle');
  // one to reject
  await p.goto(BASE + '/Account/Register'); await p.fill('#Mobile', '9876543210'); await p.click('button:has-text("Continue")'); await p.waitForLoadState('networkidle');
  await p.fill('#Input_FullName', 'Unknown Person'); await p.fill('#Input_OfficialEmail', 'x@example.com');
  await p.fill('#Input_Password', 'Unknown@Pass2026'); await p.fill('#Input_ConfirmPassword', 'Unknown@Pass2026');
  await p.click('button:has-text("Submit registration")'); await p.waitForLoadState('networkidle');

  // admin
  const a = await fresh(); await signin(a, 'admin', 'Nabcons@2026');
  check((await a.locator('.mainnav .count').last().textContent()).trim() === '4', 'admin menu shows 4 pending registrations');
  await a.goto(BASE + '/Admin/Registrations'); await a.screenshot({ path: SH + '/r3-list.png', fullPage: true });
  check(await a.locator('table.table tbody tr').count() === 4, 'pending list has 4');
  await a.click('tr:has-text("Arjun Mehta") >> text=Review'); await a.waitForLoadState('networkidle');
  check(await a.locator('tr.diff-changed:has-text("Designation")').count() === 1, 'changed designation highlighted for admin');
  await a.screenshot({ path: SH + '/r4-review.png', fullPage: true });
  await a.click('button:has-text("Approve and create account")'); await a.waitForLoadState('networkidle');
  check((await a.content()).includes('approved'), 'admin approved existing staff');
  await a.goto(BASE + '/Admin/Registrations');
  await a.click('tr:has-text("Nikhil Bose") >> text=Review'); await a.waitForLoadState('networkidle');
  await a.check('input[name=roles][value=Approver]');
  await a.click('button:has-text("Approve and create account")'); await a.waitForLoadState('networkidle');
  check((await a.content()).includes('approved'), 'admin approved new person with Approver role');
  await a.goto(BASE + '/Admin/Registrations');
  await a.click('tr:has-text("Sara Ali") >> text=Review'); await a.waitForLoadState('networkidle');
  check(await a.inputValue('#employeeCode') === 'REG-9988776655', 'provisional employee code suggested when none given');
  await a.screenshot({ path: SH + '/r4b-review-new.png', fullPage: true });
  await a.click('button:has-text("Approve and create account")'); await a.waitForLoadState('networkidle');
  await a.goto(BASE + '/Directory?q=Sara');
  check((await a.content()).includes('REG-9988776655') && (await a.content()).includes('Jaipur'), 'registered person without code populated in staff directory');
  await a.goto(BASE + '/Admin/Registrations');
  await a.click('tr:has-text("Unknown Person") >> text=Review'); await a.waitForLoadState('networkidle');
  await a.fill('#reason', 'Not a NABCONS employee'); await a.click('button:has-text("Reject")'); await a.waitForLoadState('networkidle');
  check((await a.content()).includes('rejected'), 'admin rejected registration');
  await a.goto(BASE + '/Directory?q=Arjun');
  check((await a.content()).includes('Senior Consultant'), 'directory updated with corrected designation');
  await a.goto(BASE + '/Directory?q=Nikhil');
  check((await a.content()).includes('NC2050'), 'new person added to directory with employee code');
  await a.goto(BASE + '/Audit?action=Registration'); check(await a.locator('table.table tbody tr').count() >= 6, 'registration events in audit log');

  // applicants sign in
  const u = await fresh(); await signin(u, '9000001008', PW);
  check(u.url() === BASE + '/Desk', 'approved user signs in with mobile number (lands on My Desk)');
  check((await u.content()).includes('Arjun Mehta'), 'signed in as Arjun Mehta');
  const n = await fresh(); await signin(n, '+91 91234 56789', 'Nikhil@Pass2026');
  check(n.url() === BASE + '/Desk' && (await n.content()).includes('Approval Inbox'), 'new person signs in (mobile typed with +91/spaces) and has Approver role');
  const x = await fresh(); await signin(x, '9876543210', 'Unknown@Pass2026');
  check((await x.content()).includes('not approved'), 'rejected applicant told registration not approved');
  // anonymous cannot reach admin page
  const anon = await fresh(); await anon.goto(BASE + '/Admin/Registrations'); check(anon.url().includes('/Account/Login'), 'registrations admin page needs sign-in');
  const st = await fresh(); await signin(st, 'staff', 'Nabcons@2026'); const resp = await st.goto(BASE + '/Admin/Registrations'); check(st.url().includes('AccessDenied'), 'non-admin cannot review registrations');
  // mobile layout
  const m = await fresh({ width: 390, height: 844 }); await m.goto(BASE + '/Account/Register'); await m.fill('#Mobile', '9000001004'); await m.click('button:has-text("Continue")'); await m.waitForLoadState('networkidle');
  await m.screenshot({ path: SH + '/r5-mobile.png', fullPage: true });
  check(!(await m.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1)), 'registration fits phone width');
  // throttle
  const t = await fresh(); let blocked = false;
  for (let i = 0; i < 12 && !blocked; i++) { await t.goto(BASE + '/Account/Register'); await t.fill('#Mobile', '95000000' + String(10 + i)); await t.click('button:has-text("Continue")'); await t.waitForLoadState('networkidle'); blocked = (await t.content()).includes('Too many attempts'); }
  check(blocked, 'bulk number probing is throttled');
  check(errs.length === 0, 'no console/CSP errors ' + JSON.stringify(errs.slice(0, 3)));
  await b.close(); console.log(failures ? failures + ' FAILED' : 'ALL REGISTRATION CHECKS PASSED'); process.exit(failures ? 1 : 0);
})().catch(e => { console.error(e); process.exit(2); });
