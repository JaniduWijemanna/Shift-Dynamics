(() => {
  "use strict";
  const api = window.ShiftApi;
  if (!api || !api.auth()) return;
  const role = api.auth().role;
  const userId = api.auth().userId;
  const create = (tag, cls, text) => {
    const node = document.createElement(tag);
    if (cls) node.className = cls;
    if (text !== undefined) node.textContent = text;
    return node;
  };
  const routes = {
    Customer: { Appointment: "booking", WorkOrder: "history", Estimate: "estimates", Invoice: "payments", ModificationRequest: "modifications", EmergencyRequest: "emergency" },
    ServiceAdvisor: { Appointment: "bookings", WorkOrder: "jobs", Estimate: "estimates", Invoice: "invoices", ModificationRequest: "modifications", EmergencyRequest: "emergency" },
    Manager: { WorkOrder: "workshop", VendorQuote: "quotes", PurchaseOrder: "orders", Invoice: "billing" },
    Admin: { WorkOrder: "workshop", VendorQuote: "quotes", PurchaseOrder: "orders", Invoice: "billing" },
    Mechanic: { WorkOrder: "assigned", PartRequisition: "parts" },
    Storekeeper: { PartRequisition: "requisitions", QuoteRequest: "requests", VendorQuote: "requests", PurchaseOrder: "receiving" },
    Vendor: { QuoteRequest: "requests", PurchaseOrder: "deliveries", VendorQuote: "quotes" }
  };
  let records = [], loaded = false, failure = "", polling = false, timer;
  let panel, content, bell, sideButton, badge, lastView = "", mutating = false, mutationVersion = 0;
  function date(value) {
    let text = String(value || "");
    if (/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?$/.test(text)) text += "Z";
    const parsed = new Date(text);
    return Number.isNaN(parsed.getTime()) ? "" : parsed.toLocaleString("en-GB", { timeZone: "Asia/Colombo", hour12: true });
  }
  function action(label, handler) {
    const node = create("button", "wf-btn", label);
    node.type = "button";
    node.addEventListener("click", async () => {
      node.disabled = true;
      try { await handler(); }
      catch (error) {
        failure = error.message;
        render(true);
      } finally { node.disabled = false; }
    });
    return node;
  }
  function updateBadge() {
    const unread = records.filter(n => !n.isRead).length;
    badge.textContent = failure ? "!" : unread > 99 ? "99+" : String(unread);
    badge.hidden = !failure && unread === 0;
    bell.setAttribute("aria-label", failure ? "Notifications: refresh failed" : `Notifications: ${unread} unread`);
    bell.title = failure || `${unread} unread notifications`;
    if (sideButton) sideButton.textContent = `Notifications${unread ? ` (${unread})` : ""}`;
  }
  async function refresh() {
    if (polling || mutating) return;
    const auth = api.auth();
    if (!auth || auth.userId !== userId || auth.role !== role) {
      clearTimeout(timer);
      if (panel?.open) panel.close();
      return;
    }
    polling = true;
    const version = mutationVersion;
    try {
      const response = await api.request("/api/notifications");
      if (!Array.isArray(response)) throw new Error("Unexpected notifications response.");
      if (version !== mutationVersion) return;
      records = response;
      loaded = true;
      failure = "";
    } catch (error) { if (version === mutationVersion) failure = error.message; }
    finally {
      polling = false;
      updateBadge();
      render();
    }
  }
  async function markRead(items) {
    if (mutating) return;
    mutating = true;
    mutationVersion++;
    try {
      for (const item of items) {
        if (item.isRead) continue;
        await api.request(`/api/notifications/${encodeURIComponent(item.id)}/read`, { method: "PATCH" });
        item.isRead = true;
        const current = records.find(n => n.id === item.id);
        if (current) current.isRead = true;
      }
      failure = "";
    } finally {
      mutating = false;
      updateBadge();
      render(true);
    }
  }
  function render(force = false) {
    if (!panel?.open) return;
    const view = JSON.stringify([records, loaded, failure]);
    if (!force && view === lastView) return;
    lastView = view;
    const scroll = content.scrollTop;
    content.replaceChildren();
    const toolbar = create("div", "sd-notify-tools");
    toolbar.append(action("Refresh", refresh));
    if (records.some(n => !n.isRead)) toolbar.append(action("Mark all as read", () => markRead(records.slice())));
    content.append(toolbar);
    const status = create("p", "wf-subtle", `${records.filter(n => !n.isRead).length} unread notifications`);
    status.setAttribute("role", "status");
    content.append(status);
    if (failure) {
      const error = create("p", "wf-notice error", failure);
      error.setAttribute("role", "alert");
      content.append(error);
    }
    if (!loaded) content.append(create("p", "wf-subtle", failure ? "Use Refresh to try again." : "Loading notifications…"));
    else if (!records.length) content.append(create("p", "wf-empty", "No notifications yet. Updates will appear here as your garage processes requests."));
    for (const item of records) {
      const card = create("article", `wf-card sd-notify-record${item.isRead ? "" : " sd-notify-unread"}`);
      card.append(create("h3", "", item.title || "Notification"));
      card.append(create("p", "", item.body || ""));
      card.append(create("small", "wf-subtle", `${date(item.createdAt)} · ${item.isRead ? "Read" : "Unread"}`));
      const buttons = create("div", "sd-notify-tools");
      if (!item.isRead) buttons.append(action("Mark as read", () => markRead([item])));
      let destination = routes[role]?.[item.entityType];
      if (role === "Customer" && item.entityType === "WorkOrder" && item.type !== "handover" && item.type !== "job-status") destination = "tracker";
      if (role === "Customer" && item.type === "job-status" && !/completed/i.test(item.body || "")) destination = "tracker";
      if (destination && document.querySelector(`.wf-nav button[data-module="${destination}"]`)) {
        buttons.append(action("View details", async () => {
          await markRead([item]);
          panel.close();
          document.querySelector(`.wf-nav button[data-module="${destination}"]`).click();
        }));
      }
      card.append(buttons);
      content.append(card);
    }
    content.scrollTop = scroll;
  }
  function open() {
    if (!panel) {
      panel = create("dialog", "wf-modal sd-notify-panel");
      panel.setAttribute("aria-labelledby", "sd-notify-title");
      const header = create("div", "wf-modal-head");
      const title = create("h2", "", "Notifications");
      title.id = "sd-notify-title";
      header.append(title, action("Close", () => panel.close()));
      content = create("div", "wf-modal-body");
      panel.append(header, content);
      document.body.append(panel);
      panel.addEventListener("close", () => bell.focus());
    }
    if (!panel.open) panel.showModal();
    render(true);
    refresh();
  }
  function mount() {
    const top = document.querySelector(".wf-user");
    if (!top) return false;
    const style = create("style");
    style.textContent = ".sd-notify-bell{display:inline-flex;align-items:center;gap:7px}.sd-notify-count{background:#b91c1c;color:white;border-radius:20px;padding:2px 6px;font-size:11px;min-width:18px;text-align:center}.sd-notify-count[hidden]{display:none}.sd-notify-tools{display:flex;gap:8px;flex-wrap:wrap;margin:10px 0}.sd-notify-record{margin:12px 0}.sd-notify-unread{border-left:4px solid #2563eb}.sd-notify-panel{width:min(650px,94vw)}.sd-notify-panel .wf-modal-body{max-height:70vh;overflow:auto}.sd-notify-record p{white-space:pre-wrap;overflow-wrap:anywhere}@media(max-width:500px){.sd-notify-bell{font-size:12px;padding:7px}}";
    document.head.append(style);
    bell = action("Notifications", open);
    bell.classList.add("sd-notify-bell");
    badge = create("span", "sd-notify-count", "0");
    badge.hidden = true;
    badge.setAttribute("aria-hidden", "true");
    bell.append(badge);
    // Inline SVG provides a real bell without depending on an external icon font.
    const icon = document.createElementNS("http://www.w3.org/2000/svg", "svg");
    icon.setAttribute("viewBox", "0 0 24 24");
    icon.setAttribute("width", "18"); icon.setAttribute("height", "18");
    icon.setAttribute("aria-hidden", "true");
    const path = document.createElementNS("http://www.w3.org/2000/svg", "path");
    path.setAttribute("d", "M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9M10 21h4");
    path.setAttribute("fill", "none"); path.setAttribute("stroke", "currentColor");
    path.setAttribute("stroke-width", "2"); icon.append(path); bell.prepend(icon);
    top.prepend(bell);
    document.addEventListener("shyft:notifications-refresh", refresh);
    document.addEventListener("shyft:open-notifications", open);
    sideButton = Array.from(document.querySelectorAll(".wf-nav button"))
      .find(button => button.textContent.trim() === "Notifications");
    if (sideButton) sideButton.onclick = open;
    async function poll() {
      if (!document.hidden) await refresh();
      if (api.auth()?.userId === userId && api.auth()?.role === role) timer = setTimeout(poll, 30000);
    }
    poll();
    document.addEventListener("visibilitychange", () => { if (!document.hidden) refresh(); });
    window.addEventListener("pagehide", () => clearTimeout(timer), { once: true });
    return true;
  }
  function boot() {
    if (mount()) return;
    const observer = new MutationObserver(() => { if (mount()) observer.disconnect(); });
    observer.observe(document.body, { childList: true, subtree: true });
    setTimeout(() => observer.disconnect(), 15000);
  }
  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", boot, { once: true });
  else boot();
})();