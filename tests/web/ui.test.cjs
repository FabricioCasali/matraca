const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const UI = require('../../Matraca.Web/wwwroot/ui-model.js');
const { app, flush, deferred, root } = require('./dom.cjs');

test('bridge fails without host and accepts a synchronous native response', async () => {
  const code = fs.readFileSync(path.join(root, 'bridge.js'), 'utf8');
  const context = vm.createContext({ document: { addEventListener() {} } });
  vm.runInContext(code, context);
  await assert.rejects(context.matraca.request('history.copy'), { code: 'host_unavailable' });
  context.chrome = { webview: { postMessage(json) {
    const message = JSON.parse(json);
    context.matraca.onMessage({ version: 1, id: message.id, type: 'response', ok: true, result: 'native' });
  } } };
  assert.equal(await context.matraca.request('hotkey.capture.start'), 'native');
  context.chrome.webview.postMessage = () => {};
  let settled = false;
  context.matraca.request('model.download.start').finally(() => { settled = true; });
  await flush(); assert.equal(settled, false);
});

test('shared validation covers ranges, free language, endpoint and explicit model', () => {
  for (const [field, [min, max, integer]] of Object.entries(UI.ranges)) {
    for (const value of ['', 'NaN', String(min - 1), String(max + 1)]) assert.ok(UI.validate(field, value).error, field);
    assert.equal(UI.validate(field, String(min)).error, '', field);
    assert.equal(UI.validate(field, String(max)).error, '', field);
    if (integer) assert.ok(UI.validate(field, String(min + .5)).error, field);
  }
  assert.equal(UI.validate('language', 'ja').value, 'ja');
  assert.ok(UI.validate('postProcessEndpoint', 'http://remote.test/').error);
  assert.ok(UI.validate('postProcessEndpoint', 'https://user:password@remote.test/').error);
  assert.equal(UI.validate('postProcessEndpoint', 'http://localhost:1234/v1/chat/completions').error, '');
  assert.ok(UI.validate('postProcessModel', '', { postProcessProvider: 'openai-compatible' }).error);
  assert.deepEqual(UI.validate('vocabulary', 'Matraca\nmatraca\nVoz').value, ['matraca', 'Voz']);
});

test('nullable ledger coverage never becomes free usage or a complete total', () => {
  const ledger = [{ provider: 'deepseek', requests: 3, totalTokens: 20, estimatedCostUsd: .1,
    pricedRequests: 2, unpricedRequests: 1, coverageKnown: true, pricingVersions: ['v1'] },
  { provider: 'anthropic', requests: 2, totalTokens: null, estimatedCostUsd: null,
    pricedRequests: null, unpricedRequests: null, coverageKnown: false, pricingVersions: [] }];
  const total = UI.usageSummary(ledger);
  assert.equal(total.complete, false); assert.equal(total.estimatedCostUsd, .1);
  assert.equal(total.totalTokens, null); assert.equal(total.pricedRequests, null);
  assert.equal(UI.usageSummary(ledger, 'anthropic').estimatedCostUsd, null);
  assert.equal(UI.cost(null), 'Não calculado'); assert.notEqual(UI.cost(0), UI.cost(null));
  assert.equal(UI.tokens(null), 'Não informado');
});

test('five routes, seven sections, one microphone panel and no initial fake audio', async () => {
  const ui = await app();
  assert.equal(ui.document.querySelectorAll('[data-page]').length, 5);
  assert.equal(ui.document.querySelectorAll('[data-settings-tab]').length, 7);
  assert.equal(ui.document.querySelectorAll('[data-microphone-panel]').length, 1);
  assert.equal(ui.q('[data-spectrum]').children.length, 48);
  assert.ok(ui.q('[data-spectrum]').children.every(bar => bar.style.height === '0%'));
  assert.equal(ui.q('[data-route="home"]').getAttribute('aria-current'), 'page');
  assert.equal(ui.q('[data-config-toggle="history"]').getAttribute('role'), 'switch');
  assert.equal(ui.q('[data-config-field="language"]').tagName, 'INPUT');
  for (const control of ui.document.querySelectorAll('[data-config-field]')) {
    assert.ok(control.getAttribute('aria-describedby'));
    assert.ok(control.closest('label') || control.getAttribute('aria-labelledby'), control.dataset.configField);
  }
});

