(() => {
  "use strict";
  function mount() {
    const year = document.getElementById("currentYear");
    if (year) year.textContent = String(new Date().getFullYear());
    for (const button of document.querySelectorAll("[data-toggle-password], #passwordToggle, #staffPasswordToggle")) {
      button.addEventListener("click", () => {
        const id = button.dataset.togglePassword || (button.id === "staffPasswordToggle" ? "staffPassword" : "loginPassword");
        const input = document.getElementById(id);
        if (!input) return;
        const show = input.type === "password";
        input.type = show ? "text" : "password";
        button.setAttribute("aria-label", show ? "Hide password" : "Show password");
        button.setAttribute("aria-pressed", String(show));
        const icon = button.querySelector("i");
        if (icon) icon.className = show ? "bi bi-eye-slash" : "bi bi-eye";
      });
    }
  }
  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", mount, { once: true });
  else mount();
})();