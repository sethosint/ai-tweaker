/* Shell: state, helpers, navigation, modals, confirmations, global search. */
(function () {
  const S = (window.S = {
    lang: "ru", rebootPending: false, theme: "day", themeMode: "auto", accentMode: "industrial", system: { accent: "#0078D4", dark: false }, sensors: null, profileName: "Extreme", gameMode: false, demo: false,
    init: null, scan: null, catalog: [], byId: {}, status: {}, plan: null, selected: new Set(),
    log: [], telemetry: [], guides: null, bios: null, appx: null, drivers: null, backups: null,
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
        <div class="modal-h ${hazard ? "hazard" : ""}"><span>${title}</span><button class="x" aria-label="close">${icon("x")}</button></div>
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

  /** Asks about every tweak that switches off something important ("switch off" vs "keep"). Returns the ids to apply. */
  window.confirmImportant = async (ids) => {
    const feats = await App.loadFeatures();
    const qs = ids.map((id) => App.questionFor(S.byId[id], feats)).filter(Boolean);
    if (!qs.length) return ids;
    const res = await askQuestions(qs, [], { summary: false, title: t("confirm.title") });
    if (!res) return [];
    return ids.filter((id) => !res.kept.has(id));
  };

  window.badges = (tw) => [
    `<span class="risk ${tw.risk}">${t("risk." + tw.risk)}</span>`,
    `<span class="lvl">${tw.level === 0 ? t("lvl.0") : t("lvl") + " " + tw.level}</span>`,
    tw.reboot ? `<span class="lvl">${t("reboot")}</span>` : "",
    tw.logoff ? `<span class="lvl">${t("logoff")}</span>` : "",
    tw.restartExplorer ? `<span class="lvl">${t("explorer")}</span>` : "",
    tw.reversible === false ? `<span class="risk high">${t("irreversible")}</span>` : "",
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

  /**
   * The apply sequence: a live part spins up in the middle, the ring fills and every applied tweak streams in.
   * Returns { step(i, n, id), finish(results) }.
   */
  window.applyVeil = (title, total) => {
    S.busy = true;
    $("#statusLed").className = "led busy";
    $("#statusText").textContent = t("status.busy");
    const C = 2 * Math.PI * 160;
    const kind = S.scan && S.scan.profile && S.scan.profile.gpus.some((g) => /nvidia|amd|radeon|geforce|arc/i.test(g.name)) ? "gpu" : "cpu";
    const el = document.createElement("div");
    el.className = "apply-veil";
    el.innerHTML = html`<div class="apply-stage">
      <div class="apply-core"><svg viewBox="0 0 340 340"><circle class="orbit" cx="170" cy="170" r="168"/><circle class="trk" cx="170" cy="170" r="160"/><circle class="arc" cx="170" cy="170" r="160" stroke-dasharray="${C}" stroke-dashoffset="${C}"/></svg>
        <div data-hw="${kind}" data-load="${kind}" data-dist="6.2" data-spin="1.4"></div></div>
      <div class="apply-title">${esc(title)}</div>
      <div class="apply-count"><b>0</b>/ ${total}</div>
      <div class="apply-feed"></div></div>`;
    document.body.append(el);
    let hw = null;
    if (window.HW3D) { HW3D.solo(true); HW3D.scan(el, { [kind]: () => 0.9 }); hw = $("[data-hw]", el)._hw; if (hw) hw.boost(true); }
    const arc = $(".arc", el), feed = $(".apply-feed", el), count = $(".apply-count b", el);
    const set = (i, n) => { arc.style.strokeDashoffset = C * (1 - i / Math.max(1, n)); count.textContent = i; };
    const close = () => { S.busy = false; if (window.HW3D) HW3D.solo(false); el.remove(); $("#statusLed").className = "led"; $("#statusText").textContent = t("status.ready"); };
    return {
      step(i, n, id) {
        set(i, n);
        const tw = S.byId[id];
        const row = document.createElement("div");
        row.dataset.id = id;
        row.textContent = tw ? L(tw.name) : id;
        feed.append(row);
        while (feed.children.length > 7) feed.firstChild.remove();
      },
      async finish(results) {
        if (!results) return close();
        const bad = new Set(results.filter((r) => !r.ok).map((r) => r.id));
        feed.querySelectorAll("[data-id]").forEach((r) => r.classList.toggle("err", bad.has(r.dataset.id)));
        set(1, 1);
        el.classList.add("done");
        $(".apply-title", el).textContent = t("done.title");
        if (hw) hw.boost(false);
        await new Promise((r) => setTimeout(r, 1100));
        close();
      },
    };
  };

  // ------------------------------------------------------------------ navigation

  const NAV = [
    ["dashboard", "01"], ["tune", "02"], ["tweaks", "03"], ["debloat", "04"], ["install", "05"], ["tools", "06"], ["bench", "07"], ["services", "08"],
    ["drivers", "09"], ["apps", "10"], ["manual", "11"], ["image", "12"], ["backup", "13"],
  ];
  window.NAV = NAV;
  const CODES = { dashboard: "OVERVIEW", tune: "OPTIMIZE", tweaks: "CATALOG", debloat: "DEBLOAT", install: "SOFTWARE", tools: "TOOLS", bench: "BENCH", services: "SERVICES", drivers: "DRIVERS", apps: "CONFIGS", manual: "MANUAL", image: "IMAGE", backup: "ROLLBACK" };
  const navList = () => NAV;

  // The live 3D part shown next to each page title; fans and lights follow the real load.
  const last = () => S.telemetry[S.telemetry.length - 1] || {};
  const pct = (v) => (v == null ? 0.15 : v / 100);
  window.HWLOAD = {
    cpu: () => pct(last().cpu), gpu: () => pct(S.sensors && S.sensors.gpuLoad != null ? S.sensors.gpuLoad : last().gpu), ram: () => pct(last().ram),
    board: () => pct(last().cpu), router: () => Math.min(1, (last().pingMs || 15) / 80), ssd: () => 0.25, monitor: () => 0.3, usb: () => 0.3,
  };

  /** Section code above every page title ("SEC 03 · CATALOG") and the model beside it, kept when a view re-renders itself. */
  function kicker(section) {
    const ph = section.querySelector(".page-h");
    const head = ph && ph.querySelector(":scope > div:not(.ph-icon):not(.actions)");
    if (!head || ph.querySelector(".kicker")) return;
    const view = section.id.slice(2);
    const i = navList().findIndex(([v]) => v === view);
    head.insertAdjacentHTML("afterbegin", `<span class="kicker">SEC ${String(i + 1).padStart(2, "0")} · ${CODES[view] || ""}</span>`);
    // 3D lives on the console only; other pages get their own flat emblem
    if (!ph.querySelector(".ph-icon, :scope > .app-ico")) ph.insertAdjacentHTML("afterbegin", `<div class="ph-icon">${icon(view)}</div>`);
  }
  const kickerWatch = new MutationObserver((list) => {
    const seen = new Set();
    list.forEach((m) => {
      if (m.target.closest("[data-live]")) return; // live numbers on the dashboard: nothing new to decorate
      const sec = m.target.closest(".view");
      if (sec && !seen.has(sec)) { seen.add(sec); kicker(sec); if (window.HW3D) HW3D.scan(sec, HWLOAD); }
    });
  });

  /** Slides the highlight in the menu to the current page. */
  function moveGlider() {
    const g = $(".nav-glider"), on = $(".nav-item.on");
    if (!g) return;
    g.style.opacity = on ? 1 : 0;
    if (on) g.style.transform = `translateY(${on.offsetTop}px)`;
  }

  window.go = (view, opts = {}) => {
    // BIOS now lives in the "By hand" tab, as its first section
    if (view === "bios") { view = "manual"; opts = { ...opts, section: "bios" }; }
    const changed = S.view !== view || !$(".view.on");
    S.view = view;
    $$(".nav-item").forEach((n) => n.classList.toggle("on", n.dataset.view === view));
    $$(".view").forEach((v) => v.classList.toggle("on", v.id === "v-" + view));
    moveGlider();
    const sec = $("#v-" + view);
    if (changed && sec) {
      // page entrance: sections rise in one after another
      sec.classList.remove("enter"); void sec.offsetWidth; sec.classList.add("enter");
      clearTimeout(sec._enter); sec._enter = setTimeout(() => sec.classList.remove("enter"), 900);
      sec.scrollTop = 0;
    }
    const r = Views[view];
    if (r) r(opts);
    if (window.HW3D && HW3D.wake) HW3D.wake();
  };

  // A game in front or the window minimized: CSS animations pause (see :root.idle in app.css).
  const idle = () => document.documentElement.classList.toggle("idle", document.hidden || !document.hasFocus());
  window.addEventListener("blur", idle);
  window.addEventListener("focus", idle);
  document.addEventListener("visibilitychange", idle);

  function renderShell() {
    document.documentElement.lang = S.lang;
    applyPalette();
    $("#app").innerHTML = html`
      <header class="topbar">
        <div class="brand">${brandMark()}<div class="brand-name">AI Tweaker</div></div>
        <div class="top-mid">
          <button class="search-btn" id="searchBtn">${icon("search")}<span>${t("search")}</span><span class="kbd">Ctrl K</span></button>
          <button class="gm-btn ${S.gameMode ? "on" : ""}" id="gmBtn" title="${t("gm.title")}">${icon("gamepad")}<span>${t("gm.btn")}</span><b class="gm-state">${S.gameMode ? t("gm.on") : t("gm.off")}</b></button>
          ${S.init && S.init.isAdmin ? `<span class="chip ok dot">${t("admin")}</span>` : `<span class="chip crit dot">${t("notAdmin")}</span>`}
          <button class="chip crit reboot-chip" id="rebootChip" title="${t("rs.chipTitle")}" ${S.rebootPending ? "" : "hidden"}>${icon("refresh")}${t("rs.chip")}</button>
        </div>
        <div class="top-right">
          <div class="seg" id="shiftSeg" title="${t("shift.title")}"><button data-v="auto" class="${S.themeMode === "auto" ? "on" : ""}">${t("shift.auto")}</button><button data-v="day" class="${S.themeMode === "day" ? "on" : ""}">${t("shift.day")}</button><button data-v="night" class="${S.themeMode === "night" ? "on" : ""}">${t("shift.night")}</button></div>
          <div class="seg" id="langSeg"><button data-v="ru" class="${S.lang === "ru" ? "on" : ""}">RU</button><button data-v="en" class="${S.lang === "en" ? "on" : ""}">EN</button></div>
          ${S.demo ? `<span class="demo-pill">${t("demo")}</span>` : ""}
          <span id="updSlot"></span>
          <button class="icon-btn" id="settingsBtn" title="${t("settings")}">${icon("settings")}</button>
          <div class="clock" id="clock">${fmtTime()}</div>
        </div>
      </header>
      <nav class="nav"><div class="nav-glider"></div>
        ${navList().map(([v], i, all) => `${v === "tune" || v === "drivers" || v === "backup" ? '<div class="nav-sep"></div>' : ""}<div class="nav-item ${S.view === v ? "on" : ""}" data-view="${v}"><span class="nc">${String(i + 1).padStart(2, "0")}</span>${icon(v)}<span>${t("nav." + v)}</span><span class="nav-badge" id="badge-${v}" hidden></span></div>`).join("")}
        <div class="nav-foot">${t("nav.foot")}<br><br><span class="mono">AI TWEAKER ${esc((S.init && S.init.version) || "1.0.0")}${S.init && S.init.machine ? " · " + esc(S.init.machine) : ""}</span></div>
      </nav>
      <main class="main">${NAV.map(([v]) => `<section class="view ${S.view === v ? "on" : ""}" id="v-${v}"></section>`).join("")}</main>
      <footer class="statusbar"><span class="led" id="statusLed"></span><span id="statusText">${t("status.ready")}</span><span class="ticker" id="ticker"></span><span id="sbTele"></span></footer>`;

    $$(".view").forEach((sec) => kickerWatch.observe(sec, { childList: true, subtree: true }));
    requestAnimationFrame(moveGlider);
    $(".nav").addEventListener("click", (e) => { const n = e.target.closest(".nav-item"); if (n) go(n.dataset.view); });
    $("#searchBtn").onclick = openSearch;
    $("#settingsBtn").onclick = openSettings;
    $("#rebootChip").onclick = () => App.restartPrompt("reboot");
    $("#gmBtn").onclick = () => App.gameMode();
    $("#shiftSeg").onclick = (e) => { const b = e.target.closest("button"); if (b) setTheme(b.dataset.v); };
    $("#langSeg").onclick = (e) => { const b = e.target.closest("button"); if (b) setLang(b.dataset.v); };
    updateBadges();
    if (App.updatePill) App.updatePill();
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
    const panel = S.theme === "night" ? "#111316" : "#f7f5ef";
    // Signal orange/amber by default; the Windows accent on request; monochrome on request or when that accent is colourless.
    let base = S.accentMode === "system" ? S.system.accent || "#0067C0" : S.theme === "night" ? "#f5a524" : "#e2560b";
    const c0 = rgb(base);
    if (S.accentMode === "mono" || Math.max(...c0) - Math.min(...c0) < 24) base = S.theme === "night" ? "#ebe9e3" : "#17171a";
    const a = fit(base, panel);
    root.style.setProperty("--accent", hex(a));
    root.style.setProperty("--accent-ink", lum(a) > 0.42 ? "#111214" : "#ffffff");
    root.style.setProperty("--radar", hex(a));
    if (window.HW3D) HW3D.retint();
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
      body: html`<div class="set-ver"><div><span>${t("settings.version")}</span><b>AI Tweaker ${esc(S.init.version)}</b></div><button class="btn sm" id="setUpd">${icon("update")}${t("upd.check")}</button></div>
        <label class="check"><input type="checkbox" id="setCu" ${s.checkUpdates !== false ? "checked" : ""}><span class="box"></span>${t("settings.checkUpdates")}</label>
        <label class="field">${t("settings.accent")}
          <select class="input" id="setAccent">${["industrial", "system", "mono"].map((m) => `<option value="${m}" ${S.accentMode === m ? "selected" : ""}>${t("settings.accent." + m)}</option>`).join("")}</select></label>
        <div class="td" style="display:flex;gap:8px;align-items:center"><span class="swatch" style="background:${esc(S.system.accent)}"></span>${t("settings.accent.current")} ${esc(S.system.accent)} · ${S.system.dark ? t("shift.night") : t("shift.day")}</div>
        <label class="field">${t("settings.ping")}<input class="input" id="setPing" value="${esc(s.pingHost)}"></label>
        <label class="field">${t("settings.autoRestart")}
          <select class="input" id="setAr">${[0, 30, 60, 120, 300].map((n) => `<option value="${n}" ${(s.autoRestartSeconds ?? 60) === n ? "selected" : ""}>${n ? n + " " + t("rs.sec") : t("settings.autoRestart.off")}</option>`).join("")}</select></label>
        <label class="check"><input type="checkbox" id="setRp" ${s.createRestorePoint ? "checked" : ""}><span class="box"></span>${t("settings.rp")}</label>`,
      buttons: [{ label: t("cancel"), value: false, cls: "ghost" }, { label: t("settings.save"), value: true, cls: "primary" }],
      onOpen: (ov) => {
        $("#setUpd", ov).onclick = async (e) => { e.currentTarget.disabled = true; await App.checkUpdate(true); e.currentTarget.disabled = false; };
        ov.querySelector(".modal-f [data-i='1']").addEventListener("click", async () => {
        const pingHost = $("#setPing", ov).value.trim();
        const createRestorePoint = $("#setRp", ov).checked;
        const autoRestartSeconds = +$("#setAr", ov).value;
        const checkUpdates = $("#setCu", ov).checked;
        S.accentMode = $("#setAccent", ov).value;
        applyPalette();
        await call("settings.set", { pingHost, createRestorePoint, autoRestartSeconds, checkUpdates, accent: S.accentMode });
        Object.assign(s, { pingHost, createRestorePoint, autoRestartSeconds, checkUpdates });
        toast(t("toast.saved"), "ok");
      }, { capture: true }); },
    });
  }

  // ------------------------------------------------------------------ global search (Ctrl+K)

  function searchIndex() {
    const idx = [];
    navList().forEach(([v]) => idx.push({ type: "page", title: t("nav." + v), desc: t(v === "dashboard" ? "dash.desc" : v + ".desc"), go: () => go(v) }));
    S.catalog.forEach((tw) => idx.push({ type: "tweak", title: L(tw.name), desc: tw.id + " · " + L(tw.description), keys: tw.id + " " + tw.name.ru + " " + tw.name.en + " " + tw.description.ru + " " + tw.description.en, go: () => go("tweaks", { focus: tw.id }) }));
    ((window.MOCK_DATA && MOCK_DATA.guides) || S.guides || []).forEach((g) => g.sections.forEach((sec) => sec.settings.forEach((st) =>
      idx.push({ type: "setting", title: `${g.name}: ${st.name}`, desc: `${L(sec.title)} → ${st.value}`, keys: `${g.name} ${st.name} ${sec.title.ru} ${sec.title.en} ${sec.path}`, go: () => go("apps", { guide: g.id }) }))));
    ((S.bios && S.bios.advice) || (window.MOCK_DATA && MOCK_DATA.bios) || []).forEach((b) => idx.push({ type: "bios", title: L(b.title), desc: b.value, keys: b.title.ru + " " + b.title.en + " " + b.value, go: () => go("manual", { section: "bios" }) }));
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
