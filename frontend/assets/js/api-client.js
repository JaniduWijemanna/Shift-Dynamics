(() => {
  "use strict";
  const base = (window.SHIFT_API_BASE_URL || localStorage.getItem("shift.apiBaseUrl") || "http://localhost:5174").replace(/\/$/, "");
  const key = "shift.auth";
  const stored = () => {
    try { return JSON.parse(sessionStorage.getItem(key) || "null"); }
    catch { return null; }
  };
  const clear = () => sessionStorage.removeItem(key);
  const save = auth => sessionStorage.setItem(key, JSON.stringify(auth));
  const loginPage = () => {
    const path = location.pathname;
    if (/\/vendor\//.test(path)) return "../vendor-login.html";
    if (/\/(advisor|manager|mechanic|storekeeper)\//.test(path)) return "../staff-login.html";
    return /\/customer\//.test(path) ? "../login.html" : "login.html";
  };
  async function request(path, options = {}) {
    const auth = stored();
    const headers = new Headers(options.headers || {});
    if (options.body && !headers.has("Content-Type")) headers.set("Content-Type", "application/json");
    if (auth?.accessToken) headers.set("Authorization", `Bearer ${auth.accessToken}`);
    let response;
    try { response = await fetch(`${base}${path}`, { ...options, headers }); }
    catch { throw new Error("Unable to reach ShyftDynamics. Please check your connection and try again."); }
    let payload = null;
    try { payload = await response.json(); } catch { /* Response may have no body. */ }
    if (response.status === 401) {
      clear();
      if (/\/(customer|advisor|manager|mechanic|storekeeper|vendor)\//.test(location.pathname)) location.href = loginPage();
    }
    if (!response.ok) throw new Error(payload?.message || payload?.title || "The request could not be completed.");
    return payload?.data ?? payload;
  }
  window.ShiftApi = { request, save, clear, auth: stored, base, loginPage };
})();