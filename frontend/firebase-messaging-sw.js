"use strict";
const BINDING_DB = "shyft-web-push";
function database() {
  return new Promise((resolve, reject) => {
    const open = indexedDB.open(BINDING_DB, 1);
    open.onupgradeneeded = () => open.result.createObjectStore("settings");
    open.onsuccess = () => resolve(open.result);
    open.onerror = () => reject(open.error);
  });
}
async function binding(value) {
  const db = await database();
  try {
    return await new Promise((resolve, reject) => {
      const transaction = db.transaction("settings", value === undefined ? "readonly" : "readwrite");
      const store = transaction.objectStore("settings");
      const request = value === undefined ? store.get("active") : store.put(value, "active");
      transaction.oncomplete = () => resolve(value === undefined ? request.result : value);
      transaction.onerror = () => reject(transaction.error);
      transaction.onabort = () => reject(transaction.error || new Error("Push setting could not be saved."));
    });
  } finally { db.close(); }
}
self.addEventListener("install", () => self.skipWaiting());
self.addEventListener("activate", event => event.waitUntil(self.clients.claim()));
self.addEventListener("message", event => {
  if (event.data?.type !== "SHYFT_PUSH_BINDING") return;
  const active = {
    userId: String(event.data.userId || ""),
    enabled: event.data.enabled === true,
    generation: String(event.data.generation || "")
  };
  event.waitUntil(binding(active).then(() => {
    event.ports[0]?.postMessage({ ok: true });
  }).catch(() => event.ports[0]?.postMessage({ ok: false })));
});
// Handle clicks before importing FCM, as Firebase's documentation requires.
self.addEventListener("notificationclick", event => {
  if (!event.notification.data?.shyft) return;
  event.stopImmediatePropagation();
  event.notification.close();
  event.waitUntil((async () => {
    const active = await binding();
    if (!active?.enabled || active.userId !== event.notification.data.userId ||
        active.generation !== event.notification.data.generation) return;
    const url = new URL(event.notification.data.url, self.location.origin);
    if (url.origin !== self.location.origin || !url.href.startsWith(self.registration.scope)) return;
    const windows = await self.clients.matchAll({ type: "window", includeUncontrolled: true });
    const existing = windows.find(client => client.url.startsWith(self.registration.scope));
    if (existing) {
      await existing.navigate(url.href);
      await existing.focus();
    } else await self.clients.openWindow(url.href);
  })());
});
importScripts("./firebase-config.js");
importScripts("https://www.gstatic.com/firebasejs/13.0.0/firebase-app-compat.js");
importScripts("https://www.gstatic.com/firebasejs/13.0.0/firebase-messaging-compat.js");
firebase.initializeApp(globalThis.SHYFT_FIREBASE_CONFIG);
const messaging = firebase.messaging();
messaging.onBackgroundMessage(async payload => {
  const data = payload.data || {};
  const active = await binding();
  if (!active?.enabled || active.userId !== data.userId) return;
  const url = new URL(data.url || "", self.location.origin);
  if (url.origin !== self.location.origin || !url.href.startsWith(self.registration.scope)) return;
  await self.registration.showNotification(data.title || "ShyftDynamics", {
    body: data.body || "You have a new update. Open your portal for details.",
    icon: new URL("assets/images/shift-dynamics-logo.png.png", self.registration.scope).href,
    tag: "shyft-" + String(data.notificationId || "update"),
    data: { shyft: true, url: url.href, userId: active.userId, generation: active.generation }
  });
});