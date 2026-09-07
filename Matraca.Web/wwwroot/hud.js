(() => {
  "use strict";

  const hud = document.querySelector("[data-hud]");
  const title = document.querySelector("[data-title]");
  const detail = document.querySelector("[data-detail]");
  const brand = document.querySelector("[data-brand]");
  const copy = document.querySelector(".state-copy");
  const states = new Set(["ready", "listening", "thinking", "writing", "done", "error"]);
  const reducedMotion = matchMedia("(prefers-reduced-motion: reduce)");
  const darkMode = matchMedia("(prefers-color-scheme: dark)");
  let themeMode = "system";
  let currentState = "ready";
  let generation = -1;
  let visible = false;
  let messageAnimation;

  function applyAppearance(appearance) {
    if (!appearance) return;
    if (["system", "light", "dark"].includes(appearance.themeMode)) themeMode = appearance.themeMode;
    if (["olive", "ochre", "terracotta", "plum", "teal"].includes(appearance.palette))
      document.documentElement.dataset.palette = appearance.palette;
    document.documentElement.dataset.themeMode = themeMode;
    document.documentElement.dataset.theme = themeMode === "system" ? (darkMode.matches ? "dark" : "light") : themeMode;
  }

  function updateBrand() {
    const symbol = reducedMotion.matches ? "symbol" : ({ listening: "listening", thinking: "loading", writing: "loading", done: "done" }[currentState] || "symbol");
    // The shared overlay tokens specify a dark surface in every application theme.
    const source = `assets/brand/matraca-03a-${symbol}-dark.svg`;
    if (brand.getAttribute("src") !== source) brand.setAttribute("src", source);
  }

  function applyState(message) {
    if (message.version !== 1) return;
    if (message.type === "hud.appearance") { applyAppearance(message.payload); return; }
    if (message.type !== "hud.state" || !states.has(message.payload?.state)) return;
    const payload = message.payload;
    if (Number.isSafeInteger(payload.generation)) {
      if (payload.generation < generation) return;
      generation = payload.generation;
    }
    applyAppearance(payload.appearance);
    const nextTitle = payload.title ?? "Matraca";
    const nextDetail = payload.detail ?? "";
    const changed = currentState !== payload.state || title.textContent !== nextTitle || detail.textContent !== nextDetail;
    const wasVisible = visible;
    currentState = payload.state;
    visible = currentState !== "ready";
    hud.className = `hud state-${currentState}${visible ? " is-visible" : " is-exiting"}`;
    hud.setAttribute("aria-hidden", String(!visible));
    if (changed) {
      title.textContent = nextTitle;
      detail.textContent = nextDetail;
      messageAnimation?.cancel();
      if (visible && wasVisible && !reducedMotion.matches)
        messageAnimation = copy.animate([{ opacity: .5, transform: "translateY(2px)" }, { opacity: 1, transform: "translateY(0)" }], {
          duration: parseFloat(getComputedStyle(hud).getPropertyValue("--motion-state")),
          easing: getComputedStyle(hud).getPropertyValue("--ease-out").trim()
        });
    }
    updateBrand();
  }

  reducedMotion.addEventListener("change", () => { messageAnimation?.cancel(); updateBrand(); });
  darkMode.addEventListener("change", () => applyAppearance({ themeMode }));
  applyAppearance({ themeMode: "system", palette: "olive" });
  globalThis.matraca?.subscribe(applyState);
  document.addEventListener("DOMContentLoaded", () => globalThis.matraca?.notify("ui.hudReady", {}), { once: true });
})();
