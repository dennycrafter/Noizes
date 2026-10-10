/*
 * Section 7, one-click switching: the event switch, the group switch and the
 * master mute switch all save instantly over the bridge. No Save button exists
 * in the main UI.
 */
const { test, expect } = require('@playwright/test');
const { open, calls, gotoPage, PAGES } = require('./helpers');

test.describe('one-click switching and mute', () => {
  test('one click on an event switch sends setEvent enabled false and dims the row', async ({ page }) => {
    await open(page);
    const row = page.locator('[data-event-id="gh-push"]');
    const sw = row.locator('button[role="switch"]');
    await expect(sw).toHaveAttribute('aria-checked', 'false'); // fresh install: gh-push is off

    await sw.click();
    await expect(sw).toHaveAttribute('aria-checked', 'true');
    await expect(row).not.toHaveClass(/is-off/);

    const all = await calls(page);
    const set = all.filter(c => c.type === 'setEvent' && c.payload.eventId === 'gh-push');
    expect(set).toHaveLength(1);
    expect(set[0].payload.enabled).toBe(true);
  });

  test('no Save button exists on any main page', async ({ page }) => {
    await open(page);
    for (const name of PAGES) {
      await gotoPage(page, name);
      const saveButtons = page.getByRole('button', { name: 'Save', exact: true });
      await expect(saveButtons).toHaveCount(0);
    }
  });

  test('group switch turns every row in its group off', async ({ page }) => {
    await open(page);
    const box = page.locator('section.snd-group[data-group="Deploys"]');
    const sw = box.locator('.snd-group-head button[role="switch"]');
    await expect(sw).toHaveAttribute('aria-checked', 'true'); // both deploys start on

    await sw.click();
    await expect(sw).toHaveAttribute('aria-checked', 'false');
    for (const id of ['deploy-succeeded', 'deploy-failed']) {
      await expect(page.locator(`[data-event-id="${id}"]`)).toHaveClass(/is-off/);
      await expect(page.locator(`[data-event-id="${id}"] button[role="switch"]`)).toHaveAttribute('aria-checked', 'false');
    }
    const all = await calls(page);
    const set = all.filter(c => c.type === 'setGroup');
    expect(set).toHaveLength(1);
    expect(set[0].payload).toMatchObject({ group: 'Deploys', enabled: false });
  });

  test('a mixed group shows the middle state and clicking it turns all on', async ({ page }) => {
    await open(page);
    const box = page.locator('section.snd-group[data-group="Browser"]'); // 2 of 4 on in the mock state
    const sw = box.locator('.snd-group-head button[role="switch"]');
    await expect(sw).toHaveAttribute('data-state', 'mixed'); // the middle state
    await expect(sw).toHaveAttribute('aria-checked', 'false'); // switch role stays binary

    await sw.click();
    await expect(sw).not.toHaveAttribute('data-state', 'mixed'); // back to a plain all-on switch
    for (const id of ['browser-claude-done', 'browser-obvious-done', 'browser-watch-done', 'browser-download-done']) {
      await expect(page.locator(`[data-event-id="${id}"] button[role="switch"]`)).toHaveAttribute('aria-checked', 'true');
    }
    const all = await calls(page);
    const set = all.filter(c => c.type === 'setGroup');
    expect(set).toHaveLength(1);
    expect(set[0].payload).toMatchObject({ group: 'Browser', enabled: true });
  });

  test('master switch off sends setMute muted true and the header says Sounds off', async ({ page }) => {
    await open(page);
    await page.click('#masterSwitchHost [role="switch"]');
    await expect(page.locator('#masterLabel')).toHaveText('Sounds off');
    await expect(page.locator('#masterSwitchHost [role="switch"]')).toHaveAttribute('aria-checked', 'false');

    const all = await calls(page);
    const set = all.filter(c => c.type === 'setMute');
    expect(set).toHaveLength(1);
    expect(set[0].payload.muted).toBe(true);
  });

  test('Mute 1 hour sends a timed mute and flips to Muted until', async ({ page }) => {
    await open(page);
    await page.click('#muteBtn');
    await expect(page.locator('#mutedUntil')).toHaveText(/^Muted until/);
    await expect(page.locator('#muteBtn')).toHaveText('Unmute');

    const all = await calls(page);
    const set = all.filter(c => c.type === 'setMute');
    expect(set).toHaveLength(1);
    expect(set[0].payload.muted).toBe(true);
    expect(set[0].payload.minutes).toBe(60);
  });

  test('the master switch is present on every page', async ({ page }) => {
    await open(page);
    for (const name of PAGES) {
      await gotoPage(page, name);
      await expect(page.locator('#masterSwitchHost [role="switch"]')).toBeVisible();
      await expect(page.locator('#muteBtn')).toBeVisible();
    }
  });
});
