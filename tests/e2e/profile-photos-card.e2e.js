const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const B = process.env.BASE_URL || 'http://127.0.0.1:8080', IMG = process.argv[2], OUT = process.argv[3];
(async () => { const b = await chromium.launch(); let f = 0; const ck = (c, m) => { console.log((c ? 'PASS ' : 'FAIL ') + m); if (!c) f++; };
  const errs = []; const ctx = await b.newContext({ viewport: { width: 1366, height: 900 } }); const p = await ctx.newPage(); p.on('dialog', d => d.accept());
  p.on('console', m => { if (m.type() === 'error') errs.push(m.text()); });
  await p.goto(B + '/Account/Login'); await p.fill('#Username', 'bdu'); await p.fill('#Password', 'Nabcons@2026'); await p.click('button:has-text("Sign in")'); await p.waitForLoadState('networkidle');
  await p.click('details.usermenu summary'); await p.screenshot({ path: OUT + '/p0-menu.png' });
  const items = await p.$$eval('.usermenu .menu a, .usermenu .menu button', e => e.map(x => x.textContent.trim()));
  ck(items[0] === 'My Profile' && items[1] === 'Change password', 'menu: My Profile above Change password ' + JSON.stringify(items));
  await p.goto(B + '/Profile'); await p.screenshot({ path: OUT + '/p1-profile.png', fullPage: true });
  ck((await p.content()).includes('Rohan Kapoor') && (await p.content()).includes('NC1026'), 'profile shows linked directory entry');
  for (const [file, expect, label] of [['evil.jpg', 'not a JPEG', 'renamed .exe rejected'], ['fake.png', 'not a JPEG', 'disguised GIF rejected'], ]) {
    await p.setInputFiles('#photo', IMG + '/' + file); await p.click('button:has-text("Save photo")'); await p.waitForLoadState('networkidle');
    ck((await p.content()).includes(expect), label);
  }
  // oversize: the browser refuses it, and the server refuses it too when the browser check is bypassed
  let alerted = ''; p.once('dialog', d => { alerted = d.message(); });
  await p.setInputFiles('#photo', IMG + '/over2mb.png'); await p.waitForTimeout(300);
  ck(alerted.includes('larger than the 2 MB'), 'oversize refused in the browser');
  const tok0 = await p.inputValue('form[action*=Photo] input[name=__RequestVerificationToken]');
  const big = await p.request.post(B + '/Profile?handler=Photo', { multipart: { __RequestVerificationToken: tok0, photo: { name: 'over2mb.png', mimeType: 'image/png', buffer: require('fs').readFileSync(IMG + '/over2mb.png') } } });
  ck((await big.text()).includes('larger than 2 MB'), 'oversize refused by the server');
  await p.goto(B + '/Profile');
  await p.setInputFiles('#photo', IMG + '/rotated_gps.jpg'); await p.waitForSelector('#photo-preview:not(.hidden)', { timeout: 3000 }).catch(() => {}); ck(await p.isVisible('#photo-preview'), 'preview shown before saving');
  await p.click('button:has-text("Save photo")'); await p.waitForLoadState('networkidle');
  ck((await p.content()).includes('Photo updated'), 'valid photo accepted');
  const src = await p.getAttribute('img.avatar-photo.lg', 'src'); const r = await p.request.get(B + src); require('fs').writeFileSync(OUT + '/out.jpg', await r.body());
  ck(r.headers()['cache-control'].includes('max-age=31536000'), 'versioned photo is cacheable: ' + r.headers()['cache-control']);
  const t = await p.request.get(B + src.replace('?', '?thumb=true&')); require('fs').writeFileSync(OUT + '/out_t.jpg', await t.body());
  // edit contact details
  await p.goto(B + '/Profile?edit=true');
  await p.fill('#Mobile', '12345'); await p.click('button:has-text("Save")'); await p.waitForLoadState('networkidle');
  ck((await p.content()).includes('valid 10-digit'), 'invalid mobile rejected');
  await p.fill('#Mobile', '9000001004'); await p.click('button:has-text("Save")'); await p.waitForLoadState('networkidle');
  ck((await p.content()).includes('already used by another active user'), 'mobile of another active user rejected');
  await p.fill('#Mobile', '9876500011'); await p.fill('#Extension', '2299'); await p.fill('#OfficialEmail', 'rohan.k@nabcons.local');
  await p.selectOption('#Location', 'Kohima'); await p.screenshot({ path: OUT + '/p2-edit.png', fullPage: true });
  await p.click('button:has-text("Save")'); await p.waitForLoadState('networkidle');
  ck((await p.content()).includes('Profile updated'), 'contact details saved');
  await p.goto(B + '/Directory?q=Rohan'); const row = await p.locator('table.table tbody tr').first().innerText();
  ck(row.includes('Kohima') && row.includes('2299') && row.includes('rohan.k@nabcons.local'), 'directory reflects the changes');
  ck(await p.locator('tr.card-row img.avatar-photo[loading=lazy]').count() === 1, 'directory shows lazy-loaded thumbnail');
  // crafted request: try to change name/designation/another user
  await p.goto(B + '/Profile?edit=true'); const token = await p.inputValue('form[action*=Contact] input[name=__RequestVerificationToken]');
  const resp = await p.request.post(B + '/Profile?handler=Contact', { form: { __RequestVerificationToken: token, Mobile: '9876500011', Extension: '2299', OfficialEmail: 'rohan.k@nabcons.local', Location: 'Kohima', FullName: 'Hacker', Designation: 'MD', Roles: 'Administrator', UserId: '1', StaffId: '1', Username: 'admin' }, maxRedirects: 0 });
  ck(resp.status() === 302, 'crafted post accepted only as a normal save (' + resp.status() + ')');
  await p.goto(B + '/Profile'); const prof = await p.content();
  ck(prof.includes('Rohan Kapoor') && prof.includes('Manager - Business Development') && !prof.includes('Hacker') && !prof.includes('>Administrator<'), 'name/designation/roles unchanged by crafted fields');
  // vertical change request
  await p.selectOption('#Vertical', 'IT & Digital'); await p.click('button:has-text("Request change")'); await p.waitForLoadState('networkidle');
  ck((await p.content()).includes('Vertical change pending approval'), 'vertical change pending');
  ck((await p.content()).includes('Current vertical: <strong>BDU</strong>'), 'vertical not changed until approved');
  await p.screenshot({ path: OUT + '/p3-pending.png', fullPage: true });
  // contact card
  await p.goto(B + '/Directory?q=Rohan'); await p.click('tr.card-row td:first-child');
  await p.waitForSelector('#contact-card[open]'); await p.waitForTimeout(300); await p.screenshot({ path: OUT + '/p4-card.png' });
  ck(await p.isVisible('#cc-photo'), 'card shows photo');
  ck((await p.textContent('#cc-name')) === 'Rohan Kapoor', 'card name');
  ck((await p.getAttribute('[data-cc=email]', 'href')) === 'mailto:rohan.k@nabcons.local', 'mailto link');
  ck((await p.getAttribute('[data-cc=extension]', 'href')) === 'tel:2299', 'tel link');
  await p.click('[data-copy=email]'); await p.waitForTimeout(200); ck((await p.textContent('#cc-status')).includes('Copied'), 'copy to clipboard: ' + await p.textContent('#cc-status'));
  const [dl] = await Promise.all([p.waitForEvent('download'), p.click('#cc-vcard')]); const vpath = OUT + '/card.vcf'; await dl.saveAs(vpath);
  const vcf = require('fs').readFileSync(vpath, 'utf8'); ck(vcf.startsWith('BEGIN:VCARD') && vcf.includes('VERSION:3.0') && vcf.includes('FN:Rohan Kapoor') && vcf.includes('EMAIL;TYPE=INTERNET,WORK:rohan.k@nabcons.local'), 'vCard 3.0 downloaded');
  await p.keyboard.press('Escape'); await p.waitForTimeout(200);
  ck(!(await p.isVisible('#contact-card')), 'Esc closes card');
  ck(await p.evaluate(() => document.activeElement && document.activeElement.classList.contains('card-row')), 'focus returns to the row');
  await p.focus('tr.card-row'); await p.keyboard.press('Enter'); await p.waitForSelector('#contact-card[open]'); ck(true, 'keyboard Enter opens card');
  for (let i = 0; i < 12; i++) await p.keyboard.press('Tab');
  ck(await p.evaluate(() => document.getElementById('contact-card').contains(document.activeElement)), 'focus trapped inside card');
  // DOB never anywhere
  ck(!/birth|D\.O\.B|\bDOB\b/i.test(await p.content()), 'no date of birth shown');
  // mobile sheet
  const m = await ctx.newPage(); await m.setViewportSize({ width: 390, height: 844 }); await m.goto(B + '/Directory?q=Rohan'); await m.click('tr.card-row td:first-child'); await m.waitForSelector('#contact-card[open]'); await m.waitForTimeout(300);
  await m.screenshot({ path: OUT + '/p5-card-mobile.png' });
  const bb = await m.locator('#contact-card').boundingBox(); ck(bb.width >= 389 && bb.height >= 800, 'full-screen sheet on mobile ' + JSON.stringify(bb));
  // anonymous cannot fetch photo/card
  const anon = await b.newContext(); const ap = await anon.newPage();
  const pr = await ap.request.get(B + src, { maxRedirects: 0 }); ck(pr.status() === 302 || pr.status() === 401, 'photo needs sign-in (' + pr.status() + ')');
  const cr = await ap.request.get(B + '/directory/card/1', { maxRedirects: 0 }); ck(cr.status() === 302 || cr.status() === 401, 'card JSON needs sign-in (' + cr.status() + ')');
  ck(errs.length === 0, 'no console/CSP errors ' + JSON.stringify(errs));
  await b.close(); console.log(f ? f + ' FAILED' : 'ALL PROFILE/PHOTO/CARD CHECKS PASSED'); })().catch(e => { console.error(e); process.exit(2); });
