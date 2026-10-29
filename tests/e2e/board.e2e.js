// v1.4: message board — post with photos, home-page card, new-post badge, pin (max 3) and remove, phone layout.
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const B = process.env.BASE_URL || 'http://127.0.0.1:8080', IMG = process.argv[2], OUT = process.argv[3];
(async () => {
  const b = await chromium.launch(); let f = 0; const ck = (c, m) => { console.log((c ? 'PASS ' : 'FAIL ') + m); if (!c) f++; };
  const errs = [];
  const as = async (user, vw) => {
    const ctx = await b.newContext({ viewport: vw || { width: 1366, height: 900 } }); const p = await ctx.newPage(); p.on('dialog', d => d.accept());
    p.on('console', m => { if (m.type() === 'error' && !/404/.test(m.text())) errs.push(user + ': ' + m.text()); });
    await p.goto(B + '/Account/Login'); await p.fill('#Username', user); await p.fill('#Password', 'Nabcons@2026');
    await p.click('button[type=submit]'); await p.waitForLoadState('networkidle'); return p;
  };
  const badge = async (p) => { const el = p.locator('[data-badge="board"]').first(); return (await el.isHidden()) ? 0 : parseInt(await el.textContent(), 10); };

  const a1 = await as('approver1');
  await a1.goto(B + '/Board'); await a1.goto(B + '/Circulars');                  // seen the board, now elsewhere
  const st = await as('staff');
  await st.goto(B + '/Board');
  ck(await st.locator('.mainnav a:has-text("Message Board")').count() === 1, 'Message Board tab in the menu');
  ck(await st.locator('.post.pinned').count() >= 1, 'pinned welcome post at the top');
  await st.fill('#body', 'Our team at the Lucknow review meeting.\nThank you all!');
  await st.setInputFiles('#photos', [IMG + '/rotated_gps.jpg', IMG + '/landscape.png']);
  await st.waitForSelector('#photo-previews img');
  ck(await st.locator('#photo-previews img').count() === 2, 'previews of the two chosen photos');
  await st.screenshot({ path: OUT + '/b1-compose.png', fullPage: true });
  await Promise.all([st.waitForNavigation(), st.click('.composer button[type=submit]')]);
  ck((await st.content()).includes('Your message is on the board'), 'message posted');
  const post = st.locator('.post', { hasText: 'Lucknow review meeting' }).first();
  ck(await post.locator('.post-photos img').count() === 2, 'two photos shown with the post');
  ck(await post.locator('button:has-text("Pin")').count() === 0, 'staff cannot pin');
  ck(await post.locator('summary:has-text("Remove")').count() === 1, 'author can remove their own post');
  await st.screenshot({ path: OUT + '/b2-board.png', fullPage: true });
  const src = await post.locator('.post-photos img').first().getAttribute('src');
  const img = await st.request.get(B + src);
  ck(img.status() === 200 && img.headers()['content-type'] === 'image/jpeg', 'photo served as JPEG to signed-in users');

  // an oversized photo is stopped in the browser before upload
  await st.setInputFiles('#photos', [IMG + '/over2mb.png']);
  ck((await st.inputValue('#photos')) === '', 'oversized photo (> 5 MB) cleared before upload');
  // a fake image is refused with a clear message
  await st.setInputFiles('#photos', [IMG + '/evil.jpg']); await st.fill('#body', 'fake');
  await Promise.all([st.waitForNavigation(), st.click('.composer button[type=submit]')]);
  ck((await st.content()).includes('evil.jpg') && (await st.inputValue('#body')) === 'fake', 'fake photo refused; the text is kept');

  // home page card and the badge for approver1 (one poll interval)
  await st.goto(B + '/'); ck((await st.content()).includes('Lucknow review meeting'), 'home page shows the latest posts');
  await a1.goto(B + '/Circulars');
  ck(await badge(a1) >= 1, 'new-post badge for colleagues: ' + await badge(a1));
  await a1.goto(B + '/Board'); await a1.goto(B + '/Circulars');
  ck(await badge(a1) === 0, 'badge clears after visiting the board');

  // administrator: pin up to 3, remove with a reason
  const ad = await as('admin');
  await ad.goto(B + '/Board');
  for (let i = 0; i < 3; i++) {
    const pinBtn = ad.locator('.post:not(.pinned) button:has-text("Pin")').first();
    if (await pinBtn.count() === 0) break;
    await Promise.all([ad.waitForNavigation(), pinBtn.click()]);
  }
  ck((await ad.content()).includes('At most 3 posts can be pinned') || await ad.locator('.post.pinned').count() === 3, 'three posts pinned (or the limit reached)');
  if (await ad.locator('.post.pinned').count() === 3) {
    const extra = ad.locator('.post:not(.pinned) button:has-text("Pin")').first();
    if (await extra.count()) { await Promise.all([ad.waitForNavigation(), extra.click()]); ck((await ad.content()).includes('At most 3 posts can be pinned'), 'a fourth pin is refused'); }
  }
  ck(await ad.locator('.post.pinned').count() === 3, 'exactly 3 pinned');
  await ad.screenshot({ path: OUT + '/b3-admin.png', fullPage: true });
  const target = ad.locator('.post', { hasText: 'Lucknow review meeting' }).first();
  await target.locator('summary:has-text("Remove")').click();
  await target.locator('input[name=reason]').fill('Posted twice');
  await Promise.all([ad.waitForNavigation(), target.locator('button:has-text("Remove post")').click()]);
  ck(!(await ad.content()).includes('Lucknow review meeting'), 'administrator removed the post');
  ck((await st.request.get(B + src)).status() === 404, 'photos of a removed post are no longer served');

  // phone
  const ph = await as('staff', { width: 390, height: 844 });
  await ph.goto(B + '/Board');
  ck(!(await ph.evaluate(() => document.documentElement.scrollWidth > window.innerWidth + 1)), 'no horizontal scroll at phone width');
  await ph.screenshot({ path: OUT + '/b4-phone.png', fullPage: true });

  ck(errs.length === 0, 'no console errors (CSP etc.): ' + JSON.stringify(errs.slice(0, 3)));
  await b.close();
  console.log(f ? `FAILED ${f}` : 'PASSED'); process.exit(f ? 1 : 0);
})().catch(e => { console.error(e); process.exit(1); });
