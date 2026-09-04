(() => {
  "use strict";

  const root = document.documentElement;
  const pages = [...document.querySelectorAll("[data-page]")];
  const routes = [...document.querySelectorAll("[data-route]")];
  const validRoutes = new Set(pages.map(page => page.dataset.page));
  const spectrum = document.querySelector("[data-spectrum]");
  const state = { config: null, history: [], selectedHistoryId: null, monitoredDevice: "" };
  let microphoneRequested = false;
  let microphoneGeneration = 0;
  let microphoneProofSent = false;
  let saveQueue = Promise.resolve();

  function text(selector, value) {
    const element = document.querySelector(selector);
    if (element) element.textContent = value;
  }

  function showRoute(route) {
    const selected = validRoutes.has(route) ? route : "home";
    pages.forEach(page => page.classList.toggle("active", page.dataset.page === selected));
    routes.forEach(button => button.classList.toggle("active", button.dataset.route === selected));
    document.title = selected === "home" ? "Matraca" : `Matraca · ${selected}`;
    if (selected === "microphone") startMicrophone();
    else stopMicrophone();
  }

  function db(value) {
    return value > 0 ? `${(20 * Math.log10(value)).toFixed(1)} dB` : "−∞ dB";
  }

  function setMicrophoneState(label, active) {
    const element = document.querySelector("[data-microphone-state]");
    if (!element) return;
    element.className = `status-pill${active ? " listening" : ""}`;
    element.replaceChildren(document.createElement("i"), document.createTextNode(` ${label}`));
  }

  function applyConfig(config) {
    if (!config) return;
    state.config = config;
    const hotkey = config.hotkey || "F15";
    const model = config.modelPath?.split(/[\\/]/).pop();
    text("[data-current-hotkey]", hotkey);
    text("[data-config-hotkey]", hotkey);
    text("[data-config-language]", config.language || "pt");
    text("[data-onboarding-hotkey]", hotkey);
    text("[data-onboarding-model]", model || "Modelo não configurado");
    text("[data-model-summary]", model
      ? `${model} · ${config.gpu || "auto"}`
      : "Modelo ainda não configurado");
    document.querySelectorAll("[data-config-mode] [data-value]").forEach(button => {
      button.classList.toggle("active", button.dataset.value === (config.mode || "live"));
    });
    document.querySelectorAll("[data-home-mode] [data-mode]").forEach(item => {
      item.classList.toggle("active", item.dataset.mode === (config.mode || "live"));
    });
    const autoEnter = document.querySelector("[data-config-auto-enter]");
    autoEnter?.classList.toggle("active", config.autoEnter === true);
    autoEnter?.setAttribute(
      "aria-label",
      `Enter automático ${config.autoEnter ? "ligado" : "desligado"}`);
  }

  function applyRuntime(runtime) {
    if (!runtime) return;
    const value = runtime.state || "ready";
    const labels = {
      ready: "PRONTO",
      listening: "OUVINDO",
      thinking: "PROCESSANDO",
      error: "ATENÇÃO"
    };
    const pill = document.querySelector("[data-runtime-state]");
    if (pill) {
      pill.className = `status-pill ${value === "listening" ? "listening" : "ready"}`;
      pill.replaceChildren(document.createElement("i"), document.createTextNode(` ${labels[value] || value}`));
    }
    text("[data-home-heading]", runtime.text || "Sua voz está pronta.");
  }

  function setHistory(entries) {
    state.history = entries || [];
    if (!state.history.some(entry => entry.id === state.selectedHistoryId))
      state.selectedHistoryId = state.history[0]?.id ?? null;
    renderHistory();
    const latest = state.history[0];
    text("[data-last-phrase]", latest?.text || "O histórico local aparecerá aqui.");
    text("[data-last-phrase-time]", latest
      ? `ÚLTIMA FRASE · ${new Date(latest.at).toLocaleTimeString([], {
          hour: "2-digit",
          minute: "2-digit"
        })}`
      : "SEM DITADOS");
  }

  function renderHistory() {
    const list = document.querySelector("[data-history-list]");
    if (!list) return;
    const query = document.querySelector("[data-history-search]")
      ?.value.trim().toLocaleLowerCase() || "";
    const entries = state.history.filter(entry => entry.text.toLocaleLowerCase().includes(query));
    list.replaceChildren();

    const heading = document.createElement("div");
    heading.className = "day";
    heading.textContent = "DITADOS";
    const count = document.createElement("span");
    count.textContent = `${entries.length} itens`;
    heading.append(count);
    list.append(heading);

    for (const entry of entries) {
      const button = document.createElement("button");
      button.type = "button";
      button.className = `history-item${entry.id === state.selectedHistoryId ? " active" : ""}`;
      const time = document.createElement("time");
      time.textContent = new Date(entry.at).toLocaleTimeString([], {
        hour: "2-digit",
        minute: "2-digit"
      });
      const content = document.createElement("span");
      const summary = document.createElement("strong");
      summary.textContent = entry.text;
      const meta = document.createElement("small");
      meta.textContent = `${entry.characterCount} caracteres`;
      content.append(summary, meta);
      button.append(time, content);
      button.addEventListener("click", () => {
        state.selectedHistoryId = entry.id;
        renderHistory();
      });
      list.append(button);
    }

    const selected = state.history.find(entry => entry.id === state.selectedHistoryId);
    text("[data-history-time]", selected
      ? new Date(selected.at).toLocaleString()
      : "SELECIONE UM DITADO");
    text("[data-history-text]", selected?.text || "O histórico local aparecerá aqui.");
    text("[data-history-mode]", state.config?.mode || "—");
    text("[data-history-language]", state.config?.language || "—");
    text("[data-history-length]", String(selected?.characterCount ?? 0));
    document.querySelectorAll("[data-history-detail] button")
      .forEach(button => button.disabled = !selected);
  }

  async function saveConfig(patch) {
    if (!state.config || !globalThis.matraca) return;
    text("[data-save-state]", "Salvando…");
    saveQueue = saveQueue.then(async () => {
      try {
        const result = await globalThis.matraca.request("config.set", { patch });
        applyConfig(result.config);
        text(
          "[data-save-state]",
          result.restartRequired ? "Salvo · reinicie para GPU/CPU" : "Tudo salvo");
      } catch (error) {
        text("[data-save-state]", `Não salvo · ${error?.message || error?.code || "erro"}`);
      }
    });
    return saveQueue;
  }

  async function startMicrophone() {
    if (microphoneRequested || !globalThis.matraca) return;
    const generation = ++microphoneGeneration;
    microphoneRequested = true;
    try {
      const result = await globalThis.matraca.request("mic.monitor.start", {
        device: state.config?.inputDevice || ""
      });
      if (generation !== microphoneGeneration) return;
      microphoneRequested = result.started !== false;
      setMicrophoneState(microphoneRequested ? "ESCUTANDO" : "PAUSADO", microphoneRequested);
      if (microphoneRequested) text("[data-microphone-permission]", "Permitido");
      state.monitoredDevice = result.currentDevice || "";
      text("[data-device-label]", result.currentDevice || "Microfone padrão");
      text("[data-threshold-value]", Number(result.threshold).toFixed(3));
      const input = document.querySelector("[data-threshold-input]");
      if (input) input.value = String(Math.min(.05, result.threshold));
    } catch (error) {
      if (generation !== microphoneGeneration) return;
      microphoneRequested = false;
      setMicrophoneState("INDISPONÍVEL", false);
      text("[data-microphone-permission]", "Não permitido");
      text("[data-vad-explanation]", error?.message || "Microfone indisponível.");
    }
  }

  async function stopMicrophone() {
    if (!microphoneRequested || !globalThis.matraca) return;
    ++microphoneGeneration;
    microphoneRequested = false;
    setMicrophoneState("PAUSADO", false);
    try { await globalThis.matraca.request("mic.monitor.stop"); } catch { }
  }

  function applyMicrophone(frame) {
    text("[data-current-level]", db(frame.rms));
    text("[data-peak-level]", db(frame.peak));
    text("[data-threshold-value]", Number(frame.threshold).toFixed(3));
    text("[data-vad-state]", frame.speech ? "● voz detectada" : "○ ruído ignorado");
    text("[data-vad-explanation]", frame.speech
      ? "Sua voz está acima do limiar e será capturada."
      : "O ambiente está abaixo do limiar e será ignorado.");
    const level = document.querySelector("[data-live-level]");
    if (level) level.style.width = `${Math.min(100, Math.sqrt(frame.rms || 0) * 300)}%`;
    const thresholdPosition = Math.min(
      100,
      Math.max(0, ((frame.threshold || .012) - .001) / .049 * 100));
    const thresholdMark = document.querySelector("[data-threshold-mark]");
    if (thresholdMark) thresholdMark.style.left = `${thresholdPosition}%`;
    [...(spectrum?.children || [])].forEach((bar, index) => {
      const value = frame.bands?.[index] || 0;
      bar.style.height = `${Math.min(100, Math.sqrt(value) * 260)}%`;
    });
    if (!microphoneProofSent) {
      microphoneProofSent = true;
      globalThis.matraca?.notify("ui.micReady", {
        bandCount: frame.bands?.length || 0,
        hasFiniteLevel: Number.isFinite(frame.rms)
      });
    }
  }

  async function loadSnapshot() {
    if (!globalThis.matraca) return;
    try {
      const snapshot = await globalThis.matraca.request("app.get");
      if (!snapshot?.config?.config) return;
      applyConfig(snapshot.config.config);
      setHistory(snapshot.history?.entries);
      applyRuntime(snapshot.state);
      text(
        "[data-accessibility-permission]",
        snapshot.permissions?.accessibility === "granted" ? "Permitido" : "Abrir Ajustes");
      text(
        "[data-microphone-permission]",
        snapshot.permissions?.microphone === "granted" ? "Permitido" : "Verificar nos Ajustes");
      globalThis.matraca.notify("ui.dataReady", {
        historyCount: state.history.length,
        hotkey: state.config.hotkey || "F15",
        deviceCount: snapshot.devices?.length || 0
      });
    } catch (error) {
      text("[data-home-heading]", error?.message || "A interface nativa não respondeu.");
    }
  }

  const initialHeights = [18,25,32,29,37,46,58,71,82,74,67,86,95,78,69,62,73,88,81,68,55,61,72,66,59,49,55,63,51,44,48,53,41,36,43,38,31,35,28,25,29,23,19,21,16,14,12,9];
  for (const height of initialHeights) {
    const bar = document.createElement("i");
    bar.style.height = `${height}%`;
    spectrum?.append(bar);
  }

  routes.forEach(button => button.addEventListener("click", () => {
    location.hash = button.dataset.route;
  }));
  addEventListener("hashchange", () => showRoute(location.hash.slice(1)));
  document.querySelector("[data-theme-toggle]")?.addEventListener("click", () => {
    const isDark = root.dataset.theme
      ? root.dataset.theme === "dark"
      : matchMedia("(prefers-color-scheme: dark)").matches;
    root.dataset.theme = isDark ? "light" : "dark";
  });
  document.querySelector("[data-history-search]")?.addEventListener("input", renderHistory);
  document.querySelectorAll("[data-config-mode] [data-value]").forEach(button => {
    button.addEventListener("click", () => saveConfig({ mode: button.dataset.value }));
  });
  document.querySelector("[data-config-auto-enter]")?.addEventListener("click", () => {
    saveConfig({ autoEnter: state.config?.autoEnter !== true });
  });
  document.querySelector("[data-threshold-input]")?.addEventListener("change", event => {
    const threshold = Number(event.currentTarget.value);
    if (state.monitoredDevice) {
      saveConfig({
        micSensitivity: {
          ...(state.config?.micSensitivity || {}),
          [state.monitoredDevice]: threshold
        }
      });
    } else {
      saveConfig({ vadThreshold: threshold });
    }
  });
  document.querySelector("[data-history-delete]")?.addEventListener("click", async () => {
    if (!state.selectedHistoryId || !confirm("Apagar este ditado do histórico local?")) return;
    try {
      await globalThis.matraca?.request("history.delete", { id: state.selectedHistoryId });
      const result = await globalThis.matraca?.request("history.list");
      setHistory(result?.entries);
    } catch (error) {
      text("[data-history-time]", error?.message || "Não foi possível apagar.");
    }
  });
  document.querySelector("[data-history-copy]")?.addEventListener("click", async () => {
    if (!state.selectedHistoryId) return;
    try {
      await globalThis.matraca?.request("history.copy", { id: state.selectedHistoryId });
      text("[data-history-time]", "COPIADO PARA O CLIPBOARD");
    } catch (error) {
      text("[data-history-time]", error?.message || "Não foi possível copiar.");
    }
  });
  document.querySelector("[data-history-repaste]")?.addEventListener("click", async () => {
    if (!state.selectedHistoryId) return;
    try {
      await globalThis.matraca?.request("history.repaste", { id: state.selectedHistoryId });
    } catch (error) {
      text("[data-history-time]", error?.message || "Não foi possível recolar.");
    }
  });
  document.querySelectorAll("[data-open-permission]").forEach(button => {
    button.addEventListener("click", () => globalThis.matraca?.request(
      "permissions.open-settings",
      { name: button.dataset.openPermission }));
  });
  globalThis.matraca?.subscribe(message => {
    if (message.type === "mic.frame") applyMicrophone(message.payload);
    if (message.type === "window.opened") {
      ++microphoneGeneration;
      microphoneRequested = false;
      showRoute(location.hash.slice(1));
    }
    if (message.type === "hud.state") applyRuntime(message.payload);
    if (message.type === "config.changed" && message.payload?.config)
      applyConfig(message.payload.config);
  });

  showRoute(location.hash.slice(1));
  loadSnapshot();
  globalThis.matraca?.notify("ui.ready", {
    pageCount: pages.length,
    spectrumBandCount: spectrum?.children.length ?? 0
  });
})();
