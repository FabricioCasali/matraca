(() => {
  "use strict";

  const root = document.documentElement;
  const UI = globalThis.MatracaUI;
  const systemTheme = matchMedia("(prefers-color-scheme: dark)");
  const reducedMotion = matchMedia("(prefers-reduced-motion: reduce)");
  const pages = [...document.querySelectorAll("[data-page]")];
  const routes = [...document.querySelectorAll("[data-route]")];
  const validRoutes = new Set(pages.map(page => page.dataset.page));
  const spectrum = document.querySelector("[data-spectrum]");
  const state = { config: null, history: [], aiUsage: null, deepSeekBalance: null, deepSeekBalanceAt: 0, devices: [], models: [], platform: null, capabilities: {}, selectedHistoryId: null, monitoredDevice: "", route: "", settingsTab: "key", runtime: { state: "unknown" } };
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
    pasteMethod: "unicode", themeMode: "system", palette: "olive"
  };
  let microphoneRequested = false;
  let microphoneGeneration = 0;
  let microphoneProofSent = false;
  let microphoneStarting = false;
  let microphoneStopping = false;
  let microphoneFailure = "";
  let thresholdEditing = false;
  let historyBusy = false;
  let acceptedConfig = null;
  let capturingHotkey = false;
  let saveQueue = Promise.resolve();
  let microphoneQueue = Promise.resolve();
  let downloading = false;
  const drafts = new Map();
  const pendingFields = new Map();
  const fieldErrors = new Map();
  let draftSequence = 0;

  function announce(message, kind = "error") {
    const notice = document.querySelector("[data-announcer]");
    notice.dataset.kind = kind;
    notice.setAttribute("role", kind === "error" ? "alert" : "status");
    text("[data-announcer]", message);
  }

  function applyAppearance() {
    root.dataset.theme = UI.theme(state.config, systemTheme.matches);
    root.dataset.palette = state.config?.palette || "olive";
    document.querySelectorAll("[data-brand]").forEach(image => {
      const activity = image.hasAttribute("data-brand-state") && !reducedMotion.matches
        ? ({ listening: "listening", thinking: "loading", writing: "loading", done: "done" })[state.runtime.state]
        : null;
      const source = `assets/brand/matraca-03a-${activity || "symbol"}-${root.dataset.theme}.svg`;
      if (image.getAttribute("src") !== source) image.setAttribute("src", source);
    });
  }

  function fieldError(control, message) {
    const field = control.dataset.configField;
    const error = document.getElementById(`${control.id}-error`);
    if (error) { error.textContent = message; error.hidden = !message; }
    control.setAttribute("aria-invalid", String(Boolean(message)));
    if (message) {
      if (fieldErrors.get(field) !== message) announce(message);
      fieldErrors.set(field, message);
    } else fieldErrors.delete(field);
  }

  function text(selector, value) {
    const element = document.querySelector(selector);
    if (element) {
      if (element.tagName === "TEXTAREA") element.value = value;
      else element.textContent = value;
    }
  }

  function showRoute(route) {
    const selected = validRoutes.has(route) ? route : "home";
    const changed = state.route !== selected;
    state.route = selected;
    pages.forEach(page => {
      page.classList.toggle("active", page.dataset.page === selected);
      page.hidden = page.dataset.page !== selected;
    });
    routes.forEach(button => {
      button.classList.toggle("active", button.dataset.route === selected);
      if (button.dataset.route === selected) button.setAttribute("aria-current", "page");
      else button.removeAttribute("aria-current");
    });
    document.title = selected === "home" ? "Matraca" : `Matraca · ${selected}`;
    if (changed) stopMicrophone();
    placeSharedPanels();
    if (changed) document.querySelector(".pages").scrollTop = 0;
    if (selected === "history") refreshAiUsage();
  }

  function placeSharedPanels() {
    const micSlot = document.querySelector(`[data-microphone-slot="${state.route === "settings" ? "settings" : "page"}"]`);
    const mic = document.querySelector("[data-microphone-panel]");
    if (mic && mic.parentElement !== micSlot) micSlot?.append(mic);
    const modelSlot = document.querySelector(`[data-model-slot="${state.route === "settings" ? "settings" : "onboarding"}"]`);
    const model = document.querySelector("[data-model-setup]");
    if (model && model.parentElement !== modelSlot) modelSlot?.append(model);
  }

  function showSettingsTab(tab) {
    if (state.settingsTab !== tab) stopMicrophone();
    state.settingsTab = tab;
    document.querySelectorAll("[data-settings-tab]").forEach(item => {
      const active = item.dataset.settingsTab === tab;
      item.classList.toggle("active", active);
      item.setAttribute("aria-pressed", String(active));
    });
    document.querySelectorAll("[data-settings-panel]").forEach(panel => {
      const active = panel.dataset.settingsPanel === tab;
      panel.classList.toggle("active", active);
      panel.hidden = !active;
    });
    placeSharedPanels();
    if (tab === "review") refreshAiUsage();
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
    const previousDevice = state.config?.inputDevice;
    acceptedConfig = {
      ...configDefaults,
      ...Object.fromEntries(Object.entries(config).filter(([, value]) => value != null))
    };
    state.config = { ...acceptedConfig };
    // A response for an earlier field must not roll back a later queued intent.
    for (const [field, pending] of pendingFields) state.config[field] = pending.value;
    if (previousDevice != null && previousDevice !== state.config.inputDevice) {
      stopMicrophone();
      state.monitoredDevice = "";
      updateMicrophoneControls();
      text("[data-device-label]", "entrada ainda não identificada");
      text("[data-threshold-value]", "—");
    }
    applyAppearance();
    const hotkey = state.config.hotkey;
    const model = state.config.modelPath?.split(/[\\/]/).pop();
    text("[data-current-hotkey]", hotkey);
    text("[data-config-hotkey]", hotkey);
    text("[data-config-pin-hotkey]",
      state.config.pinHotkey && state.config.pinHotkey !== "none"
        ? state.config.pinHotkey
        : "Nenhuma");
    text("[data-onboarding-hotkey]", hotkey);
    if (!downloading) text("[data-onboarding-model]", model ? `Caminho configurado: ${model}. Valide com um ditado real.` : "Modelo não configurado");
    text("[data-model-summary]", model
      ? `${model} · ${state.config.gpu}`
      : "Modelo ainda não configurado");
    document.querySelectorAll("[data-config-mode] [data-value]").forEach(button => {
      button.classList.toggle("active", button.dataset.value === state.config.mode);
      button.setAttribute("aria-pressed", String(button.dataset.value === state.config.mode));
    });
    document.querySelectorAll("[data-home-mode] [data-mode]").forEach(item => {
      item.classList.toggle("active", item.dataset.mode === state.config.mode);
      item.setAttribute("aria-pressed", String(item.dataset.mode === state.config.mode));
    });
    const autoEnter = document.querySelector("[data-config-auto-enter]");
    autoEnter?.classList.toggle("active", state.config.autoEnter === true);
    autoEnter?.setAttribute("role", "switch");
    autoEnter?.setAttribute("aria-checked", String(state.config.autoEnter === true));
    autoEnter?.setAttribute(
      "aria-label",
      `Enter automático ${state.config.autoEnter ? "ligado" : "desligado"}`);
    document.querySelectorAll("[data-config-field]").forEach(control => {
      const value = state.config[control.dataset.configField];
      if (value != null && !drafts.has(control.dataset.configField) && document.activeElement !== control)
        control.value = control.hasAttribute("data-config-list") && Array.isArray(value)
          ? value.join("\n")
          : String(value);
    });
    document.querySelectorAll("[data-config-toggle]").forEach(control => {
      const enabled = state.config[control.dataset.configToggle] === true;
      control.classList.toggle("active", enabled);
      control.setAttribute("role", "switch");
      control.setAttribute("aria-checked", String(enabled));
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
      reviewModel.placeholder = deepSeek || openAiCompatible ? "Informe um modelo" : "claude-opus-5";
    }
    const reviewReady = Boolean(state.config.postProcessModel?.trim())
      && (!deepSeek || Boolean(state.config.postProcessReasoning));
    const reviewToggle = document.querySelector('[data-config-toggle="postProcess"]');
    if (reviewToggle) {
      // An invalid new model must never prevent turning an already active review off.
      reviewToggle.disabled = !reviewReady && state.config.postProcess !== true;
    }
    const modelSetup = document.querySelector("[data-model-setup]");
    modelSetup?.classList.remove("complete");
    text("[data-model-icon]", "↓");
    document.querySelectorAll("[data-config-field]").forEach(control => {
      const field = control.dataset.configField;
      if (["beepVolume", "startSound", "stopSound"].includes(field)) control.disabled = !state.config.beep;
      if (field.startsWith("focusBorder") && field !== "focusBorder") control.disabled = !state.config.focusBorder;
      if (field === "historyMaxItems") control.disabled = !state.config.history;
    });
    updateMicrophoneControls();
    const savedThreshold = Object.entries(state.config.micSensitivity || {}).find(([device]) =>
      device.toLocaleLowerCase() === state.monitoredDevice.toLocaleLowerCase())?.[1];
    if (savedThreshold != null) setThreshold(savedThreshold);
    updateConfigEffects();
    applyRuntime(state.runtime);
  }

  function applyDevices(devices) {
    state.devices = devices || [];
    const select = document.querySelector("[data-device-select]");
    if (!select) return;
    select.replaceChildren(new Option("Padrão do sistema", ""));
    for (const device of state.devices) select.append(new Option(device, device));
    if (state.config) {
      if (state.config.inputDevice && !state.devices.includes(state.config.inputDevice))
        select.append(new Option(`${state.config.inputDevice} · indisponível`, state.config.inputDevice));
      select.value = state.config.inputDevice || "";
    }
    if (state.monitoredDevice && !state.devices.includes(state.monitoredDevice)) {
      microphoneFailure = "A entrada foi desconectada. Escolha um dispositivo e teste novamente.";
      stopMicrophone();
      state.monitoredDevice = "";
      setMicrophoneState("DESCONECTADO", false);
      text("[data-vad-explanation]", "A entrada foi desconectada. Escolha um dispositivo e teste novamente.");
      announce("Microfone desconectado. Escolha uma entrada e teste novamente.");
      updateMicrophoneControls();
    }
  }

  function applyModels(models) {
    state.models = models?.entries || [];
    const select = document.querySelector("[data-model-choice]");
    if (!select) return;
    const selected = select.value;
    select.replaceChildren(...state.models.map(model => new Option(
      `${model.label} · ${formatTokens(Math.round(model.bytes / 1048576))} MB${model.downloaded ? " · baixado" : ""}`,
      model.id)));
    const preferred = state.models.find(model => model.id === selected)
      || state.models.find(model => model.downloaded)
      || state.models.find(model => model.id === "ggml-base.bin")
      || state.models[0];
    if (preferred) select.value = preferred.id;
    const button = document.querySelector("[data-model-download]");
    if (button && preferred && !downloading) button.textContent = preferred.downloaded ? "Usar" : "Baixar";
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
      writing: "INSERINDO", done: "ENTREGA CONCLUÍDA", unknown: "SEM CONEXÃO",
      error: "ATENÇÃO"
    };
    const pill = document.querySelector("[data-runtime-state]");
    if (pill) {
      pill.className = `status-pill ${value === "listening" ? "listening" : value === "error" ? "error" : value === "ready" || value === "done" ? "ready" : ""}`;
      pill.replaceChildren(document.createElement("i"), document.createTextNode(` ${labels[value] || value}`));
    }
    text("[data-home-heading]", runtime.text || runtime.title || (value === "ready" ? "Fale. Siga com seu dia." : labels[value] || value));
    text("[data-dictation-help]", state.config?.mode === "toggle"
      ? "Inicie e encerre pelo botão ou pelo atalho no aplicativo de destino."
      : `Use o atalho no destino. Modo ${state.config?.mode || "ainda não informado"}; o botão inicia apenas o modo toggle.`);
    applyAppearance();
    const toggle = document.querySelector("[data-dictation-toggle]");
    if (toggle) {
      const recording = value === "listening";
      toggle.disabled = state.config?.mode !== "toggle" || !["ready", "listening", "done", "error"].includes(value);
      toggle.setAttribute("aria-pressed", String(recording));
      toggle.setAttribute("aria-label", recording ? "Encerrar gravação" : "Iniciar gravação");
      text("[data-dictation-label]", recording ? "Encerrar ditado" : "Iniciar ditado");
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
    updateProviderOptions();
    renderHistory();
    renderUsageSummary();
    if (updateLastPhrase) setLastPhrase(state.history[0]);
    document.querySelectorAll("[data-last-phrase-copy], [data-last-phrase-repaste], [data-last-phrase-delete]")
      .forEach(button => { button.disabled = !state.history.length; });
  }

  function formatTokens(value) {
    return UI.tokens(value);
  }

  function applyAiUsage(usage) {
    state.aiUsage = usage || { providers: [] };
    const deepSeek = state.aiUsage.providers?.find(item => item.provider === "deepseek");
    text("[data-ai-requests]", formatTokens(deepSeek?.requests));
    text("[data-ai-prompt-tokens]", formatTokens(deepSeek?.promptTokens));
    text("[data-ai-cache-tokens]", formatTokens(deepSeek?.promptCacheHitTokens));
    text("[data-ai-completion-tokens]", formatTokens(deepSeek?.completionTokens));
    text("[data-ai-total-tokens]", formatTokens(deepSeek?.totalTokens));
    text("[data-ai-estimated-cost]", formatEstimatedCost(deepSeek?.estimatedCostUsd));
    const costLabel = document.querySelector("[data-ai-estimated-cost]")?.parentElement.querySelector("small");
    if (costLabel) costLabel.textContent = UI.usageSummary(state.aiUsage.providers, "deepseek").complete
      ? "CUSTO ESTIMADO USD" : "SUBTOTAL CONHECIDO USD";
    text("[data-ai-pricing-version]", deepSeek?.pricingVersions?.join(", ") || "não informada");
    updateProviderOptions();
    renderUsageSummary();
  }

  function formatEstimatedCost(value) {
    return UI.cost(value);
  }

  async function refreshAiUsage() {
    if (!globalThis.matraca) return;
    try {
      applyAiUsage(await globalThis.matraca.request("ai.usage.get"));
    } catch (error) {
      announce(error?.message || "Não foi possível ler o consumo local.");
    }
  }

  function formatBalance(balance) {
    try {
      return new Intl.NumberFormat(undefined, {
        style: "currency",
        currency: balance.currency
      }).format(balance.totalBalance);
    } catch {
      return `${balance.totalBalance} ${balance.currency}`;
    }
  }

  function applyDeepSeekBalance(result, cached = false) {
    const balances = result?.balances || [];
    text("[data-deepseek-balance]", balances.length
      ? balances.map(formatBalance).join(" · ")
      : result?.isAvailable ? "Sem saldo informado" : "Saldo indisponível");
    const checkedAt = new Date(state.deepSeekBalanceAt).toLocaleTimeString([], {
      hour: "2-digit",
      minute: "2-digit",
      second: "2-digit"
    });
    text("[data-deepseek-balance-detail]", result?.isAvailable
      ? `Saldo atual da conta · consultado às ${checkedAt}${cached ? " · cache recente" : ""}.`
      : "A DeepSeek informou que o saldo não está disponível.");
  }

  async function refreshDeepSeekBalance(button) {
    if (!globalThis.matraca || button.disabled) return;
    if (state.deepSeekBalance && Date.now() - state.deepSeekBalanceAt < 30000) {
      applyDeepSeekBalance(state.deepSeekBalance, true);
      return;
    }
    button.disabled = true;
    button.setAttribute("aria-busy", "true");
    button.textContent = "Consultando…";
    text("[data-deepseek-balance-detail]", "Consultando a DeepSeek agora.");
    try {
      await saveQueue;
      const result = await globalThis.matraca.request("deepseek.balance.get");
      state.deepSeekBalance = result;
      state.deepSeekBalanceAt = Date.now();
      applyDeepSeekBalance(result);
    } catch (error) {
      text("[data-deepseek-balance]", "Consulta falhou");
      text("[data-deepseek-balance-detail]", error?.message || "A DeepSeek não respondeu.");
    } finally {
      button.disabled = false;
      button.setAttribute("aria-busy", "false");
      button.textContent = "Atualizar saldo";
    }
  }

  function reviewProviderLabel(provider) {
    if (provider === "deepseek") return "DeepSeek";
    if (provider === "anthropic") return "Anthropic";
    return provider === "openai-compatible" ? "API compatível com OpenAI" : provider || "Não informado";
  }

  function updateProviderOptions() {
    const select = document.querySelector("[data-history-provider]");
    const selected = select.value;
    const providers = [...new Set([...(state.aiUsage?.providers || []).map(x => x.provider),
      ...state.history.map(x => x.reviewUsage?.provider).filter(Boolean)])];
    select.replaceChildren(new Option("Todos os provedores", "all"),
      ...providers.map(provider => new Option(reviewProviderLabel(provider), provider)));
    select.value = providers.includes(selected) ? selected : "all";
  }

  function renderUsageSummary() {
    const provider = document.querySelector("[data-history-provider]")?.value || "all";
    document.querySelectorAll("[data-usage-summary]").forEach(panel => {
      panel.replaceChildren();
      const total = UI.usageSummary(state.aiUsage?.providers, provider);
      const title = document.createElement("h2");
      title.textContent = total.complete ? "Custo estimado registrado" : "Subtotal estimado conhecido";
      const metrics = document.createElement("div");
      metrics.className = "usage-metrics";
      for (const [label, value] of [["Estimativa em USD", formatEstimatedCost(total.estimatedCostUsd)],
        ["Chamadas com consumo informado", formatTokens(total.requests)], ["Tokens informados", formatTokens(total.totalTokens)]]) {
        const metric = document.createElement("div");
        const caption = document.createElement("small"); caption.textContent = label;
        const number = document.createElement("strong"); number.textContent = value;
        metric.append(caption, number); metrics.append(metric);
      }
      const detail = document.createElement("p");
      detail.textContent = `${formatTokens(total.requests)} chamadas com consumo informado · ${formatTokens(total.totalTokens)} tokens. ` +
        `Com preço: ${formatTokens(total.pricedRequests)}; sem preço: ${formatTokens(total.unpricedRequests)}. ` +
        (total.complete ? "Estimativa em USD, não é uma fatura." : "Cobertura parcial ou desconhecida; não representa todo o gasto.");
      panel.append(title, metrics, detail);
      for (const item of (state.aiUsage?.providers || []).filter(x => provider === "all" || x.provider === provider)) {
        const row = document.createElement("p");
        row.textContent = `${reviewProviderLabel(item.provider)}: ${formatTokens(item.requests)} chamadas; ` +
          `entrada ${formatTokens(item.promptTokens)}, cache ${formatTokens(item.promptCacheHitTokens)}, ` +
          `sem cache ${formatTokens(item.promptCacheMissTokens)}, saída ${formatTokens(item.completionTokens)}, ` +
          `raciocínio ${formatTokens(item.reasoningTokens)} (incluído na saída), total ${formatTokens(item.totalTokens)}; ` +
          `${formatEstimatedCost(item.estimatedCostUsd)}. Tabelas: ${item.pricingVersions?.join(", ") || "não informadas"}.`;
        panel.append(row);
      }
    });
  }

  function renderHistory() {
    const list = document.querySelector("[data-history-list]");
    if (!list) return;
    const query = document.querySelector("[data-history-search]")
      ?.value.trim().toLocaleLowerCase() || "";
    const entries = UI.filterHistory(state.history, query, document.querySelector("[data-history-provider]")?.value || "all");
    if (!entries.some(entry => entry.id === state.selectedHistoryId)) state.selectedHistoryId = entries[0]?.id ?? null;
    list.replaceChildren();

    const heading = document.createElement("div");
    heading.className = "day";
    heading.textContent = "DITADOS";
    const count = document.createElement("span");
    count.textContent = `${entries.length} itens`;
    heading.append(count);
    list.append(heading);
    if (!entries.length) {
      const empty = document.createElement("p");
      empty.textContent = state.history.length ? "Nenhum ditado encontrado. Ajuste os filtros; o consumo não muda com a busca." : "Ainda não há ditados no histórico local.";
      list.append(empty);
    }

    for (const entry of entries) {
      const button = document.createElement("button");
      button.type = "button";
      button.className = `history-item${entry.id === state.selectedHistoryId ? " active" : ""}`;
      button.setAttribute("aria-pressed", String(entry.id === state.selectedHistoryId));
      const time = document.createElement("time");
      time.textContent = new Date(entry.at).toLocaleTimeString([], {
        hour: "2-digit",
        minute: "2-digit"
      });
      const content = document.createElement("span");
      const summary = document.createElement("strong");
      summary.textContent = entry.text;
      const meta = document.createElement("small");
      meta.textContent = entry.reviewUsage
        ? `${entry.characterCount} caracteres · ${formatTokens(entry.reviewUsage.totalTokens)} tokens`
        : `${entry.characterCount} caracteres`;
      content.append(summary, meta);
      button.append(time, content);
      button.addEventListener("click", () => {
        state.selectedHistoryId = entry.id;
        renderHistory();
      });
      list.append(button);
    }

    const selected = entries.find(entry => entry.id === state.selectedHistoryId);
    text("[data-history-time]", selected
      ? new Date(selected.at).toLocaleString()
      : "SELECIONE UM DITADO");
    text("[data-history-text]", selected?.text || "O histórico local aparecerá aqui.");
    text("[data-history-mode]", selected?.mode || "Não informado");
    text("[data-history-language]", selected?.language || "Não informado");
    text("[data-history-length]", String(selected?.characterCount ?? 0));
    const usage = selected?.reviewUsage;
    const usagePanel = document.querySelector("[data-history-review-usage]");
    if (usagePanel) usagePanel.hidden = !usage;
    text("[data-history-review-model]", usage
      ? `${reviewProviderLabel(usage.provider)} · ${usage.model}`
      : "—");
    text("[data-history-prompt-tokens]", formatTokens(usage?.promptTokens));
    text("[data-history-cache-tokens]", formatTokens(usage?.promptCacheHitTokens));
    text("[data-history-completion-tokens]", formatTokens(usage?.completionTokens));
    text("[data-history-reasoning-tokens]", formatTokens(usage?.reasoningTokens));
    text("[data-history-total-tokens]", formatTokens(usage?.totalTokens));
    text("[data-history-estimated-cost]", formatEstimatedCost(usage?.estimatedCostUsd));
    text("[data-history-cache-miss-tokens]", formatTokens(usage?.promptCacheMissTokens));
    text("[data-history-pricing-version]", usage?.pricingVersion || "Não informada");
    text("[data-history-usage-note]", usage ? "Raciocínio já integra a saída. Estimativa em USD, não é uma fatura."
      : "Consumo não informado. Isso não confirma ausência de revisão ou cobrança.");
    document.querySelectorAll("[data-history-detail] button")
      .forEach(button => button.disabled = !selected || historyBusy);
  }

  async function saveConfig(patch) {
    if (!state.config || !globalThis.matraca) { announce("Configuração indisponível: aguarde a conexão com o aplicativo."); return false; }
    const ticket = ++draftSequence;
    const revisions = new Map(Object.keys(patch).map(field => [field, drafts.get(field)?.revision]));
    for (const [field, value] of Object.entries(patch)) pendingFields.set(field, { ticket, value });
    text("[data-save-state]", "Salvando…");
    let saved = false;
    saveQueue = saveQueue.then(async () => {
      try {
        const result = await globalThis.matraca.request("config.set", { patch });
        if (!result?.config) throw new Error("O host não confirmou a configuração.");
        for (const field of Object.keys(patch)) {
          if (pendingFields.get(field)?.ticket === ticket) pendingFields.delete(field);
          if (drafts.get(field)?.revision === revisions.get(field)) drafts.delete(field);
        }
        applyConfig(result.config);
        applyPostProcessRuntime(result.postProcessActive === true);
        text(
          "[data-save-state]",
          result.restartRequired ? "Salvo · reinicie para GPU/CPU" : "Tudo salvo");
        saved = true;
      } catch (error) {
        for (const field of Object.keys(patch)) if (pendingFields.get(field)?.ticket === ticket) pendingFields.delete(field);
        if (acceptedConfig) applyConfig(acceptedConfig);
        text("[data-save-state]", `Não salvo · ${error?.message || error?.code || "erro"}`);
        announce(`Não salvo. ${error?.message || "Tente novamente."}`);
      }
    });
    await saveQueue;
    return saved;
  }

  async function startMicrophone() {
    if (microphoneRequested || microphoneStopping || !state.config || !globalThis.matraca) return;
    const generation = ++microphoneGeneration;
    microphoneRequested = true;
    microphoneFailure = "";
    thresholdEditing = false;
    microphoneStarting = true;
    state.monitoredDevice = "";
    zeroMicrophone();
    updateMicrophoneControls();
    setMicrophoneState("INICIANDO", false);
    microphoneQueue = microphoneQueue.then(async () => {
      try {
      await saveQueue;
      if (generation !== microphoneGeneration) return;
      const result = await globalThis.matraca.request("mic.monitor.start", {
        device: state.config?.inputDevice || ""
      });
      if (generation !== microphoneGeneration) return;
      if (result.started !== true || !result.currentDevice?.trim() ||
        !Number.isFinite(result.threshold) || result.threshold < .001 || result.threshold > .5)
        throw new Error("O host não iniciou o teste com identidade e ajuste válidos. Confira a entrada e tente novamente.");
      state.monitoredDevice = result.currentDevice;
      if (Array.isArray(result.devices)) applyDevices(result.devices);
      microphoneStarting = false;
      setMicrophoneState("TESTANDO", true);
      text("[data-microphone-permission]", "Permitido");
      text("[data-device-label]", result.currentDevice);
      setThreshold(result.threshold);
      updateMicrophoneControls();
    } catch (error) {
      if (generation !== microphoneGeneration) return;
      microphoneFailure = error?.message || "Microfone indisponível.";
      try { await globalThis.matraca.request("mic.monitor.stop"); microphoneRequested = false; }
      catch { announce("Não foi possível confirmar o encerramento. Tente Encerrar teste novamente."); }
      microphoneStarting = false;
      setMicrophoneState("INDISPONÍVEL", false);
      text("[data-vad-explanation]", error?.message || "Microfone indisponível.");
      announce(error?.message || "Microfone indisponível.");
      updateMicrophoneControls();
    }
    });
    return microphoneQueue;
  }

  async function stopMicrophone() {
    if (!microphoneRequested || microphoneStopping || !globalThis.matraca) return;
    ++microphoneGeneration;
    microphoneStopping = true;
    microphoneStarting = false;
    zeroMicrophone();
    setMicrophoneState("ENCERRANDO", false);
    updateMicrophoneControls();
    microphoneQueue = microphoneQueue.then(async () => {
      try {
        await globalThis.matraca.request("mic.monitor.stop");
        microphoneRequested = false;
        setMicrophoneState(microphoneFailure ? "FALHA" : "ENCERRADO", false);
        text("[data-vad-explanation]", microphoneFailure || "Teste encerrado. Nenhum áudio está sendo capturado pelo monitor.");
      } catch (error) {
        setMicrophoneState("FALHA AO ENCERRAR", false);
        announce(error?.message || "Não foi possível encerrar o teste. Tente novamente.");
      } finally {
        microphoneStopping = false;
        updateMicrophoneControls();
      }
    });
    return microphoneQueue;
  }

  function zeroMicrophone() {
    [...(spectrum?.children || [])].forEach(bar => { bar.style.height = "0%"; });
    const level = document.querySelector("[data-live-level]");
    if (level) level.style.width = "0%";
    text("[data-current-level]", "— dB");
    text("[data-peak-level]", "— dB");
    text("[data-vad-state]", "Sem sinal medido");
    document.querySelector(".threshold")?.classList.remove("voice-detected");
    document.querySelector("[data-vad-state]")?.classList.remove("active");
  }

  function updateMicrophoneControls() {
    const disabled = !state.config;
    document.querySelector("[data-microphone-start]").disabled = disabled || microphoneRequested || microphoneStopping;
    document.querySelector("[data-microphone-stop]").disabled = !microphoneRequested || microphoneStopping;
    document.querySelector("[data-device-select]").disabled = disabled || microphoneRequested || microphoneStopping;
    document.querySelector("[data-threshold-input]").disabled = !state.monitoredDevice || microphoneStarting || microphoneStopping;
    document.querySelector("[data-microphone-reset]").disabled = !state.monitoredDevice || microphoneStarting || microphoneStopping;
  }

  function setThreshold(threshold) {
    if (!Number.isFinite(threshold) || threshold < .001 || threshold > .5) return;
    const input = document.querySelector("[data-threshold-input]");
    if (document.activeElement !== input && !thresholdEditing) input.value = String(threshold);
    const value = Number(input.value);
    text("[data-threshold-value]", value.toFixed(3));
    input.setAttribute("aria-valuetext", `${value.toFixed(3)}; menor capta voz mais baixa, maior ignora mais ruído`);
    document.querySelector("[data-threshold-mark]").style.left = `${levelPosition(value)}%`;
  }

  function applyMicrophone(frame) {
    if (!microphoneRequested || microphoneStarting || microphoneStopping || !Number.isFinite(frame?.rms) || !Number.isFinite(frame?.threshold)) return;
    const frameDevice = frame.device || frame.currentDevice;
    if (frameDevice && frameDevice !== state.monitoredDevice) {
      microphoneFailure = "A entrada mudou durante o teste. Inicie novamente para carregar o ajuste correto.";
      stopMicrophone();
      announce("A entrada mudou durante o teste. Inicie novamente para carregar o ajuste correto.");
      state.monitoredDevice = "";
      return;
    }
    const speech = Number(frame.rms) > Number(frame.threshold);
    text("[data-current-level]", db(frame.rms));
    text("[data-peak-level]", db(frame.peak));
    setThreshold(frame.threshold);
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
    [...(spectrum?.children || [])].forEach((bar, index) => {
      const value = Math.max(0, Number(frame.bands?.[index]) || 0);
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
      if (!snapshot?.config?.config) throw new Error("O host não devolveu a configuração do Matraca.");
      applyCapabilities(snapshot.platform, snapshot.capabilities);
      applyDevices(snapshot.devices);
      applyModels(snapshot.models);
      applyConfig(snapshot.config.config);
      applyPostProcessRuntime(snapshot.config.runtime?.postProcessActive === true);
      setHistory(snapshot.history?.entries);
      applyAiUsage(snapshot.aiUsage);
      applyRuntime(snapshot.state);
      text("[data-save-state]", snapshot.config.runtime?.restartRequired
        ? `Reinicie: usando ${snapshot.config.runtime.gpu}, salvo ${snapshot.config.runtime.desiredGpu}`
        : "Configuração carregada");
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
      announce(error?.message || "A interface nativa não respondeu.");
    }
  }

  // Keep the existing composition and data attributes, adding native form semantics.
  document.querySelectorAll("[data-config-field], [data-config-toggle], [data-config-secret], [data-config-auto-enter]")
    .forEach((control, index) => {
      const setting = control.closest(".setting");
      const label = setting?.querySelector("strong");
      const help = setting?.querySelector("p");
      control.id ||= `config-control-${index}`;
      if (label) {
        label.id ||= `${control.id}-label`;
        if (!control.closest("label") || control.closest(".number-control")) control.setAttribute("aria-labelledby", label.id);
      }
      if (help) {
        help.id ||= `${control.id}-help`;
        control.setAttribute("aria-describedby", help.id);
      }
      if (control.hasAttribute("data-config-field")) {
        const error = document.createElement("p");
        error.id = `${control.id}-error`;
        error.className = "field-error";
        error.hidden = true;
        control.parentElement.append(error);
        control.setAttribute("aria-describedby", `${control.getAttribute("aria-describedby") || ""} ${error.id}`.trim());
        const range = UI.ranges[control.dataset.configField];
        if (range) { control.min = String(range[0]); control.max = String(range[1]); }
      }
    });
  const historyText = document.querySelector("[data-history-text]");
  const fullText = document.createElement("textarea");
  fullText.setAttribute("data-history-text", "");
  fullText.setAttribute("aria-label", "Texto integral do ditado");
  fullText.className = "field wide";
  fullText.readOnly = true;
  fullText.rows = 8;
  fullText.value = historyText.textContent;
  historyText.replaceWith(fullText);
  document.querySelector("[data-history-copy]").textContent = "Copiar mensagem";
  document.querySelector("[data-history-repaste]").classList.remove("primary");
  const usagePanel = document.querySelector("[data-history-review-usage]");
  for (const [name, label] of [["cache-miss-tokens", "ENTRADA SEM CACHE"], ["pricing-version", "VERSÃO DA TABELA"]]) {
    const item = document.createElement("span");
    const caption = document.createElement("small"); caption.textContent = label;
    const value = document.createElement("b"); value.setAttribute(`data-history-${name}`, "");
    item.append(caption, value); usagePanel.append(item);
  }
  const usageNote = document.createElement("p"); usageNote.setAttribute("data-history-usage-note", "");
  document.querySelector("[data-history-detail]").append(usageNote);
  const reviewSummary = document.createElement("section");
  reviewSummary.className = "panel usage-summary";
  reviewSummary.setAttribute("data-usage-summary", "");
  document.querySelector('[data-settings-panel="review"]').append(reviewSummary);
  const account = document.querySelector(".review-account");
  account.querySelector("header p").textContent = "Consumo local informado. Custo estimado conhecido em USD; registros legados podem ter cobertura desconhecida.";

  function closeMenu(menu, restoreFocus = true) {
    if (!menu.open) return;
    menu.open = false;
    if (restoreFocus) menu.querySelector("summary")?.focus();
  }
  document.addEventListener("keydown", event => {
    if (event.key !== "Escape") return;
    document.querySelectorAll("details[open]").forEach(menu => closeMenu(menu));
    if (capturingHotkey) globalThis.matraca.request("hotkey.capture.cancel").catch(error => announce(error?.message || "Não foi possível cancelar a captura."));
  });
  document.addEventListener("click", event => {
    document.querySelectorAll("details[open]").forEach(menu => {
      if (!menu.contains(event.target)) closeMenu(menu);
    });
  });
  document.querySelectorAll("[data-last-phrase-menu] button").forEach(button => button.addEventListener("click", () => {
    document.querySelector("[data-last-phrase-menu] summary")?.focus();
  }));

  for (let band = 0; band < 48; band++) {
    const bar = document.createElement("i");
    bar.style.height = "0%";
    spectrum?.append(bar);
  }

  routes.forEach(button => button.addEventListener("click", () => {
    location.hash = button.dataset.route;
  }));
  addEventListener("hashchange", () => showRoute(location.hash.slice(1)));
  systemTheme.addEventListener("change", applyAppearance);
  reducedMotion.addEventListener("change", applyAppearance);
  document.querySelector("[data-window-minimize]")?.addEventListener("click", () =>
    globalThis.matraca?.request("window.minimize"));
  document.querySelector("[data-window-maximize]")?.addEventListener("click", () =>
    globalThis.matraca?.request("window.toggleMaximize"));
  document.querySelector("[data-window-close]")?.addEventListener("click", () =>
    globalThis.matraca?.request("window.close"));
  let titlebarPointer = null;
  document.querySelector(".titlebar")?.addEventListener("pointerdown", event => {
    if (event.button !== 0 || event.target.closest("button, details, input, select, label")) return;
    titlebarPointer = { id: event.pointerId, x: event.clientX, y: event.clientY };
  });
  document.addEventListener("pointermove", event => {
    if (!titlebarPointer || event.pointerId !== titlebarPointer.id) return;
    if (!(event.buttons & 1)) { titlebarPointer = null; return; }
    // Do not hand clicks to the native move loop: it consumes the double-click sequence.
    if (Math.max(Math.abs(event.clientX - titlebarPointer.x), Math.abs(event.clientY - titlebarPointer.y)) < 4) return;
    titlebarPointer = null;
    globalThis.matraca?.request("window.drag");
  });
  document.addEventListener("pointerup", () => { titlebarPointer = null; });
  document.addEventListener("pointercancel", () => { titlebarPointer = null; });
  addEventListener("blur", () => { titlebarPointer = null; });
  document.querySelector(".titlebar")?.addEventListener("dblclick", event => {
    titlebarPointer = null;
    if (event.button !== 0 || event.target.closest("button, details, input, select, label")) return;
    globalThis.matraca?.request("window.toggleMaximize");
  });
  document.querySelector("[data-history-search]")?.addEventListener("input", renderHistory);
  document.querySelector("[data-history-provider]")?.addEventListener("change", () => { renderHistory(); renderUsageSummary(); });
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
    button.addEventListener("click", () => showSettingsTab(button.dataset.settingsTab));
  });
  document.querySelector("[data-ai-usage-refresh]")?.addEventListener("click", refreshAiUsage);
  document.querySelector("[data-deepseek-balance-refresh]")?.addEventListener("click", event =>
    refreshDeepSeekBalance(event.currentTarget));
  document.querySelector("[data-model-choice]")?.addEventListener("change", event => {
    const model = state.models.find(item => item.id === event.currentTarget.value);
    const button = document.querySelector("[data-model-download]");
    if (button && model) button.textContent = model.downloaded ? "Usar" : "Baixar";
  });
  document.querySelector("[data-model-download]")?.addEventListener("click", async event => {
    if (!globalThis.matraca || event.currentTarget.disabled) return;
    const select = document.querySelector("[data-model-choice]");
    const progress = document.querySelector("[data-model-progress]");
    const button = event.currentTarget;
    downloading = true;
    button.disabled = true;
    button.setAttribute("aria-busy", "true");
    button.textContent = "Preparando…";
    select.disabled = true;
    document.querySelector("[data-model-cancel]").hidden = false;
    if (progress) {
      progress.hidden = false;
      progress.removeAttribute("value");
    }
    try {
      const result = await globalThis.matraca.request("model.download.start", { id: select.value });
      applyModels(result.models);
      applyConfig(result.config);
      text("[data-onboarding-model]", `${result.path.split(/[\\/]/).pop()} selecionado. Valide o reconhecimento com um ditado real.`);
    } catch (error) {
      text("[data-onboarding-model]", error?.message || "Não foi possível baixar o modelo.");
      announce(error?.message || "Download interrompido.");
    } finally {
      downloading = false;
      button.disabled = false;
      button.setAttribute("aria-busy", "false");
      select.disabled = false;
      document.querySelector("[data-model-cancel]").hidden = true;
      if (progress) progress.hidden = true;
      const selected = state.models.find(model => model.id === select.value);
      button.textContent = selected?.downloaded ? "Usar" : "Baixar";
    }
  });
  document.querySelector("[data-model-cancel]").addEventListener("click", async event => {
    const button = event.currentTarget;
    button.disabled = true;
    try { await globalThis.matraca.request("model.download.cancel"); }
    catch (error) { announce(error?.message || "Não foi possível cancelar o download."); }
    finally { button.disabled = false; }
  });
  document.querySelectorAll("[data-config-field]").forEach(control => {
    control.addEventListener("input", () => {
      drafts.set(control.dataset.configField, { value: control.value, revision: ++draftSequence });
      if (control.type === "range") updateConfigEffects();
    });
    control.addEventListener("change", async () => {
      const field = control.dataset.configField;
      drafts.set(field, { value: control.value, revision: ++draftSequence });
      const { value, error } = UI.validate(field, control.value, state.config || {});
      fieldError(control, error);
      if (error) return;
      if (field === "postProcessProvider" && value !== state.config?.postProcessProvider) {
        if (!confirm("Trocar o provedor desliga a revisão, redefine modelo e raciocínio e remove a credencial anterior. Continuar?")) {
          drafts.delete(field);
          control.value = state.config?.postProcessProvider || "anthropic";
          return;
        }
        const apiKeyField = state.config?.postProcessProvider === "deepseek"
          ? "postProcessDeepSeekApiKey"
          : state.config?.postProcessProvider === "openai-compatible"
            ? "postProcessOpenAiApiKey"
            : "postProcessApiKey";
        control.disabled = true;
        for (const name of ["postProcessModel", "postProcessReasoning"]) {
          drafts.delete(name);
          const input = document.querySelector(`[data-config-field="${name}"]`);
          fieldError(input, "");
        }
        const secret = document.querySelector("[data-config-secret]");
        secret.value = "";
        await saveConfig({
          postProcessProvider: value,
          postProcess: false,
          postProcessModel: value === "anthropic" ? "claude-opus-5" : "",
          postProcessReasoning: "",
          [apiKeyField]: null
        });
        control.disabled = false;
      } else {
        if (field === "inputDevice") {
          await stopMicrophone();
          if (microphoneRequested) return;
          state.monitoredDevice = "";
        }
        await saveConfig({ [field]: value });
      }
    });
  });
  document.querySelectorAll("[data-config-toggle]").forEach(control => {
    control.addEventListener("click", () => {
      const field = control.dataset.configToggle;
      const enabled = state.config?.[field] !== true;
      if (field === "postProcess" && enabled && [...fieldErrors.keys(), ...drafts.keys()]
        .some(name => name.startsWith("postProcess"))) {
        announce("Corrija ou salve os campos de revisão antes de ativar.");
        return;
      }
      control.disabled = true;
      saveConfig({ [field]: enabled }).finally(() => { control.disabled = false; applyConfig(acceptedConfig); });
    });
  });
  document.querySelectorAll("[data-config-secret]").forEach(control => {
    control.addEventListener("change", async () => {
      if (!control.value) return;
      const submitted = control.value;
      const saved = await saveConfig({ [control.dataset.configSecret]: submitted });
      if (saved && control.value === submitted) control.value = "";
    });
  });
  document.querySelectorAll("[data-hotkey-capture]").forEach(button => {
    button.addEventListener("click", async event => {
      if (!globalThis.matraca) return;
      if (capturingHotkey) {
        try { await globalThis.matraca.request("hotkey.capture.cancel"); }
        catch (error) { announce(error?.message || "Não foi possível cancelar a captura."); }
        return;
      }
      const field = event.currentTarget.dataset.hotkeyCapture;
      const help = event.currentTarget.dataset.hotkeyHelpTarget;
      const button = event.currentTarget;
      const originalLabel = button.textContent;
      capturingHotkey = true;
      button.textContent = "Cancelar captura";
      button.setAttribute("aria-busy", "true");
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
        if (result?.hotkey && await saveConfig({ [field]: result.hotkey })) text(help, "Atalho aplicado a quente.");
        else text(help, "Nenhum novo atalho aplicado.");
      } catch (error) {
        if (error?.code !== "canceled")
          text(help, error?.message || "Não foi possível capturar o atalho.");
      } finally {
        capturingHotkey = false;
        button.textContent = originalLabel;
        button.setAttribute("aria-busy", "false");
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
    thresholdEditing = true;
    text("[data-threshold-value]", Number(event.currentTarget.value).toFixed(3));
    const mark = document.querySelector("[data-threshold-mark]");
    if (mark) mark.style.left = `${levelPosition(event.currentTarget.value)}%`;
  });
  document.querySelector("[data-threshold-input]")?.addEventListener("change", async event => {
    const threshold = Number(event.currentTarget.value);
    if (!Number.isFinite(threshold) || threshold < .001 || threshold > .5) { announce("O ajuste precisa estar entre 0.001 e 0.5."); return; }
    if (state.monitoredDevice) {
      const sensitivity = { ...(state.config?.micSensitivity || {}) };
      for (const key of Object.keys(sensitivity)) if (key.toLocaleLowerCase() === state.monitoredDevice.toLocaleLowerCase()) delete sensitivity[key];
      const saved = await saveConfig({
        micSensitivity: {
          ...sensitivity,
          [state.monitoredDevice]: threshold
        }
      });
      if (saved && Number(document.querySelector("[data-threshold-input]").value) === threshold) thresholdEditing = false;
    } else announce("Inicie o teste para identificar a entrada antes de ajustar.");
  });
  document.querySelector("[data-microphone-start]").addEventListener("click", startMicrophone);
  document.querySelector("[data-microphone-stop]").addEventListener("click", stopMicrophone);
  document.querySelector("[data-microphone-reset]").addEventListener("click", async () => {
    const device = state.monitoredDevice;
    if (!device || !confirm(`Redefinir somente o ajuste de ${device}?`)) return;
    await stopMicrophone();
    if (microphoneRequested) return;
    const sensitivity = { ...(state.config?.micSensitivity || {}) };
    for (const key of Object.keys(sensitivity)) if (key.toLocaleLowerCase() === device.toLocaleLowerCase()) delete sensitivity[key];
    if (await saveConfig({ micSensitivity: sensitivity })) {
      state.monitoredDevice = "";
      updateMicrophoneControls();
      text("[data-threshold-value]", "—");
      text("[data-vad-explanation]", "Ajuste removido apenas desta entrada. Teste novamente para carregar o valor inicial do host.");
    }
  });
  document.querySelector("[data-history-delete]")?.addEventListener("click", async () => {
    if (historyBusy) return;
    if (!state.selectedHistoryId || !confirm("Apagar este ditado do histórico local?")) return;
    const deletingId = state.selectedHistoryId;
    historyBusy = true;
    renderHistory();
    try {
      await globalThis.matraca?.request("history.delete", { id: deletingId });
      const result = await globalThis.matraca?.request("history.list");
      setHistory(result?.entries);
    } catch (error) {
      text("[data-history-time]", error?.message || "Não foi possível apagar.");
      announce(error?.message || "Não foi possível apagar.");
    } finally {
      historyBusy = false; renderHistory();
    }
  });
  document.querySelector("[data-history-clear]")?.addEventListener("click", async () => {
    if (historyBusy) return;
    if (!state.history.length || !confirm("Limpar todo o histórico local? Esta ação não pode ser desfeita.")) return;
    historyBusy = true;
    renderHistory();
    try {
      await globalThis.matraca?.request("history.clear");
      setHistory([]);
    } catch (error) {
      text("[data-history-time]", error?.message || "Não foi possível limpar o histórico.");
      announce(error?.message || "Não foi possível limpar o histórico.");
    } finally {
      historyBusy = false; renderHistory();
    }
  });
  document.querySelector("[data-history-copy]")?.addEventListener("click", async () => {
    if (!state.selectedHistoryId || historyBusy) return;
    historyBusy = true;
    try {
      await globalThis.matraca?.request("history.copy", { id: state.selectedHistoryId });
      text("[data-history-time]", "COPIADO PARA O CLIPBOARD");
      announce("Mensagem copiada.", "success");
    } catch (error) {
      announce(`${error?.message || "Não foi possível copiar."} Selecione o texto completo no detalhe e copie manualmente.`);
      location.hash = "history";
      document.querySelector("[data-history-search]").value = "";
      document.querySelector("[data-history-provider]").value = "all";
      renderHistory();
      const content = document.querySelector("[data-history-text]");
      content.focus();
      content.select();
    } finally {
      historyBusy = false;
      document.querySelectorAll("[data-history-detail] button").forEach(button => { button.disabled = !state.selectedHistoryId; });
    }
  });
  document.querySelector("[data-history-repaste]")?.addEventListener("click", async () => {
    if (!state.selectedHistoryId || historyBusy) return;
    historyBusy = true;
    try {
      await globalThis.matraca?.request("history.repaste", { id: state.selectedHistoryId });
    } catch (error) {
      text("[data-history-time]", error?.message || "Não foi possível recolar.");
      announce(error?.message || "Não foi possível recolar.");
    } finally {
      historyBusy = false;
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
    button.addEventListener("click", async () => {
      try { await globalThis.matraca.request("permissions.open-settings", { name: button.dataset.openPermission }); }
      catch (error) { announce(error?.message || "Não foi possível abrir os ajustes do sistema."); }
    });
  });
  document.querySelector("[data-permissions-refresh]").addEventListener("click", async () => {
    try {
      const snapshot = await globalThis.matraca.request("app.get");
      text("[data-accessibility-permission]", snapshot.permissions?.accessibility === "granted" ? "Permitido" : "Conferir no sistema");
      text("[data-microphone-permission]", snapshot.permissions?.microphone === "granted" ? "Permitido" : "Conferir no sistema");
    } catch (error) { announce(error?.message || "Não foi possível verificar as permissões."); }
  });
  document.querySelector(".onboarding-footer button")?.addEventListener("click", () => {
    location.hash = "home";
  });
  globalThis.matraca?.subscribe(message => {
    if (message.type === "mic.frame") applyMicrophone(message.payload);
    if (message.type === "mic.error" || message.type === "mic.monitor.error") {
      microphoneFailure = message.payload?.message || "Microfone indisponível. Confira a conexão e teste novamente.";
      stopMicrophone();
      state.monitoredDevice = "";
      text("[data-vad-explanation]", message.payload?.message || "Microfone indisponível. Confira a conexão e teste novamente.");
      announce(message.payload?.message || "Falha no microfone.");
    }
    if (message.type === "devices.changed") applyDevices(message.payload?.devices || []);
    if (message.type === "window.opened") {
      stopMicrophone();
      showRoute(location.hash.slice(1));
    }
    if (message.type === "hud.state") applyRuntime(message.payload);
    if (message.type === "dictation.completed") {
      setHistory(message.payload.history?.entries, false);
      setLastPhrase(message.payload);
      refreshAiUsage();
      if (message.payload?.text) text("[data-onboarding-test]", message.payload.delivered === true
        ? "Entrega real confirmada pelo aplicativo nesta sessão. Confira o texto no destino."
        : message.payload.delivered === false
          ? "A entrega falhou. Confira o destino antes de reinserir para não duplicar um trecho; o texto está no histórico quando habilitado."
          : "Ditado processado nesta sessão. Confira a inserção no destino; este evento não informa o resultado da entrega.");
    }
    if (message.type === "model.download.progress") {
      if (!downloading) return;
      const progress = document.querySelector("[data-model-progress]");
      const measured = Number.isFinite(message.payload.total) && message.payload.total > 0 && Number.isFinite(message.payload.done) && message.payload.done >= 0;
      if (progress && measured) {
        progress.max = message.payload.total;
        progress.value = message.payload.done;
      } else progress?.removeAttribute("value");
      const percent = measured
        ? Math.round(message.payload.done / message.payload.total * 100)
        : null;
      text("[data-onboarding-model]", percent == null ? "Baixando modelo local · tamanho total não informado" : `Baixando modelo local · ${percent}%`);
    }
    if (message.type === "config.changed" && message.payload?.config) {
      applyConfig(message.payload.config);
      applyPostProcessRuntime(message.payload.postProcessActive === true);
    }
  });

  showSettingsTab("key");
  updateMicrophoneControls();
  applyAppearance();
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
