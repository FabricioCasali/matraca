// Minimal DOM for behavior tests. Not a layout engine or accessibility audit.
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const root = path.resolve(__dirname, '../../Matraca.Web/wwwroot');
const decode = text => text.replace(/&(?:quot|amp|lt|gt|#10|#39);/g,
  entity => ({ '&quot;': '"', '&amp;': '&', '&lt;': '<', '&gt;': '>', '&#10;': '\n', '&#39;': "'" })[entity]);
class Element {
  constructor(tag, document) {
    this.tagName = tag.toUpperCase(); this.ownerDocument = document;
    this.children = []; this.attrs = {}; this.style = {}; this.events = {};
    this.parentElement = null; this._text = ''; this._value = undefined;
    this.dataset = new Proxy({}, {
      get: (_, key) => this.getAttribute('data-' + key.replace(/[A-Z]/g, c => '-' + c.toLowerCase())),
      set: (_, key, value) => { this.setAttribute('data-' + key.replace(/[A-Z]/g, c => '-' + c.toLowerCase()), value); return true; }
    });
    this.classList = {
      contains: name => this.className.split(/\s+/).includes(name),
      add: (...names) => { this.className = [...new Set([...this.className.split(/\s+/), ...names])].filter(Boolean).join(' '); },
      remove: (...names) => { this.className = this.className.split(/\s+/).filter(x => !names.includes(x)).join(' '); },
      toggle: (name, force) => {
        const next = force === undefined ? !this.classList.contains(name) : force;
        this.classList[next ? 'add' : 'remove'](name); return next;
      }
    };
  }
  setAttribute(name, value) { this.attrs[name] = String(value); }
  getAttribute(name) { return this.attrs[name] ?? null; }
  hasAttribute(name) { return Object.hasOwn(this.attrs, name); }
  removeAttribute(name) { delete this.attrs[name]; }
  get id() { return this.attrs.id || ''; } set id(value) { this.attrs.id = value; }
  get className() { return this.attrs.class || ''; } set className(value) { this.attrs.class = value; }
  get type() { return this.attrs.type || ''; } set type(value) { this.attrs.type = value; }
  get open() { return this.hasAttribute('open'); } set open(value) { value ? this.setAttribute('open', '') : this.removeAttribute('open'); }
  get hidden() { return this.hasAttribute('hidden'); } set hidden(value) { value ? this.setAttribute('hidden', '') : this.removeAttribute('hidden'); }
  get disabled() { return this.hasAttribute('disabled'); } set disabled(value) { value ? this.setAttribute('disabled', '') : this.removeAttribute('disabled'); }
  get value() { return this._value ?? this.attrs.value ?? (this.tagName === 'SELECT' ? this.children[0]?.value || '' : ''); }
  set value(value) { this._value = String(value); }
  get textContent() { return this._text + this.children.map(x => x.textContent).join(''); }
  set textContent(value) { this.replaceChildren(); this._text = String(value); }
  get innerHTML() { return this.textContent; }
  set innerHTML(value) { this.replaceChildren(); this._text = String(value).replace(/<br\s*\/?\s*>/gi, '\n').replace(/<[^>]+>/g, ''); }
  append(...children) {
    for (let child of children) {
      if (typeof child === 'string') { const text = new Element('#text', this.ownerDocument); text._text = child; child = text; }
      child.remove(); child.parentElement = this; this.children.push(child);
    }
  }
  replaceChildren(...children) { this.children.forEach(x => { x.parentElement = null; }); this.children = []; this._text = ''; this.append(...children); }
  remove() { if (this.parentElement) this.parentElement.children = this.parentElement.children.filter(x => x !== this); this.parentElement = null; }
  replaceWith(other) { const p = this.parentElement, i = p.children.indexOf(this); other.remove(); p.children[i] = other; other.parentElement = p; this.parentElement = null; }
  contains(node) { return node === this || this.children.some(child => child.contains(node)); }
  matches(selector) {
    return selector.split(',').some(group => {
      const parts = group.trim().split(/\s+(?=(?:[^"]*"[^"]*")*[^"]*$)/);
      const match = (node, simple) => {
        const tag = simple.match(/^[a-z][\w-]*/i)?.[0];
        if (tag && node.tagName !== tag.toUpperCase()) return false;
        for (const [, cls] of simple.matchAll(/\.([\w-]+)/g)) if (!node.classList.contains(cls)) return false;
        for (const [, name, value] of simple.matchAll(/\[([^\]=]+)(?:=["']?([^"'\]]*)["']?)?\]/g))
          if (!node.hasAttribute(name) || (value !== undefined && node.getAttribute(name) !== value)) return false;
        return true;
      };
      let node = this;
      if (!match(node, parts.pop())) return false;
      while (parts.length) { const part = parts.pop(); node = node.parentElement; while (node && !match(node, part)) node = node.parentElement; if (!node) return false; }
      return true;
    });
  }
  querySelectorAll(selector) { return this.children.flatMap(child => [...(child.matches(selector) ? [child] : []), ...child.querySelectorAll(selector)]); }
  querySelector(selector) { return this.querySelectorAll(selector)[0] || null; }
  closest(selector) { for (let node = this; node; node = node.parentElement) if (node.matches(selector)) return node; return null; }
  addEventListener(type, callback) { (this.events[type] ||= []).push(callback); }
  dispatch(type, data = {}) {
    const event = { target: this, currentTarget: this, ...data };
    const pending = (this.events[type] || []).map(callback => callback(event));
    event.currentTarget = null;
    return Promise.all(pending);
  }
  click() { if (!this.disabled) return this.dispatch('click'); }
  focus() { this.ownerDocument.activeElement = this; }
  select() { this.selectionStart = 0; this.selectionEnd = this.value.length; }
}
function parse(html) {
  const document = new Element('#document'); document.ownerDocument = document;
  document.createElement = tag => new Element(tag, document);
  document.createTextNode = value => { const node = document.createElement('#text'); node._text = value; return node; };
  document.getElementById = id => document.querySelectorAll('[id]').find(node => node.id === id) || null;
  const stack = [document];
  for (const token of html.matchAll(/<!--[\s\S]*?-->|<![^>]+>|<\/([^>]+)>|<([a-z][\w-]*)([^>]*?)>|([^<]+)/gi)) {
    if (token[1]) {
      if (stack.at(-1).tagName !== token[1].toUpperCase()) throw new Error(`Unbalanced HTML: ${token[1]}`);
      stack.pop();
    } else if (token[2]) {
      const node = document.createElement(token[2]);
      for (const [, key, value] of token[3].matchAll(/([^\s=/>]+)(?:="([^"]*)")?/g)) node.setAttribute(key, decode(value || ''));
      stack.at(-1).append(node);
      if (!['meta', 'link', 'input', 'img', 'br', 'hr'].includes(token[2]) && !/\/\s*>$/.test(token[0])) stack.push(node);
    } else if (token[4]) stack.at(-1)._text += decode(token[4]);
  }
  document.documentElement = document.querySelector('html');
  return document;
}
async function flush() { for (let i = 0; i < 30; i++) await Promise.resolve(); }
function deferred() { let resolve, reject; const promise = new Promise((a, b) => { resolve = a; reject = b; }); return { promise, resolve, reject }; }
async function app(options = {}) {
  const document = parse(fs.readFileSync(path.join(root, 'index.html'), 'utf8'));
  const calls = [], subscribers = [], events = {}, media = {};
  let config = { hotkey: 'F15', mode: 'toggle', inputDevice: '', modelPath: 'models/test.bin',
    themeMode: 'system', palette: 'olive', silenceMs: 450, postProcessProvider: 'anthropic', ...options.config };
  let history = options.history || [];
  const usage = options.usage || { providers: [] };
  const context = vm.createContext({
    document, URL, Intl, console, Date, Set, Map, Promise, navigator: { language: options.systemLanguage || 'pt-BR' },
    location: { hash: options.hash || '#home' }, confirm: () => options.confirm !== false,
    addEventListener: (type, fn) => { (events[type] ||= []).push(fn); },
    matchMedia: query => media[query] ||= { matches: false, addEventListener(_, fn) { this.callback = fn; } },
    Option: function(text, value) { const node = document.createElement('option'); node.textContent = text; node.value = value; return node; },
    matraca: {
      subscribe: fn => subscribers.push(fn), notify() {},
      request: async (method, params) => {
        calls.push({ method, params });
        const override = options.request?.(method, params);
        if (override !== undefined) return override;
        if (method === 'app.get') return { config: { config }, state: { state: 'ready' }, devices: ['USB mic'], capabilities: { historyClear: true },
          models: { entries: [{ id: 'base', label: 'Base', bytes: 1048576, downloaded: false, path: 'models/base.bin' }] }, history: { entries: history }, aiUsage: usage };
        if (method === 'config.set') { config = { ...config, ...params.patch }; return { config }; }
        if (method === 'ai.usage.get') return usage;
        if (method === 'history.list') return { entries: history };
        if (method === 'history.clear') { history = []; return {}; }
        if (method === 'mic.monitor.start') return { started: true, currentDevice: 'USB mic', threshold: .02 };
        if (method === 'mic.monitor.stop') return { stopped: true };
        return {};
      }
    }
  });
  vm.runInContext(fs.readFileSync(path.join(root, 'i18n/catalog-pt-BR.js'), 'utf8'), context);
  vm.runInContext(fs.readFileSync(path.join(root, 'i18n/catalog-en-US.js'), 'utf8'), context);
  vm.runInContext(fs.readFileSync(path.join(root, 'i18n.js'), 'utf8'), context);
  vm.runInContext(fs.readFileSync(path.join(root, 'ui-model.js'), 'utf8'), context);
  vm.runInContext(fs.readFileSync(path.join(root, 'app.js'), 'utf8'), context);
  await flush();
  return {
    context, document, calls, media, q: selector => document.querySelector(selector),
    async emit(type, payload) { subscribers.forEach(fn => fn({ type, payload })); await flush(); },
    async route(route) { context.location.hash = '#' + route; (events.hashchange || []).forEach(fn => fn()); await flush(); }
  };
}
module.exports = { app, flush, deferred, root };
