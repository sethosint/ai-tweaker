/* Console — the PC's parts as live 3D models with their sensors, readiness index, one-click apply, issues, log, monitoring. */
(function () {
  const GROUPS = { latency: ["input", "network", "devices"], fps: ["gaming", "power", "security"], privacy: ["privacy"], clean: ["system", "interface", "look", "apps"] };
  let logFilter = "all";
  let shownScore = 0;

  const planItems = () => (S.plan ? S.plan.items : []);

  function scoreOf(cats) {
    const rec = planItems().filter((i) => i.recommended && (!cats || cats.includes((S.byId[i.id] || {}).category)));
    if (!rec.length) return 0;
    return Math.round((100 * rec.filter((i) => S.status[i.id] === "applied").length) / rec.length);
  }

  function ticketState(ins) {
    if (ins.action && ins.action.startsWith("tweak:") && S.status[ins.action.slice(6)] === "applied") return "resolved";
    return ins.action ? "fix" : "open";
  }

  const shortCpu = (n) => (n || "").replace(/\(R\)|\(TM\)|CPU|Processor|\d+-Core|@.*$/gi, "").replace(/\s+/g, " ").trim();
  const shortGpu = (n) => (n || "").replace(/NVIDIA |GeForce |AMD |Radeon\(TM\) |\(R\)|\(TM\)/g, "").trim();

  // ------------------------------------------------------------------ the four steps: automatic → by hand → apps → Game Mode

  function steps() {
    const auto = S.plan ? [...S.selected].filter((id) => S.status[id] !== "applied").length : 0;
    const man = S.scan ? App.manualLeft() : { left: 0, total: 0 };
    const appsPending = S.guides ? new Set(S.guides.filter((g) => g.installed).flatMap((g) => (g.autoTweaks || []).filter((id) => S.byId[id] && S.status[id] !== "applied" && S.status[id] !== "n/a"))).size : null;
    const step = (n, id, state, done, btn) => html`<div class="step ${done ? "done" : ""}" data-step="${id}">
      <span class="step-n">${done ? icon("check") : n}</span>
      <div><b>${t("step." + id)}</b><span>${state}</span></div>
      <button class="btn sm ${done ? "ghost" : "primary"}" data-go-step="${id}">${btn}</button></div>`;
    return [
      step(1, "auto", auto ? t("step.autoN").replace("{n}", auto) : t("step.autoDone"), S.scan && !auto, auto ? t("tune.go") : t("man.openTab")),
      step(2, "manual", man.left ? t("step.manualN").replace("{n}", man.left).replace("{m}", man.total) : t("man.allDone"), S.scan && !man.left, t("man.openTab")),
      step(3, "apps", appsPending === null ? "…" : appsPending ? t("step.appsN").replace("{n}", appsPending) : t("step.appsDone"), appsPending === 0, t("man.openTab")),
      step(4, "game", S.gameMode ? t("step.gameOn") : t("step.gameOff"), !!S.gameMode, S.gameMode ? t("gm.stop") : t("gm.start")),
    ].join("");
  }

  function bindSteps() {
    $$("[data-go-step]").forEach((b) => (b.onclick = () => {
      const id = b.dataset.goStep;
      if (id === "auto") { if (S.plan && [...S.selected].some((x) => S.status[x] !== "applied")) App.gameSetup(); else go("tune"); }
      else if (id === "manual") go("manual");
      else if (id === "apps") go("apps");
      else App.gameMode();
    }));
  }

  function refreshSteps() {
    const box = $("#dashSteps");
    if (!box || S.view !== "dashboard") return;
    box.innerHTML = steps();
    bindSteps();
  }
  (window.App ||= {}).refreshSteps = refreshSteps;

  // ------------------------------------------------------------------ render

  function render() {
    const v = $("#v-dashboard");
    const p = S.scan && S.scan.profile;
    const ins = (S.scan && S.scan.insights) || [];
    const sc = scoreOf();
    const fresh = p && p.os.installDate && Date.now() - new Date(p.os.installDate).getTime() < 3 * 864e5 && sc < 50;
    const C = 2 * Math.PI * 64;
    const open = ins.filter((i) => ticketState(i) !== "resolved" && i.severity !== "info").length;
    // the same set the Game setup button applies: everything selected there that isn't applied yet
    const left = S.plan ? [...S.selected].filter((id) => S.status[id] !== "applied") : [];
    // the whole machine, not one part: a laptop or a tower, whichever this PC is
    const heroKind = p && p.machine && p.machine.isLaptop ? "laptop" : "tower";

    v.innerHTML = html`
      <section class="hero">
        <div class="hero-stage">
          <div class="hero-model" data-hw="${heroKind}" data-load="cpu" data-key="dash-hero-${heroKind}" data-dist="${heroKind === "laptop" ? 6.2 : 7.4}" data-yaw="-0.55"></div>
          <div class="hero-tag l t"><span class="k">GPU</span><span class="v" id="tgGpuT">—</span></div>
          <div class="hero-tag l b"><span class="k">GPU · ${t("hw.load")}</span><span class="v" id="tgGpuL">—</span></div>
          <div class="hero-tag r t"><span class="k">CPU</span><span class="v" id="tgCpuT">—</span></div>
          <div class="hero-tag r b"><span class="k">${t("sn.ping")}</span><span class="v" id="tgPing">—</span></div>
        </div>
        <div class="hero-info">
          <span class="kicker">SEC 01 · OPS</span>
          <h1>${t("dash.title")}</h1>
          <div class="rig">${p ? esc(`${shortCpu(p.cpu.name)} · ${p.gpus.map((g) => shortGpu(g.name)).join(" + ")} · ${p.memory.totalGb} GB`) : t("scanFirst")}</div>
          <div class="hero-score">
            <div class="ring ${S.busy && !S.scan ? "scanning" : ""}" id="ring">
              <svg viewBox="0 0 160 160"><circle class="ticks" cx="80" cy="80" r="76"/><circle class="track" cx="80" cy="80" r="64"/><circle class="val" cx="80" cy="80" r="64" stroke-dasharray="${C}" stroke-dashoffset="${C * (1 - shownScore / 100)}"/></svg>
              <div class="c"><b><span id="ringN">${S.scan ? shownScore : "—"}</span><small>%</small></b><span>${t("h.index")}</span></div>
            </div>
            <div class="subs">${Object.entries(GROUPS).map(([k, cats]) => { const v2 = scoreOf(cats); return `<div class="sub"><span class="k">${t("h." + k)}</span><span class="bar"><i style="width:0" data-w="${v2}"></i></span><span class="p">${S.scan ? v2 : "—"}%</span></div>`; }).join("")}</div>
          </div>
          <div class="hero-actions">
            ${left.length ? `<button class="btn hazard" id="dashApply" ${S.busy ? "disabled" : ""}><span>${t("tune.go")} · ${left.length}</span></button>` : `<button class="btn done" id="dashApply" disabled>${icon("check")}${t("dash.allSet")}</button>`}
            <button class="btn ghost" id="dashTune">${t("dash.tune")}${icon("arrow")}</button>
          </div>
          <div class="hero-actions"><button class="btn sm ghost" id="dashScan">${icon("refresh")}${t("h.scan")}</button><span class="meta">${S.scan ? `${t("h.lastScan")} ${fmtTime(p.scannedAt)}` : t("h.never")}</span></div>
        </div>
      </section>

      <div class="steps" id="dashSteps">${steps()}</div>

      ${fresh ? `<div class="callout ok" style="margin-bottom:22px;display:flex;gap:12px;align-items:center">${t("h.fresh")}<button class="btn sm primary" style="margin-left:auto" id="freshTune">${t("dash.tune")}</button></div>` : ""}

      <div class="hw-strip">${PARTS.map((x) => `<div class="hw-tile" data-go="${x.go}"><div class="hw-model" data-hw="${x.hw}" data-load="${x.hw}" data-key="dash-${x.id}" data-yaw="${x.yaw}"></div><div class="hw-data" id="hd-${x.id}">${x.data()}</div></div>`).join("")}</div>

      <div class="dash-row">
        <div class="panel">
          <div class="panel-h">${t("dash.issues")}<span class="right">${open ? `${open} ${t("dash.open")}` : ""}</span></div>
          <div class="issues">${ins.length ? ins.map((x, n) => issue(x, n)).join("") : `<div class="empty">${S.scan ? t("tk.none") : t("scanFirst")}</div>`}</div>
        </div>
        <div class="panel">
          <div class="panel-h">${t("dash.stream")}<span class="right"><span class="filters" id="logFilters">${["all", "warn", "err"].map((f) => `<button data-f="${f}" class="${logFilter === f ? "on" : ""}">${t(f)}</button>`).join("")}</span></span></div>
          <div class="log" id="stream">${logRows()}</div>
        </div>
      </div>

      <div class="dash-row">
        <div class="panel">
          <div class="panel-h">${t("dash.progress")}<span class="right">${t("mx.applied")}</span></div>
          ${bars()}
        </div>
        <div class="panel">
          <div class="panel-h">${t("dash.system")}<span class="right">${esc((S.init && S.init.machine) || "")}</span></div>
          ${p ? html`<div class="sys-lines">
            <div><b>Windows</b><span>${esc(p.os.caption.replace("Microsoft Windows ", "").replace("Microsoft ", ""))} ${esc(p.os.displayVersion)} · ${p.os.build}.${p.os.ubr}</span></div>
            <div><b>${t("sys.cpu")}</b><span>${esc(p.cpu.name)} · ${p.cpu.cores}/${p.cpu.threads}${p.cpu.isHybrid ? ` · ${p.cpu.pCores}P+${p.cpu.eCores}E` : ""}</span></div>
            <div><b>${t("sys.gpu")}</b><span>${esc(p.gpus.map((g) => g.name).join(" + ") || "—")}</span></div>
            <div><b>${t("sys.ram")}</b><span>${p.memory.totalGb} GB ${esc(p.memory.type)} · ${p.memory.configuredSpeed} MHz · ${p.memory.modules.length}×</span></div>
            <div><b>${t("sys.disk")}</b><span>${esc(p.disks.map((d) => d.model).join(" · "))}</span></div>
            <div><b>${t("sys.board")}</b><span>${esc(p.board.manufacturer)} ${esc(p.board.product)}</span></div>
          </div>` : `<div class="empty">${t("scanFirst")}</div>`}
          <div class="quick">
            <button class="btn sm" data-q="purge">${t("q.purge")}</button><button class="btn sm" data-q="dns">${t("q.dns")}</button>
            <button class="btn sm" data-q="net">${t("q.net")}</button><button class="btn sm" data-q="clean">${t("q.clean")}</button>
            <button class="btn sm" data-q="explorer">${t("q.explorer")}</button><button class="btn sm" data-q="rp">${t("backup.rp")}</button>
          </div>
        </div>
      </div>

      <div class="panel flow">
        <div class="panel-h">${t("dash.monitor")}<span class="right"><button class="btn sm ghost" id="exportReport">${icon("download")}${t("dash.export")}</button></span></div>
        <div class="flow-stats">
          <div class="c1">${t("flow.cpu")}<b id="fCpu">—</b></div><div class="c2">GPU<b id="fGpu">—</b></div><div>${t("flow.ram")}<b id="fRam">—</b></div><div class="c3">${t("flow.ping")}<b id="fPing">—</b></div><div>${t("flow.proc")}<b id="fProc">—</b></div>
        </div>
        <canvas id="flowCanvas"></canvas>
      </div>`;

    $("#dashScan").onclick = () => App.scan();
    $("#dashTune").onclick = () => go("tune");
    $("#dashApply").onclick = () => App.gameSetup();
    bindSteps();
    if (S.scan && !S.guides) call("guides.list", {}, { silent: true }).then((g) => { S.guides = g; refreshSteps(); }).catch(() => {});
    if (S.scan && !S.bios) call("bios.list", {}, { silent: true }).then((b) => { S.bios = b; refreshSteps(); }).catch(() => {});
    if ($("#freshTune")) $("#freshTune").onclick = () => go("tune");
    $("#exportReport").onclick = async () => { const path = await call("report.export"); toast(t("toast.report") + ": " + path, "ok"); };
    $("#logFilters").onclick = (e) => {
      const b = e.target.closest("button");
      if (!b) return;
      logFilter = b.dataset.f;
      $$("#logFilters button").forEach((x) => x.classList.toggle("on", x === b));
      $("#stream").innerHTML = logRows();
    };
    v.querySelectorAll("[data-tk]").forEach((b) => (b.onclick = () => ticketAction(ins.find((x) => x.id === b.dataset.tk))));
    v.querySelectorAll("[data-q]").forEach((b) => (b.onclick = () => quick(b.dataset.q)));
    v.querySelectorAll(".hw-tile").forEach((el) => el.addEventListener("click", (e) => { if (!e.target.closest(".hw-model")) go(el.dataset.go); }));
    bindHints();
    tags();
    drawFlow();
    if (S.scan) animateIn(sc);
  }

  /** Ring, index and bars fill up from where they were. */
  function animateIn(sc) {
    const C = 2 * Math.PI * 64;
    requestAnimationFrame(() => requestAnimationFrame(() => {
      const val = $("#ring .val");
      if (val) val.style.strokeDashoffset = C * (1 - sc / 100);
      $$("#v-dashboard .sub .bar i").forEach((i) => (i.style.width = i.dataset.w + "%"));
    }));
    const from = shownScore, n = $("#ringN"), t0 = performance.now();
    shownScore = sc;
    if (!n || from === sc) return;
    const step = (now) => {
      const k = Math.min(1, (now - t0) / 1100), e = 1 - Math.pow(1 - k, 3);
      if (!n.isConnected) return;
      n.textContent = Math.round(from + (sc - from) * e);
      if (k < 1) requestAnimationFrame(step);
    };
    requestAnimationFrame(step);
  }

  async function quick(q) {
    if (q === "net" || q === "clean") return go("tools");
    if (q === "purge") { const r = await call("memory.purge"); toast(`${t("mem.freed")}: ${(r.freedBytes / 1048576).toFixed(0)} MB`, "ok"); }
    if (q === "dns") { await call("net.flushdns"); toast(t("toast.dns"), "ok"); }
    if (q === "explorer") { await call("explorer.restart"); toast(t("toast.explorer"), "ok"); }
    if (q === "rp") { const b = busy(t("backup.rp")); try { await call("restore.create"); toast(t("toast.done"), "ok"); } finally { b.done(); } }
  }

  // ------------------------------------------------------------------ parts (3D tile + live numbers)

  const lvl = (v, warn, crit) => (v == null ? "" : v >= crit ? "crit" : v >= warn ? "warn" : "");
  const round = (x) => (x == null ? null : Math.round(x));
  const stat = (v, unit, label, level = "", title = "") => `<div class="${level}" ${title ? `title="${esc(title)}"` : ""}><b>${v ?? "—"}${v != null && unit ? `<small>${unit}</small>` : ""}</b><span>${label}</span></div>`;
  const data = (k, name, stats, gauge, gLevel = "", flag = "") => `${flag ? `<span class="hw-flag">${flag}</span>` : ""}<div class="hw-meta"><div class="k">${k}</div><div class="n" title="${esc(name)}">${esc(name || "—")}</div></div><div class="hw-stats">${stats.join("")}</div><div class="hw-gauge ${gLevel}"><i style="width:${Math.max(0, Math.min(100, gauge || 0))}%"></i></div>`;
  const prof = () => (S.scan && S.scan.profile) || null;
  const sn = () => S.sensors || {};
  const tl = () => S.telemetry[S.telemetry.length - 1] || {};

  const PARTS = [
    { id: "cpu", hw: "cpu", go: "tweaks", yaw: -0.4, data() {
      const s = sn(), l = tl(), p = prof();
      return data(t("sys.cpu"), p && shortCpu(p.cpu.name), [
        stat(round(l.cpu), "%", t("hw.load"), lvl(l.cpu, 90, 98)),
        s.cpuTempC != null ? stat(round(s.cpuTempC), "°", t("hw.temp"), lvl(s.cpuTempC, 85, 95)) : stat(null, "", `<span class="hint" data-hint="lhm">${t("hw.noSensor")}</span>`, "", t("sn.noCpuTemp")),
      ], l.cpu, lvl(l.cpu, 90, 98));
    } },
    { id: "gpu", hw: "gpu", go: "drivers", yaw: -0.5, data() {
      const s = sn(), p = prof(), g = p && p.gpus[0];
      return data(t("sys.gpu"), g && shortGpu(g.name), [
        stat(round(s.gpuLoad), "%", t("hw.load")),
        stat(round(s.gpuTempC), "°", s.gpuFanRpm ? `${t("hw.temp")} · ${s.gpuFanRpm} rpm` : t("hw.temp"), lvl(s.gpuTempC, 80, 88)),
      ], s.gpuLoad, lvl(s.gpuTempC, 80, 88));
    } },
    { id: "ram", hw: "ram", go: "bios", yaw: -0.6, data() {
      const l = tl(), p = prof();
      const slow = p && p.memory.xmpLikelyOff;
      return data(t("sys.ram"), p && `${p.memory.totalGb} GB ${p.memory.type} · ${p.memory.configuredSpeed} MHz`, [
        stat(l.ram, "%", t("hw.used"), lvl(l.ram, 85, 95)),
        stat(l.ramUsedGb, "GB", l.ramTotalGb ? `${t("hw.of")} ${l.ramTotalGb} GB` : ""),
      ], l.ram, lvl(l.ram, 85, 95), slow ? t("hw.xmpOff") : "");
    } },
    { id: "ssd", hw: "ssd", go: "tools", yaw: -0.5, data() {
      const s = sn(), p = prof();
      const d = (s.disks || []).find((x) => x.tempC != null) || (s.disks || [])[0];
      return data(t("hw.disk"), d ? d.name : p && p.disks[0] && p.disks[0].model, [
        stat(d ? round(d.tempC) : null, "°", t("hw.temp"), lvl(d && d.tempC, 65, 75)),
        stat(d && d.wearPercent != null ? d.wearPercent : null, "%", t("sn.wear"), lvl(d && d.wearPercent, 70, 90)),
      ], d && d.tempC ? d.tempC / 0.85 : 0, lvl(d && d.tempC, 65, 75));
    } },
    { id: "net", hw: "router", go: "tools", yaw: -0.5, data() {
      const l = tl(), p = prof();
      const n = p && (p.network.find((x) => x.connected) || p.network[0]);
      return data(t("sys.net"), n && n.description, [
        stat(l.pingMs, "ms", t("sn.ping"), lvl(l.pingMs, 60, 120), `${t("sn.ping")} · ${S.init.settings.pingHost}`),
        stat(n && n.connected ? n.speedMbps : null, "Mb", t("hw.link")),
      ], 100 - Math.min(100, (l.pingMs || 0) / 1.5), lvl(l.pingMs, 60, 120));
    } },
    { id: "screen", hw: "monitor", go: "tools", yaw: -0.45, data() {
      const p = prof(), s = sn();
      const d = p && p.displays[0];
      const low = d && d.maxRefreshHz && d.refreshHz < d.maxRefreshHz;
      return data(t("sys.screen"), d && `${d.width}×${d.height}`, [
        stat(d ? d.refreshHz : null, "Hz", low ? `${t("hw.max")} ${d.maxRefreshHz}` : t("hw.hz"), low ? "warn" : ""),
        stat(s.timerResolutionMs != null ? (+s.timerResolutionMs).toFixed(s.timerResolutionMs < 1 ? 2 : 1) : null, "ms", t("hw.timer"), "", t("sn.timerHint")),
      ], d && d.maxRefreshHz ? (100 * d.refreshHz) / d.maxRefreshHz : 0, low ? "warn" : "", low ? `${d.refreshHz} / ${d.maxRefreshHz} Hz` : "");
    } },
  ];

  function tags() {
    const s = sn(), l = tl();
    const f = (id, v, unit) => { const el = $(id); if (el) el.innerHTML = v == null ? "—" : `${v}<small>${unit}</small>`; };
    f("#tgGpuT", round(s.gpuTempC), "°C");
    f("#tgGpuL", round(s.gpuLoad), "%");
    f("#tgCpuT", round(s.cpuTempC != null ? s.cpuTempC : l.cpu), s.cpuTempC != null ? "°C" : "%");
    f("#tgPing", l.pingMs, "ms");
  }

  function updateParts() {
    PARTS.forEach((x) => { const el = $("#hd-" + x.id); if (el) el.innerHTML = x.data(); });
    bindHints();
    tags();
  }

  function bindHints() {
    $$("#v-dashboard [data-hint=lhm]").forEach((h) => (h.onclick = (e) => { e.stopPropagation(); call("shell.open", { url: "https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases" }); }));
  }

  // ------------------------------------------------------------------ events / issues / charts

  const passFilter = (e) => logFilter === "all" || (logFilter === "warn" ? e.level === "warn" || e.level === "error" : e.level === "error");
  const logRow = (e) => `<div class="row"><span class="t">${fmtTime(e.time)}</span><span class="lv ${e.level}">${e.level === "error" ? "ERR" : e.level.toUpperCase()}</span><span class="msg"><b>${esc(e.source)}</b>${esc(e.text)}</span></div>`;
  const logRows = () => S.log.filter(passFilter).slice(0, 150).map(logRow).join("") || `<div class="empty">—</div>`;

  function issue(i, n = 0) {
    const st = ticketState(i);
    const act = i.action || "";
    const label = act.startsWith("tweak:") ? t("tk.go.tweak") : act ? t("tk.go." + act) : "";
    return html`<div class="issue ${st === "resolved" ? "resolved" : i.severity}">
      <div class="issue-top"><span class="code">#${String(n + 1).padStart(2, "0")}</span><span class="sev">${t("sev." + (st === "resolved" ? "resolved" : i.severity))}</span></div>
      <div class="issue-title">${esc(L(i.title))}${st === "resolved" ? `<span class="chip ok">${t("tk.resolved")}</span>` : ""}</div>
      <div class="issue-detail">${esc(L(i.detail))}</div>
      ${(label && st !== "resolved") || i.metric ? `<div class="issue-foot">${label && st !== "resolved" ? `<button class="btn sm" data-tk="${esc(i.id)}">${label}</button>` : ""}${i.metric ? `<span class="issue-metric">${esc(i.metric)}</span>` : ""}</div>` : ""}
    </div>`;
  }

  async function ticketAction(i) {
    if (!i || !i.action) return;
    if (i.action.startsWith("tweak:")) {
      const id = i.action.slice(6);
      if (S.byId[id] && S.status[id] !== "applied") await App.apply([id]);
      return;
    }
    go(i.action);
  }

  function bars() {
    const cats = S.init.categories.map((c) => {
      const items = planItems().filter((i) => S.byId[i.id] && S.byId[i.id].category === c.id);
      return { name: L(c.name), total: items.length, done: items.filter((i) => S.status[i.id] === "applied").length };
    }).filter((c) => c.total > 0);
    if (!cats.length) return `<div class="empty">${t("scanFirst")}</div>`;
    return `<div class="bars">${cats.map((c) => `<div class="bar-row"><div class="n">${esc(c.name)}</div><span class="p">${c.done}/${c.total}</span><div class="track"><i style="width:${Math.round((100 * c.done) / c.total)}%"></i></div></div>`).join("")}</div>`;
  }

  // ------------------------------------------------------------------ monitoring canvas

  function drawFlow() {
    const c = $("#flowCanvas");
    if (!c) return;
    const dpr = window.devicePixelRatio || 1;
    const w = c.clientWidth, h = c.clientHeight;
    if (!w) return;
    if (c.width !== Math.round(w * dpr)) { c.width = Math.round(w * dpr); c.height = Math.round(h * dpr); }
    const g = c.getContext("2d");
    g.setTransform(dpr, 0, 0, dpr, 0, 0);
    g.clearRect(0, 0, w, h);
    const css = getComputedStyle(document.documentElement);
    const col = (n) => css.getPropertyValue(n).trim();
    const data = S.telemetry.slice(-120);
    const n = 120, step = w / (n - 1), off = n - data.length;
    g.strokeStyle = col("--line"); g.lineWidth = 1;
    for (let i = 1; i < 4; i++) { g.beginPath(); g.moveTo(0, (h * i) / 4); g.lineTo(w, (h * i) / 4); g.stroke(); }
    g.fillStyle = col("--ok");
    data.forEach((d, i) => { if (d.pingMs == null) return; const bh = Math.min(1, d.pingMs / 100) * h * 0.35; g.globalAlpha = 0.3; g.fillRect((i + off) * step - 1.5, h - bh, 3, bh); });
    g.globalAlpha = 1;
    const line = (key, color, dash, fill) => {
      const pts = [];
      data.forEach((d, i) => { if (d[key] != null) pts.push([(i + off) * step, h - (d[key] / 100) * (h - 8) - 4]); });
      if (pts.length < 2) return;
      if (fill) {
        const grad = g.createLinearGradient(0, 0, 0, h);
        grad.addColorStop(0, color + "55"); grad.addColorStop(1, color + "00");
        g.fillStyle = grad; g.beginPath(); g.moveTo(pts[0][0], h); pts.forEach(([x, y]) => g.lineTo(x, y)); g.lineTo(pts[pts.length - 1][0], h); g.fill();
      }
      g.strokeStyle = color; g.lineWidth = 1.8; g.setLineDash(dash || []); g.beginPath();
      pts.forEach(([x, y], i) => (i ? g.lineTo(x, y) : g.moveTo(x, y)));
      g.stroke(); g.setLineDash([]);
      const [lx, ly] = pts[pts.length - 1];
      g.fillStyle = color; g.beginPath(); g.arc(lx, ly, 3, 0, Math.PI * 2); g.fill();
    };
    line("ram", col("--muted"), [4, 3]);
    line("gpu", col("--info"));
    line("cpu", col("--accent"), null, true);
  }

  window.Dash = {
    appendLog(e) {
      const s = $("#stream");
      if (!s || !passFilter(e)) return;
      if (s.querySelector(".empty")) s.innerHTML = "";
      s.insertAdjacentHTML("afterbegin", logRow(e));
      while (s.children.length > 150) s.lastChild.remove();
    },
    tick(sm) {
      const f = (id, v) => { const el = $(id); if (el) el.textContent = v; };
      f("#fCpu", Math.round(sm.cpu) + "%");
      f("#fGpu", sm.gpu != null ? Math.round(sm.gpu) + "%" : "—");
      f("#fRam", `${sm.ram}% · ${sm.ramUsedGb}/${sm.ramTotalGb} GB`);
      f("#fPing", sm.pingMs != null ? sm.pingMs + " ms" : "—");
      f("#fProc", sm.processes);
      updateParts();
      drawFlow();
    },
    sensors() { updateParts(); },
  };

  window.addEventListener("resize", () => { if (S.view === "dashboard") drawFlow(); });
  (window.Views ||= {}).dashboard = render;
})();
