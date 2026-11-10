// v1.7: inward register and invoices — registry inward, marking, accept, close, acknowledgement slip, confidential
// masking and 404s, self-inward, duplicate invoice block and override, certify, send to Accounts, payment, ERP import
// preview, invoice dashboard and MSME report, register exports, access grants, phone layout, no CSP errors.
// Uses the synthetic demo data: publisher = Inward Operator (HO), coo = Inward Viewer, approver2 = Invoice Accounts.
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const fs = require('fs');
const B = process.env.BASE_URL || 'http://127.0.0.1:8080', OUT = process.argv[2];
(async () => {
  const b = await chromium.launch(); let f = 0; const ck = (c, m) => { console.log((c ? 'PASS ' : 'FAIL ') + m); if (!c) f++; };
  const errs = [];
  const as = async (user, vw) => {
    const ctx = await b.newContext({ viewport: vw || { width: 1366, height: 900 }, acceptDownloads: true }); const p = await ctx.newPage();
    p.on('console', m => { if (m.type() === 'error' && !/404/.test(m.text())) errs.push(user + ': ' + m.text()); });
    await p.goto(B + '/Account/Login'); await p.fill('#Username', user); await p.fill('#Password', 'Nabcons@2026');
    await p.click('button[type=submit]'); await p.waitForLoadState('networkidle'); return p;
  };
  const submit = async (p, sel) => { await Promise.all([p.waitForNavigation(), p.click(sel)]); };
  const status = async (p, url) => (await p.goto(B + url)).status();
  const download = async (p, url) => {
    const [d] = await Promise.all([p.waitForEvent('download'), p.evaluate(u => { const a = document.createElement('a'); a.href = u; document.body.appendChild(a); a.click(); }, url)]);
    const path = OUT + '/' + d.suggestedFilename(); await d.saveAs(path); return fs.readFileSync(path);
  };
  const today = new Date(Date.now() + 330 * 60000).toISOString().slice(0, 10);
  const plus = d => new Date(Date.now() + 330 * 60000 + d * 86400000).toISOString().slice(0, 10);
  const dot = iso => iso.split('-').reverse().join('.');

  // ---- registry (publisher): dashboard and a new letter marked to staff
  const op = await as('publisher');
  await op.goto(B + '/Inward');
  ck(await op.locator('.mainnav a:has-text("Inward")').count() === 1, 'Inward tab in the menu');
  ck(await op.locator('.stat').count() >= 6, 'dashboard tiles');
  await op.screenshot({ path: OUT + '/i1-dashboard.png', fullPage: true });
  await op.goto(B + '/Inward/New');
  await op.selectOption('#type', 'Letter'); await op.selectOption('#mode', 'SpeedPost');
  await op.fill('#sname', 'Deputy General Manager'); await op.fill('#sorg', 'Sample Bank (e2e)'); await op.fill('#sref', 'SB/E2E/7');
  await op.fill('#subj', 'Request for a concept note on climate finance (e2e)'); await op.fill('#kept', 'Registry rack 1');
  await op.selectOption('#mark', { label: 'Vikram Singh' });
  await op.screenshot({ path: OUT + '/i2-new.png', fullPage: true });
  await submit(op, 'form.card button[type=submit]');
  ck(/\/Inward\/Item\/\d+/.test(op.url()), 'letter inwarded');
  const letterUrl = op.url();
  const number = (await op.locator('h1').innerText()).trim();
  ck(/^NCS\/IN\/HO\/\d{4}-\d{2}\/\d{5}$/.test(number), 'inward number format ' + number);
  const ack = await download(op, letterUrl + '?handler=Ack');
  ck(ack.slice(0, 4).toString() === '%PDF', 'acknowledgement slip is a PDF');

  // same sender reference and date again → warning, then confirm
  await op.goto(B + '/Inward/New');
  await op.fill('#sname', 'Deputy General Manager'); await op.fill('#sref', 'SB/E2E/7'); await op.fill('#sdate', today);
  await op.fill('#subj', 'Duplicate check (e2e)'); await op.fill('#kept', 'Registry rack 1');
  await submit(op, 'form.card button[type=submit]');
  await op.goto(B + '/Inward/New');
  await op.fill('#sname', 'Deputy General Manager'); await op.fill('#sref', 'SB/E2E/7'); await op.fill('#sdate', today);
  await op.fill('#subj', 'Duplicate check again (e2e)'); await op.fill('#kept', 'Registry rack 1');
  await submit(op, 'form.card button[type=submit]');
  ck((await op.content()).includes('same sender') && await op.locator('button:has-text("Confirm and save")').count() === 1, 'same reference warns before saving');
  await submit(op, 'button:has-text("Confirm and save")');
  ck(/\/Inward\/Item\/\d+/.test(op.url()), 'saved after confirmation');

  // ---- holder (staff): My Desk, accept, close as replied
  const st = await as('staff');
  await st.goto(B + '/Desk');
  ck((await st.content()).includes(number), 'item appears on the holder\'s My Desk');
  await st.goto(letterUrl);
  await submit(st, 'button:has-text("Accept")');
  ck((await st.content()).includes('Accepted.'), 'holder accepted');
  await st.selectOption('#ctype', 'Replied');
  ck(await st.locator('#crd').isVisible(), 'reply-date field shown for "Replied"');
  ck(!(await st.locator('#cfile').isVisible()), 'file-number field hidden for "Replied"');
  await st.fill('#cout', 'NCS/OUT/2026/55'); await st.fill('#crem', 'Concept note sent.');
  await submit(st, 'form:has(#ctype) button[type=submit]');
  ck((await st.content()).includes('Item closed.'), 'holder closed the item');

  // ---- confidential masking and 404s
  await op.goto(B + '/Inward/Register?Status=open&Type=LegalNotice');
  const confHref = await op.locator('table a[href^="/Inward/Item/"]').first().getAttribute('href');
  ck((await op.content()).includes('Confidential'), 'operator sees a confidential subject masked in the register');
  await op.goto(B + confHref);
  ck((await op.content()).includes('Notice regarding a contract dispute'), 'the operator who inwarded it keeps full access');
  ck(await status(st, confHref) === 404, 'unrelated staff get 404 for the confidential item');
  const coo = await as('coo');
  await coo.goto(B + confHref);
  ck((await coo.content()).includes('Notice regarding a contract dispute'), 'Inward Viewer sees the confidential subject');
  const aud = await as('auditor');
  await aud.goto(B + confHref);
  ck(!(await aud.content()).includes('Notice regarding a contract dispute'), 'Auditor sees the confidential item masked');
  ck(await status(st, '/Inward/Register') === 404, 'staff without a grant cannot open the register');

  // ---- self-inward (bdu): own office, held by themselves
  const bdu = await as('bdu');
  await bdu.goto(B + '/Inward/New');
  ck((await bdu.content()).includes('you received directly'), 'self-inward form for an ordinary user');
  await bdu.selectOption('#type', 'Email'); await bdu.selectOption('#mode', 'Email');
  await bdu.fill('#sname', 'Programme Officer'); await bdu.fill('#sorg', 'Sample Foundation (e2e)'); await bdu.fill('#subj', 'Expression of interest — FPO study (e2e)');
  await bdu.fill('#kept', 'Mailbox folder EOI/2026');
  await submit(bdu, 'form.card button[type=submit]');
  ck(/\/Inward\/Item\/\d+/.test(bdu.url()) && (await bdu.content()).includes('self-inwarded'), 'self-inwarded e-mail');

  // ---- invoices: duplicate block and override
  await op.goto(B + '/Inward/New?type=Invoice');
  ck(await op.locator('#vname').isVisible(), 'invoice section shown for type Invoice');
  await op.fill('#sname', 'Accounts Manager'); await op.fill('#subj', 'Invoice again (e2e)'); await op.fill('#kept', 'Accounts tray');
  await op.fill('#vname', 'Sample Survey Agency Pvt Ltd (SYNTHETIC)'); await op.fill('#gstin', '27AAPFU0939F1ZV');
  await op.fill('#ino', 'ssa-26-27-041'); await op.fill('#idate', plus(-50)); await op.fill('#tot', '1,18,000');
  await submit(op, 'form.card button[type=submit]');
  ck((await op.content()).includes('already inwarded as'), 'duplicate invoice is blocked');
  await op.check('input[name="Input.Invoice.NotDuplicate"]'); await op.fill('#ovr', 'Separate invoice with a reused number (e2e)');
  await submit(op, 'form.card button[type=submit]');
  ck((await op.content()).includes('was marked MSME'), 'MSME hint from an earlier invoice of the same GSTIN');
  await submit(op, 'button:has-text("Confirm and save")');
  ck(/\/Inward\/Item\/\d+/.test(op.url()), 'override saves the invoice');
  ck((await op.content()).includes('not a duplicate'), 'override is flagged on the item');
  await op.goto(B + '/Inward/New?type=Invoice');
  await op.fill('#sname', 'Accounts'); await op.fill('#subj', 'Bad GSTIN (e2e)'); await op.fill('#kept', 'Tray');
  await op.fill('#vname', 'X (e2e)'); await op.fill('#gstin', '27AAPFU0939F1ZX'); await op.fill('#ino', '1'); await op.fill('#idate', today); await op.fill('#tot', '100');
  await submit(op, 'form.card button[type=submit]');
  ck((await op.content()).includes('GSTIN'), 'wrong GSTIN check digit refused');

  // ---- verifier (staff holds the demo printing invoice): certify and send to Accounts
  await st.goto(B + '/Inward/Mine');
  const invHref = await st.locator('table a:has-text("NCS/IN/HO")').evaluateAll(as => as.map(a => a.getAttribute('href')));
  let printing = null;
  for (const h of invHref) { await st.goto(B + h); if ((await st.content()).includes('SP-778')) { printing = h; break; } }
  ck(printing != null, 'verifier finds the invoice marked to them');
  await submit(st, 'button:has-text("Certify")');
  ck((await st.content()).includes('Invoice certified.'), 'invoice certified');
  await submit(st, 'button:has-text("Send the certified invoice to Accounts")');
  ck((await st.content()).includes('Sent to Accounts.'), 'sent to Accounts');

  // ---- Accounts (approver2): pay in full, invoice dashboard, MSME report, ERP preview
  const ac = await as('approver2');
  await ac.goto(B + '/Desk');
  ck((await ac.content()).includes('Accounts — invoices to pay'), 'Accounts queue on My Desk');
  await ac.goto(B + printing);
  await ac.fill('#pnet', '12,400'); await ac.fill('#pv', 'UTR-E2E-1');
  await submit(ac, 'button:has-text("Record payment")');
  ck((await ac.content()).includes('Payment recorded.') && (await ac.content()).includes('Paid'), 'full payment marks the invoice paid');
  await ac.click('summary:has-text("Reverse")'); await ac.fill('input[name=reason]', 'Wrong beneficiary (e2e)');
  await submit(ac, 'form:has(input[name=paymentId]) button[type=submit]');
  ck((await ac.content()).includes('Payment reversed.'), 'payment reversed and invoice reopened');
  await ac.fill('#pnet', '12,400'); await ac.fill('#pv', 'UTR-E2E-1');
  await submit(ac, 'button:has-text("Record payment")');
  ck((await ac.content()).includes('Payment recorded.'), 'the same voucher can be recorded again after a reversal');
  await ac.goto(B + '/Inward/Invoices');
  ck(await ac.locator('.stat').count() >= 6, 'invoice dashboard tiles');
  await ac.screenshot({ path: OUT + '/i3-invoices.png', fullPage: true });
  const msme = await download(ac, '/Inward/Invoices?handler=Msme&asOn=' + today);
  ck(msme.slice(0, 2).toString() === 'PK', 'MSME / 43B(h) report is an Excel file');
  const csv = 'InwardNo,PaidOn,Amount,TDS,VoucherRef\r\nNCS/IN/HO/2026-27/00004,' + dot(today) + ',"10,000",0,UTR-E2E-2\r\nNCS/IN/HO/2026-27/00004,' + dot(today) + ',"10,00,000",0,UTR-E2E-3\r\nNOPE/1,' + dot(today) + ',5,0,X\r\n';
  fs.writeFileSync(OUT + '/payments.csv', csv);
  await ac.goto(B + '/Inward/Payments');
  await ac.setInputFiles('#file', OUT + '/payments.csv');
  await submit(ac, 'button:has-text("Check the file")');
  const pv = await ac.content();
  ck(pv.includes('Record 1 payment(s)') && pv.includes('Would overpay') && pv.includes('No invoice matches'), 'ERP preview classifies new, overpay and unmatched rows');
  await submit(ac, 'button:has-text("Record 1 payment(s)")');
  ck((await ac.content()).includes('1 payment(s) recorded.'), 'ERP import recorded the matched row');
  await ac.screenshot({ path: OUT + '/i4-payments.png', fullPage: true });

  // ---- exports and register print
  const xl = await download(op, '/Inward/Register?handler=Export&format=xlsx&Status=all');
  ck(xl.slice(0, 2).toString() === 'PK', 'register Excel export');
  const pdf = await download(op, '/Inward/Register?handler=Export&format=pdf&Status=all&From=' + plus(-7) + '&To=' + today);
  ck(pdf.slice(0, 4).toString() === '%PDF', 'register PDF for a week');

  // ---- access grants (admin)
  const ad = await as('admin');
  await ad.goto(B + '/Inward/Access');
  ck((await ad.content()).includes('Inward Operator'), 'access page lists grants');
  ck(await ad.locator('#gu option:has-text("System Administrator")').count() === 0, 'an administrator cannot pick themselves');
  await ad.selectOption('#gu', { label: 'Rohan Kapoor (bdu)' }); await ad.selectOption('#gr', 'InwardOperator'); await ad.selectOption('#go', { label: 'New Delhi (DEL)' });
  await submit(ad, 'form:has(#gu) button[type=submit]');
  ck((await ad.content()).includes('Access granted'), 'operator granted for New Delhi');
  await bdu.goto(B + '/Inward/New');
  ck((await bdu.locator('#office').inputValue()) !== '', 'new operator inwards for their office');

  // ---- phone layout
  const ph = await as('publisher', { width: 390, height: 844 });
  await ph.goto(B + letterUrl.replace(B, ''));
  const sw = await ph.evaluate(() => document.documentElement.scrollWidth);
  ck(sw <= 392, 'item page fits a phone (' + sw + 'px)');
  await ph.screenshot({ path: OUT + '/i5-phone.png', fullPage: true });

  ck(errs.length === 0, 'no console errors (CSP)' + (errs.length ? ': ' + errs.slice(0, 3).join(' | ') : ''));
  await b.close();
  console.log(f === 0 ? 'ALL PASSED' : f + ' FAILED');
  process.exit(f === 0 ? 0 : 1);
})().catch(e => { console.error('Error', e); process.exit(1); });
