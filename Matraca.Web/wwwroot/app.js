(() => {
  "use strict";

  const root = document.documentElement;
  const pages = [...document.querySelectorAll("[data-page]")];
  const routes = [...document.querySelectorAll("[data-route]")];
  const validRoutes = new Set(pages.map(page => page.dataset.page));
  const spectrum = document.querySelector("[data-spectrum]");
  const state = { config: null, history: [], devices: [], models: [], platform: null, capabilities: {}, selectedHistoryId: null, monitoredDevice: "", runtime: { state: "ready" } };
  const configDefaults = {
    language: "pt", hotkey: "F15", pinHotkey: "", pinDelivery: "focus",
    mode: "live", autoEnter: false,
    beep: true, beepVolume: .8, silenceMs: 450, phraseMaxSeconds: 6,
    startSound: "", stopSound: "", vocabulary: [],
    inputDevice: "", history: true, historyMaxItems: 100,
    postProcess: false, postProcessProvider: "anthropic", postProcessEndpoint: "",
    postProcessModel: "claude-opus-5", postProcessReasoning: "", postProcessPrompt: "",
    postProcessTimeoutMs: 8000, idleUnloadMinutes: 5, gpu: "auto",
    focusBorder: true, focusBorderThickness: 4, focusBorderOpacity: .9,
    focusBorderColor: "#E81123", focusBorderColorBusy: "#FFB900",
    focusBorderColorPinned: "#0078D4",
    pasteMethod: "unicode"
  };
  let microphoneRequested = false;
  let microphoneGeneration = 0;
  let microphoneProofSent = false;
  let capturingHotkey = false;
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

  function levelPosition(value) {
    const minimum = .001;
    const maximum = .5;
    return Math.min(100, Math.max(0, (Number(value) - minimum) / (maximum - minimum) * 100));
  }

  function applyCapabilities(platform, capabilities) {
    state.platform = platform || null;
    state.capabilities = capabilities || {};
    if (state.platform) root.dataset.platform = state.platform;
    document.querySelectorAll("[data-capability]").forEach(control => {
      control.hidden = state.capabilities[control.dataset.capability] !== true;
    });
  }

  function setMicrophoneState(label, active) {
    const element = document.querySelector("[data-microphone-state]");
    if (!element) return;
    element.className = `status-pill${active ? " listening" : ""}`;
    element.replaceChildren(document.createElement("i"), document.createTextNode(` ${label}`));
  }

  function applyConfig(config) {
    if (!config) return;
    state.config = {
      ...configDefaults,
      ...Object.fromEntries(Object.entries(config).filter(([, value]) => value != null))
    };
    const hotkey = state.config.hotkey;
    const model = state.config.modelPath?.split(/[\\/]/).pop();
    text("[data-current-hotkey]", hotkey);
    text("[data-config-hotkey]", hotkey);
    text("[data-config-pin-hotkey]",
      state.config.pinHotkey && state.config.pinHotkey !== "none"
        ? state.config.pinHotkey
        : "Nenhuma");
    text("[data-onboarding-hotkey]", hotkey);
    text("[data-onboarding-model]", model || "Modelo não configurado");
    text("[data-model-summary]", model
      ? `${model} · ${state.config.gpu}`
      : "Modelo ainda não configurado");
    document.querySelectorAll("[data-config-mode] [data-value]").forEach(button => {
      button.classList.toggle("active", button.dataset.value === state.config.mode);
    });
    document.querySelectorAll("[data-home-mode] [data-mode]").forEach(item => {
      item.classList.toggle("active", item.dataset.mode === state.config.mode);
    });
    const autoEnter = document.querySelector("[data-config-auto-enter]");
    autoEnter?.classList.toggle("active", state.config.autoEnter === true);
    autoEnter?.setAttribute(
      "aria-label",
      `Enter automático ${state.config.autoEnter ? "ligado" : "desligado"}`);
    document.querySelectorAll("[data-config-field]").forEach(control => {
      const value = state.config[control.dataset.configField];
      if (value != null)
        control.value = control.hasAttribute("data-config-list") && Array.isArray(value)
          ? value.join("\n")
          : String(value);
    });
    document.querySelectorAll("[data-config-toggle]").forEach(control => {
      const enabled = state.config[control.dataset.configToggle] === true;
      control.classList.toggle("active", enabled);
      control.setAttribute("aria-pressed", String(enabled));
    });
    text("[data-api-key-state]", state.config.postProcessApiKeyConfigured
      ? "Configurada; digite apenas para substituir."
      : "Não configurada; nunca é devolvida à interface.");
    const openAiCompatible = state.config.postProcessProvider === "openai-compatible";
    const deepSeek = state.config.postProcessProvider === "deepseek";
    const apiKey = document.querySelector("[data-config-secret]");
    if (apiKey) apiKey.dataset.configSecret = deepSeek
      ? "postProcessDeepSeekApiKey"
      : openAiCompatible ? "postProcessOpenAiApiKey" : "postProcessApiKey";
    document.querySelectorAll("[data-openai-compatible]")
      .forEach(element => element.hidden = !openAiCompatible);
    document.querySelectorAll("[data-deepseek]")
      .forEach(element => element.hidden = !deepSeek);
    text("[data-review-provider-effect]", deepSeek
      ? "Usa a API oficial da DeepSeek; apenas o texto transcrito é enviado."
      : openAiCompatible
        ? "Usa o endpoint configurado; apenas o texto transcrito é enviado."
        : "Usa a API da Anthropic; apenas o texto transcrito é enviado.");
    text("[data-review-model-effect]", deepSeek
      ? "Escolha DeepSeek V4 Flash ou Pro."
      : "Nome aceito pelo provedor para revisar o texto transcrito.");
    const reviewModel = document.querySelector("[data-review-model]");
    if (reviewModel) {
      if (deepSeek) reviewModel.setAttribute("list", "deepseek-models");
      else reviewModel.removeAttribute("list");
      reviewModel.placeholder = deepSeek ? "Escolha um modelo" : "claude-opus-5";
    }
    const reviewReady = !deepSeek
      || Boolean(state.config.postProcessModel && state.config.postProcessReasoning);
    const reviewToggle = document.querySelector('[data-config-toggle="postProcess"]');
    if (reviewToggle) {
      reviewToggle.disabled = !reviewReady;
      if (!reviewReady) {
        reviewToggle.classList.remove("active");
        reviewToggle.setAttribute("aria-pressed", "false");
      }
    }
    const modelSetup = document.querySelector("[data-model-setup]");
    modelSetup?.classList.toggle("complete", Boolean(state.config.modelPath));
    text("[data-model-icon]", state.config.modelPath ? "✓" : "↓");
    updateConfigEffects();
    applyRuntime(state.runtime);
  }

  function applyDevices(devices) {
    state.devices = devices || [];
    const select = document.querySelector("[data-device-select]");
    if (!select) return;
    select.replaceChildren(new Option("Padrão do sistema", ""));
    for (const device of state.devices) select.append(new Option(device, device));
    if (state.config) select.value = state.config.inputDevice || "";
  }

  function applyModels(models) {
    state.models = models?.entries || [];
    const select = document.querySelector("[data-model-choice]");
    if (!select) return;
    const selected = select.value;
    select.replaceChildren(...state.models.map(model => new Option(
      `${model.label}${model.downloaded ? " · pronto" : ""}`,
      model.id)));
    const preferred = state.models.find(model => model.id === selected)
      || state.models.find(model => model.downloaded)
      || state.models.find(model => model.id === "ggml-base.bin")
      || state.models[0];
    if (preferred) select.value = preferred.id;
    const button = document.querySelector("[data-model-download]");
    if (button && preferred) button.textContent = preferred.downloaded ? "Usar" : "Baixar";
  }

  function updateConfigEffects() {
    const value = field => Number(document.querySelector(`[data-config-field="${field}"]`)?.value);
    text("[data-config-effect=\"beepVolume\"]", `${Math.round(value("beepVolume") * 100)}% do volume máximo.`);
    text("[data-config-effect=\"focusBorderThickness\"]", `${value("focusBorderThickness")} pixels ao redor do destino.`);
    text("[data-config-effect=\"focusBorderOpacity\"]", `${Math.round(value("focusBorderOpacity") * 100)}% de opacidade.`);
  }

  function applyRuntime(runtime) {
    if (!runtime) return;
    state.runtime = runtime;
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
    const toggle = document.querySelector("[data-dictation-toggle]");
    if (toggle) {
      const recording = value === "listening";
      toggle.disabled = state.config?.mode !== "toggle" || value === "thinking";
      toggle.setAttribute("aria-pressed", String(recording));
      toggle.setAttribute("aria-label", recording ? "Encerrar gravação" : "Iniciar gravação");
    }
  }

  function applyPostProcessRuntime(active) {
    text("[data-post-process-state]", !state.config?.postProcess
      ? "Desligado; nenhuma chamada de rede será feita."
      : active
        ? "Ativo para os próximos ditados."
        : "Inativo; revise provedor, endpoint, modelo e chave.");
  }

  function setLastPhrase(entry) {
    text("[data-last-phrase]", entry?.text || "O histórico local aparecerá aqui.");
    text("[data-last-phrase-time]", entry
      ? `ÚLTIMA FRASE · ${new Date(entry.at).toLocaleTimeString([], {
          hour: "2-digit",
          minute: "2-digit"
        })}`
      : "SEM DITADOS");
  }

  function setHistory(entries, updateLastPhrase = true) {
    state.history = entries || [];
    if (!state.history.some(entry => entry.id === state.selectedHistoryId))
      state.selectedHistoryId = state.history[0]?.id ?? null;
    renderHistory();
    if (updateLastPhrase) setLastPhrase(state.history[0]);
    document.querySelectorAll("[data-last-phrase-copy], [data-last-phrase-repaste], [data-last-phrase-delete]")
      .forEach(button => { button.disabled = !state.history.length; });
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
        applyPostProcessRuntime(result.postProcessActive === true);
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
      if (input) input.value = String(Math.min(.5, result.threshold));
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
    const speech = Number(frame.rms) > Number(frame.threshold);
    text("[data-current-level]", db(frame.rms));
    text("[data-peak-level]", db(frame.peak));
    text("[data-threshold-value]", Number(frame.threshold).toFixed(3));
    text("[data-vad-state]", speech ? "● voz detectada" : "○ ruído ignorado");
    text("[data-vad-explanation]", speech
      ? "Sua voz está acima do limiar e será capturada."
      : "O ambiente está abaixo do limiar e será ignorado.");
    document.querySelector(".threshold")?.classList.toggle("voice-detected", speech);
    document.querySelector("[data-vad-state]")?.classList.toggle("active", speech);
    const level = document.querySelector("[data-live-level]");
    if (level) {
      level.style.width = `${levelPosition(frame.rms)}%`;
      level.classList.toggle("active", speech);
    }
    const thresholdMark = document.querySelector("[data-threshold-mark]");
    if (thresholdMark) thresholdMark.style.left = `${levelPosition(frame.threshold)}%`;
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
      applyCapabilities(snapshot.platform, snapshot.capabilities);
      applyDevices(snapshot.devices);
      applyModels(snapshot.models);
      applyConfig(snapshot.config.config);
      applyPostProcessRuntime(snapshot.config.runtime?.postProcessActive === true);
      if (snapshot.config.runtime?.restartRequired)
        text("[data-save-state]", `Reinicie: usando ${snapshot.config.runtime.gpu}, salvo ${snapshot.config.runtime.desiredGpu}`);
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
        deviceCount: snapshot.devices?.length || 0,
        modelCount: state.models.length,
        modelsValid: state.models.length > 0 && state.models.every(model =>
          typeof model.id === "string" && model.id.length > 0
          && typeof model.label === "string" && model.label.length > 0
          && Number.isFinite(model.bytes) && model.bytes > 0
          && typeof model.downloaded === "boolean"
          && typeof model.path === "string" && model.path.length > 0)
          && [...document.querySelectorAll("[data-model-choice] option")]
            .every(option => option.textContent && !option.textContent.includes("undefined"))
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
  document.querySelector("[data-window-minimize]")?.addEventListener("click", () =>
    globalThis.matraca?.request("window.minimize"));
  document.querySelector("[data-window-maximize]")?.addEventListener("click", () =>
    globalThis.matraca?.request("window.toggleMaximize"));
  document.querySelector("[data-window-close]")?.addEventListener("click", () =>
    globalThis.matraca?.request("window.close"));
  document.querySelector(".titlebar")?.addEventListener("pointerdown", event => {
    if (event.button !== 0 || event.target.closest("button")) return;
    globalThis.matraca?.request("window.drag");
  });
  document.querySelector(".titlebar")?.addEventListener("dblclick", event => {
    if (event.target.closest("button")) return;
    globalThis.matraca?.request("window.toggleMaximize");
  });
  document.querySelector("[data-history-search]")?.addEventListener("input", renderHistory);
  document.querySelectorAll("[data-config-mode] [data-value]").forEach(button => {
    button.addEventListener("click", () => saveConfig({ mode: button.dataset.value }));
  });
  document.querySelectorAll("[data-home-mode] [data-mode]").forEach(button => {
    button.addEventListener("click", () => saveConfig({ mode: button.dataset.mode }));
  });
  document.querySelector("[data-dictation-toggle]")?.addEventListener("click", async event => {
    if (!globalThis.matraca || event.currentTarget.disabled) return;
    event.currentTarget.disabled = true;
    try {
      const runtime = await globalThis.matraca.request("dictation.toggle");
      applyRuntime(runtime);
    } catch (error) {
      text("[data-home-heading]", error?.message || "Não foi possível alternar a gravação.");
      applyRuntime(state.runtime);
    }
  });
  document.querySelector("[data-config-auto-enter]")?.addEventListener("click", () => {
    saveConfig({ autoEnter: state.config?.autoEnter !== true });
  });
  document.querySelectorAll("[data-settings-tab]").forEach(button => {
    button.addEventListener("click", () => {
      document.querySelectorAll("[data-settings-tab]").forEach(item =>
        item.classList.toggle("active", item === button));
      document.querySelectorAll("[data-settings-panel]").forEach(panel =>
        panel.classList.toggle("active", panel.dataset.settingsPanel === button.dataset.settingsTab));
    });
  });
  document.querySelector("[data-model-choice]")?.addEventListener("change", event => {
    const model = state.models.find(item => item.id === event.currentTarget.value);
    const button = document.querySelector("[data-model-download]");
    if (button && model) button.textContent = model.downloaded ? "Usar" : "Baixar";
  });
  document.querySelector("[data-model-download]")?.addEventListener("click", async event => {
    if (!globalThis.matraca || event.currentTarget.disabled) return;
    const select = document.querySelector("[data-model-choice]");
    const progress = document.querySelector("[data-model-progress]");
    event.currentTarget.disabled = true;
    event.currentTarget.textContent = "Preparando…";
    if (progress) {
      progress.hidden = false;
      progress.removeAttribute("value");
    }
    try {
      const result = await globalThis.matraca.request("model.download.start", { id: select.value });
      applyModels(result.models);
      applyConfig(result.config);
      text("[data-onboarding-model]", `${result.path.split(/[\\/]/).pop()} pronto para uso`);
    } catch (error) {
      text("[data-onboarding-model]", error?.message || "Não foi possível baixar o modelo.");
    } finally {
      event.currentTarget.disabled = false;
      if (progress) progress.hidden = true;
      const selected = state.models.find(model => model.id === select.value);
      event.currentTarget.textContent = selected?.downloaded ? "Usar" : "Baixar";
    }
  });
  document.querySelectorAll("[data-config-field]").forEach(control => {
    if (control.type === "range") control.addEventListener("input", updateConfigEffects);
    control.addEventListener("change", () => {
      const field = control.dataset.configField;
      const numeric = control.type === "number" || control.type === "range";
      const value = control.hasAttribute("data-config-list")
        ? control.value.split(/[\n,]/).map(item => item.trim()).filter(Boolean)
        : numeric ? Number(control.value) : control.value;
      if (field === "postProcessProvider" && value !== state.config?.postProcessProvider) {
        const apiKeyField = state.config?.postProcessProvider === "deepseek"
          ? "postProcessDeepSeekApiKey"
          : state.config?.postProcessProvider === "openai-compatible"
            ? "postProcessOpenAiApiKey"
            : "postProcessApiKey";
        saveConfig({
          postProcessProvider: value,
          postProcess: false,
          postProcessModel: value === "anthropic" ? "claude-opus-5" : "",
          postProcessReasoning: "",
          [apiKeyField]: null
        });
      } else {
        saveConfig({ [field]: value });
      }
    });
  });
  document.querySelectorAll("[data-config-toggle]").forEach(control => {
    control.addEventListener("click", () => {
      const field = control.dataset.configToggle;
      saveConfig({ [field]: state.config?.[field] !== true });
    });
  });
  document.querySelectorAll("[data-config-secret]").forEach(control => {
    control.addEventListener("change", async () => {
      if (!control.value) return;
      await saveConfig({ [control.dataset.configSecret]: control.value });
      control.value = "";
    });
  });
  document.querySelectorAll("[data-hotkey-capture]").forEach(button => {
    button.addEventListener("click", async event => {
      if (!globalThis.matraca) return;
      if (capturingHotkey) {
        await globalThis.matraca.request("hotkey.capture.cancel");
        return;
      }
      const field = event.currentTarget.dataset.hotkeyCapture;
      const help = event.currentTarget.dataset.hotkeyHelpTarget;
      const originalLabel = event.currentTarget.textContent;
      capturingHotkey = true;
      event.currentTarget.textContent = "Pressione uma tecla";
      text(help, "O próximo atalho será capturado sem iniciar um ditado.");
      try {
        const result = await globalThis.matraca.request("hotkey.capture.start");
        if (field === "pinHotkey" && result?.hotkey === state.config?.hotkey)
          throw new Error("Use uma tecla diferente da tecla de ditado.");
        if (field === "hotkey"
          && state.config?.pinHotkey
          && state.config.pinHotkey !== "none"
          && result?.hotkey === state.config.pinHotkey)
          throw new Error("Use uma tecla diferente da tecla que fixa o destino.");
        if (result?.hotkey) await saveConfig({ [field]: result.hotkey });
        text(help, "Atalho aplicado a quente.");
      } catch (error) {
        if (error?.code !== "canceled")
          text(help, error?.message || "Não foi possível capturar o atalho.");
      } finally {
        capturingHotkey = false;
        event.currentTarget.textContent = originalLabel;
      }
    });
  });
  document.querySelectorAll("[data-clear-config]").forEach(button => {
    button.addEventListener("click", () => saveConfig({ [button.dataset.clearConfig]: "none" }));
  });
  document.querySelectorAll("[data-file-pick]").forEach(button => {
    button.addEventListener("click", async () => {
      try {
        const result = await globalThis.matraca?.request("file.pick", {
          kind: button.dataset.fileKind
        });
        if (result?.path) await saveConfig({ [button.dataset.filePick]: result.path });
      } catch (error) {
        text("[data-save-state]", `Não selecionado · ${error?.message || error?.code || "erro"}`);
      }
    });
  });
  document.querySelectorAll("[data-sound-preview]").forEach(button => {
    button.addEventListener("click", async () => {
      const field = button.dataset.soundPreview;
      try {
        await globalThis.matraca?.request("sound.preview", {
          start: button.dataset.soundStart === "true",
          path: document.querySelector(`[data-config-field="${field}"]`)?.value || ""
        });
      } catch (error) {
        text("[data-save-state]", `Não foi possível ouvir · ${error?.message || error?.code || "erro"}`);
      }
    });
  });
  document.querySelector("[data-threshold-input]")?.addEventListener("input", event => {
    text("[data-threshold-value]", Number(event.currentTarget.value).toFixed(3));
    const mark = document.querySelector("[data-threshold-mark]");
    if (mark) mark.style.left = `${levelPosition(event.currentTarget.value)}%`;
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
  document.querySelector("[data-history-clear]")?.addEventListener("click", async () => {
    if (!state.history.length || !confirm("Limpar todo o histórico local? Esta ação não pode ser desfeita.")) return;
    try {
      await globalThis.matraca?.request("history.clear");
      setHistory([]);
    } catch (error) {
      text("[data-history-time]", error?.message || "Não foi possível limpar o histórico.");
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
  document.querySelector("[data-last-phrase-copy]")?.addEventListener("click", () => {
    state.selectedHistoryId = state.history[0]?.id ?? null;
    document.querySelector("[data-history-copy]")?.click();
    document.querySelector("[data-last-phrase-menu]")?.removeAttribute("open");
  });
  document.querySelector("[data-last-phrase-repaste]")?.addEventListener("click", () => {
    state.selectedHistoryId = state.history[0]?.id ?? null;
    document.querySelector("[data-history-repaste]")?.click();
    document.querySelector("[data-last-phrase-menu]")?.removeAttribute("open");
  });
  document.querySelector("[data-last-phrase-delete]")?.addEventListener("click", () => {
    state.selectedHistoryId = state.history[0]?.id ?? null;
    document.querySelector("[data-history-delete]")?.click();
    document.querySelector("[data-last-phrase-menu]")?.removeAttribute("open");
  });
  document.querySelectorAll("[data-open-permission]").forEach(button => {
    button.addEventListener("click", () => globalThis.matraca?.request(
      "permissions.open-settings",
      { name: button.dataset.openPermission }));
  });
  document.querySelector(".onboarding-footer button")?.addEventListener("click", () => {
    location.hash = "home";
  });
  globalThis.matraca?.subscribe(message => {
    if (message.type === "mic.frame") applyMicrophone(message.payload);
    if (message.type === "window.opened") {
      ++microphoneGeneration;
      microphoneRequested = false;
      showRoute(location.hash.slice(1));
    }
    if (message.type === "hud.state") applyRuntime(message.payload);
    if (message.type === "dictation.completed") {
      setHistory(message.payload.history?.entries, false);
      setLastPhrase(message.payload);
    }
    if (message.type === "model.download.progress") {
      const progress = document.querySelector("[data-model-progress]");
      if (progress) {
        progress.max = message.payload.total || 1;
        progress.value = message.payload.done || 0;
      }
      const percent = message.payload.total
        ? Math.round(message.payload.done / message.payload.total * 100)
        : 0;
      text("[data-onboarding-model]", `Baixando modelo local · ${percent}%`);
    }
    if (message.type === "config.changed" && message.payload?.config) {
      applyConfig(message.payload.config);
      applyPostProcessRuntime(message.payload.postProcessActive === true);
    }
  });

  showRoute(location.hash.slice(1));
  loadSnapshot();
  globalThis.matraca?.notify("ui.ready", {
    pageCount: pages.length,
    spectrumBandCount: spectrum?.children.length ?? 0,
    settingsTabCount: document.querySelectorAll("[data-settings-tab]").length,
    settingsControlCount: document.querySelectorAll(
      "[data-config-field],[data-config-toggle],[data-config-secret],[data-hotkey-capture],[data-config-auto-enter],[data-config-mode] [data-value]").length
  });
})();
