// v1.3 Phase 1: bell + badge polling, mark all read, My Desk sections by role, keyboard use of the bell.
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const B = process.env.BASE_URL || 'http://127.0.0.1:8080', OUT = process.argv[2];
(async () => {
  const b = await chromium.launch(); let f = 0; const ck = (c, m) => { console.log((c ? 'PASS ' : 'FAIL ') + m); if (!c) f++; };
  const errs = [];
  const as = async (user, vw) => {
    const ctx = await b.newContext({ viewport: vw || { width: 1366, height: 900 } }); const p = await ctx.newPage(); p.on('dialog', d => d.accept());
    p.on('console', m => { if (m.type() === 'error' && !/404/.test(m.text())) errs.push(user + ': ' + m.text()); });
    await p.goto(B + '/Account/Login'); await p.fill('#Username', user); await p.fill('#Password', 'Nabcons@2026');
    await p.click('button[type=submit]'); await p.waitForLoadState('networkidle'); return p;
  };
  const pick = async (p, sel, text) => { const v = await p.$eval(sel, (s, t) => [...s.options].find(o => o.text.startsWith(t)).value, text); await p.selectOption(sel, v); };
  const badge = async (p, name) => { const el = p.locator(`[data-badge="${name}"]`).first(); if (await el.count() === 0) return null; return (await el.isHidden()) ? 0 : parseInt(await el.textContent(), 10); };

  // approver1 sits on a page; staff submits a request to them; the bell updates within one poll interval (30 s in this run)
  const a1 = await as('approver1');
  ck(a1.url().endsWith('/Desk'), 'sign-in opens My Desk by default');
  await a1.goto(B + '/Circulars');
  const before = await badge(a1, 'unread'), inboxBefore = await badge(a1, 'inbox');
  const st = await as('staff');
  await st.goto(B + '/Requests/Edit');
  await st.fill('#Input_Subject', 'Laptop for field survey'); await st.selectOption('#Input_RequestCategory', { index: 1 });
  await st.fill('#Input_Details', 'Needed for the Rajasthan evaluation.');
  await pick(st, '#Input_ReviewerUserId', 'Rajesh Iyer'); await pick(st, '#Input_ApproverUserId', 'Anita Deshmukh');
  await Promise.all([st.waitForNavigation(), st.click('button[name=submit]')]);
  ck((await st.content()).includes('Submitted'), 'staff submitted a request');
  const t0 = Date.now(); let after = before;
  while (Date.now() - t0 < 40000) { await a1.waitForTimeout(1000); after = await badge(a1, 'unread'); if (after > before) break; }
  ck(after === before + 1, `bell badge rose ${before} -> ${after} without a reload, in ${Math.round((Date.now() - t0) / 1000)} s`);
  ck(await badge(a1, 'inbox') === inboxBefore + 1, 'Approval Inbox badge updated by the same poll');
  await a1.screenshot({ path: OUT + '/n1-badge.png' });

  // keyboard: focus the bell, open with Enter, arrow down into the list, Esc closes and returns focus
  await a1.focus('#bell-summary'); await a1.keyboard.press('Enter');
  await a1.waitForSelector('#bell-list .nitem');
  ck(await a1.locator('#bell-list .nitem.unread').count() >= 1, 'drop-down lists the new notification as unread');
  await a1.keyboard.press('ArrowDown');
  ck(await a1.evaluate(() => document.activeElement.closest('.bell-menu') !== null), 'ArrowDown moves focus into the drop-down');
  await a1.screenshot({ path: OUT + '/n2-dropdown.png' });
  await a1.keyboard.press('Escape');
  ck(await a1.evaluate(() => !document.getElementById('bell').open && document.activeElement.id === 'bell-summary'), 'Esc closes the drop-down and returns focus to the bell');

  // open the notification: it goes to the request and is marked read
  await a1.click('#bell-summary'); await a1.waitForSelector('#bell-list .nitem');
  await Promise.all([a1.waitForNavigation(), a1.click('#bell-list .nitem >> nth=0')]);
  ck(a1.url().includes('/Requests/Details/'), 'notification link opens the request');
  ck(await badge(a1, 'unread') === before, 'opened notification no longer counted as unread');

  // mark all read (create one more first)
  await st.goto(B + '/Requests/Edit');
  await st.fill('#Input_Subject', 'Second request'); await st.selectOption('#Input_RequestCategory', { index: 1 }); await st.fill('#Input_Details', 'More.');
  await pick(st, '#Input_ReviewerUserId', 'Rajesh Iyer'); await pick(st, '#Input_ApproverUserId', 'Anita Deshmukh');
  await Promise.all([st.waitForNavigation(), st.click('button[name=submit]')]);
  await a1.goto(B + '/Notifications?show=unread');
  ck(await a1.locator('.nitem.unread').count() >= 1, 'Notifications page lists unread items');
  await a1.screenshot({ path: OUT + '/n3-notifications.png', fullPage: true });
  await Promise.all([a1.waitForNavigation(), a1.click('main button:has-text("Mark all read")')]);
  ck(await a1.locator('.nitem.unread').count() === 0 && await badge(a1, 'unread') === 0, 'mark all read clears the list and the badge');

  // My Desk by role
  await a1.goto(B + '/Desk'); await a1.screenshot({ path: OUT + '/n4-desk-approver.png', fullPage: true });
  ck(await a1.locator('h2:has-text("Requests awaiting my action")').count() === 1, 'approver: requests section');
  ck(await a1.locator('h2:has-text("Self-registrations")').count() === 0, 'approver: no admin sections');
  await st.goto(B + '/Desk');
  ck(await st.locator('h2:has-text("Requests awaiting my action")').count() === 0, 'staff: no approver section');
  ck(await st.locator('h2:has-text("Recent notifications")').count() === 1, 'staff: recent notifications');
  const ad = await as('admin');
  ck(await ad.locator('h2:has-text("Self-registrations to approve")').count() === 1 && await ad.locator('h2:has-text("Vertical / profile changes")').count() === 1, 'admin: approval sections');
  await ad.screenshot({ path: OUT + '/n5-desk-admin.png', fullPage: true });
  const bd = await as('bdu');
  ck(await bd.locator('h2:has-text("Leads in")').count() === 1, 'BDU: leads section');
  // the preference
  await st.goto(B + '/Profile'); await st.uncheck('input[name=openDesk]'); await Promise.all([st.waitForNavigation(), st.click('button:has-text("Save preference")')]);
  const st2 = await as('staff'); ck(st2.url() === B + '/', 'preference off: sign-in opens the home page');

  // phone width: bell and desk usable, no horizontal scroll
  const ph = await as('approver1', { width: 390, height: 844 });
  const overflow = await ph.evaluate(() => document.documentElement.scrollWidth > window.innerWidth + 1);
  ck(!overflow, 'My Desk: no horizontal scroll at phone width');
  await ph.click('#bell-summary'); await ph.waitForTimeout(500);
  const box = await ph.locator('.bell-menu').boundingBox();
  ck(box && box.x >= 0 && box.x + box.width <= 391, 'bell drop-down fits the phone screen');
  await ph.screenshot({ path: OUT + '/n6-phone.png' });

  ck(errs.length === 0, 'no console errors (CSP etc.): ' + JSON.stringify(errs.slice(0, 3)));
  await b.close();
  console.log(f ? `FAILED ${f}` : 'PASSED'); process.exit(f ? 1 : 0);
})().catch(e => { console.error(e); process.exit(1); });
