(() => {
  "use strict";

  const hud = document.querySelector("[data-hud]");
  const title = document.querySelector("[data-title]");
  const detail = document.querySelector("[data-detail]");
  const states = new Set(["ready", "listening", "thinking", "writing", "done", "error"]);

  function applyState(message) {
    if (message.type !== "hud.state" || !states.has(message.payload?.state)) return;
    hud.className = `hud state-${message.payload.state}`;
    title.textContent = message.payload.title ?? "Matraca";
    detail.textContent = message.payload.detail ?? "";
  }

  if (globalThis.matraca) globalThis.matraca.subscribe(applyState);
  else addEventListener("DOMContentLoaded", () => globalThis.matraca?.subscribe(applyState), { once: true });
  document.addEventListener("DOMContentLoaded", () => {
    globalThis.matraca?.notify("ui.hudReady", {});
  }, { once: true });
})();