test('route changes reveal navigation without resetting scroll on appearance updates', async () => {
  const ui = await app();
  const layout = ui.q('.pages');
  await ui.route('settings');
  layout.scrollTop = 600;
  await ui.route('home');
  assert.equal(layout.scrollTop, 0);
  layout.scrollTop = 200;
  await ui.route('settings');
  assert.equal(layout.scrollTop, 0);
  await ui.q('[data-settings-tab="appearance"]').click();
  layout.scrollTop = 100;
  await ui.emit('config.changed', { config: { themeMode: 'dark', palette: 'teal' } });
  assert.equal(layout.scrollTop, 100);
  assert.equal(ui.q('[data-settings-panel="appearance"]').hidden, false);
});

test('invalid drafts and newer typing survive config events and delayed responses', async () => {
  const waiting = deferred();
  const ui = await app({ request: method => method === 'config.set' ? waiting.promise : undefined });
  const field = ui.q('[data-config-field="silenceMs"]');
  field.value = '50'; await field.dispatch('input'); await field.dispatch('change');
  assert.equal(field.getAttribute('aria-invalid'), 'true');
  await ui.emit('config.changed', { config: { silenceMs: 450 } });
  assert.equal(field.value, '50'); assert.equal(ui.calls.filter(x => x.method === 'config.set').length, 0);
  field.value = '500'; await field.dispatch('input'); const saving = field.dispatch('change'); await flush();
  field.value = '750'; await field.dispatch('input');
  waiting.resolve({ config: { silenceMs: 500 } }); await saving;
  assert.equal(field.value, '750');
});

test('microphone is moved, not cloned, and theme keeps capture and focus', async () => {
  const ui = await app(); const panel = ui.q('[data-microphone-panel]');
  await ui.route('microphone'); await ui.q('[data-microphone-start]').click();
  assert.equal(ui.q('[data-device-label]').textContent, 'USB mic');
  assert.equal(ui.q('[data-device-select]').disabled, true);
  const field = ui.q('[data-config-field="themeMode"]'); field.focus();
  field.value = 'dark'; await field.dispatch('change');
  assert.equal(ui.document.activeElement, field);
  assert.equal(ui.document.documentElement.dataset.theme, 'dark');
  assert.equal(ui.calls.filter(x => x.method === 'mic.monitor.stop').length, 0);
  await ui.emit('mic.frame', { device: 'USB mic', rms: .03, peak: .04, threshold: .02, bands: Array(48).fill(.1) });
  assert.notEqual(ui.q('[data-spectrum]').children[0].style.height, '0%');
  await ui.route('settings'); await ui.q('[data-settings-tab="audio"]').click();
  assert.equal(ui.q('[data-microphone-panel]'), panel);
  assert.equal(panel.parentElement.dataset.microphoneSlot, 'settings');
  assert.equal(ui.calls.filter(x => x.method === 'mic.monitor.stop').length, 1);
  assert.ok(ui.q('[data-spectrum]').children.every(bar => bar.style.height === '0%'));
});

test('leaving while microphone starts queues stop and ignores late frames', async () => {
  const waiting = deferred();
  const ui = await app({ request: method => method === 'mic.monitor.start' ? waiting.promise : undefined });
  await ui.route('microphone'); const starting = ui.q('[data-microphone-start]').click(); await flush();
  await ui.route('home'); waiting.resolve({ started: true, currentDevice: 'USB mic', threshold: .02 });
  await starting; await flush();
  assert.equal(ui.calls.at(-1).method, 'mic.monitor.stop');
  await ui.emit('mic.frame', { rms: .2, threshold: .02, bands: Array(48).fill(1) });
  assert.ok(ui.q('[data-spectrum]').children.every(bar => bar.style.height === '0%'));
});

