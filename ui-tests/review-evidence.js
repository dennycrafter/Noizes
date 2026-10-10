/*
 * Reviewer evidence capture (Agent H): interactive states the static
 * section 7 screenshots cannot show. Writes to docs/screenshots/v1.3/review/
 * and prints JSON verdicts for the Claude desktop one-source-of-truth check.
 * Not part of the CI suite; run with: node review-evidence.js
 */
const { chromium } = require('@playwright/test');
const path = require('path');
const fs = require('fs');

const REPO = path.resolve(__dirname, '..');
const OUT = path.join(REPO, 'docs', 'screenshots', 'v1.3', 'review');
const PORT = 4174;

(async () => {
  fs.mkdirSync(OUT, { recursive: true });
  // serve the real UI the same way playwright.config does
  const { spawn } = require('child_process');
  const server = spawn('node', [path.join(__dirname, 'dev-server.js'), '--root', path.join(REPO, 'src', 'Noizes', 'ui'), '--port', String(PORT)], { stdio: 'pipe' });
  await new Promise(r => setTimeout(r, 700));

  const browser = await chromium.launch();
  const context = await browser.newContext({ viewport: { width: 1080, height: 720 }, colorScheme: 'dark' });
  const page = await context.newPage();
  await page.addInitScript({ path: path.join(__dirname, 'mock-bridge.js') });
  await page.goto(`http://127.0.0.1:${PORT}/`, { waitUntil: 'load' });
  await page.waitForFunction(() => document.querySelectorAll('.snd-row').length > 0);
  const results = {};

  // 1. muted master state: switch off -> "Sounds off"; timed mute -> "Muted until ..." + Unmute
  const masterSwitch = page.locator('#masterSwitchHost [role="switch"]');
  await masterSwitch.click();
  await page.waitForTimeout(200);
  results.masterOff = {
    label: (await page.locator('#masterLabel').textContent()).trim(),
    muteBtn: (await page.locator('#muteBtn').textContent()).trim()
  };
  await page.screenshot({ path: `${OUT}/muted-master-1x.png` });

  // unmute via the master switch first: while master-muted the muteBtn acts as
  // Unmute (see UI-REVIEW finding), so the timed-mute capture must start unmuted
  await masterSwitch.click();
  await page.waitForTimeout(150);
  await page.locator('#muteBtn').click();
  await page.waitForTimeout(200);
  results.mute1h = {
    label: (await page.locator('#masterLabel').textContent()).trim(),
    until: (await page.locator('#mutedUntil').textContent()).trim(),
    untilVisible: await page.locator('#mutedUntil').isVisible(),
    muteBtn: (await page.locator('#muteBtn').textContent()).trim()
  };
  await page.screenshot({ path: `${OUT}/muted-until-1x.png` });
  // back to unmuted
  await page.locator('#muteBtn').click();
  await masterSwitch.click();

  // 2. expanded row: click row body -> detail with slider, Choose file, Use default, Test, Advanced
  const firstRow = page.locator('.snd-row').first();
  await firstRow.locator('.snd-rowmain').click();
  await page.waitForTimeout(250);
  results.expanded = {
    slider: await firstRow.locator('input[type="range"]').isVisible(),
    choose: await firstRow.getByRole('button', { name: 'Choose file' }).isVisible(),
    useDefault: await firstRow.getByRole('button', { name: 'Use default' }).isVisible(),
    test: await firstRow.locator('[data-testid="event-test-inline"]').isVisible(),
    advanced: (await firstRow.textContent()).includes('Advanced')
  };
  await page.screenshot({ path: `${OUT}/row-expanded-1x.png` });
  await page.keyboard.press('Escape');
  await page.waitForTimeout(150);
  results.escapeCollapses = !(await firstRow.locator('input[type="range"]').isVisible().catch(() => false));

  // 3. hover state on a row and on a button
  await firstRow.hover();
  await page.waitForTimeout(150);
  await page.screenshot({ path: `${OUT}/row-hover-1x.png` });
  const muteBtn = page.locator('#muteBtn');
  await muteBtn.hover();
  await page.waitForTimeout(150);
  results.hoverBg = await muteBtn.evaluate(el => getComputedStyle(el).backgroundColor);
  await page.screenshot({ path: `${OUT}/button-hover-1x.png` });

  // 4. keyboard focus ring on a switch (Tab until a switch receives focus)
  await page.evaluate(() => document.activeElement && document.activeElement.blur());
  for (let i = 0; i < 40; i++) {
    await page.keyboard.press('Tab');
    const role = await page.evaluate(() => (document.activeElement || {}).getAttribute && document.activeElement.getAttribute('role'));
    if (role === 'switch') break;
  }
  const focused = await page.evaluate(() => {
    const el = document.activeElement;
    if (!el || el.getAttribute('role') !== 'switch') return null;
    const cs = getComputedStyle(el);
    return { outline: cs.outlineStyle + ' ' + cs.outlineWidth + ' ' + cs.outlineColor, ariaChecked: el.getAttribute('aria-checked'), label: el.getAttribute('aria-label') };
  });
  results.focusRing = focused;
  await page.screenshot({ path: `${OUT}/focus-switch-1x.png` });

  // 5. Claude desktop one source of truth: Turn on in Connections -> event row turns on
  await page.locator('.nz-nav-btn[data-page="connections"]').click();
  await page.waitForTimeout(150);
  await page.getByRole('button', { name: 'Turn on the Claude desktop app watcher' }).click();
  await page.waitForTimeout(250);
  results.claudeDesktopSync = await page.evaluate(() => {
    const s = window.__noizesMock.state;
    const ev = s.sounds.find(e => e.id === 'claude-desktop-done');
    return { watcherEnabled: s.watcherEnabled, connectionsEnabled: s.connections.claudeDesktop.enabled, eventEnabled: ev.enabled, agree: s.watcherEnabled && s.connections.claudeDesktop.enabled && ev.enabled };
  });
  const desktopRowText = (await page.locator('.nz-row', { hasText: 'Claude desktop app' }).textContent()).replace(/\s+/g, ' ').trim();
  results.connectionsRowAfterOn = desktopRowText;
  await page.screenshot({ path: `${OUT}/claude-desktop-on-1x.png` });

  // and back off: the event switch on Sounds must reflect the same state
  await page.locator('.nz-nav-btn[data-page="sounds"]').click();
  await page.waitForTimeout(150);
  const desktopEventRow = page.locator('.snd-row', { hasText: 'Claude desktop app: finished' });
  results.soundsRowAfterOn = {
    ariaChecked: await desktopEventRow.locator('[role="switch"]').getAttribute('aria-checked'),
    className: await desktopEventRow.evaluate(el => el.className)
  };
  await desktopEventRow.locator('[role="switch"]').click();
  await page.waitForTimeout(250);
  results.turnEventOff = await page.evaluate(() => {
    const s = window.__noizesMock.state;
    return { watcherEnabled: s.watcherEnabled, eventEnabled: s.sounds.find(e => e.id === 'claude-desktop-done').enabled, bothOff: !s.watcherEnabled && !s.sounds.find(e => e.id === 'claude-desktop-done').enabled };
  });

  console.log(JSON.stringify(results, null, 2));
  await browser.close();
  server.kill();
})().catch(e => { console.error(e); process.exit(1); });
