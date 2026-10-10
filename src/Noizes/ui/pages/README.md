# Noizes UI pages

Lane D owns `index.html`, `app.css`, `app.js` and `dev-stub.js`. The page modules in
this folder are owned by the page lanes (E: sounds, F: connections, schedule and
general). This file describes the contract they render into.

## Contract

Load order is fixed in index.html: `app.js` first, then every page module. Each
module registers one page with the shell:

    NZ.registerPage({
      id: "sounds", // sounds | connections | schedule | general
      render: function (el, ctx) { ... },    // required, fills the #page container
      update: function (state, ctx) { ... }  // optional, called on every state push
    });

- `render` runs when the page becomes active and must rebuild the page from
  scratch. Do not keep references to elements from an earlier render.
- `update` runs on every pushed state while the page is active. Use it to refresh
  in place, so a search box keeps focus. Without `update`, the shell re-runs
  `render` on every push.
- `ctx` carries: `state`, `api` (every bridge call from section 2 of the brief,
  each returning a promise for the `{id, ok, data?, error?}` reply), `toast(message,
  kind)` with kind `ok | warn | bad | info`, `panel({ title, build })` and
  `closePanel()` for the floating panel, `toggle(...)` and `icon(name)` factories,
  and `onEscape(fn)` which returns an unsubscribe function.
- Test actions (`testGitHub`, `testSound`) reply with `data.message` in plain words.

## Shared classes from app.css

`.nz-card` group box, `.nz-group-head` small muted group heading, `.nz-card-pad`,
`.nz-row` (56px min height, hover included) with `.nz-row-main`, `.nz-row-title`,
`.nz-row-sub`, `.nz-row-actions`, `.nz-row-clickable`, `.nz-row-dim` (dimmed but
readable disabled rows), `.nz-btn`, `.nz-btn-primary` (the one accent action inside
a floating panel), `.nz-linkbtn`, `.nz-iconbtn`, `.nz-pill` with
`.nz-pill-ok | warn | bad | info` and `.nz-pill-dot`, `.nz-input`, `.nz-select`,
`.nz-textarea`, `.nz-hint`, `.nz-muted`, `.nz-num` (countdown digits), `.nz-field`,
`.nz-label`, `.nz-actions`.

## Rules that come from the master brief

- Dynamic strings go through `textContent`, never `innerHTML`. Group names such as
  "Coding & AI" must render literally.
- No Save button anywhere. Changes apply and save instantly. The main action in a
  panel is labeled by what it does (for example "Apply" or "Connect"), never "Save".
- No em dashes and no en dashes in any text or comment. No double spaces in
  visible text.
- Pills are for status only. Boxes use 8px corners, buttons and inputs 6px.
- Long names wrap. Nothing ever truncates.
- No web fonts, no CDN, nothing loaded from the internet.
