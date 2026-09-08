/* DS 1.0.1: shared field validation and presentation, without native simulations. */
((scope) => {
  "use strict";
  const ranges = Object.freeze({
    idleUnloadMinutes: [0, 240, true], silenceMs: [200, 5000, true],
    phraseMaxSeconds: [2, 20, true], beepVolume: [0, 1],
    focusBorderThickness: [1, 40, true], focusBorderOpacity: [.1, 1],
    historyMaxItems: [1, 5000, true], postProcessTimeoutMs: [1000, 60000, true]
  });
  const choices = Object.freeze({
    mode: ["toggle", "hold", "live", "push"], gpu: ["auto", "gpu", "cpu"],
    pasteMethod: ["unicode", "clipboard"], pinDelivery: ["focus", "nofocus"],
    themeMode: ["system", "light", "dark"], palette: ["olive", "ochre", "terracotta", "plum", "teal"], uiLanguage: ["system", "pt-BR", "en-US"],
    postProcessProvider: ["anthropic", "deepseek", "openai-compatible"],
    postProcessReasoning: ["", "off", "low", "high", "max"]
  });
  function validate(field, raw, config = {}) {
    let value = raw, error = "";
    if (ranges[field]) {
      const [min, max, integer] = ranges[field];
      value = Number(raw);
      if (String(raw).trim() === "" || !Number.isFinite(value) || value < min || value > max)
         error = message("validation.range", { min, max });
       else if (integer && !Number.isInteger(value)) error = message("validation.integer");
    }
    if (choices[field] && !choices[field].includes(raw)) error = message("validation.choice");
    if (field.startsWith("focusBorderColor") && !/^#[0-9a-f]{6}$/i.test(raw)) error = message("validation.color");
    if (field === "vocabulary") value = [...new Map(String(raw).split(/[\n,]/)
      .map(x => x.trim()).filter(Boolean).map(x => [x.toLowerCase(), x])).values()];
    if (field === "postProcessEndpoint" && raw) {
      try {
        const url = new URL(raw);
        if (url.username || url.password || !(url.protocol === "https:" ||
          (url.protocol === "http:" && ["localhost", "127.0.0.1", "[::1]"].includes(url.hostname))))
           error = message("validation.endpointProtocol");
       } catch { error = message("validation.endpoint"); }
    }
    if (field === "postProcessModel" && config.postProcessProvider !== "anthropic" && !String(raw).trim())
       error = message("validation.model");
    return { value, error };
  }
  function message(key, values) {
    values ||= {};
    const translated = globalThis.MatracaI18n?.t(key, values);
    if (translated && translated !== key) return translated;
    return ({
      "validation.range": `Informe um valor entre ${values.min} e ${values.max}.`,
      "validation.integer": "Informe um número inteiro.", "validation.choice": "Selecione uma opção válida.",
      "validation.color": "Use #RRGGBB.", "validation.endpointProtocol": "Use HTTPS remoto ou HTTP local, sem credenciais na URL.",
      "validation.endpoint": "Informe uma URL completa válida.", "validation.model": "Informe o modelo aceito pelo provedor.",
      "misc.noInformation": "Não informado", "history.noCost": "Não calculado"
    }[key] || key);
  }
  const tokens = value => value == null ? message("misc.noInformation") : new Intl.NumberFormat(globalThis.MatracaI18n?.locale() || "pt-BR").format(value);
  const cost = value => value == null ? message("history.noCost") : (globalThis.MatracaI18n?.currency(value) || "US$ " + new Intl.NumberFormat("pt-BR", {
    minimumFractionDigits: value < .01 ? 6 : 4, maximumFractionDigits: value < .01 ? 6 : 4
  }).format(value));
  function filterHistory(entries, query, provider) {
    return entries.filter(entry => (provider === "all" || entry.reviewUsage?.provider === provider) &&
      String(entry.text || "").toLowerCase().includes(query.trim().toLowerCase()));
  }
  function usageSummary(providers, provider = "all") {
    const selected = (providers || []).filter(x => provider === "all" || x.provider === provider);
    const sum = key => selected.length && selected.every(x => x[key] != null)
      ? selected.reduce((n, x) => n + x[key], 0) : null;
    const priced = selected.filter(x => x.estimatedCostUsd != null);
    return {
      requests: sum("requests"), totalTokens: sum("totalTokens"),
      pricedRequests: sum("pricedRequests"), unpricedRequests: sum("unpricedRequests"),
      estimatedCostUsd: priced.length ? priced.reduce((n, x) => n + x.estimatedCostUsd, 0) : null,
      complete: selected.length > 0 && selected.every(x => x.coverageKnown === true &&
        x.unpricedRequests === 0 && x.pricedRequests != null && x.pricedRequests === x.requests),
      pricingVersions: [...new Set(selected.flatMap(x => x.pricingVersions || []))]
    };
  }
  function theme(config, systemDark) {
    return config?.themeMode === "dark" || (config?.themeMode !== "light" && systemDark) ? "dark" : "light";
  }
  const api = Object.freeze({ ranges, choices, validate, tokens, cost, filterHistory, usageSummary, theme });
  scope.MatracaUI = api;
  if (typeof module !== "undefined") module.exports = api;
})(globalThis);
