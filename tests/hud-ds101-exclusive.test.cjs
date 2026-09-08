const assert = require('node:assert/strict');
const { test } = require('node:test');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

function harness() {
  const elements = new Map();
  for (const selector of ['[data-hud]', '[data-title]', '[data-detail]', '[data-brand]', '.state-copy']) {
    const attributes = new Map();
    elements.set(selector, {
      textContent: '', className: '', writes: 0, animations: [],
      getAttribute: key => attributes.get(key),
      setAttribute(key, value) { attributes.set(key, value); this.writes++; },
      animate(frames, options) {
        const animation = { frames, options, cancelled: false, cancel() { this.cancelled = true; } };
        this.animations.push(animation);
        return animation;
      }
    });
  }
  const media = new Map();
  const events = new Map();
  const document = { documentElement: { dataset: {} }, title: '', querySelectorAll: () => [], querySelector: selector => elements.get(selector), addEventListener: (name, fn) => events.set(name, fn) };
  let receive;
  const notifications = [];
  const context = {
    document,
    navigator: { language: 'pt-BR' }, Intl, Date, Set, Map,
    matraca: { subscribe: fn => { receive = fn; }, notify: (...args) => notifications.push(args) },
    matchMedia(query) {
      const item = { matches: false, addEventListener(name, fn) { this.change = fn; } };
      media.set(query, item);
      return item;
    },
    getComputedStyle: () => ({ getPropertyValue: key => key === '--motion-state' ? '160ms' : 'cubic-bezier(.2,.8,.2,1)' })
  };
  vm.runInNewContext(read('Matraca.Web/wwwroot/i18n/catalog-pt-BR.js'), context);
  vm.runInNewContext(read('Matraca.Web/wwwroot/i18n/catalog-en-US.js'), context);
  vm.runInNewContext(read('Matraca.Web/wwwroot/i18n.js'), context);
  vm.runInNewContext(read('Matraca.Web/wwwroot/hud.js'), context);
  return { elements, media, document, notifications, events, receive,
    state: (state, generation, title = state, detail = '') => receive({ version: 1, type: 'hud.state', payload: { state, generation, title, detail } }) };
}

test('HUD initializes only the handshake, without simulated recording or clock', () => {
  const h = harness();
  h.events.get('DOMContentLoaded')();
  assert.equal(h.notifications[0][0], 'ui.hudReady');
  const html = read('Matraca.Web/wwwroot/hud.html');
  assert.match(html, /state-ready/);
  assert.match(html, /aria-hidden="true"/);
  assert.match(html, /design-tokens\.css/);
  assert.doesNotMatch(html, /<button|tabindex|00:08|data-tail/);
});

test('title and detail retain complete Unicode and markup as text', () => {
  const h = harness();
  const detail = '\u{1f399}\u00e7\n<script>not markup</script> '.repeat(600);
  const title = 'Long title '.repeat(90);
  h.state('done', 1, title, detail);
  assert.equal(h.elements.get('[data-title]').textContent, title);
  assert.equal(h.elements.get('[data-detail]').textContent, detail);
  const css = read('Matraca.Web/wwwroot/hud.css');
  assert.match(css, /text-overflow:ellipsis/);
  assert.match(css, /-webkit-line-clamp:2/);
  assert.match(css, /min-height:72px/);
  assert.match(css, /font-size:14px/);
});

test('same message does not restart brand or message animation', () => {
  const h = harness();
  h.state('listening', 1);
  const brand = h.elements.get('[data-brand]');
  const writes = brand.writes;
  h.state('listening', 2);
  assert.equal(brand.writes, writes);
  assert.equal(h.elements.get('.state-copy').animations.length, 0);
  h.state('listening', 3, 'New message');
  assert.equal(brand.writes, writes);
  assert.equal(h.elements.get('.state-copy').animations[0].options.duration, 160);
});

test('old hide cannot overwrite a newer delivery, and ready animates exit', () => {
  const h = harness();
  h.state('done', 10);
  h.state('ready', 11);
  assert.match(h.elements.get('[data-hud]').className, /is-exiting/);
  h.state('writing', 12);
  h.state('ready', 11);
  assert.match(h.elements.get('[data-hud]').className, /state-writing is-visible/);
  assert.equal(h.elements.get('[data-hud]').getAttribute('aria-hidden'), 'false');
  h.receive({ version: 2, type: 'hud.state', payload: { state: 'ready', generation: 99 } });
  assert.match(h.elements.get('[data-hud]').className, /state-writing/);
});

