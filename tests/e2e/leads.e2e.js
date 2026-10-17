const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const B = process.env.BASE_URL || 'http://127.0.0.1:8080', IMG = process.argv[2], OUT = process.argv[3];
(async () => { const b = await chromium.launch(); let f = 0; const ck = (c, m) => { console.log((c ? 'PASS ' : 'FAIL ') + m); if (!c) f++; };
  const errs = [];
  const as = async (user, vw) => { const ctx = await b.newContext({ viewport: vw || { width: 1366, height: 900 }, acceptDownloads: true }); const p = await ctx.newPage(); p.on('dialog', d => d.accept());
    p.on('console', m => { if (m.type() === 'error' && !/403|404/.test(m.text())) errs.push(user + ': ' + m.text()); });
    await p.goto(B + '/Account/Login'); await p.fill('#Username', user); await p.fill('#Password', 'Nabcons@2026'); await p.click('button:has-text("Sign in")'); await p.waitForLoadState('networkidle'); return p; };
  const fill = async (p, o) => { for (const [k, v] of Object.entries(o)) { const sel = '#Input_' + k; if ((await p.getAttribute(sel, 'type')) === null && await p.locator('select' + sel).count()) await p.selectOption(sel, v); else await p.fill(sel, v); } };

  // staff submits a lead with attachment
  const st = await as('staff');
  ck(await st.locator('.mainnav a:has-text("Business Leads")').count() === 1, 'Business Leads in nav for everyone');
  await st.goto(B + '/Leads/New'); await st.screenshot({ path: OUT + '/l1-form.png', fullPage: true });
  ck(await st.locator('.tabs a:has-text("Pipeline")').count() === 0, 'no pipeline tab for ordinary staff');
  await fill(st, { Title: 'Impact evaluation of watershed programme', ClientName: 'State Rural Livelihood Mission', ClientType: 'State Govt', Sector: 'Watershed & Natural Resources',
    StateLocation: 'Rajasthan', Source: 'Tender portal', TenderNumber: 'SRLM/2026/EV/14', EstimatedValueLakh: '45.5', Description: 'Third-party evaluation across 12 districts.' });
  const due = new Date(Date.now() + 3 * 864e5).toISOString().slice(0, 10); await st.fill('#Input_DueDate', due);
  await st.setInputFiles('#Uploads', [IMG + '/notreal.pdf']); await st.click('#lead-submit'); await st.waitForLoadState('networkidle');
  ck((await st.content()).includes('does not match a genuine .pdf'), 'disguised attachment rejected, no lead created');
  await st.setInputFiles('#Uploads', [IMG + '/proposal.pdf']); await st.click('#lead-submit'); await st.waitForLoadState('networkidle');
  const msg = await st.locator('.alert.success').textContent(); const ref = (msg.match(/NCS\/BD\/\d{4}-\d{2}\/\d{4}/) || [''])[0];
  ck(/NCS\/BD\/20\d\d-\d\d\/0001/.test(ref), 'reference generated: ' + ref);
  const leadUrl = st.url(); const leadId = leadUrl.split('/').pop();
  await st.screenshot({ path: OUT + '/l2-submitted.png', fullPage: true });
  const attHref = await st.getAttribute('a[href^="/Leads/Attachment/"]', 'href');
  // duplicate warning
  await st.goto(B + '/Leads/New'); await fill(st, { Title: 'Watershed programme impact evaluation', ClientName: 'State Rural Livelihood Mission', Description: 'Same thing again' });
  await st.click('#lead-submit'); await st.waitForSelector('#dup-live:not(.hidden)'); ck((await st.textContent('#dup-live')).includes(ref), 'duplicate warning shows existing reference');
  await st.screenshot({ path: OUT + '/l3-duplicate.png', fullPage: true });
  await Promise.all([st.waitForNavigation(), st.click('#lead-submit')]);
  ck((await st.content()).includes('/0002'), 'submit anyway after warning -> second reference');
  await st.goto(B + '/Leads/Mine'); await st.screenshot({ path: OUT + '/l4-mine.png', fullPage: true }); ck(await st.locator('table.table tbody tr').count() === 2, 'My Leads lists both');
  // staff follow-up note while Submitted
  await st.goto(leadUrl); await st.fill('#note', 'Pre-bid meeting on Monday.'); await st.click('button:has-text("Add")'); await st.waitForLoadState('networkidle');
  ck((await st.content()).includes('Pre-bid meeting on Monday.'), 'submitter follow-up note added');
  // pipeline forbidden for staff and auditor/admin
  for (const [u, p] of [['staff', st], ['auditor', await as('auditor')], ['admin', await as('admin')]]) {
    const r = await p.goto(B + '/Leads'); ck(r.status() === 403, u + ': pipeline 403');
    const d = await p.goto(B + '/Leads/Dashboard'); ck(d.status() === 403, u + ': dashboard 403');
  }
  // another user cannot open someone else's lead or its attachment by URL
  const other = await as('publisher');
  ck((await other.goto(leadUrl)).status() === 403, "other user: someone else's lead 403");
  ck((await other.goto(B + attHref)).status() === 403, "other user: someone else's attachment 403");
  ck((await other.goto(B + '/Leads?handler=Export')).status() === 403, 'other user: export 403');

  // BDU member manages the lead
  const bdu = await as('bdu');
  ck((await bdu.locator('.mainnav a:has-text("Business Leads") .count').textContent()).trim() === '2', 'BDU nav badge counts Submitted leads');
  await bdu.goto(B + '/Leads'); await bdu.screenshot({ path: OUT + '/l5-pipeline.png', fullPage: true });
  ck(await bdu.locator('tr.row-due').count() >= 1, 'due-within-7-days highlighted');
  await bdu.goto(leadUrl);
  { const r = await bdu.request.get(B + attHref); ck(r.status() === 200 && (r.headers()['content-disposition'] || '').startsWith('attachment'), 'BDU can download attachment (Content-Disposition: attachment)'); }
  await bdu.goto(leadUrl);
  await bdu.selectOption('#newStatus', 'Lost'); await bdu.fill('#remarks', 'Client chose another bidder');
  await bdu.evaluate(() => { document.querySelectorAll('#lossReason').forEach(e => e.required = false); });
  await bdu.click('button:has-text("Update status")'); await bdu.waitForLoadState('networkidle');
  ck((await bdu.content()).includes('Choose a reason'), 'Lost without reason rejected');
  await bdu.selectOption('#newStatus', 'Under Review'); await bdu.evaluate(() => document.getElementById('remarks').required = false);
  await bdu.click('button:has-text("Update status")'); await bdu.waitForLoadState('networkidle');
  ck((await bdu.content()).includes('remark is required'), 'status change without remark rejected');
  await bdu.selectOption('#newStatus', 'Under Review'); await bdu.fill('#remarks', 'Looks promising; checking eligibility'); await bdu.click('button:has-text("Update status")'); await bdu.waitForLoadState('networkidle');
  ck((await bdu.content()).includes('Status changed to “Under Review”'), 'status -> Under Review');
  await bdu.selectOption('#ownerUserId', { label: 'Rohan Kapoor' }); await bdu.click('button:has-text("Save owner")'); await bdu.waitForLoadState('networkidle');
  ck((await bdu.content()).includes('Lead owner updated'), 'owner assigned');
  await bdu.setInputFiles('input[name=uploads]', IMG + '/proposal.pdf'); await bdu.fill('#note', 'Draft proposal attached'); await bdu.click('button:has-text("Add")'); await bdu.waitForLoadState('networkidle');
  ck(await bdu.locator('.badge.info:has-text("BDU")').count() === 1, 'BDU-side attachment added');
  await bdu.selectOption('#newStatus', 'Won'); await bdu.fill('#remarks', 'LoA received'); await bdu.fill('#awardedValueLakh', '42'); await bdu.fill('#awardDate', '2026-10-15');
  await bdu.click('button:has-text("Update status")'); await bdu.waitForLoadState('networkidle');
  ck((await bdu.content()).includes('Awarded</dt><dd>₹ 42 lakh'), 'Won with award value/date');
  await bdu.screenshot({ path: OUT + '/l6-detail.png', fullPage: true });
  const timeline = await bdu.locator('.timeline li').count(); ck(timeline >= 7, 'timeline has every event: ' + timeline);
  // submitter can no longer add notes once past Under Review
  await st.goto(leadUrl); ck(await st.locator('#note').count() === 0, 'submitter follow-up closed after Under Review');
  // COO (LeadViewer) sees pipeline + dashboard; can mark the other lead dropped
  const coo = await as('coo');
  ck((await coo.goto(B + '/Leads')).status() === 200, 'COO (LeadViewer) sees pipeline');
  await coo.goto(B + '/Leads/Dashboard'); await coo.screenshot({ path: OUT + '/l7-dashboard.png', fullPage: true });
  ck((await coo.content()).includes('100%'), 'conversion rate Won/(Won+Lost) shown');
  await coo.goto(B + '/Leads');
  const [dl] = await Promise.all([coo.waitForEvent('download'), coo.click('a:has-text("Export to Excel")')]);
  const xp = OUT + '/pipeline.xlsx'; await dl.saveAs(xp); ck(require('fs').statSync(xp).size > 1000, 'Excel export downloaded');
  // mobile pipeline
  const m = await as('bdu', { width: 390, height: 844 }); await m.goto(B + '/Leads'); await m.screenshot({ path: OUT + '/l8-pipeline-mobile.png', fullPage: true });
  ck(!(await m.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1)), 'pipeline page has no horizontal page scroll on phone');
  await m.goto(B + '/Leads/New'); ck(!(await m.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1)), 'lead form fits phone width');
  ck(errs.length === 0, 'no console/CSP errors ' + JSON.stringify(errs.slice(0, 3)));
  await b.close(); console.log(f ? f + ' FAILED' : 'ALL LEAD CHECKS PASSED'); })().catch(e => { console.error(e); process.exit(2); });
