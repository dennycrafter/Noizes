/*
 * Section 7: row expansion behavior, the volume slider save debounce and the
 * search filter.
 */
const { test, expect } = require('@playwright/test');
const { open, calls } = require('./helpers');

test.describe('rows, expansion and search', () => {
  test('a row expands on click with volume slider, sound buttons and advanced', async ({ page }) => {
    await open(page);
    const row = page.locator('[data-event="gh-push"]');
    await row.locator('.event-name').click();

    const detail = row.locator('.event-detail');
    await expect(detail).toBeVisible();
    await expect(row.locator('input[type="range"]')).toBeVisible();
    await expect(row.getByText('Choose file')).toBeVisible();
    await expect(row.getByText('Use default')).toBeVisible();
    await expect(row.getByText('Advanced')).toBeVisible();
    await expect(row.getByText('Stay quiet while these apps are in front')).toBeAttached();
  });

  test('only one row is open at a time', async ({ page }) => {
    await open(page);
    await page.locator('[data-event="gh-push"] .event-name').click();
    await expect(page.locator('[data-event="gh-push"] .event-detail')).toBeVisible();

    await page.locator('[data-event="uptime-alarm"] .event-name').click();
    await expect(page.locator('[data-event="gh-push"] .event-detail')).toHaveCount(0);
    await expect(page.locator('[data-event="uptime-alarm"] .event-detail')).toBeVisible();
  });

  test('Escape collapses the open row', async ({ page }) => {
    await open(page);
    await page.locator('[data-event="gh-push"] .event-name').click();
    await expect(page.locator('[data-event="gh-push"] .event-detail')).toBeVisible();

    await page.keyboard.press('Escape');
    await expect(page.locator('.event-detail')).toHaveCount(0);
  });

  test('clicking the switch or the play button does not expand the row', async ({ page }) => {
    await open(page);
    await page.locator('[data-event="gh-push"] button[role="switch"]').click();
    await expect(page.locator('.event-detail')).toHaveCount(0);
    await page.locator('[data-event="gh-push"] .play-btn').click();
    await expect(page.locator('.event-detail')).toHaveCount(0);
  });

  test('the volume slider sends at most one save per 300ms', async ({ page }) => {
    await open(page);
    await page.locator('[data-event="gh-push"] .event-name').click();
    const slider = page.locator('[data-event="gh-push"] input[type="range"]');

    // six quick changes, 40ms apart: the trailing debounce must collapse them
    // into exactly one setEvent, carrying the final value
    await page.evaluate(() => {
      const s = document.querySelector('[data-event="gh-push"] input[type="range"]');
      const vals = [10, 20, 30, 40, 50, 66];
      let i = 0;
      const t = setInterval(() => {
        if (i >= vals.length) { clearInterval(t); return; }
        s.value = String(vals[i++]);
        s.dispatchEvent(new Event('input', { bubbles: true }));
      }, 40);
    });
    await page.waitForTimeout(900); // last change + 300ms debounce + slack

    const all = await calls(page);
    const set = all.filter(c => c.type === 'setEvent' && c.payload.eventId === 'gh-push' && c.payload.volume !== undefined);
    expect(set).toHaveLength(1);
    expect(set[0].payload.volume).toBe(66);
  });

  test('a second volume change after the debounce window sends its own save', async ({ page }) => {
    await open(page);
    await page.locator('[data-event="gh-push"] .event-name').click();
    const slider = page.locator('[data-event="gh-push"] input[type="range"]');
    await slider.fill('30');
    await slider.dispatchEvent('input');
    await page.waitForTimeout(500);
    await slider.fill('80');
    await slider.dispatchEvent('input');
    await page.waitForTimeout(500);

    const all = await calls(page);
    const set = all.filter(c => c.type === 'setEvent' && c.payload.eventId === 'gh-push' && c.payload.volume !== undefined);
    expect(set).toHaveLength(2);
    expect(set[1].payload.volume).toBe(80);
  });

  test('search github leaves only GitHub rows', async ({ page }) => {
    await open(page);
    await page.fill('#search', 'github');
    await page.waitForTimeout(100);

    const visibleRows = page.locator('.event-row:visible');
    const count = await visibleRows.count();
    expect(count).toBe(7); // GitHub has 7 events in EventRegistry
    for (let i = 0; i < count; i++) {
      const name = await visibleRows.nth(i).locator('.event-name').textContent();
      expect(name.toLowerCase()).toContain('github');
    }
    const boxes = await page.locator('.group-box:visible').evaluateAll(els => els.map(e => e.dataset.group));
    expect(boxes).toEqual(['GitHub']);
  });

  test('search is case insensitive and an unknown string shows no rows', async ({ page }) => {
    await open(page);
    await page.fill('#search', 'GITHUB');
    await page.waitForTimeout(100);
    expect(await page.locator('.event-row:visible').count()).toBe(7);

    await page.fill('#search', 'zzz-no-match');
    await page.waitForTimeout(100);
    expect(await page.locator('.event-row:visible').count()).toBe(0);
  });
});
