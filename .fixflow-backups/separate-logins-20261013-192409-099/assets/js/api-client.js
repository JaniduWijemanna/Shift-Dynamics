(() => {
  const base = (window.SHIFT_API_BASE_URL || localStorage.getItem("shift.apiBaseUrl") || "http://localhost:5174").replace(/\/$/, "");
  const key = "shift.auth";
  const stored = () => { try { return JSON.parse(sessionStorage.getItem(key) || "null"); } catch { return null; } };
  const clear = () => sessionStorage.removeItem(key);
  const save = auth => sessionStorage.setItem(key, JSON.stringify(auth));
  async function request(path, options = {}) {
    const auth = stored();
    const headers = new Headers(options.headers || {});
    if (options.body && !headers.has("Content-Type")) headers.set("Content-Type", "application/json");
    if (auth?.accessToken) headers.set("Authorization", `Bearer ${auth.accessToken}`);
    let response;
    try { response = await fetch(`${base}${path}`, { ...options, headers }); }
    catch { throw new Error("Unable to reach Shift Dynamics. Please check your connection and try again."); }
    let payload = null; try { payload = await response.json(); } catch { /* no response body */ }
    if (response.status === 401) { clear(); if (!location.pathname.endsWith("login.html") && !location.pathname.endsWith("staff-login.html")) location.href = (/\/(advisor|manager|mechanic|storekeeper|vendor)\//.test(location.pathname) ? "../staff-login.html" : `${location.pathname.includes("/customer/") ? "../" : ""}login.html`); }
    if (!response.ok) throw new Error(payload?.message || payload?.title || "The request could not be completed.");
    return payload?.data ?? payload;
  }
  window.ShiftApi = { request, save, clear, auth: stored, base };
})();