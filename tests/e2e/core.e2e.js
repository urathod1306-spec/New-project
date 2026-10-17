const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const fs = require('fs');
const path = require('path');
const BASE = process.env.BASE_URL || 'http://127.0.0.1:8080';
const SHOTS = process.argv[2] || './shots';
fs.mkdirSync(SHOTS, { recursive: true });
const PW = 'Nabcons@2026';
let failures = 0;
function check(cond, msg) { console.log((cond ? 'PASS ' : 'FAIL ') + msg); if (!cond) failures++; }

(async () => {
  const browser = await chromium.launch();
  const consoleErrors = [];
  async function session(user, viewport) {
    const ctx = await browser.newContext({ viewport: viewport || { width: 1366, height: 900 }, acceptDownloads: true });
    const page = await ctx.newPage();
    page.on('console', m => { if (m.type() === 'error') consoleErrors.push(user + ': ' + m.text()); });
    page.on('dialog', d => d.accept());
    await page.goto(BASE + '/Account/Login');
    await page.fill('#Username', user);
    await page.fill('#Password', PW);
    await page.click('button[type=submit]');
    await page.waitForLoadState('networkidle');
    return { ctx, page };
  }

  // ---- admin ----
  let { ctx, page } = await session('admin');
  check(page.url() === BASE + '/Desk', 'admin lands on My Desk (default preference)');
  await page.screenshot({ path: path.join(SHOTS, '01-home.png'), fullPage: true });
  await page.goto(BASE + '/Directory?q=consultant');
  const dirRows = await page.locator('table.table tbody tr').count();
  check(dirRows > 3, 'directory search returns rows: ' + dirRows);
  await page.screenshot({ path: path.join(SHOTS, '02-directory.png'), fullPage: true });
  await page.goto(BASE + '/Circulars?q=leave');
  const circRows = await page.locator('table.table tbody tr').count();
  check(circRows >= 2, 'circular FTS search "leave" returns ' + circRows);
  await page.screenshot({ path: path.join(SHOTS, '03-circulars.png'), fullPage: true });
  await page.goto(BASE + '/Circulars?q=' + encodeURIComponent('"*)(OR AND NEAR( \' ; DROP TABLE'));
  check((await page.title()).includes('Circulars'), 'malformed search input handled');
  await page.goto(BASE + '/Admin');
  await page.screenshot({ path: path.join(SHOTS, '04-admin.png'), fullPage: true });
  await page.goto(BASE + '/Admin/Backup');
  await page.click('text=Create backup now');
  await page.waitForLoadState('networkidle');
  check(await page.locator('.alert.success').count() === 1, 'backup created');
  await ctx.close();

  // ---- publisher: duplicate number, fake pdf, valid upload ----
  ({ ctx, page } = await session('publisher'));
  const y = new Date().getFullYear();
  const fake = path.join(SHOTS, 'fake.pdf'); fs.writeFileSync(fake, 'this is not a pdf');
  const real = path.join(SHOTS, 'real.pdf'); fs.writeFileSync(real, '%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n');
  const exe = path.join(SHOTS, 'evil.exe'); fs.writeFileSync(exe, 'MZ....');
  await page.goto(BASE + '/Circulars/Edit');
  await page.fill('#Input_CircularNumber', `NABCONS/HR/${y}/01`);
  await page.selectOption('#Input_Category', 'HR Circulars');
  await page.fill('#Input_Title', 'Duplicate test');
  await page.setInputFiles('#Upload', real);
  await page.click('button:has-text("Save")');
  await page.waitForLoadState('networkidle');
  check((await page.content()).includes('already exists'), 'duplicate circular number rejected');
  await page.fill('#Input_CircularNumber', `NABCONS/IT/${y}/09`);
  await page.setInputFiles('#Upload', fake);
  await page.click('button:has-text("Save")');
  await page.waitForLoadState('networkidle');
  check((await page.content()).includes('does not match a genuine'), 'fake PDF rejected by signature check');
  await page.setInputFiles('#Upload', exe);
  await page.click('button:has-text("Save")');
  await page.waitForLoadState('networkidle');
  check((await page.content()).includes('is not allowed'), '.exe rejected by extension allow-list');
  await page.setInputFiles('#Upload', real);
  await page.fill('#Input_Keywords', 'e2e, automated');
  await page.click('button:has-text("Save")');
  await page.waitForLoadState('networkidle');
  check(page.url().includes('/Circulars/Details/'), 'draft circular saved');
  const draftUrl = page.url();
  const draftDownload = await page.locator('a[href^="/Circulars/Download/"]').first().getAttribute('href');
  await page.screenshot({ path: path.join(SHOTS, '05-circular-draft.png'), fullPage: true });
  await ctx.close();

  // ---- staff: cannot see draft, cannot reach admin, raises request ----
  ({ ctx, page } = await session('staff'));
  let r = await page.goto(BASE + draftUrl.replace(BASE, ''));
  check(r.status() === 404, 'staff cannot open draft circular (404)');
  r = await page.goto(BASE + draftDownload);
  check(r.status() === 404, 'staff cannot download draft file by guessing URL (404)');
  r = await page.goto(BASE + '/Admin');
  check(r.status() === 403 || page.url().includes('AccessDenied'), 'staff blocked from /Admin');
  r = await page.goto(BASE + '/Audit');
  check(r.status() === 403 || page.url().includes('AccessDenied'), 'staff blocked from /Audit');
  await page.goto(BASE + '/Circulars?q=approval matrix');
  check(await page.locator('table.table tbody tr').count() === 1, 'staff (Rural Infra) sees vertical-only circular');

  await page.goto(BASE + '/Requests/Edit');
  await page.fill('#Input_Subject', 'E2E test: purchase of a laminator');
  await page.selectOption('#Input_RequestCategory', { index: 1 });
  await page.fill('#Input_Amount', '4500');
  await page.fill('#Input_Details', 'Automated end-to-end test request.');
  await page.selectOption('#Input_ReviewerUserId', { label: 'Rajesh Iyer (HR & Administration)' });
  await page.selectOption('#Input_ApproverUserId', { label: 'Anita Deshmukh (Finance & Accounts)' });
  await page.setInputFiles('#Uploads', real);
  await page.click('button:has-text("Save & submit")');
  await page.waitForLoadState('networkidle');
  check(page.url().includes('/Requests/Details/'), 'request submitted');
  const reqUrl = page.url().replace(BASE, '');
  check((await page.locator('.badge.Submitted').count()) >= 1, 'request state Submitted');
  await page.screenshot({ path: path.join(SHOTS, '06-request-submitted.png'), fullPage: true });
  await ctx.close();

  // ---- approver2 cannot act before level 1 ----
  ({ ctx, page } = await session('approver2'));
  await page.goto(BASE + reqUrl);
  check(await page.locator('button[name=wfAction]').count() === 0, 'sanctioning authority has no actions before recommendation');
  const ctx2 = ctx, page2 = page;

  // ---- approver1 recommends; stale form test ----
  ({ ctx, page } = await session('approver1'));
  await page.goto(BASE + '/Requests/Inbox');
  await page.screenshot({ path: path.join(SHOTS, '07-inbox.png'), fullPage: true });
  await page.goto(BASE + reqUrl);
  const stalePage = await ctx.newPage(); stalePage.on('dialog', d => d.accept());
  await stalePage.goto(BASE + reqUrl);
  await page.click('button[value=Recommend]');
  await page.waitForLoadState('networkidle');
  check((await page.locator('.badge.Under').count()) >= 1, 'recommended -> Under Review');
  await stalePage.fill('#remarks', 'second tab');
  await stalePage.click('button[value=Reject]');
  await stalePage.waitForLoadState('networkidle');
  check((await stalePage.content()).includes('updated by someone else'), 'concurrent/stale approval blocked');
  await ctx.close();

  // ---- approver2: reject without remarks fails, approve works ----
  await page2.goto(BASE + reqUrl);
  await page2.click('button[value=Reject]');
  await page2.waitForLoadState('networkidle');
  check((await page2.content()).includes('Remarks are required'), 'reject requires remarks');
  await page2.fill('#remarks', 'Sanctioned.');
  await page2.click('button[value=Approve]');
  await page2.waitForLoadState('networkidle');
  check((await page2.locator('.badge.Approved').count()) >= 1, 'approved');
  await page2.screenshot({ path: path.join(SHOTS, '08-request-approved.png'), fullPage: true });
  await ctx2.close();

  // ---- auditor: audit log + verify ----
  ({ ctx, page } = await session('auditor'));
  await page.goto(BASE + '/Audit');
  await page.click('button:has-text("Verify integrity")');
  await page.waitForLoadState('networkidle');
  check((await page.locator('.alert.success').textContent() || '').includes('verified'), 'audit chain verified');
  await page.screenshot({ path: path.join(SHOTS, '09-audit.png'), fullPage: true });
  r = await page.goto(BASE + '/Circulars/Edit');
  check(r.status() === 403 || page.url().includes('AccessDenied'), 'auditor cannot create circulars');
  await ctx.close();

  // ---- mobile layout ----
  ({ ctx, page } = await session('staff', { width: 390, height: 844 }));
  await page.screenshot({ path: path.join(SHOTS, '10-mobile-home.png'), fullPage: true });
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth + 1);
  check(!overflow, 'no horizontal scroll on phone width');
  await ctx.close();

  // ---- lockout ----
  const lctx = await browser.newContext(); const lp = await lctx.newPage();
  for (let i = 0; i < 5; i++) {
    await lp.goto(BASE + '/Account/Login');
    await lp.fill('#Username', 'auditor'); await lp.fill('#Password', 'wrong-password'); await lp.click('button[type=submit]');
    await lp.waitForLoadState('networkidle');
  }
  check((await lp.content()).includes('locked'), 'account locks after 5 failures');
  await lctx.close();

  const realErrors = consoleErrors.filter(e => !e.includes('status of 404'));
  check(realErrors.length === 0, 'no browser console errors (CSP etc.): ' + JSON.stringify(realErrors.slice(0, 5)));
  await browser.close();
  console.log(failures === 0 ? '\nALL CHECKS PASSED' : `\n${failures} CHECK(S) FAILED`);
  process.exit(failures ? 1 : 0);
})().catch(e => { console.error(e); process.exit(2); });
