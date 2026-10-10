/*
 * Boot contract: the real shell renders every EventRegistry event in its real
 * group over the mocked bridge (state shape mirrors UiBridge.BuildState).
 */
const { test, expect } = require('@playwright/test');
const { open, calls } = require('./helpers');

// The real groups from src/Noizes/EventRegistry.cs, in registry order
const GROUPS = ['Coding & AI', 'GitHub', 'Apps', 'Browser', 'Deploys', 'Extras'];
const EVENT_COUNT = 22;

test.describe('boot and state contract', () => {
  test('renders all 22 registry events across the 6 real groups', async ({ page }) => {
    await open(page);
    expect(await page.locator('.snd-row').count()).toBe(EVENT_COUNT);
    const names = await page.locator('section.snd-group').evaluateAll(boxes => boxes.map(b => b.dataset.group));
    expect(names).toEqual(GROUPS);
  });

  test('row shows sound name, sound and volume, custom sounds included', async ({ page }) => {
    await open(page);
    const merged = page.locator('[data-event-id="gh-pr-merged"]');
    await expect(merged.locator('.snd-rowname')).toHaveText('GitHub: PR merged');
    // custom sound: the file name from the mock story plus its volume
    await expect(merged.locator('.snd-rowmeta')).toHaveText('fanfare.wav · 60%');
    // default sound: the pretty default label plus the fresh-install volume
    const push = page.locator('[data-event-id="gh-push"]');
    await expect(push.locator('.snd-rowmeta')).toHaveText('Default arp up · 40%');
  });

  test('the page pulls state with a getState message and the reply is used', async ({ page }) => {
    await open(page);
    const all = await calls(page);
    expect(all[0].type).toBe('getState');
    // rendered version comes from the state reply
    await expect(page.locator('#version')).toHaveText('v1.3.0');
  });

  test('a state push from the app side re-renders the page', async ({ page }) => {
    await open(page);
    await page.evaluate(() => window.__noizesMock.setState({
      muted: true,
      mutedUntil: null
    }));
    await expect(page.locator('#masterLabel')).toHaveText('Sounds off');
    await expect(page.locator('#masterSwitchHost [role="switch"]')).toHaveAttribute('aria-checked', 'false');
  });
});