test('native appearance supports all families, modes and live system changes', () => {
  const h = harness();
  h.state('listening', 1);
  const writes = h.elements.get('[data-brand]').writes;
  for (const palette of ['olive', 'ochre', 'terracotta', 'plum', 'teal']) {
    for (const themeMode of ['light', 'dark', 'system']) {
      h.receive({ version: 1, type: 'hud.appearance', payload: { themeMode, palette } });
      assert.equal(h.document.documentElement.dataset.palette, palette);
      assert.equal(h.document.documentElement.dataset.themeMode, themeMode);
    }
  }
  const dark = h.media.get('(prefers-color-scheme: dark)');
  dark.matches = true;
  dark.change();
  assert.equal(h.document.documentElement.dataset.theme, 'dark');
  assert.equal(h.elements.get('[data-brand]').writes, writes);
  assert.match(h.elements.get('[data-hud]').className, /state-listening/);
});

test('HUD follows the effective interface locale while preserving native message text', () => {
  const h = harness();
  h.receive({ version: 1, type: 'hud.appearance', payload: { uiLanguage: 'en-US', effectiveUiLanguage: 'en-US' } });
  assert.equal(h.document.documentElement.lang, 'en-US');
  const detail = 'Texto original do host: ação e 日本語.';
  h.state('error', 1, 'Entrega falhou', detail);
  assert.equal(h.elements.get('[data-detail]').textContent, detail);
});

test('reduced motion selects a complete static brand and cancels active motion', () => {
  const h = harness();
  h.state('listening', 1);
  h.state('thinking', 2);
  const animation = h.elements.get('.state-copy').animations[0];
  const reduced = h.media.get('(prefers-reduced-motion: reduce)');
  reduced.matches = true;
  reduced.change();
  assert.equal(animation.cancelled, true);
  assert.match(h.elements.get('[data-brand]').getAttribute('src'), /symbol-dark\.svg$/);
  h.state('done', 3);
  assert.equal(h.elements.get('.state-copy').animations.length, 1);
  assert.match(h.elements.get('[data-brand]').getAttribute('src'), /symbol-dark\.svg$/);
});

test('approved 03A assets supply complete motion cycles', () => {
  const h = harness();
  for (const [state, asset, duration] of [['listening', 'listening', 1400], ['thinking', 'loading', 2400], ['done', 'done', 1100]]) {
    h.state(state);
    assert.match(h.elements.get('[data-brand]').getAttribute('src'), new RegExp(`${asset}-dark\\.svg$`));
    const svg = read(`design/assets/brand/matraca-03a-${asset}-dark.svg`);
    assert.ok(svg.includes(`${duration}ms`));
    assert.match(svg, /prefers-reduced-motion:reduce/);
  }
});

// Source contracts protect native integration; they are not an AppKit/Win32 execution test.
for (const file of ['Matraca.Windows/WindowsHudWindow.cs', 'Matraca.Mac/Platform/Web/MacHudWindowController.cs']) {
  test(`${file}: persistent delivery error and guarded native exit`, () => {
    const source = read(file);
    assert.match(source, /if \(_deliveryError\) return;/);
    assert.match(source, /_deliveryError = !delivered;/);
    assert.match(source, /if \(!delivered\) return;/);
    assert.match(source, /Task\.Delay\(140\)/);
    assert.match(source, /generation [!=]= Volatile\.Read\(ref _hideGeneration\)/);
    assert.match(source, /Task\.Delay\(900\)/);
    assert.match(source, /generation = _hideGeneration/);
    assert.match(source, /invalidateTimers: false/);
    assert.match(source, /hud\.appearance/);
    assert.match(source, file.includes('Windows') ? /config\.ThemeMode/ : /raw\.themeMode/);
    assert.match(source, file.includes('Windows') ? /config\.Palette/ : /raw\.palette/);
    assert.match(source, /uiLanguage/);
    assert.match(source, /effectiveUiLanguage/);
    const start = source.slice(source.indexOf(file.includes('Windows') ? 'public void PublishDeliveryStarted' : 'private void OnDeliveryStarted'));
    assert.match(start, /Interlocked\.Increment\(ref _hideGeneration\);\s*if \(_deliveryError\) return;/);
    assert.doesNotMatch(source, /Substring|\.\.\d|\.Take\(/);
  });
}

test('native positioning preserves non-activation, work area, pinned target and DPI', () => {
  const windows = read('Matraca.Windows/WindowsHudWindow.cs');
  for (const contract of ['WsExTransparent', 'WsExNoActivate', 'SwpNoActivate', 'GetDpiForWindow', 'info.WorkArea', 'ApplyDpiBounds'])
    assert.ok(windows.includes(contract));
  const mac = read('Matraca.Mac/Platform/Web/MacWebViewHost.cs');
  assert.match(mac, /sel_registerName\("visibleFrame"\)/);
  assert.match(mac, /SetIgnoresMouseEvents, true/);
  assert.match(mac, /if \(_nonActivatingOverlay\)[\s\S]*?OrderFrontRegardless\);\s*return;/);
  const app = read('Matraca.Mac/MacTrayApp.cs');
  assert.match(app, /TryGetHudTargetBounds[\s\S]*?_controller\.PinnedTarget[\s\S]*?TryAcquireLease/);
});
