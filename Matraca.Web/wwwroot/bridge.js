(() => {
  "use strict";

  const version = 1;
  const listeners = new Set();
  const pending = new Map();
  let sequence = 0;

  function nativePost(message) {
    const json = JSON.stringify(message);
    if (globalThis.chrome?.webview?.postMessage) {
      globalThis.chrome.webview.postMessage(json);
      return true;
    }
    if (globalThis.webkit?.messageHandlers?.matraca?.postMessage) {
      globalThis.webkit.messageHandlers.matraca.postMessage(json);
      return true;
    }
    return false;
  }

  function dispatch(message) {
    for (const listener of listeners) listener(message);
  }

  function onMessage(value) {
    let message;
    try {
      message = typeof value === "string" ? JSON.parse(value) : value;
    } catch {
      return;
    }
    if (!message || message.version !== version) return;

    if (message.type === "response" && message.id && pending.has(message.id)) {
      const request = pending.get(message.id);
      pending.delete(message.id);
      if (message.ok === false) request.reject(message.error ?? { code: "unknown" });
      else request.resolve(message.result);
      return;
    }
    dispatch(message);
  }

  function request(method, params = {}) {
    const id = `web-${Date.now()}-${++sequence}`;
    const message = { version, id, type: "request", method, params };
    if (!nativePost(message)) {
      return Promise.resolve({ mock: true, method, params });
    }
    return new Promise((resolve, reject) => pending.set(id, { resolve, reject }));
  }

  function notify(type, payload = {}) {
    if (typeof type !== "string" || type.length === 0) return false;
    return nativePost({ version, type, payload });
  }

  function subscribe(listener) {
    listeners.add(listener);
    return () => listeners.delete(listener);
  }

  globalThis.matraca = Object.freeze({ version, onMessage, request, notify, subscribe });
  document.addEventListener("DOMContentLoaded", () => {
    nativePost({ version, type: "bridge.ready", payload: {} });
  }, { once: true });
})();
