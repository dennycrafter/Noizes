/*
 * Boot contract: the page renders every EventRegistry event in its real group
 * over the mocked bridge, before lanes A and B exist.
 */
const { test, expect } = require('@playwright/test');
const { open, calls } = require('./helpers');

// The real groups from src/Noizes/EventRegistry.cs
const GROUPS = ['Coding & AI', 'GitHub', 'Apps', 'Browser', 'Deploys', 'Extras'];
const EVENT_COUNT = 22;

test.describe('boot and state contract', () => {
  test('renders all 22 registry events across the 6 real groups', async ({ page }) => {
    await open(page);
    expect(await page.locator('.event-row').count()).toBe(EVENT_COUNT);
    const names = await page.locator('.group-box').evaluateAll(boxes => boxes.map(b => b.dataset.group));
    expect(names).toEqual(GROUPS);
  });

  test('row shows sound name and volume, custom sounds included', async ({ page }) => {
    await open(page);
    const sub = page.locator('[data-event="gh-pr-merged"] .event-sub');
    await expect(sub).toHaveText('Victory Royale.mp3 · 75%');
    const def = page.locator('[data-event="gh-push"] .event-sub');
    await expect(def).toHaveText('Default chime · 50%');
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
      mute: { muted: true, mutedUntil: null }
    }));
    await expect(page.locator('#master-label')).toHaveText('Sounds off');
    await expect(page.locator('#master-switch')).toHaveAttribute('aria-checked', 'false');
  });
});
