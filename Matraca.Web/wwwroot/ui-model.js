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
    themeMode: ["system", "light", "dark"], palette: ["olive", "ochre", "terracotta", "plum", "teal"],
    postProcessProvider: ["anthropic", "deepseek", "openai-compatible"],
    postProcessReasoning: ["", "off", "low", "high", "max"]
  });
  function validate(field, raw, config = {}) {
    let value = raw, error = "";
    if (ranges[field]) {
      const [min, max, integer] = ranges[field];
      value = Number(raw);
      if (String(raw).trim() === "" || !Number.isFinite(value) || value < min || value > max)
        error = `Informe um valor entre ${min} e ${max}.`;
      else if (integer && !Number.isInteger(value)) error = "Informe um número inteiro.";
    }
    if (choices[field] && !choices[field].includes(raw)) error = "Selecione uma opção válida.";
    if (field.startsWith("focusBorderColor") && !/^#[0-9a-f]{6}$/i.test(raw)) error = "Use #RRGGBB.";
    if (field === "vocabulary") value = [...new Map(String(raw).split(/[\n,]/)
      .map(x => x.trim()).filter(Boolean).map(x => [x.toLocaleLowerCase("pt-BR"), x])).values()];
    if (field === "postProcessEndpoint" && raw) {
      try {
        const url = new URL(raw);
        if (url.username || url.password || !(url.protocol === "https:" ||
          (url.protocol === "http:" && ["localhost", "127.0.0.1", "[::1]"].includes(url.hostname))))
          error = "Use HTTPS remoto ou HTTP local, sem credenciais na URL.";
      } catch { error = "Informe uma URL completa válida."; }
    }
    if (field === "postProcessModel" && config.postProcessProvider !== "anthropic" && !String(raw).trim())
      error = "Informe o modelo aceito pelo provedor.";
    return { value, error };
  }
  const tokens = value => value == null ? "Não informado" : new Intl.NumberFormat("pt-BR").format(value);
  const cost = value => value == null ? "Não calculado" : "US$ " + new Intl.NumberFormat("pt-BR", {
    minimumFractionDigits: value < .01 ? 6 : 4, maximumFractionDigits: value < .01 ? 6 : 4
  }).format(value);
  function filterHistory(entries, query, provider) {
    return entries.filter(entry => (provider === "all" || entry.reviewUsage?.provider === provider) &&
      String(entry.text || "").toLocaleLowerCase("pt-BR").includes(query.trim().toLocaleLowerCase("pt-BR")));
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