test('no generic threshold writes and reset affects only resolved microphone', async () => {
  const ui = await app({ config: { micSensitivity: { 'USB mic': .02, Other: .03 } } });
  await ui.route('microphone'); await ui.q('[data-microphone-start]').click();
  const threshold = ui.q('[data-threshold-input]'); threshold.value = '.04'; await threshold.dispatch('change'); await flush();
  const patch = ui.calls.find(x => x.method === 'config.set').params.patch;
  assert.equal(patch.micSensitivity['USB mic'], .04); assert.equal(patch.micSensitivity.Other, .03);
  assert.equal(Object.hasOwn(patch, 'vadThreshold'), false);
  await ui.q('[data-microphone-reset]').click();
  const reset = ui.calls.filter(x => x.method === 'config.set').at(-1).params.patch;
  assert.equal(Object.hasOwn(reset.micSensitivity, 'USB mic'), false);
  assert.equal(reset.micSensitivity.Other, .03);
});

test('filters synchronize selection, preserve multiline copy fallback and leave ledger unchanged', async () => {
  const history = [{ id: '1', text: 'primeira\nlinha 👋', at: '2026-01-01', reviewUsage: { provider: 'deepseek' } },
    { id: '2', text: 'segunda', at: '2026-01-02', reviewUsage: { provider: 'anthropic' } }];
  const ui = await app({ history, request: method => method === 'history.copy' ? Promise.reject(new Error('Clipboard indisponivel')) : undefined });
  const provider = ui.q('[data-history-provider]'); provider.value = 'anthropic'; await provider.dispatch('change');
  assert.equal(ui.q('[data-history-text]').value, 'segunda');
  assert.equal(ui.q('[data-history-mode]').textContent, 'Não informado');
  const search = ui.q('[data-history-search]'); search.value = 'ausente'; await search.dispatch('input');
  assert.equal(ui.q('[data-history-copy]').disabled, true);
  search.value = ''; provider.value = 'all'; await provider.dispatch('change');
  await ui.q('[data-history-copy]').click();
  assert.equal(ui.q('[data-history-text]').value, history[0].text);
  assert.equal(ui.q('[data-history-text]').selectionEnd, history[0].text.length);
  assert.ok(ui.q('[data-announcer]').textContent.includes('manualmente'));
  const before = ui.calls.filter(x => x.method === 'ai.usage.get').length;
  await ui.emit('dictation.completed', { text: 'real', history: { entries: history } });
  assert.equal(ui.calls.filter(x => x.method === 'ai.usage.get').length, before + 1);
});

test('provider removal requires confirmation and reasoning applies without disabling review', async () => {
  const ui = await app({ confirm: false });
  const provider = ui.q('[data-config-field="postProcessProvider"]'); provider.value = 'deepseek'; await provider.dispatch('change');
  assert.equal(provider.value, 'anthropic'); assert.equal(ui.calls.filter(x => x.method === 'config.set').length, 0);
  const second = await app({ config: { postProcessProvider: 'deepseek', postProcessModel: 'deepseek-v4-flash', postProcessReasoning: 'off', postProcess: true } });
  const reasoning = second.q('[data-config-field="postProcessReasoning"]'); reasoning.value = 'high'; await reasoning.dispatch('change');
  const patch = second.calls.find(x => x.method === 'config.set').params.patch;
  assert.deepEqual(Object.keys(patch), ['postProcessReasoning']);
});

test('download uses measured progress, supports cancel and never claims model readiness', async () => {
  const waiting = deferred();
  const ui = await app({ request: method => method === 'model.download.start' ? waiting.promise : undefined });
  const downloading = ui.q('[data-model-download]').click(); await flush();
  await ui.emit('model.download.progress', { done: 10, total: 0 });
  assert.equal(ui.q('[data-model-progress]').hasAttribute('value'), false);
  assert.ok(ui.q('[data-onboarding-model]').textContent.includes('não informado'));
  await ui.emit('model.download.progress', { done: 25, total: 100 });
  assert.equal(ui.q('[data-model-progress]').value, '25');
  await ui.q('[data-model-cancel]').click();
  assert.ok(ui.calls.some(x => x.method === 'model.download.cancel'));
  waiting.reject(new Error('Cancelado')); await downloading;
  assert.equal(ui.q('[data-model-cancel]').hidden, true);
  assert.equal(ui.q('[data-model-setup]').classList.contains('complete'), false);
});

