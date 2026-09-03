(() => {
  "use strict";

  const state = document.querySelector("[data-smoke-state]");

  function post(payload) {
    globalThis.webkit?.messageHandlers?.matraca?.postMessage(JSON.stringify(payload));
  }

  function receive(message) {
    if (message.type !== "smoke.native") return;
    state.textContent = "Ponte nativa recebida.";
    const probe = new Image();
    probe.addEventListener("error", () => {
      post({
        version: 1,
        type: "smoke.pong",
        payload: {
          cssLoaded: getComputedStyle(document.body).display === "grid",
          securityProbeBlocked: true
        }
      });
      location.href = "https://example.invalid/blocked-by-matraca";
    }, { once: true });
    probe.src = "matraca://app/%2e%2e%2fblocked.png";

    globalThis.matraca.request("smoke.not_implemented").catch(error => {
      post({
        version: 1,
        type: "smoke.requestRejected",
        payload: { code: error?.code ?? "unknown" }
      });
    });
  }

  if (globalThis.matraca) globalThis.matraca.subscribe(receive);
  else addEventListener("DOMContentLoaded", () => globalThis.matraca?.subscribe(receive), { once: true });
})();
