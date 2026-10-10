/*
 * Section 7: nothing cut off and nothing overflowing.
 * Button scrollWidth <= clientWidth at widths 960 and 1400 and device scale
 * factors 1, 1.25, 1.5 and 2; no horizontal scroll at 960; no rendered text
 * contains an em dash or en dash; no visible text contains a double space.
 */
const { test, expect } = require('@playwright/test');
const { open, gotoPage, PAGES } = require('./helpers');

const WIDTHS = [960, 1400];
const SCALES = [1, 1.25, 1.5, 2];

// In-page sweep: every visible button must hug its text, and the page must
// not scroll sideways. Returns a list of violations, empty when clean.
async function sweep(page) {
  return page.evaluate(() => {
    const problems = [];
    const scroller = document.scrollingElement;
    if (scroller.scrollWidth > scroller.clientWidth + 1) {
      problems.push(`horizontal scroll: scrollWidth ${scroller.scrollWidth} > clientWidth ${scroller.clientWidth}`);
    }
    for (const b of document.querySelectorAll('button')) {
      if (!b.checkVisibility()) continue;
      if (b.scrollWidth > b.clientWidth) {
        problems.push(`button cut off: "${(b.textContent || b.getAttribute('aria-label') || '').trim()}" scrollWidth ${b.scrollWidth} > clientWidth ${b.clientWidth}`);
      }
    }
    for (const input of document.querySelectorAll('input, textarea')) {
      if (!input.checkVisibility()) continue;
      if (input.scrollWidth > input.clientWidth + 1) {
        problems.push(`input cut off: "${input.getAttribute('aria-label') || input.placeholder || input.type}" scrollWidth ${input.scrollWidth} > clientWidth ${input.clientWidth}`);
      }
    }
    return problems;
  });
}

// Visible text audit: dashes and double spaces.
async function textAudit(page) {
  return page.evaluate(() => {
    const bad = [];
    const walk = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
    let node;
    while ((node = walk.nextNode())) {
      const parent = node.parentElement;
      if (!parent || !parent.checkVisibility()) continue;
      const text = node.textContent || '';
      if (text.includes('\u2014') || text.includes('\u2013')) {
        bad.push(`dash in text: "${text.trim().slice(0, 60)}"`);
      }
      if (/[^\n ]  +[^\n ]/.test(text)) {
        bad.push(`double space in text: "${text.trim().slice(0, 60)}"`);
      }
    }
    for (const input of document.querySelectorAll('input[placeholder], textarea[placeholder]')) {
      const p = input.getAttribute('placeholder') || '';
      if (p.includes('\u2014') || p.includes('\u2013')) bad.push(`dash in placeholder: "${p}"`);
      if (/[^\n ]  +[^\n ]/.test(p)) bad.push(`double space in placeholder: "${p}"`);
    }
    return bad;
  });
}

test.describe('layout at every width and scale', () => {
  for (const width of WIDTHS) {
    for (const dsf of SCALES) {
      test(`no truncation at ${width}px wide, device scale ${dsf}`, async ({ browser }) => {
        const context = await browser.newContext({
          viewport: { width, height: 800 },
          deviceScaleFactor: dsf,
          colorScheme: 'dark'
        });
        const page = await context.newPage();
        try {
          await open(page);
          for (const name of PAGES) {
            await gotoPage(page, name);
            const problems = await sweep(page);
            expect(problems, `${width}px dsf ${dsf} on the ${name} page`).toEqual([]);
          }
        } finally {
          await context.close();
        }
      });
    }
  }
});

test.describe('text rules', () => {
  test('no em dash, en dash or double space in visible text on any page', async ({ page }) => {
    await open(page);
    for (const name of PAGES) {
      await gotoPage(page, name);
      const bad = await textAudit(page);
      expect(bad, `on the ${name} page`).toEqual([]);
    }
  });

  test('no dashes or double spaces in the GitHub panel either', async ({ page }) => {
    await open(page);
    await gotoPage(page, 'connections');
    await page.getByRole('button', { name: 'Edit', exact: true }).click();
    await expect(page.locator('.panel')).toBeVisible();
    const bad = await textAudit(page);
    expect(bad).toEqual([]);
  });
});