test('titlebar separates double-clicks from drag and ignores controls', async () => {
  const ui = await app(); const bar = ui.q('.titlebar');
  const down = { button: 0, pointerId: 1, clientX: 100, clientY: 20 };
  for (let i = 0; i < 2; i++) {
    await bar.dispatch('pointerdown', down);
    await ui.document.dispatch('pointerup');
    await bar.dispatch('pointerdown', down);
    await ui.document.dispatch('pointerup');
    await bar.dispatch('dblclick', { button: 0 });
  }
  assert.equal(ui.calls.filter(x => x.method === 'window.toggleMaximize').length, 2);
  assert.equal(ui.calls.filter(x => x.method === 'window.drag').length, 0);
  await bar.dispatch('pointerdown', down);
  await ui.document.dispatch('pointermove', { ...down, buttons: 1, clientX: 102 });
  assert.equal(ui.calls.filter(x => x.method === 'window.drag').length, 0);
  await ui.document.dispatch('pointermove', { ...down, buttons: 1, clientX: 110 });
  await ui.document.dispatch('pointermove', { ...down, buttons: 1, clientX: 120 });
  assert.equal(ui.calls.filter(x => x.method === 'window.drag').length, 1);
  for (const end of ['pointerup', 'pointercancel']) {
    await bar.dispatch('pointerdown', down);
    await ui.document.dispatch(end);
    await ui.document.dispatch('pointermove', { ...down, buttons: 1, clientX: 120 });
  }
  const button = ui.q('[data-window-maximize]');
  await bar.dispatch('pointerdown', { ...down, target: button });
  await ui.document.dispatch('pointermove', { ...down, buttons: 1, clientX: 120 });
  await bar.dispatch('dblclick', { button: 0, target: button });
  assert.equal(ui.calls.filter(x => x.method === 'window.drag').length, 1);
  assert.equal(ui.calls.filter(x => x.method === 'window.toggleMaximize').length, 2);
  await button.click();
  assert.equal(ui.calls.filter(x => x.method === 'window.toggleMaximize').length, 3);
  assert.equal(bar.querySelector('[data-config-field]'), null);
  for (const field of ['themeMode', 'palette']) {
    assert.equal(ui.document.querySelectorAll(`[data-config-field="${field}"]`).length, 1);
    assert.ok(ui.q(`[data-config-field="${field}"]`).closest('[data-settings-panel="appearance"]'));
  }
});

test('Escape and outside click close menus and return focus', async () => {
  const ui = await app(); const menu = ui.q('[data-last-phrase-menu]'); const trigger = menu.querySelector('summary');
  menu.open = true; await ui.document.dispatch('keydown', { key: 'Escape' });
  assert.equal(menu.open, false); assert.equal(ui.document.activeElement, trigger);
  menu.open = true; await ui.document.dispatch('click', { target: ui.q('[data-home-heading]') });
  assert.equal(menu.open, false); assert.equal(ui.document.activeElement, trigger);
});

test('system appearance and reduced motion preserve the five families and hotkey capture', async () => {
  const capture = deferred();
  const ui = await app({ request: method => method === 'hotkey.capture.start' ? capture.promise : undefined });
  const button = ui.q('[data-hotkey-capture]'); const pending = button.click(); await flush();
  for (const palette of UI.choices.palette) {
    await ui.emit('config.changed', { config: { themeMode: 'system', palette } });
    const media = ui.media['(prefers-color-scheme: dark)'];
    for (const dark of [true, false]) {
      media.matches = dark; media.callback();
      assert.equal(ui.document.documentElement.dataset.theme, dark ? 'dark' : 'light');
      assert.equal(ui.document.documentElement.dataset.palette, palette);
    }
  }
  assert.equal(button.textContent, 'Cancelar captura');
  assert.equal(ui.calls.some(x => x.method === 'hotkey.capture.cancel'), false);
  await ui.emit('hud.state', { state: 'listening' });
  assert.ok(ui.q('[data-brand-state]').getAttribute('src').includes('listening'));
  const reduced = ui.media['(prefers-reduced-motion: reduce)']; reduced.matches = true; reduced.callback();
  assert.ok(ui.q('[data-brand-state]').getAttribute('src').includes('symbol'));
  capture.resolve({ hotkey: 'F8' }); await pending;
  assert.equal(button.textContent, 'Capturar nova');
});

