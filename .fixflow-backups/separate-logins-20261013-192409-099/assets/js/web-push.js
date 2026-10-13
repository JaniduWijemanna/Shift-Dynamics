import "../../firebase-config.js";

const api = window.ShiftApi;
const current = api?.auth();
const baseUrl = new URL("../../", import.meta.url);
const key = "shyft.webPush";
let sdk, registration, listenerSet = false, button, panel, content, busy = false, epoch = 0;
const read = () => { try { return JSON.parse(localStorage.getItem(key) || "null"); } catch { return null; } };
const sameAccount = () => api?.auth()?.userId === current?.userId && api?.auth()?.role === current?.role;
const localEnabled = () => read()?.enabled === true && read()?.userId === current?.userId;
function node(tag, cls, text) {
  const result = document.createElement(tag);
  if (cls) result.className = cls;
  if (text !== undefined) result.textContent = text;
  return result;
}
function action(text, callback) {
  const result = node("button", "wf-btn", text);
  result.type = "button";
  result.onclick = async () => {
    if (busy) return;
    busy = true; result.disabled = true;
    try { await callback(); }
    catch (error) { status(error.message, true); }
    finally { busy = false; result.disabled = false; updateButton(); }
  };
  return result;
}
function status(text, error = false) {
  if (!content) return;
  let message = content.querySelector(".shyft-push-message");
  if (!message) {
    message = node("p", "wf-notice shyft-push-message");
    message.setAttribute("role", "status");
    content.prepend(message);
  }
  message.textContent = text;
  message.classList.toggle("error", error);
}
function updateButton() { if (button) button.textContent = localEnabled() ? "Push settings" : "Enable push"; }
function supported() {
  if (!window.isSecureContext) throw new Error("Use http://localhost or http://127.0.0.1 for local testing. A LAN address needs HTTPS for web push.");
  if (!("Notification" in window) || !("serviceWorker" in navigator) || !("PushManager" in window))
    throw new Error("This browser does not support web push. Try a current Chrome or Edge browser.");
}
async function load() {
  supported();
  if (!sdk) {
    const [app, messaging] = await Promise.all([
      import("https://www.gstatic.com/firebasejs/13.0.0/firebase-app.js"),
      import("https://www.gstatic.com/firebasejs/13.0.0/firebase-messaging.js")
    ]);
    if (!await messaging.isSupported()) throw new Error("Web push is unavailable in this browser or browsing mode.");
    const firebaseApp = app.initializeApp(globalThis.SHYFT_FIREBASE_CONFIG, "shyft-web");
    sdk = { ...messaging, messaging: messaging.getMessaging(firebaseApp) };
  }
  if (!registration) {
    registration = await navigator.serviceWorker.register(new URL("firebase-messaging-sw.js", baseUrl), { scope: baseUrl.pathname });
    if (!registration.active) {
      const worker = registration.installing || registration.waiting;
      if (!worker) throw new Error("The push service worker could not start. Refresh and try again.");
      await new Promise((resolve, reject) => {
        const timeout = setTimeout(() => reject(new Error("Push service worker timed out. Check your internet connection.")), 30000);
        const check = () => {
          if (worker.state === "activated") { clearTimeout(timeout); resolve(); }
          else if (worker.state === "redundant") { clearTimeout(timeout); reject(new Error("Push service worker failed to install.")); }
        };
        worker.addEventListener("statechange", check);
        check();
      });
    }
  }
  if (!listenerSet) {
    listenerSet = true;
    sdk.onMessage(sdk.messaging, async payload => {
      if (!sameAccount() || !localEnabled() || payload.data?.userId !== current.userId) return;
      document.dispatchEvent(new Event("shyft:notifications-refresh"));
      const data = payload.data || {};
      const url = new URL(data.url || "", location.origin);
      if (url.origin !== location.origin || !url.href.startsWith(baseUrl.href)) return;
      const saved = read();
      try {
        await registration.showNotification(data.title || "ShyftDynamics", {
          body: data.body || "You have a new update.",
          icon: new URL("assets/images/shift-dynamics-logo.png.png", baseUrl).href,
          tag: "shyft-" + String(data.notificationId || "update"),
          data: { shyft: true, url: url.href, userId: current.userId, generation: saved.generation }
        });
      } catch { status("A new notification arrived. Open the notification bell to read it."); }
    });
  }
}
async function bind(enabled, userId = current.userId, generation = read()?.generation || "") {
  const worker = registration?.active;
  if (!worker) return;
  await new Promise((resolve, reject) => {
    const channel = new MessageChannel();
    const timeout = setTimeout(() => reject(new Error("Could not update the browser's push setting.")), 8000);
    channel.port1.onmessage = event => {
      clearTimeout(timeout);
      channel.port1.close();
      if (event.data?.ok) resolve(); else reject(new Error("Push settings could not be saved."));
    };
    worker.postMessage({ type: "SHYFT_PUSH_BINDING", enabled, userId, generation }, [channel.port2]);
  });
}
async function enable(requestPermission = true) {
  const ticket = epoch;
  supported();
  if (!sameAccount()) throw new Error("Sign in again before enabling push.");
  const old = read();
  if (old?.enabled && old.userId !== current.userId)
    throw new Error("Another account has push enabled in this browser. Disable it there or use another browser profile.");
  // Permission is requested directly from the user's click, never automatically on page load.
  const permission = requestPermission && Notification.permission !== "granted"
    ? await Notification.requestPermission() : Notification.permission;
  if (permission !== "granted") throw new Error("Notifications are blocked. Allow notifications in this site's browser settings, then try again.");
  await load();
  if (!sameAccount() || ticket !== epoch) throw new Error("Your account changed. Sign in again.");
  const token = await sdk.getToken(sdk.messaging, { vapidKey: globalThis.SHYFT_FIREBASE_VAPID, serviceWorkerRegistration: registration });
  if (!token) throw new Error("Firebase did not return a browser token. Try again.");
  if (!sameAccount() || ticket !== epoch) throw new Error("Your account changed. Sign in again.");
  const response = await api.request("/api/web-push/subscriptions", {
    method: "POST", body: JSON.stringify({ token, origin: location.origin, basePath: baseUrl.pathname })
  });
  if (!sameAccount() || ticket !== epoch) {
    await api.request("/api/web-push/subscriptions/disable", { method: "POST", body: JSON.stringify({ token }) }).catch(() => {});
    if (!read()?.enabled) await sdk.deleteToken(sdk.messaging).catch(() => {});
    throw new Error("Your account changed. Sign in again.");
  }
  const generation = old?.generation || crypto.randomUUID();
  try { await bind(true, current.userId, generation); }
  catch (error) {
    await api.request("/api/web-push/subscriptions/disable", { method: "POST", body: JSON.stringify({ token }) }).catch(() => {});
    throw error;
  }
  if (!sameAccount() || ticket !== epoch) {
    await bind(false, current.userId, generation);
    await api.request("/api/web-push/subscriptions/disable", { method: "POST", body: JSON.stringify({ token }) }).catch(() => {});
    throw new Error("Your account changed. Sign in again.");
  }
  localStorage.setItem(key, JSON.stringify({ enabled: true, userId: current.userId, token, generation }));
  if (old?.token && old.token !== token)
    await api.request("/api/web-push/subscriptions/disable", { method: "POST", body: JSON.stringify({ token: old.token }) }).catch(() => {});
  status(response.configured ? "Push notifications are enabled for this browser." : "This browser is registered. Configure the Firebase service-account file on the backend to start sending push notifications.");
  updateButton();
}
async function disable() {
  epoch++;
  const saved = read();
  if (!saved || saved.userId !== current.userId) return;
  // Clear account binding first, so queued messages cannot appear after logout.
  localStorage.removeItem(key);
  try {
    registration ||= await navigator.serviceWorker.getRegistration(baseUrl.href);
    await bind(false, current.userId, saved.generation);
  } catch { /* deleting the FCM token below also revokes delivery */ }
  const results = await Promise.allSettled([
    api.request("/api/web-push/subscriptions/disable", { method: "POST", body: JSON.stringify({ token: saved.token }) }),
    (async () => { await load(); await sdk.deleteToken(sdk.messaging); })()
  ]);
  updateButton();
  if (results.some(r => r.status === "rejected"))
    status("Browser alerts have been disabled locally. A server cleanup failed; retry Disable push if you still receive alerts.", true);
  else status("Push notifications are disabled for this browser.");
}
async function test() {
  if (!localEnabled()) throw new Error("Enable push for this browser first.");
  const result = await api.request("/api/web-push/test", { method: "POST" });
  status(result.message || "Test queued. Switch to another tab and watch for the notification.");
}
function open() {
  if (!panel) {
    panel = node("dialog", "wf-modal");
    panel.setAttribute("aria-labelledby", "shyft-push-title");
    const header = node("div", "wf-modal-head");
    const title = node("h2", "", "Browser push notifications"); title.id = "shyft-push-title";
    const close = node("button", "wf-btn", "Close");close.type = "button";close.onclick = () => panel.close();
    header.append(title, close);
    content = node("div", "wf-modal-body");
    content.append(node("p", "wf-subtle", "Receive ShyftDynamics alerts while your website tab is in the background. Browser and system notification settings apply."));
    const controls = node("div", "sd-notify-tools");
    controls.append(action("Enable push", () => enable()), action("Disable push", disable), action("Send test notification", test));
    content.append(controls);
    panel.append(header, content); document.body.append(panel);
  }
  if (!panel.open) panel.showModal();
  status(localEnabled() ? "Push is enabled for your account in this browser." : "Click Enable push, then choose Allow in the browser prompt.");
  api.request("/api/web-push/status").then(result => {
    if (!result.configured) status("The backend Firebase service-account file is not configured yet. Finish the installer credential step.", true);
  }).catch(error => status(error.message, true));
}
function mount() {
  const top = document.querySelector(".wf-user");
  if (!top) return false;
  button = node("button", "wf-btn"); button.type = "button";button.onclick = open;
  updateButton(); top.prepend(button);
  // Await push cleanup before the existing portal logout handler clears the bearer token.
  document.querySelector(".wf-account")?.addEventListener("click", async event => {
    const logout = event.target.closest("button");
    if (!logout || !/log out/i.test(logout.textContent)) return;
    event.preventDefault();event.stopImmediatePropagation();logout.disabled = true;
    try { await disable(); } finally {
      api.clear();
      location.href = current.role === "Customer" ? "../login.html" : "../staff-login.html";
    }
  }, true);
  if (localEnabled() && typeof Notification !== "undefined" && Notification.permission === "granted") {
    busy = true;
    enable(false).catch(() => { button.title = "Push registration needs attention. Open Push settings."; })
      .finally(() => { busy = false; });
  }
  if (new URLSearchParams(location.search).has("pushNotification"))
    document.dispatchEvent(new Event("shyft:open-notifications"));
  const originalClear = api.clear;
  api.clear = () => {
    epoch++;
    const saved = read();
    if (saved?.userId === current.userId) {
      localStorage.removeItem(key);
      navigator.serviceWorker?.getRegistration(baseUrl.href).then(reg => {
        registration = reg;
        return bind(false, current.userId, saved.generation);
      }).catch(() => {});
    }
    originalClear();
  };
  window.addEventListener("storage", event => { if (event.key === key) updateButton(); });
  return true;
}
function boot() {
  if (!current) return;
  if (mount()) return;
  const observer = new MutationObserver(() => { if (mount()) observer.disconnect(); });
  observer.observe(document.body, { childList: true, subtree: true });
  setTimeout(() => observer.disconnect(), 15000);
}
if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", boot, { once: true });
else boot();