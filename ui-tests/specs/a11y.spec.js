/*
 * Section 7: accessibility. Every switch has role="switch" and a label, and
 * axe-core finds no serious or critical issues on any page or in the GitHub
 * panel.
 */
const { test, expect } = require('@playwright/test');
const fs = require('fs');
const path = require('path');
const { open, gotoPage, PAGES } = require('./helpers');

const AXE = require.resolve('axe-core/axe.min.js');

async function injectAxe(page) {
  await page.addScriptTag({ content: fs.readFileSync(AXE, 'utf8') });
}

test.describe('switch roles and labels', () => {
  test('every switch on every page has role switch and a non-empty label', async ({ page }) => {
    await open(page);
    for (const name of PAGES) {
      await gotoPage(page, name);
      const bad = await page.evaluate(() => {
        const problems = [];
        for (const sw of document.querySelectorAll('[role="switch"]')) {
          const labelledby = sw.getAttribute('aria-labelledby');
          const label = (sw.getAttribute('aria-label')
            || (labelledby && document.getElementById(labelledby)?.textContent)
            || '').trim();
          if (!label) problems.push(`switch without a label: #${sw.id || '(no id)'}`);
          const checked = sw.getAttribute('aria-checked');
          if (checked !== 'true' && checked !== 'false') {
            problems.push(`switch "${label}" has invalid aria-checked "${checked}"`);
          }
        }
        return problems;
      });
      expect(bad, `on the ${name} page`).toEqual([]);
    }
  });

  test('switches are keyboard operable with Space', async ({ page }) => {
    await open(page);
    const sw = page.locator('[data-event="gh-push"] button[role="switch"]');
    await sw.focus();
    await page.keyboard.press('Space');
    await expect(sw).toHaveAttribute('aria-checked', 'false'); // a focused button fires on Space
  });
});

test.describe('axe-core', () => {
  for (const name of PAGES) {
    test(`no serious or critical issues on the ${name} page`, async ({ page }) => {
      await open(page);
      await gotoPage(page, name);
      await injectAxe(page);
      const results = await page.evaluate(() => axe.run({
        resultTypes: ['violations']
      }));
      const bad = results.violations
        .filter(v => v.impact === 'serious' || v.impact === 'critical')
        .map(v => `${v.id} (${v.impact}): ${v.nodes.slice(0, 3).map(n => n.target.join(' ')).join(' | ')}`);
      expect(bad, `axe on the ${name} page`).toEqual([]);
    });
  }

  test('no serious or critical issues with the GitHub panel open', async ({ page }) => {
    await open(page);
    await gotoPage(page, 'connections');
    await page.getByRole('button', { name: 'Edit', exact: true }).click();
    await expect(page.locator('.panel')).toBeVisible();
    await injectAxe(page);
    const results = await page.evaluate(() => axe.run({ resultTypes: ['violations'] }));
    const bad = results.violations
      .filter(v => v.impact === 'serious' || v.impact === 'critical')
      .map(v => `${v.id} (${v.impact}): ${v.nodes.slice(0, 3).map(n => n.target.join(' ')).join(' | ')}`);
    expect(bad, 'axe with the GitHub panel open').toEqual([]);
  });
});
