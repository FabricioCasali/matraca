(() => {
  "use strict";

  const root = document.documentElement;
  const pages = [...document.querySelectorAll("[data-page]")];
  const routes = [...document.querySelectorAll("[data-route]")];
  const validRoutes = new Set(pages.map(page => page.dataset.page));

  function showRoute(route) {
    const selected = validRoutes.has(route) ? route : "home";
    pages.forEach(page => page.classList.toggle("active", page.dataset.page === selected));
    routes.forEach(button => button.classList.toggle("active", button.dataset.route === selected));
    document.title = selected === "home" ? "Matraca" : `Matraca · ${selected}`;
  }

  routes.forEach(button => button.addEventListener("click", () => {
    location.hash = button.dataset.route;
  }));
  addEventListener("hashchange", () => showRoute(location.hash.slice(1)));
  showRoute(location.hash.slice(1));

  document.querySelector("[data-theme-toggle]")?.addEventListener("click", () => {
    const isDark = root.dataset.theme
      ? root.dataset.theme === "dark"
      : matchMedia("(prefers-color-scheme: dark)").matches;
    root.dataset.theme = isDark ? "light" : "dark";
  });

  const heights = [18,25,32,29,37,46,58,71,82,74,67,86,95,78,69,62,73,88,81,68,55,61,72,66,59,49,55,63,51,44,48,53,41,36,43,38,31,35,28,25,29,23,19,21,16,14,12,9];
  const spectrum = document.querySelector("[data-spectrum]");
  for (const height of heights) {
    const bar = document.createElement("i");
    bar.style.height = `${height}%`;
    spectrum?.append(bar);
  }

  globalThis.matraca?.notify("ui.ready", {
    pageCount: pages.length,
    spectrumBandCount: spectrum?.children.length ?? 0
  });
})();
