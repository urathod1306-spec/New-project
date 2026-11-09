// v1.6: compliance management — event, CSV import, officer update, Cell return/accept/close, revision, ATR preview,
// downloads, mark as final and snapshot, access rules, phone layout. Uses the synthetic demo data (cs = Compliance Admin).
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
  const today = new Date(Date.now() + 330 * 60000).toISOString().slice(0, 10);
  const plus = d => new Date(Date.now() + 330 * 60000 + d * 86400000).toISOString().slice(0, 10);

  // ---- Compliance Cell: dashboard and a new event
  const cs = await as('cs');
  await cs.goto(B + '/Compliance');
  ck(await cs.locator('.mainnav a:has-text("Compliance")').count() === 1, 'Compliance tab in the menu');
  ck(await cs.locator('.stat').count() >= 8, 'dashboard tiles');
  ck((await cs.content()).includes('Board of Directors'), 'source x status matrix lists the demo Board meeting');
  await cs.screenshot({ path: OUT + '/c1-dashboard.png', fullPage: true });
  await cs.goto(B + '/Compliance/EventEdit');
  await cs.selectOption('#src', 'StatutoryAudit');
  await cs.selectOption('#forum', { label: 'Statutory Auditor' });
  await cs.fill('#no', 'FY 2025-26'); await cs.fill('#title', 'Statutory audit report (e2e)'); await cs.fill('#recv', today);
  await cs.fill('#kept', 'File F&A/SA/2026');
  await submit(cs, 'form.card button[type=submit]');
  ck(/\/Compliance\/Event\/\d+/.test(cs.url()), 'event registered');
  const eventUrl = cs.url();

  // ---- CSV import: preview then confirm
  const csv = 'RefNo,ActionPoint,PrimaryOfficer,DueDate,Category\r\nPara 4.1,"Unbilled revenue to be reconciled with project ledgers",staff,' + plus(30).split('-').reverse().join('.') + ',Finance & Accounts\r\n' +
              'Para 4.2,Provision for doubtful debts to be reviewed,approver2,' + plus(45).split('-').reverse().join('.') + ',\r\n';
  fs.writeFileSync(OUT + '/import.csv', csv);
  await cs.click('a:has-text("Import from CSV")'); await cs.waitForLoadState('networkidle');
  await cs.setInputFiles('#file', OUT + '/import.csv');
  await submit(cs, 'button:has-text("Check the file")');
  ck((await cs.content()).includes('Import 2 action point(s)'), 'import preview shows 2 valid rows');
  await cs.screenshot({ path: OUT + '/c2-import-preview.png', fullPage: true });
  await submit(cs, 'button:has-text("Import 2 action point(s)")');
  ck((await cs.content()).includes('2 action point(s) imported'), 'points imported');
  const pointHref = await cs.locator('table a[href^="/Compliance/Point/"]').first().getAttribute('href');

  // ---- officer: My Desk, submit compliance
  const st = await as('staff');
  await st.goto(B + '/Desk');
  ck((await st.content()).includes('Compliance — my action points'), 'officer sees the compliance section on My Desk');
  await st.goto(B + pointHref);
  await st.fill('#text', 'Reconciliation started for 18 projects.');
  await submit(st, 'button:has-text("Submit to the Compliance Cell")');
  ck((await st.content()).includes('Update awaiting review'), 'update submitted');

  // ---- Cell: queue, return
  await cs.goto(B + '/Compliance/Review');
  ck((await cs.content()).includes('Updates awaiting review (') && await cs.locator('a[href="' + pointHref + '"]').count() >= 1, 'update is in the Cell queue');
  await cs.goto(B + pointHref);
  await cs.fill('#retRemarks', 'Please give the amount reconciled.');
  await submit(cs, 'button:has-text("Return to the officer")');
  await st.goto(B + pointHref);
  ck((await st.content()).includes('was returned by the Compliance Cell'), 'officer sees the returned update');
  await st.fill('#text', 'Reconciled ₹ 1.2 crore across 18 projects; differences cleared.');
  await st.selectOption('#proposed', 'ProposedClosure');
  await submit(st, 'button:has-text("Submit to the Compliance Cell")');

  // ---- Cell: accept with edited wording, close with authority
  await cs.goto(B + pointHref);
  await cs.fill('#atrText', 'Unbilled revenue reconciled (₹ 1.2 crore, 18 projects); differences cleared.');
  await submit(cs, 'form[action*="handler=Accept"] button');
  ck((await cs.content()).includes('Proposed for closure'), 'accepted: proposed for closure');
  await cs.fill('#clAuth', 'Dropped by the statutory auditor in the FY 2026-27 report');
  await cs.fill('#clRemarks', 'Complied.');
  await submit(cs, 'form[action*="handler=Close"] button');
  ck((await cs.content()).includes('Action point closed.'), 'closed with authority');
  await cs.screenshot({ path: OUT + '/c3-point.png', fullPage: true });

  // ---- revision: officer of demo Board point asks, Cell approves
  const a1 = await as('approver1');
  await a1.goto(B + '/Compliance/Mine');
  const memo = await a1.locator('a[href^="/Compliance/Point/"]').first().getAttribute('href');
  await a1.goto(B + memo);
  await a1.fill('#newDate', plus(60)); await a1.fill('#revReason', 'Risk Committee meeting rescheduled.');
  await submit(a1, 'form[action*="handler=Revision"] button');
  await cs.goto(B + memo);
  await submit(cs, 'form[action*="handler=Decide"] button[value="true"]');
  ck((await cs.content()).includes('New due date approved.'), 'revision approved');

  // ---- ATR: pack, preview, downloads, final, snapshot
  await cs.goto(B + '/Compliance/Atr');
  await cs.selectOption('#forumId', { label: 'Board of Directors' });
  await submit(cs, 'button:has-text("Prepare pack")');
  await submit(cs, 'button:has-text("Preview")');
  ck((await cs.content()).includes('Follow-up Actions on the Minutes of the 98th Meeting'), 'Board ATR preview with the title');
  ck((await cs.content()).includes('Annexure I'), 'Annexure I shown');
  await cs.screenshot({ path: OUT + '/c4-atr-preview.png', fullPage: true });
  for (const [label, ext] of [['Download Word (draft)', 'docx'], ['Download PDF (draft)', 'pdf'], ['Download Excel (draft)', 'xlsx']]) {
    const [dl] = await Promise.all([cs.waitForEvent('download'), cs.click('a:has-text("' + label + '")')]);
    const path = OUT + '/atr-draft.' + ext; await dl.saveAs(path);
    ck(dl.suggestedFilename().endsWith('.' + ext) && fs.statSync(path).size > 1500, ext + ' download (' + fs.statSync(path).size + ' bytes)');
  }
  await submit(cs, 'button:has-text("Mark as final")');
  ck(/\/Compliance\/Snapshot\/\d+/.test(cs.url()) && (await cs.content()).includes('Verified'), 'snapshot created and verified');
  const [sdl] = await Promise.all([cs.waitForEvent('download'), cs.click('a.btn:has-text("Word")')]);
  ck(sdl.suggestedFilename().startsWith('ATR-snapshot-'), 'final copy downloaded from the snapshot');
  await cs.screenshot({ path: OUT + '/c5-snapshot.png', fullPage: true });

  // ---- access rules
  const bdu = await as('bdu');
  const r = await bdu.goto(B + pointHref);
  ck(r.status() === 404, 'another user gets 404 on the point');
  ck(await bdu.locator('.mainnav a:has-text("Compliance")').count() === 0, 'no Compliance tab for someone with no points');
  const ad = await as('admin');
  ck((await ad.goto(B + '/Compliance/Points')).status() === 404, 'intranet administrator has no compliance access');

  // ---- phone layout
  const ph = await as('cs', { width: 390, height: 844 });
  await ph.goto(B + '/Compliance');
  const w = await ph.evaluate(() => document.documentElement.scrollWidth);
  ck(w <= 392, 'dashboard fits a phone screen (' + w + ' px)');
  await ph.screenshot({ path: OUT + '/c6-phone.png', fullPage: true });

  ck(errs.length === 0, 'no browser console errors (CSP etc.)' + (errs.length ? ': ' + errs.join(' | ') : ''));
  await b.close();
  console.log(f === 0 ? 'PASSED' : 'FAILED (' + f + ')');
  process.exit(f === 0 ? 0 : 1);
})();
