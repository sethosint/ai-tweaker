/* Shell: state, helpers, navigation, modals, confirmations, global search. */
(function () {
  const S = (window.S = {
    lang: "ru", rebootPending: false, theme: "day", themeMode: "auto", accentMode: "system", system: { accent: "#0078D4", dark: false }, sensors: null, profileName: "Performance", demo: false,
    init: null, scan: null, catalog: [], byId: {}, status: {}, plan: null, selected: new Set(),
    log: [], telemetry: [], guides: null, bios: null, appx: null, drivers: null, backups: null, ai: null,
    busy: false, view: "dashboard",
  });

  // ------------------------------------------------------------------ helpers

  const t = (window.t = (k) => (I18N[S.lang] && I18N[S.lang][k]) || I18N.ru[k] || k);
  const L = (window.L = (o) => (o == null ? "" : typeof o === "string" ? o : (S.lang === "en" ? o.en || o.ru : o.ru || o.en) || ""));
  const esc = (window.esc = (s) => String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c])));
  window.$ = (sel, root = document) => root.querySelector(sel);
  window.$$ = (sel, root = document) => [...root.querySelectorAll(sel)];
  window.fmtTime = (d) => { const x = d ? new Date(d) : new Date(); return x.toLocaleTimeString(S.lang === "en" ? "en-GB" : "ru-RU", { hour12: false }); };
  window.fmtDate = (d) => (d ? new Date(d).toLocaleDateString(S.lang === "en" ? "en-GB" : "ru-RU") : "—");
  window.html = (strings, ...vals) => strings.reduce((a, s, i) => a + s + (i < vals.length ? vals[i] ?? "" : ""), "");

  window.call = async (method, params, opts = {}) => {
    try {
      return await API.call(method, params);
    } catch (e) {
      if (!opts.silent) toast(e.message || String(e), "error");
      throw e;
    }
  };

  window.toast = (text, kind = "info", ms = 4200) => {
    let box = $(".toasts");
    if (!box) { box = document.createElement("div"); box.className = "toasts"; document.body.append(box); }
    const el = document.createElement("div");
    el.className = "toast " + kind;
    el.textContent = text;
    box.append(el);
    setTimeout(() => el.remove(), ms);
  };

  // ------------------------------------------------------------------ modal

  window.modal = ({ title, body, buttons = [], wide = false, hazard = false, onOpen }) =>
    new Promise((resolve) => {
      const ov = document.createElement("div");
      ov.className = "overlay";
      ov.innerHTML = html`<div class="modal ${wide ? "wide" : ""}" role="dialog">
        <div class="modal-h ${hazard ? "hazard" : ""}"><span>${title}</span><button class="x" aria-label="close">×</button></div>
        <div class="modal-b">${body}</div>
        ${buttons.length ? `<div class="modal-f">${buttons.map((b, i) => `<button class="btn ${b.cls || ""}" data-i="${i}">${b.label}</button>`).join("")}</div>` : ""}
      </div>`;
      const close = (v) => { ov.remove(); document.removeEventListener("keydown", key); resolve(v); };
      const key = (e) => { if (e.key === "Escape") close(null); };
      ov.addEventListener("click", (e) => {
        if (e.target === ov || e.target.closest(".x")) return close(null);
        const b = e.target.closest("[data-i]");
        if (b) { const btn = buttons[+b.dataset.i]; close(btn.value !== undefined ? btn.value : btn.label); }
      });
      document.addEventListener("keydown", key);
      document.body.append(ov);
      if (onOpen) onOpen(ov, close);
    });

  window.confirmBox = (title, text, yes, cls = "primary", hazard = false) =>
    modal({ title, body: `<div>${text}</div>`, hazard, buttons: [{ label: t("cancel"), value: false, cls: "ghost" }, { label: yes || t("yes"), value: true, cls }] });

  /** Walks the user through every tweak that disables something important. Returns the ids they accepted. */
  window.confirmImportant = async (ids) => {
    const need = ids.map((id) => S.byId[id]).filter((tw) => tw && (tw.confirm || tw.risk === "high"));
    const declined = new Set();
    for (let i = 0; i < need.length; i++) {
      const tw = need[i];
      const res = await modal({
        title: `${t("confirm.title")} · ${i + 1} ${t("confirm.of")} ${need.length}`,
        hazard: true,
        body: html`<div class="tt" style="font:700 15px/1.3 var(--font-head);letter-spacing:.06em">${esc(L(tw.name))}</div>
          <div style="color:var(--ink-2)">${esc(L(tw.description))}</div>
          ${tw.confirmText ? `<div class="callout crit"><b>${t("confirm.lose")}</b><br>${esc(L(tw.confirmText))}</div>` : ""}
          <div class="tm" style="display:flex;gap:6px;flex-wrap:wrap">${badges(tw)}</div>`,
        buttons: [{ label: t("confirm.no"), value: false, cls: "ghost" }, { label: t("confirm.yes"), value: true, cls: tw.risk === "high" ? "danger" : "primary" }],
      });
      if (res !== true) declined.add(tw.id);
    }
    return ids.filter((id) => !declined.has(id));
  };

  window.badges = (tw) => [
    `<span class="risk ${tw.risk}">${t("risk." + tw.risk)}</span>`,
    `<span class="lvl">${tw.level === 0 ? t("lvl.0") : "L" + tw.level}</span>`,
    tw.reboot ? `<span class="lvl">⟳ ${t("reboot")}</span>` : "",
    tw.logoff ? `<span class="lvl">⇄ ${t("logoff")}</span>` : "",
    tw.restartExplorer ? `<span class="lvl">⌂ ${t("explorer")}</span>` : "",
    tw.reversible === false ? `<span class="lvl" style="color:var(--crit)">✕ ${t("irreversible")}</span>` : "",
  ].join("");

  // ------------------------------------------------------------------ busy veil

  let veil = null;
  window.busy = (title) => {
    S.busy = true;
    $("#statusLed").className = "led busy";
    $("#statusText").textContent = t("status.busy");
    veil = document.createElement("div");
    veil.className = "busy-veil";
    veil.innerHTML = html`<div class="busy-card"><div class="bt">${title}</div><div class="progress"><i style="width:4%"></i></div><div class="bs">…</div></div>`;
    document.body.append(veil);
    return {
      progress(v, text) {
        if (!veil) return;
        $("i", veil).style.width = Math.max(2, Math.min(100, v * 100)) + "%";
        if (text) $(".bs", veil).textContent = text;
      },
      done() {
        S.busy = false;
        veil && veil.remove();
        veil = null;
        $("#statusLed").className = "led";
        $("#statusText").textContent = t("status.ready");
      },
    };
  };

  // ------------------------------------------------------------------ navigation

  const NAV = [
    ["dashboard", "01"], ["tune", "02"], ["tweaks", "03"], ["debloat", "04"], ["tools", "05"],
    ["drivers", "06"], ["apps", "07"], ["bios", "08"], ["image", "09"], ["ai", "10"], ["backup", "11"],
  ];
  window.NAV = NAV;

  window.go = (view, opts = {}) => {
    S.view = view;
    $$(".nav-item").forEach((n) => n.classList.toggle("on", n.dataset.view === view));
    $$(".view").forEach((v) => v.classList.toggle("on", v.id === "v-" + view));
    const r = Views[view];
    if (r) r(opts);
  };

  function renderShell() {
    document.documentElement.lang = S.lang;
    applyPalette();
    $("#app").innerHTML = html`
      <header class="topbar">
        <div class="brand"><div class="brand-mark"></div><div><div class="brand-name">AI TWEAKER</div><div class="brand-sub">${t("brand.sub")}</div></div></div>
        <div class="top-mid">
          <button class="search-btn" id="searchBtn">⌕ <span>${t("search")}</span><span class="kbd">Ctrl K</span></button>
          <span class="chip accent" id="profileChip">${t("prof." + S.profileName)}</span>
          ${S.init && S.init.isAdmin ? `<span class="chip ok">● ${t("admin")}</span>` : `<span class="chip crit">● ${t("notAdmin")}</span>`}
          <button class="chip crit reboot-chip" id="rebootChip" title="${t("rs.chipTitle")}" ${S.rebootPending ? "" : "hidden"}>⟳ ${t("rs.chip")}</button>
        </div>
        <div class="top-right">
          <div class="seg" id="shiftSeg" title="${t("shift.title")}"><button data-v="auto" class="${S.themeMode === "auto" ? "on" : ""}">${t("shift.auto")}</button><button data-v="day" class="${S.themeMode === "day" ? "on" : ""}">☀ ${t("shift.day")}</button><button data-v="night" class="${S.themeMode === "night" ? "on" : ""}">☾ ${t("shift.night")}</button></div>
          <div class="seg" id="langSeg"><button data-v="ru" class="${S.lang === "ru" ? "on" : ""}">RU</button><button data-v="en" class="${S.lang === "en" ? "on" : ""}">EN</button></div>
          <button class="icon-btn" id="settingsBtn" title="${t("settings")}">⚙</button>
          <div class="clock" id="clock">${fmtTime()}</div>
        </div>
      </header>
      <nav class="nav">
        ${NAV.map(([v, n], i) => `${i === 1 || i === 5 || i === 9 ? '<div class="nav-sep"></div>' : ""}<div class="nav-item ${S.view === v ? "on" : ""}" data-view="${v}"><span class="nc">${n}</span><span>${t("nav." + v)}</span><span class="nav-badge" id="badge-${v}" hidden></span></div>`).join("")}
        <div class="nav-foot">${t("nav.foot")}<br><br>v${esc((S.init && S.init.version) || "1.0.0")} · ${esc((S.init && S.init.machine) || "")}</div>
      </nav>
      <main class="main">${NAV.map(([v]) => `<section class="view ${S.view === v ? "on" : ""}" id="v-${v}"></section>`).join("")}</main>
      <footer class="statusbar"><span class="led" id="statusLed"></span><span id="statusText">${t("status.ready")}</span><span class="ticker" id="ticker"></span><span id="sbTele"></span></footer>`;
    if (S.demo) { const b = document.createElement("div"); b.className = "demo-badge"; b.textContent = t("demo"); $("#app").append(b); }

    $(".nav").addEventListener("click", (e) => { const n = e.target.closest(".nav-item"); if (n) go(n.dataset.view); });
    $("#searchBtn").onclick = openSearch;
    $("#settingsBtn").onclick = openSettings;
    $("#rebootChip").onclick = () => App.restartPrompt("reboot");
    $("#shiftSeg").onclick = (e) => { const b = e.target.closest("button"); if (b) setTheme(b.dataset.v); };
    $("#langSeg").onclick = (e) => { const b = e.target.closest("button"); if (b) setLang(b.dataset.v); };
    updateBadges();
  }
  window.renderShell = renderShell;

  // ------------------------------------------------------------------ palette (Windows accent + light/dark)

  const rgb = (h) => { h = h.replace("#", ""); return [0, 2, 4].map((i) => parseInt(h.slice(i, i + 2), 16)); };
  const hex = (c) => "#" + c.map((x) => Math.max(0, Math.min(255, x)).toString(16).padStart(2, "0")).join("");
  const lum = (c) => { const f = (v) => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); }; return 0.2126 * f(c[0]) + 0.7152 * f(c[1]) + 0.0722 * f(c[2]); };
  const contrast = (a, b) => { const x = lum(a), y = lum(b); return (Math.max(x, y) + 0.05) / (Math.min(x, y) + 0.05); };
  /** Shifts the accent toward black/white until it reads well on the panel color (WCAG 3:1 for UI elements). */
  const fit = (accent, bg) => { let c = rgb(accent); const b = rgb(bg), to = lum(b) > 0.4 ? [0, 0, 0] : [255, 255, 255]; for (let i = 0; i < 24 && contrast(c, b) < 3.2; i++) c = c.map((v, k) => Math.round(v + (to[k] - v) * 0.08)); return c; };

  window.applyPalette = () => {
    S.theme = S.themeMode === "auto" ? (S.system.dark ? "night" : "day") : S.themeMode;
    const root = document.documentElement;
    root.dataset.theme = S.theme;
    const panel = S.theme === "night" ? "#14160f" : "#f6f3ea";
    const base = S.accentMode === "industrial" ? (S.theme === "night" ? "#f2a93b" : "#e8590c") : S.system.accent || "#0078D4";
    const a = fit(base, panel);
    root.style.setProperty("--accent", hex(a));
    root.style.setProperty("--accent-ink", lum(a) > 0.42 ? "#14160f" : "#ffffff");
    root.style.setProperty("--radar", hex(a));
    call("window.theme", { theme: S.theme }, { silent: true }).catch(() => {});
  };

  window.setTheme = (mode) => {
    S.themeMode = mode;
    $$("#shiftSeg button").forEach((b) => b.classList.toggle("on", b.dataset.v === mode));
    call("settings.set", { theme: mode }, { silent: true }).catch(() => {});
    applyPalette();
    if (S.view === "dashboard") Views.dashboard();
  };

  window.setLang = (lang) => {
    S.lang = lang;
    call("settings.set", { language: lang }, { silent: true }).catch(() => {});
    renderShell();
    go(S.view);
  };

  window.updateBadges = () => {
    const crit = S.scan ? S.scan.insights.filter((i) => i.severity === "critical").length : 0;
    const b = $("#badge-dashboard");
    if (b) { b.hidden = !crit; b.textContent = crit; }
  };

  // ------------------------------------------------------------------ settings

  async function openSettings() {
    const s = S.init.settings;
    await modal({
      title: t("settings"),
      body: html`<label class="field">${t("settings.accent")}
          <select class="input" id="setAccent"><option value="system" ${S.accentMode === "system" ? "selected" : ""}>${t("settings.accent.system")}</option><option value="industrial" ${S.accentMode === "industrial" ? "selected" : ""}>${t("settings.accent.industrial")}</option></select></label>
        <div class="td" style="display:flex;gap:8px;align-items:center"><span class="swatch" style="background:${esc(S.system.accent)}"></span>${t("settings.accent.current")} ${esc(S.system.accent)} · ${S.system.dark ? t("shift.night") : t("shift.day")}</div>
        <label class="field">${t("settings.ping")}<input class="input" id="setPing" value="${esc(s.pingHost)}"></label>
        <label class="field">${t("settings.autoRestart")}
          <select class="input" id="setAr">${[0, 30, 60, 120, 300].map((n) => `<option value="${n}" ${(s.autoRestartSeconds ?? 60) === n ? "selected" : ""}>${n ? n + " " + t("rs.sec") : t("settings.autoRestart.off")}</option>`).join("")}</select></label>
        <label class="check"><input type="checkbox" id="setRp" ${s.createRestorePoint ? "checked" : ""}><span class="box"></span>${t("settings.rp")}</label>`,
      buttons: [{ label: t("cancel"), value: false, cls: "ghost" }, { label: t("ai.save"), value: true, cls: "primary" }],
      onOpen: (ov) => { ov.querySelector(".modal-f [data-i='1']").addEventListener("click", async () => {
        const pingHost = $("#setPing", ov).value.trim();
        const createRestorePoint = $("#setRp", ov).checked;
        const autoRestartSeconds = +$("#setAr", ov).value;
        S.accentMode = $("#setAccent", ov).value;
        applyPalette();
        await call("settings.set", { pingHost, createRestorePoint, autoRestartSeconds, accent: S.accentMode });
        Object.assign(s, { pingHost, createRestorePoint, autoRestartSeconds });
        toast(t("toast.saved"), "ok");
      }, { capture: true }); },
    });
  }

  // ------------------------------------------------------------------ global search (Ctrl+K)

  function searchIndex() {
    const idx = [];
    NAV.forEach(([v]) => idx.push({ type: "page", title: t("nav." + v), desc: t(v === "dashboard" ? "dash.desc" : v + ".desc"), go: () => go(v) }));
    S.catalog.forEach((tw) => idx.push({ type: "tweak", title: L(tw.name), desc: tw.id + " · " + L(tw.description), keys: tw.id + " " + tw.name.ru + " " + tw.name.en + " " + tw.description.ru + " " + tw.description.en, go: () => go("tweaks", { focus: tw.id }) }));
    ((window.MOCK_DATA && MOCK_DATA.guides) || S.guides || []).forEach((g) => g.sections.forEach((sec) => sec.settings.forEach((st) =>
      idx.push({ type: "setting", title: `${g.name}: ${st.name}`, desc: `${L(sec.title)} → ${st.value}`, keys: `${g.name} ${st.name} ${sec.title.ru} ${sec.title.en} ${sec.path}`, go: () => go("apps", { guide: g.id }) }))));
    ((S.bios && S.bios.advice) || (window.MOCK_DATA && MOCK_DATA.bios) || []).forEach((b) => idx.push({ type: "bios", title: L(b.title), desc: b.value, keys: b.title.ru + " " + b.title.en + " " + b.value, go: () => go("bios") }));
    ((window.MOCK_DATA && MOCK_DATA.apps) || []).forEach((a) => idx.push({ type: "app", title: L(a.name), desc: a.package, keys: a.package + " " + a.name.ru + " " + a.name.en, go: () => go("debloat") }));
    return idx;
  }

  function openSearch() {
    if ($(".palette")) return;
    const idx = searchIndex();
    const ov = document.createElement("div");
    ov.className = "overlay";
    ov.innerHTML = `<div class="modal palette"><input class="input" id="q" placeholder="${esc(t("search"))}" autocomplete="off"><div class="results"></div></div>`;
    document.body.append(ov);
    const q = $("#q", ov), list = $(".results", ov);
    let cur = 0, found = [];
    const close = () => ov.remove();
    const render = () => {
      const terms = q.value.toLowerCase().split(/\s+/).filter(Boolean);
      found = (terms.length ? idx.filter((r) => { const hay = (r.title + " " + r.desc + " " + (r.keys || "")).toLowerCase(); return terms.every((x) => hay.includes(x)); }) : idx.filter((r) => r.type === "page")).slice(0, 60);
      cur = Math.min(cur, Math.max(0, found.length - 1));
      list.innerHTML = found.length ? found.map((r, i) => `<div class="result ${i === cur ? "on" : ""}" data-i="${i}"><div class="rt">${t("res." + r.type)}</div><div><div class="rn">${esc(r.title)}</div><div class="rd">${esc(r.desc)}</div></div></div>`).join("") : `<div class="empty">${t("res.none")}</div>`;
    };
    q.addEventListener("input", () => { cur = 0; render(); });
    q.addEventListener("keydown", (e) => {
      if (e.key === "ArrowDown") { cur = Math.min(found.length - 1, cur + 1); render(); e.preventDefault(); }
      if (e.key === "ArrowUp") { cur = Math.max(0, cur - 1); render(); e.preventDefault(); }
      if (e.key === "Enter" && found[cur]) { close(); found[cur].go(); }
      if (e.key === "Escape") close();
    });
    list.addEventListener("click", (e) => { const r = e.target.closest(".result"); if (r) { close(); found[+r.dataset.i].go(); } });
    ov.addEventListener("click", (e) => { if (e.target === ov) close(); });
    render();
    q.focus();
  }
  window.openSearch = openSearch;

  document.addEventListener("keydown", (e) => {
    if ((e.ctrlKey && e.key.toLowerCase() === "k") || (e.key === "/" && !/INPUT|TEXTAREA|SELECT/.test(document.activeElement.tagName))) { e.preventDefault(); openSearch(); }
  });

  // ------------------------------------------------------------------ live data

  window.pushLog = (e) => {
    S.log.unshift(e);
    if (S.log.length > 400) S.log.length = 400;
    const tk = $("#ticker");
    if (tk) tk.textContent = `${fmtTime(e.time)} [${e.level.toUpperCase()}] ${e.source}: ${e.text}`;
    if (S.view === "dashboard" && window.Dash) Dash.appendLog(e);
  };

  window.pushTelemetry = (s) => {
    if (S.sensors) { s.gpu = S.sensors.gpuLoad; s.gpuTemp = S.sensors.gpuTempC; s.cpuTemp = S.sensors.cpuTempC; }
    S.telemetry.push(s);
    if (S.telemetry.length > 120) S.telemetry.shift();
    const sb = $("#sbTele");
    if (sb) sb.textContent = `CPU ${Math.round(s.cpu)}%${s.cpuTemp != null ? " " + Math.round(s.cpuTemp) + "°" : ""} · GPU ${s.gpu != null ? Math.round(s.gpu) + "%" : "—"}${s.gpuTemp != null ? " " + Math.round(s.gpuTemp) + "°" : ""} · RAM ${s.ram}% · PING ${s.pingMs ?? "—"} ms`;
    if (S.view === "dashboard" && window.Dash) Dash.tick(s);
  };

  window.pushSensors = (s) => {
    S.sensors = s;
    if (S.view === "dashboard" && window.Dash) Dash.sensors(s);
  };

  setInterval(() => { const c = $("#clock"); if (c) c.textContent = fmtTime(); }, 1000);
})();
