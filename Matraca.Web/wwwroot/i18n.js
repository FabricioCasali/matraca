(() => {
  "use strict";

  const catalogs = globalThis.MatracaCatalogs || {};
  const fallbackLocale = "en-US";
  const supported = new Set(["pt-BR", fallbackLocale]);
  let requestedLocale = "pt-BR";
  let effectiveLocale = "pt-BR";

  function localeFor(value) {
    if (supported.has(value)) return value;
    if (value === "system") {
      const system = globalThis.navigator?.language || fallbackLocale;
      return String(system).toLowerCase().startsWith("pt") ? "pt-BR" : fallbackLocale;
    }
    return fallbackLocale;
  }

  function get(key, locale = effectiveLocale) {
    const parts = String(key).split(".");
    let value = catalogs[locale];
    for (const part of parts) value = value?.[part];
    if (value == null && locale !== fallbackLocale) return get(key, fallbackLocale);
    return value == null ? key : value;
  }

  function interpolate(value, values) {
    if (!values) return String(value);
    return String(value).replace(/\{(\w+)\}/g, (_, name) => values[name] == null ? `{${name}}` : String(values[name]));
  }

  function t(key, values) {
    return interpolate(get(key), values);
  }

  function plural(key, count, values = {}) {
    const options = get(key);
    const category = new Intl.PluralRules(effectiveLocale).select(count);
    const value = options?.[category] ?? options?.other ?? options;
    return interpolate(value, { ...values, count: formatNumber(count) });
  }

  function formatNumber(value) {
    if (value == null || !Number.isFinite(Number(value))) return t("misc.noInformation");
    return new Intl.NumberFormat(effectiveLocale).format(Number(value));
  }

  function formatDate(value, options) {
    return new Intl.DateTimeFormat(effectiveLocale, options || { dateStyle: "medium" }).format(new Date(value));
  }

  function formatTime(value, options) {
    return new Intl.DateTimeFormat(effectiveLocale, options || { hour: "2-digit", minute: "2-digit" }).format(new Date(value));
  }

  function formatDateTime(value) {
    return new Intl.DateTimeFormat(effectiveLocale, { dateStyle: "short", timeStyle: "short" }).format(new Date(value));
  }

  function formatCurrency(value, currency = "USD") {
    if (value == null || !Number.isFinite(Number(value))) return t("history.noCost");
    return new Intl.NumberFormat(effectiveLocale, {
      style: "currency", currency, currencyDisplay: "code", minimumFractionDigits: Number(value) < .01 ? 6 : 4,
      maximumFractionDigits: Number(value) < .01 ? 6 : 4
    }).format(Number(value));
  }

  function setLocale(locale, effective) {
    requestedLocale = locale || "system";
    effectiveLocale = localeFor(effective || requestedLocale);
    if (typeof document !== "undefined") {
      document.documentElement.lang = effectiveLocale;
      apply(document);
    }
    return effectiveLocale;
  }

  function apply(scope) {
    const target = scope || document;
    if (!target?.querySelectorAll) return;
    target.querySelectorAll("[data-i18n]").forEach(element => { element.textContent = t(element.dataset.i18n); });
    target.querySelectorAll("[data-i18n-html]").forEach(element => { element.innerHTML = t(element.dataset.i18nHtml); });
    target.querySelectorAll("[data-i18n-placeholder]").forEach(element => { element.placeholder = t(element.dataset.i18nPlaceholder); });
    target.querySelectorAll("[data-i18n-aria-label]").forEach(element => { element.setAttribute("aria-label", t(element.dataset.i18nAriaLabel)); });
    target.querySelectorAll("[data-i18n-title]").forEach(element => { element.setAttribute("title", t(element.dataset.i18nTitle)); });
    if (typeof document !== "undefined") document.title = t("app.title");
  }

  const api = Object.freeze({
    supported: Object.freeze([...supported]),
    catalogs,
    t,
    plural,
    apply,
    setLocale,
    locale: () => effectiveLocale,
    requested: () => requestedLocale,
    resolve: localeFor,
    number: formatNumber,
    date: formatDate,
    time: formatTime,
    dateTime: formatDateTime,
    currency: formatCurrency
  });
  globalThis.MatracaI18n = api;
  setLocale("pt-BR");
})();
