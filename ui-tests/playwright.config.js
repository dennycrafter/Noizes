/*
 * Playwright config for the Noizes v1.3.0 ui-tests (lane G).
 *
 * UI source resolution, in order:
 *   1. UI_DIR env var, if set (absolute or relative to the repo root)
 *   2. the real UI at src/Noizes/ui (used as soon as lanes D, E and F land)
 *   3. the contract fixture at ui-tests/fixture, so the suite runs and the
 *      harness is verified before the real UI exists
 *
 * The chosen source is announced on the console and the page under test
 * carries <meta name="noizes-ui-source">, so a fallback can never pass
 * silently as the real UI.
 */
const { defineConfig } = require('@playwright/test');
const fs = require('fs');
const path = require('path');

const repoRoot = path.resolve(__dirname, '..');
const realUi = path.join(repoRoot, 'src', 'Noizes', 'ui');
const fixture = path.join(__dirname, 'fixture');

let uiDir = process.env.UI_DIR ? path.resolve(repoRoot, process.env.UI_DIR) : null;
if (!uiDir) uiDir = fs.existsSync(path.join(realUi, 'index.html')) ? realUi : fixture;
const isFixture = path.resolve(uiDir) === path.resolve(fixture) && path.resolve(uiDir) !== path.resolve(realUi);

console.log('[ui-tests] page under test: ' + uiDir + (isFixture ? ' (contract fixture - the real UI has not landed yet)' : ' (real UI)'));

const PORT = 4173;

module.exports = defineConfig({
  testDir: path.join(__dirname, 'specs'),
  fullyParallel: true,
  timeout: 30000,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: `http://127.0.0.1:${PORT}/`,
    viewport: { width: 1080, height: 720 },
    colorScheme: 'dark'
  },
  webServer: {
    command: `node ${JSON.stringify(path.join(__dirname, 'dev-server.js'))} --root ${JSON.stringify(uiDir)} --port ${PORT}`,
    url: `http://127.0.0.1:${PORT}/`,
    reuseExistingServer: !process.env.CI,
    timeout: 15000
  }
});
