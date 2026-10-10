/*
 * Section 7: screenshots of every page and the GitHub panel at 1080x720,
 * device scale factor 1 and 1.5, saved to docs/screenshots/v1.3/.
 */
const { test, expect } = require('@playwright/test');
const fs = require('fs');
const { open, gotoPage, SHOTS } = require('./helpers');

const SCALES = [1, 1.5];

test.describe('screenshots', () => {
  for (const dsf of SCALES) {
    test(`every page and the GitHub panel at 1080x720, scale ${dsf}`, async ({ browser }) => {
      fs.mkdirSync(SHOTS, { recursive: true });
      const tag = dsf === 1 ? '1x' : String(dsf).replace('.', '_') + 'x';
      const context = await browser.newContext({
        viewport: { width: 1080, height: 720 },
        deviceScaleFactor: dsf,
        colorScheme: 'dark'
      });

      for (const name of ['sounds', 'connections', 'schedule', 'general']) {
        const page = await context.newPage();
        await open(page);
        await gotoPage(page, name);
        await page.waitForTimeout(120); // let transitions settle
        await page.screenshot({ path: `${SHOTS}/${name}-${tag}.png` });
      }

      // the GitHub panel with the token field and the accent Save
      const page = await context.newPage();
      await open(page);
      await gotoPage(page, 'connections');
      await page.getByRole('button', { name: 'Edit', exact: true }).click();
      await expect(page.locator('.panel')).toBeVisible();
      await page.waitForTimeout(120);
      await page.screenshot({ path: `${SHOTS}/github-panel-${tag}.png` });

      await context.close();
      for (const f of ['sounds', 'connections', 'schedule', 'general', 'github-panel']) {
        expect(fs.existsSync(`${SHOTS}/${f}-${tag}.png`)).toBe(true);
      }
    });
  }
});
