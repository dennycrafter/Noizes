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
    const row = page.locator('[data-event="gh-push"]');
    const sw = row.locator('button[role="switch"]');
    await expect(sw).toHaveAttribute('aria-checked', 'true');

    await sw.click();
    await expect(sw).toHaveAttribute('aria-checked', 'false');
    await expect(row).toHaveAttribute('data-disabled', 'true');

    const all = await calls(page);
    const set = all.filter(c => c.type === 'setEvent' && c.payload.eventId === 'gh-push');
    expect(set).toHaveLength(1);
    expect(set[0].payload.enabled).toBe(false);
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
    const box = page.locator('.group-box[data-group="Deploys"]');
    const sw = box.locator('.group-head button[role="switch"]');
    await expect(sw).toHaveAttribute('aria-checked', 'true'); // both deploys start on

    await sw.click();
    await expect(sw).toHaveAttribute('aria-checked', 'false');
    for (const id of ['deploy-succeeded', 'deploy-failed']) {
      await expect(page.locator(`[data-event="${id}"]`)).toHaveAttribute('data-disabled', 'true');
      await expect(page.locator(`[data-event="${id}"] button[role="switch"]`)).toHaveAttribute('aria-checked', 'false');
    }
    const all = await calls(page);
    const set = all.filter(c => c.type === 'setGroup');
    expect(set).toHaveLength(1);
    expect(set[0].payload).toMatchObject({ group: 'Deploys', enabled: false });
  });

  test('a mixed group shows the middle state and clicking it turns all on', async ({ page }) => {
    await open(page);
    const box = page.locator('.group-box[data-group="Browser"]'); // 2 of 4 on in the mock state
    const sw = box.locator('.group-head button[role="switch"]');
    await expect(sw).toHaveAttribute('data-state', 'mixed'); // the middle state
    await expect(sw).toHaveAttribute('aria-checked', 'false'); // switch role stays binary

    await sw.click();
    await expect(sw).toHaveAttribute('data-state', 'true');
    for (const id of ['browser-claude-done', 'browser-obvious-done', 'browser-watch-done', 'browser-download-done']) {
      await expect(page.locator(`[data-event="${id}"] button[role="switch"]`)).toHaveAttribute('aria-checked', 'true');
    }
    const all = await calls(page);
    const set = all.filter(c => c.type === 'setGroup');
    expect(set).toHaveLength(1);
    expect(set[0].payload).toMatchObject({ group: 'Browser', enabled: true });
  });

  test('master switch off sends setMute muted true and the header says Sounds off', async ({ page }) => {
    await open(page);
    await page.click('#master-switch');
    await expect(page.locator('#master-label')).toHaveText('Sounds off');
    await expect(page.locator('#master-switch')).toHaveAttribute('aria-checked', 'false');

    const all = await calls(page);
    const set = all.filter(c => c.type === 'setMute');
    expect(set).toHaveLength(1);
    expect(set[0].payload.muted).toBe(true);
  });

  test('Mute 1 hour sends a timed mute and flips to Muted until', async ({ page }) => {
    await open(page);
    await page.click('#mute-1h');
    await expect(page.locator('#mute-1h')).toHaveText(/^Muted until/);

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
      await expect(page.locator('#master-switch')).toBeVisible();
      await expect(page.locator('#mute-1h')).toBeVisible();
    }
  });
});