test('invalid native microphone identity is rejected and disconnection keeps a recovery message', async () => {
  const invalid = await app({ request: method => method === 'mic.monitor.start'
    ? Promise.resolve({ started: true, currentDevice: '', threshold: .02 }) : undefined });
  await invalid.route('microphone'); await invalid.q('[data-microphone-start]').click();
  assert.equal(invalid.q('[data-threshold-input]').disabled, true);
  assert.ok(invalid.q('[data-announcer]').textContent.includes('identidade'));
  assert.ok(invalid.calls.some(x => x.method === 'mic.monitor.stop'));
  const ui = await app(); await ui.route('microphone'); await ui.q('[data-microphone-start]').click();
  await ui.emit('mic.error', { message: 'Entrada desconectada; teste novamente.' });
  assert.equal(ui.q('[data-vad-explanation]').textContent, 'Entrada desconectada; teste novamente.');
  assert.equal(ui.q('[data-threshold-input]').disabled, true);
});

test('history deletion leaves independent usage and nullable pricing version visible', async () => {
  const ui = await app({ history: [{ id: '1', text: 'Texto de teste', at: '2026-01-01', reviewUsage: {
    provider: 'deepseek', model: 'test', pricingVersion: null, estimatedCostUsd: null } }],
  usage: { providers: [{ provider: 'deepseek', requests: 1, totalTokens: 25, estimatedCostUsd: .01,
    pricedRequests: 1, unpricedRequests: 0, coverageKnown: true, pricingVersions: ['v1'] }] } });
  assert.equal(ui.q('[data-history-pricing-version]').textContent, 'Não informada');
  assert.equal(ui.q('[data-history-estimated-cost]').textContent, 'Não calculado');
  const before = ui.q('[data-usage-summary]').textContent;
  await ui.q('[data-history-clear]').click();
  assert.equal(ui.q('[data-usage-summary]').textContent, before);
  assert.equal(ui.q('[data-history-copy]').disabled, true);
});

test('confirmed provider switch clears previous credential only in its explicit patch', async () => {
  const ui = await app(); const provider = ui.q('[data-config-field="postProcessProvider"]');
  provider.value = 'openai-compatible'; await provider.dispatch('change');
  const patch = ui.calls.find(x => x.method === 'config.set').params.patch;
  assert.equal(patch.postProcessApiKey, null); assert.equal(patch.postProcess, false);
  assert.equal(patch.postProcessModel, '');
  assert.equal(ui.q('[data-config-toggle="postProcess"]').disabled, true);
  assert.equal(ui.q('[data-config-secret]').dataset.configSecret, 'postProcessOpenAiApiKey');
});

test('an enabled review can be disabled even when its model field is empty', async () => {
  const ui = await app({ config: { postProcess: true, postProcessProvider: 'anthropic', postProcessModel: '' } });
  const toggle = ui.q('[data-config-toggle="postProcess"]');
  assert.equal(toggle.disabled, false);
  assert.equal(toggle.getAttribute('aria-checked'), 'true');
  await toggle.click();
  assert.equal(ui.calls.find(call => call.method === 'config.set').params.patch.postProcess, false);
});

test('last phrase deletion keeps its id even when history filters hide it', async () => {
  const ui = await app({ history: [
    { id: 'latest', text: 'new phrase', at: '2026-09-07T12:00:00', characterCount: 10 },
    { id: 'older', text: 'old phrase', at: '2026-09-07T11:00:00', characterCount: 10 }
  ] });
  ui.q('[data-history-search]').value = 'old';
  await ui.q('[data-history-search]').dispatch('input');
  await ui.q('[data-last-phrase-delete]').click();
  assert.equal(ui.calls.find(call => call.method === 'history.delete').params.id, 'latest');
});
