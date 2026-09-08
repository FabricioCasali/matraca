// Visual integration with a test-only bridge. This is not proof of native audio or delivery.
// Requires Playwright (via NODE_PATH or local install) and Microsoft Edge; writes screenshots only when requested.
const { chromium } = require('playwright');
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const root = path.resolve(__dirname, '../..');
const web = path.resolve(process.env.MATRACA_WEB_ROOT || path.join(root, 'Matraca.Web/wwwroot'));
const output = process.env.MATRACA_SCREENSHOTS;
async function serveAsset(route) {
  const pathname = new URL(route.request().url()).pathname;
  if (pathname === '/favicon.ico') { await route.fulfill({ status: 204 }); return; }
  const base = !process.env.MATRACA_WEB_ROOT && pathname.startsWith('/assets/brand/') ? path.join(root, 'design') : web;
  const file = path.resolve(base, '.' + (pathname === '/' ? '/index.html' : pathname));
  if (!file.startsWith(base + path.sep) || !fs.existsSync(file) || !fs.statSync(file).isFile()) {
    await route.fulfill({ status: 404 }); return;
  }
  await route.fulfill({ contentType: { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8', '.svg': 'image/svg+xml' }[path.extname(file)] || 'application/octet-stream', body: fs.readFileSync(file) });
}

(async () => {
  const browser = await chromium.launch({ channel: process.env.MATRACA_BROWSER_CHANNEL || 'msedge', headless: true });
  const page = await browser.newPage({ viewport: { width: 1280, height: 900 }, reducedMotion: 'reduce' });
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  page.on('console', message => { if (message.type() === 'error') errors.push(`${message.text()} (${message.location().url})`); });
  try {
    // Fulfill local assets without HTTP interception by machine-wide browser software.
    // Keep the application's CSP unchanged and verify the policy actually loaded below.
    await page.route('http://matraca.test/**', serveAsset);
    await page.addInitScript(() => {
      const config = { themeMode: 'light', palette: 'olive', language: 'pt', mode: 'toggle', hotkey: 'F15',
        uiLanguage: 'pt-BR', effectiveUiLanguage: 'pt-BR',
        modelPath: 'models/ggml-small.bin', gpu: 'cpu', micSensitivity: { 'Microfone de teste': .012 },
        postProcess: false, postProcessProvider: 'deepseek', postProcessModel: 'deepseek-v4-flash', postProcessReasoning: 'off' };
      const entries = [{ id: 'test-only', at: '2026-09-07T10:30:00', text: 'Uma ideia, uma pausa.\nO texto continua inteiro: ação, café e 日本語.', characterCount: 74,
        reviewUsage: { provider: 'deepseek', model: 'deepseek-v4-flash', promptTokens: 80, completionTokens: 20, totalTokens: 100, estimatedCostUsd: .00002, pricingVersion: '2026-09-06' } }];
      const aiUsage = { providers: [{ provider: 'deepseek', requests: 2, promptTokens: 80, completionTokens: 20, totalTokens: 100,
        estimatedCostUsd: .00002, pricedRequests: 1, unpricedRequests: 1, coverageKnown: true, pricingVersions: ['2026-09-06'] }] };
      const models = { entries: [{ id: 'ggml-small.bin', label: 'Small', bytes: 487000000, downloaded: true, path: 'models/ggml-small.bin' }] };
      window.testAppearance = patch => {
        Object.assign(config, patch);
        window.matraca.onMessage({ version: 1, type: 'config.changed', payload: { config } });
      };
      window.chrome = window.chrome || {};
      window.testWindowCalls = [];
      window.chrome.webview = { postMessage(json) {
        const message = JSON.parse(json); if (!message.id) return;
        let result;
        if (message.method === 'app.get') result = { config: { config, runtime: { postProcessActive: false } }, platform: 'windows', capabilities: { filePick: true, soundPreview: true, historyClear: true }, devices: ['Microfone de teste'], models, history: { entries }, aiUsage, permissions: { microphone: 'granted', accessibility: 'granted' }, state: { state: 'ready', text: 'Sua voz, no lugar certo.' } };
        else if (message.method === 'config.set') {
          Object.assign(config, message.params.patch);
          config.effectiveUiLanguage = config.uiLanguage === 'system'
            ? (navigator.language.toLowerCase().startsWith('pt') ? 'pt-BR' : 'en-US')
            : config.uiLanguage;
          result = { config };
        }
        else if (message.method === 'ai.usage.get') result = aiUsage;
        else if (message.method === 'history.list') result = { entries };
        else if (message.method === 'mic.monitor.start') result = { started: true, currentDevice: 'Microfone de teste', threshold: .012, devices: ['Microfone de teste'] };
        else if (message.method === 'mic.monitor.stop') result = { stopped: true };
        else if (message.method.startsWith('window.')) { window.testWindowCalls.push(message.method); result = {}; }
        else throw Error(`Unexpected native request in visual test: ${message.method}`);
        queueMicrotask(() => window.matraca.onMessage({ version: 1, type: 'response', id: message.id, ok: true, result }));
      } };
    });
    await page.goto('http://matraca.test/');
    const declaredPolicy = fs.readFileSync(path.join(web, 'index.html'), 'utf8').match(/http-equiv="Content-Security-Policy" content="([^"]+)"/)[1];
    assert.equal(await page.locator('meta[http-equiv="Content-Security-Policy"]').getAttribute('content'), declaredPolicy);
    await page.waitForFunction(() => document.querySelector('[data-save-state]').textContent === 'Configuração carregada');
    await page.evaluate(() => { location.hash = 'settings'; });
    await page.waitForFunction(() => !document.querySelector('[data-page="settings"]').hidden);
    await page.locator('[data-settings-tab="appearance"]').click();
    const recognitionLanguage = await page.locator('[data-config-field="language"]').inputValue();
    await page.locator('[data-config-field="uiLanguage"]').selectOption('en-US');
    await page.waitForFunction(() => document.documentElement.lang === 'en-US');
    assert.equal(await page.locator('[data-page="settings"] h1').textContent(), 'Settings');
    assert.equal(await page.locator('[data-config-field="language"]').inputValue(), recognitionLanguage);
    assert.equal(await page.evaluate(() => location.hash), '#settings');
    await page.locator('[data-config-field="uiLanguage"]').selectOption('pt-BR');
    await page.waitForFunction(() => document.documentElement.lang === 'pt-BR');
    assert.equal(await page.locator('[data-page="settings"] h1').textContent(), 'Configurações');
    await page.evaluate(() => { location.hash = 'home'; });
    await page.waitForFunction(() => !document.querySelector('[data-page="home"]').hidden);
    assert.equal(await page.locator('.titlebar [data-config-field], [data-appearance-menu]').count(), 0);
    await page.locator('[data-last-phrase-menu] summary').click();
    await page.keyboard.press('Escape');
    assert.equal(await page.locator('[data-last-phrase-menu]').evaluate(menu => !menu.open && document.activeElement === menu.querySelector('summary')), true);
    await page.locator('.titlebar-name').dblclick();
    await page.locator('.titlebar-name').dblclick();
    assert.deepEqual(await page.evaluate(() => window.testWindowCalls), ['window.toggleMaximize', 'window.toggleMaximize']);
    const title = await page.locator('.titlebar-name').boundingBox();
    await page.mouse.move(title.x + title.width / 2, title.y + title.height / 2);
    await page.mouse.down();
    await page.mouse.move(title.x + title.width / 2 + 20, title.y + title.height / 2, { steps: 5 });
    await page.mouse.up();
    for (const control of ['minimize', 'maximize', 'close']) await page.locator(`[data-window-${control}]`).click();
    assert.deepEqual(await page.evaluate(() => window.testWindowCalls), [
      'window.toggleMaximize', 'window.toggleMaximize', 'window.drag',
      'window.minimize', 'window.toggleMaximize', 'window.close'
    ]);
    let checks = 0;
    // Measure before click(): Playwright would scroll a hidden navigation into view.
    for (const width of [1120, 840, 360, 760, 1280]) {
      await page.setViewportSize({ width, height: 760 });
      await page.evaluate(() => { location.hash = 'settings'; });
      await page.waitForFunction(() => !document.querySelector('[data-page="settings"]').hidden);
      await page.locator('[data-settings-tab="review"]').click();
      await page.locator('.pages').evaluate(layout => { layout.scrollTop = layout.scrollHeight; });
      await page.evaluate(() => { location.hash = 'home'; });
      await page.waitForFunction(() => !document.querySelector('[data-page="home"]').hidden);
      await page.evaluate(() => { location.hash = 'settings'; });
      await page.waitForFunction(() => !document.querySelector('[data-page="settings"]').hidden);
      const navigation = await page.locator('[data-settings-tab="appearance"]').evaluate(tab => {
        const rect = tab.getBoundingClientRect();
        const bar = document.querySelector('.titlebar').getBoundingClientRect();
        return { top: rect.top, bottom: rect.bottom, barBottom: bar.bottom, height: innerHeight,
          reachable: !!document.elementFromPoint(rect.x + rect.width / 2, rect.y + rect.height / 2)?.closest('[data-settings-tab="appearance"]') };
      });
      assert.ok(navigation.top >= navigation.barBottom && navigation.bottom <= navigation.height && navigation.reachable,
        `appearance navigation hidden after returning to settings at ${width}: ${JSON.stringify(navigation)}`);
      await page.locator('[data-settings-tab="appearance"]').click();
      assert.equal(await page.locator('[data-config-field="palette"]').count(), 1);
      assert.equal(await page.locator('[data-config-field="themeMode"]').count(), 1);
      for (const palette of ['olive', 'ochre', 'terracotta', 'plum', 'teal']) {
        await page.locator('[data-config-field="palette"]').selectOption(palette);
        for (const themeMode of ['light', 'dark', 'system']) {
          await page.locator('[data-config-field="themeMode"]').focus();
          await page.locator('[data-config-field="themeMode"]').selectOption(themeMode);
          await page.waitForFunction(({ palette, themeMode }) => document.documentElement.dataset.palette === palette &&
            document.documentElement.dataset.theme === (themeMode === 'system' ? (matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light') : themeMode), { palette, themeMode });
          assert.equal(await page.locator('[data-config-field="palette"]').inputValue(), palette);
          assert.equal(await page.locator('[data-config-field="themeMode"]').evaluate(control => control === document.activeElement), true);
          checks++;
        }
      }
      if (output && width === 1120) await page.screenshot({ path: path.join(output, 'matraca-appearance-mt037.png') });
    }
    for (const width of [360, 760, 1280]) {
      await page.setViewportSize({ width, height: 900 });
      for (const palette of ['olive', 'ochre', 'terracotta', 'plum', 'teal']) {
        for (const themeMode of ['light', 'dark']) {
          await page.evaluate(({ palette, themeMode }) => window.testAppearance({ palette, themeMode }), { palette, themeMode });
          for (const route of ['home', 'microphone', 'history', 'settings', 'onboarding']) {
            await page.evaluate(route => { location.hash = route; }, route);
            await page.waitForFunction(route => !document.querySelector(`[data-page="${route}"]`).hidden, route);
            const overflow = await page.evaluate(() => ({ width: innerWidth, actual: document.documentElement.scrollWidth }));
             assert.ok(overflow.actual <= width + 1, `${route}/${palette}/${themeMode}/${width}: horizontal overflow ${overflow.actual}`);
             await page.locator('.pages').evaluate(layout => { layout.scrollTop = layout.scrollHeight; });
             const frame = await page.evaluate(() => {
               const bar = document.querySelector('.titlebar').getBoundingClientRect();
               const content = document.querySelector('.pages');
               const rect = content.getBoundingClientRect();
               const close = document.querySelector('[data-window-close]').getBoundingClientRect();
               return { top: bar.top, bottom: bar.bottom, contentTop: rect.top, scroll: content.scrollTop,
                 maxScroll: content.scrollHeight - content.clientHeight,
                 closeReachable: !!document.elementFromPoint(close.x + close.width / 2, close.y + close.height / 2)?.closest('[data-window-close]') };
             });
             assert.equal(frame.top, 0);
             assert.ok(frame.contentTop >= frame.bottom);
             assert.ok(frame.closeReachable);
             if (frame.maxScroll > 0) assert.ok(frame.scroll > 0);
             await page.locator('.pages').evaluate(layout => { layout.scrollTop = 0; });
            const smallTargets = await page.locator('button:visible, select:visible, input:visible, summary:visible').evaluateAll(controls => controls.filter(control => {
              const bounds = control.getBoundingClientRect();
              return bounds.width < 44 || bounds.height < 44;
            }).map(control => control.getAttribute('aria-label') || control.textContent || control.type));
            assert.deepEqual(smallTargets, [], `${route}/${width}: targets smaller than 44px`);
            const badImages = await page.locator('img:visible').evaluateAll(images => images.filter(image => !image.complete || image.naturalWidth === 0).map(image => image.src));
            if (badImages.length) await page.waitForFunction(() => [...document.images].every(image => image.complete && image.naturalWidth));
            checks++;
          }
        }
      }
    }
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.evaluate(() => window.testAppearance({ themeMode: 'light', palette: 'olive' }));
    for (const zoom of [1, 1.5, 2]) {
      // Browser zoom reduces the CSS viewport; body.style.zoom does not and gives a false dvh result.
      await page.setViewportSize({ width: Math.round(1280 / zoom), height: Math.round(900 / zoom) });
      await page.evaluate(() => { location.hash = 'settings'; });
      await page.waitForFunction(() => !document.querySelector('[data-page="settings"]').hidden);
      for (const tab of ['key', 'model', 'audio', 'delivery', 'review', 'history', 'appearance']) {
        await page.locator(`[data-settings-tab="${tab}"]`).click();
        const overflow = await page.evaluate(() => document.documentElement.scrollWidth);
        assert.ok(overflow <= Math.round(1280 / zoom) + 1, `settings/${tab}/${zoom}: overflow ${overflow}`);
        checks++;
      }
    }
    for (const viewport of [{ width: 1200, height: 820 }, { width: 1280, height: 320 },
      { width: 360, height: 640 }, { width: 360, height: 320 },
      { width: 853, height: 600 }, { width: 640, height: 450 }]) {
      await page.setViewportSize(viewport);
      await page.evaluate(() => { location.hash = 'settings'; });
      await page.waitForFunction(() => !document.querySelector('[data-page="settings"]').hidden);
      await page.locator('[data-settings-tab="review"]').click();
      await page.locator('.sidebar').evaluate(nav => { nav.scrollTop = 0; });
      const before = await page.locator('.sidebar').boundingBox();
      await page.locator('.pages').evaluate(content => { content.scrollTop = 0; });
      const contentBox = await page.locator('.pages').boundingBox();
      await page.mouse.move(contentBox.x + contentBox.width / 2, contentBox.y + contentBox.height / 2);
      await page.mouse.wheel(0, 10000);
      await page.waitForFunction(() => {
        const content = document.querySelector('.pages');
        return content.scrollTop >= content.scrollHeight - content.clientHeight - 1;
      });
      const geometry = await page.evaluate(() => {
        const content = document.querySelector('.pages');
        const title = document.querySelector('.titlebar').getBoundingClientRect();
        const nav = document.querySelector('.sidebar').getBoundingClientRect();
        const rect = content.getBoundingClientRect();
        return { titleTop: title.top, titleBottom: title.bottom, navTop: nav.top, navBottom: nav.bottom,
          contentTop: rect.top, contentBottom: rect.bottom, scroll: content.scrollTop,
          documentScroll: document.scrollingElement.scrollTop, documentHeight: document.scrollingElement.scrollHeight,
          layoutScroll: document.querySelector('.app-layout').scrollTop, height: innerHeight };
      });
      assert.deepEqual(await page.locator('.sidebar').boundingBox(), before, `navigation moved: ${JSON.stringify(viewport)}`);
      assert.equal(geometry.titleTop, 0);
      assert.ok(geometry.navTop >= geometry.titleBottom && geometry.navBottom <= geometry.height);
      assert.ok(geometry.contentTop >= geometry.titleBottom && geometry.contentBottom <= geometry.height + 1);
      if (viewport.width <= 760) assert.ok(geometry.contentTop >= geometry.navBottom);
      assert.ok(geometry.scroll > 0, 'long settings content must actually scroll');
      assert.equal(geometry.documentScroll, 0);
      assert.equal(geometry.layoutScroll, 0);
      assert.ok(geometry.documentHeight <= geometry.height + 1);
      if (output && (viewport.width === 1200 || viewport.width === 360 && viewport.height === 320)) {
        await page.screenshot({ path: path.join(output, `matraca-fixed-nav-${viewport.width}x${viewport.height}.png`) });
      }
      const contentScroll = geometry.scroll;
      // Real tab sequence must reveal clipped navigation without moving the content or title.
      await page.locator('.sidebar [data-route="home"]').focus();
      for (const route of ['home', 'microphone', 'history', 'settings', 'onboarding']) {
        const reachable = await page.locator(`.sidebar [data-route="${route}"]`).evaluate(button => {
          const rect = button.getBoundingClientRect();
          return button === document.activeElement && document.elementFromPoint(rect.x + rect.width / 2, rect.y + rect.height / 2)?.closest('button') === button;
        });
        assert.ok(reachable, `keyboard navigation ${route}: ${JSON.stringify(viewport)}`);
        if (route !== 'onboarding') await page.keyboard.press('Tab');
      }
      assert.equal(await page.locator('.pages').evaluate(content => content.scrollTop), contentScroll);
      if (viewport.height === 320) {
        const navigationScroll = await page.locator('.sidebar').evaluate(nav => ({
          overflowing: nav.scrollHeight > nav.clientHeight,
          scrollTop: nav.scrollTop
        }));
        if (navigationScroll.overflowing) assert.ok(navigationScroll.scrollTop > 0);
      }
      await page.keyboard.press('Enter');
      await page.waitForFunction(() => !document.querySelector('[data-page="onboarding"]').hidden);
      checks++;
    }
    await page.setViewportSize({ width: 1280, height: 900 });
    if (output) {
      assert.ok(fs.statSync(output).isDirectory(), 'Screenshot destination must exist');
      for (const route of ['home', 'microphone', 'history', 'settings', 'onboarding']) {
        await page.evaluate(route => { location.hash = route; }, route);
        await page.waitForFunction(route => !document.querySelector(`[data-page="${route}"]`).hidden, route);
        if (route === 'settings') await page.locator('[data-settings-tab="key"]').click();
        await page.screenshot({ path: path.join(output, `matraca-${route}.png`), fullPage: true });
      }
      await page.setViewportSize({ width: 360, height: 900 });
      await page.screenshot({ path: path.join(output, 'matraca-small.png'), fullPage: true });
    }
    await page.setViewportSize({ width: 430, height: 92 });
    await page.goto('http://matraca.test/hud.html');
    const longDetail = 'Texto completo com Unicode: ação e 日本語. '.repeat(30);
    await page.evaluate(detail => window.matraca.onMessage({ version: 1, type: 'hud.state', payload: { state: 'error', title: 'Entrega falhou · confira o histórico', detail, generation: 10 } }), longDetail);
    assert.equal(await page.locator('[data-detail]').textContent(), longDetail);
    assert.ok(await page.locator('[data-hud]').evaluate(hud => hud.getBoundingClientRect().height <= 72));
    if (output) await page.screenshot({ path: path.join(output, 'matraca-hud.png') });
    assert.deepEqual(errors, []);
    console.log(`${checks} browser layout checks passed (test bridge, not native delivery).`);
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
