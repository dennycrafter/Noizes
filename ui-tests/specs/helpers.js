/*
 * Shared helpers for the ui-tests specs (lane G).
 * Every spec loads the page with the mock bridge injected before any page
 * script runs, so the suite never needs the C# side.
 */
const path = require('path');
const fs = require('fs');

const MOCK = path.join(__dirname, '..', 'mock-bridge.js');
const REPO_ROOT = path.resolve(__dirname, '..', '..');
const SHOTS = path.join(REPO_ROOT, 'docs', 'screenshots', 'v1.3');

// Load the page under test with the fake bridge. Resolves once the page has
// pulled state over the bridge and rendered the event rows.
async function open(page, urlPath = '/') {
  await page.addInitScript({ path: MOCK });
  await page.goto(urlPath, { waitUntil: 'load' });
  await page.waitForFunction(() => !!window.__noizesMock, null, { timeout: 10000 });
  await page.waitForFunction(() => document.querySelectorAll('.snd-row').length > 0, null, { timeout: 10000 });
  return page;
}

// Every page-to-app message the page sent so far: [{type, payload}]
async function calls(page) {
  return page.evaluate(() => window.__noizesMock.calls.map(c => ({ type: c.type, payload: c.payload })));
}

function callsOf(all, type) {
  return all.filter(c => c.type === type);
}

// Navigate with the sidebar. Side effect free re-render included in the fixture.
async function gotoPage(page, name) {
  await page.click(`.nz-nav-btn[data-page="${name}"]`);
  await page.waitForTimeout(50);
}

const PAGES = ['sounds', 'connections', 'schedule', 'general'];

module.exports = { open, calls, callsOf, gotoPage, PAGES, SHOTS, REPO_ROOT };
