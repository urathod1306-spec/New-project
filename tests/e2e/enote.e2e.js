// v1.3 Phase 2: e-Note end to end (the chain from the brief), PDF + closure, visibility, keyboard-only use, phone layout.
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const { execFileSync } = require('child_process');
const fs = require('fs');
const B = process.env.BASE_URL || 'http://127.0.0.1:8080', IMG = process.argv[2], OUT = process.argv[3];
(async () => {
  const b = await chromium.launch(); let f = 0; const ck = (c, m) => { console.log((c ? 'PASS ' : 'FAIL ') + m); if (!c) f++; };
  const errs = [];
  const as = async (user, vw) => {
    const ctx = await b.newContext({ viewport: vw || { width: 1366, height: 900 }, acceptDownloads: true }); const p = await ctx.newPage(); p.on('dialog', d => d.accept());
    p.on('console', m => { if (m.type() === 'error' && !/404/.test(m.text())) errs.push(user + ': ' + m.text()); });
    await p.goto(B + '/Account/Login'); await p.fill('#Username', user); await p.fill('#Password', 'Nabcons@2026');
    await p.click('button[type=submit]'); await p.waitForLoadState('networkidle'); return p;
  };
  // keyboard-only "Mark to": type, arrow down, Enter picks; Tab to the remarks; Enter on the button submits
  const markTo = async (p, name, remarks) => {
    await p.focus('#toQuery'); await p.keyboard.type(name.slice(0, 5), { delay: 30 });
    await p.waitForSelector('#toList li[role=option]'); await p.keyboard.press('Enter');
    const chosen = await p.textContent('#toChosen');
    if (remarks) { await p.focus('#markRemarks'); await p.keyboard.type(remarks); }
    await p.focus('.mark-form button[type=submit]');
    await Promise.all([p.waitForNavigation(), p.keyboard.press('Enter')]);
    return chosen;
  };
  const act = async (p, handler, remarks) => {
    await p.fill('#decRemarks', remarks);
    await Promise.all([p.waitForNavigation(), p.click(`.decide-panel button[formaction*="handler=${handler}"]`)]);
  };
  const flash = async p => (await p.locator('.alert').allTextContents()).join(' | ');

  // 1. initiator drafts with two attachments
  const st = await as('staff');
  await st.goto(B + '/ENotes/New');
  await st.fill('#Subject', 'Engagement of a GIS consultant for the Odisha study');
  await st.selectOption('#Category', 'Projects & assignments');
  await st.fill('#Body', 'The Odisha watershed study needs GIS mapping of 40 micro-watersheds.\n\nTwo quotations are attached.\n\nपरामर्शदाता की नियुक्ति हेतु अनुमोदन अपेक्षित है।');
  await st.check('input[name=Declaration]');
  await st.setInputFiles('#Uploads', [IMG + '/proposal.pdf', IMG + '/scope.docx']);
  await st.screenshot({ path: OUT + '/e1-new.png', fullPage: true });
  await Promise.all([st.waitForNavigation(), st.click('button:has-text("Save draft")')]);
  const noteUrl = st.url(); const id = noteUrl.split('/').pop();
  ck(/\/ENotes\/\d+$/.test(noteUrl) && (await flash(st)).includes('Draft saved'), 'draft saved: ' + noteUrl);
  ck(await st.locator('.att-list li').count() === 2, 'two draft attachments');
  ck((await st.content()).includes('Draft #'), 'no number until submitted');

  // 2. initiator marks to A (approver1) — keyboard-only type-ahead
  const chosenA = await markTo(st, 'Rajesh Iyer', null);
  ck(chosenA.includes('Rajesh Iyer'), 'type-ahead picked by keyboard: ' + chosenA.trim());
  const number = ((await st.textContent('.mono-num')) || '').trim();
  ck(/^NCS\/EN\/RID\/\d{4}-\d{2}\/\d{4}$/.test(number), 'note number assigned on submission: ' + number);
  ck((await st.locator('.action-panel').count()) === 0, 'initiator has no action panel once the note has moved');

  // 3. A comments, attaches and marks to B
  const a = await as('approver1');
  await a.goto(noteUrl);
  await a.fill('#commentText', 'The rates are within the empanelment ceiling.'); await Promise.all([a.waitForNavigation(), a.click('button:has-text("Add comment")')]);
  await a.setInputFiles('#uploads', IMG + '/proposal.pdf'); await Promise.all([a.waitForNavigation(), a.click('button:has-text("Attach")')]);
  ck(await a.locator('.att-list li').count() === 3, 'A attached a file');
  await markTo(a, 'Anita Deshmukh', 'Finance concurrence requested.');
  ck((await a.content()).includes('Marked to Anita Deshmukh'), 'A marked to B');

  // 4. B sends back to A
  const bb = await as('approver2');
  await bb.goto(noteUrl); await bb.screenshot({ path: OUT + '/e2-holder.png', fullPage: true });
  await act(bb, 'SendBack', 'Budget head to be confirmed by the reviewing officer.');
  ck((await flash(bb)).includes('Sent back'), 'B sent back to A');

  // 5. A marks to C (COO); 6. C returns to the initiator
  await a.goto(noteUrl); await markTo(a, 'Kavita Rao', 'Budget head confirmed. Recommended.');
  const c = await as('coo'); await c.goto(noteUrl);
  await act(c, 'Return', 'Please add the expected deliverables and timeline.');
  ck((await flash(c)).includes('Returned'), 'C returned to the initiator');

  // 7. initiator revises (version 2) and re-submits to C
  await st.goto(noteUrl);
  ck((await st.content()).includes('returned to you'), 'initiator sees the returned banner');
  await st.click('a:has-text("Revise the note")'); await st.waitForLoadState('networkidle');
  ck(await st.locator('input[name=Confidential]').count() === 0, 'confidential flag locked after first submission');
  await st.fill('#Body', (await st.inputValue('#Body')) + '\n\nDeliverables: maps of 40 micro-watersheds within 10 weeks.');
  await Promise.all([st.waitForNavigation(), st.click('button:has-text("Save")')]);
  await markTo(st, 'Kavita Rao', 'Revised with deliverables and timeline.');
  ck(await st.locator('#v option').count() === 2, 'two text versions on record');
  // 8. C approves
  await c.goto(noteUrl); await act(c, 'Approve', 'Approved. Engage the consultant at the quoted rate.');
  ck((await flash(c)).includes('Approved'), 'C approved');
  await c.screenshot({ path: OUT + '/e3-approved.png', fullPage: true });

  // initiator: My Desk shows it awaiting closure; PDF; close
  await st.goto(B + '/Desk');
  ck((await st.content()).includes('Approved — awaiting my closure') && (await st.content()).includes('Engagement of a GIS consultant'), 'My Desk: approved — awaiting closure');
  await st.screenshot({ path: OUT + '/e4-desk.png', fullPage: true });
  await st.goto(noteUrl);
  const [dl] = await Promise.all([st.waitForEvent('download'), st.click('a:has-text("Download PDF")')]);
  const pdfPath = OUT + '/note.pdf'; await dl.saveAs(pdfPath);
  let text = ''; try { text = execFileSync('pdftotext', ['-layout', pdfPath, '-']).toString(); } catch (e) { text = ''; }
  ck(text.includes(number) && text.includes('not digitally signed') && /Page 1 of \d+/.test(text) && text.includes('Record SHA-256'), 'PDF: number, footer, page x of y, record hash');
  ck(text.includes('Deliverables: maps of 40') && text.includes('Kavita Rao') && text.includes('Attachment schedule'), 'PDF: approved text, note-sheet and attachment schedule');
  try { execFileSync('pdftoppm', ['-r', '60', '-png', '-f', '1', '-l', '1', pdfPath, OUT + '/e5-pdf']); } catch (e) { }
  await st.goto(noteUrl);
  await Promise.all([st.waitForNavigation(), st.click('button:has-text("Close / file")')]);
  ck((await st.content()).includes('Closed (Approved)'), 'initiator closed the note');
  ck(await st.locator('.action-panel, a:has-text("Revise")').count() === 0, 'closed note is read-only');

  // unrelated staff member sees it under Approved notes; a confidential note stays hidden
  await st.goto(B + '/ENotes/New');
  await st.fill('#Subject', 'Confidential staffing proposal'); await st.selectOption('#Category', 'General administration');
  await st.fill('#Body', 'Restricted.'); await st.check('input[name=Confidential]'); await st.check('input[name=Declaration]');
  await Promise.all([st.waitForNavigation(), st.click('button:has-text("Save draft")')]);
  const confUrl = st.url();
  await markTo(st, 'Rajesh Iyer', null);
  await a.goto(confUrl); await act(a, 'Approve', 'Approved.');
  const pub = await as('publisher');
  await pub.goto(B + '/ENotes?tab=approved');
  ck((await pub.content()).includes('Engagement of a GIS consultant'), 'unrelated staff: approved note listed under Approved notes');
  ck(!(await pub.content()).includes('Confidential staffing proposal'), 'unrelated staff: confidential approved note not listed');
  ck((await pub.goto(confUrl)).status() === 404, 'unrelated staff: confidential note by URL -> 404');
  ck((await pub.goto(noteUrl)).status() === 200, 'unrelated staff: approved note opens');
  ck((await pub.goto(noteUrl + '/Pdf')).status() === 404, 'unrelated staff: no PDF (initiator only)');
  const au = await as('auditor');
  ck((await au.goto(confUrl)).status() === 404, 'auditor: confidential note content not shown (metadata only)');
  await au.goto(B + '/ENotes/Register');
  ck((await au.content()).includes(number), 'auditor: register lists the note');
  await au.screenshot({ path: OUT + '/e6-register.png', fullPage: true });

  // phone layout
  const ph = await as('approver1', { width: 390, height: 844 });
  await ph.goto(noteUrl);
  ck(!(await ph.evaluate(() => document.documentElement.scrollWidth > window.innerWidth + 1)), 'note view: no horizontal scroll at phone width');
  await ph.screenshot({ path: OUT + '/e7-phone.png', fullPage: true });
  await ph.goto(B + '/ENotes?tab=seen');
  ck(!(await ph.evaluate(() => document.documentElement.scrollWidth > window.innerWidth + 1)), 'list: no horizontal scroll at phone width');

  ck(errs.length === 0, 'no console errors (CSP etc.): ' + JSON.stringify(errs.slice(0, 3)));
  await b.close();
  console.log(f ? `FAILED ${f}` : 'PASSED'); process.exit(f ? 1 : 0);
})().catch(e => { console.error(e); process.exit(1); });
