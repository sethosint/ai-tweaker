/* Overview — readiness index, live sensors, configuration, issues, progress by category, log, monitoring. */
(function () {
  const GROUPS = { latency: ["input", "network", "devices"], fps: ["gaming", "power", "security"], privacy: ["privacy"], clean: ["system", "interface", "look", "apps"] };
  let logFilter = "all";

  const planItems = () => (S.plan ? S.plan.items : []);
  const applicable = () => planItems().map((i) => S.byId[i.id]).filter(Boolean);

  function scoreOf(cats) {
    const rec = planItems().filter((i) => i.recommended && (!cats || cats.includes((S.byId[i.id] || {}).category)));
    if (!rec.length) return 0;
    return Math.round((100 * rec.filter((i) => S.status[i.id] === "applied").length) / rec.length);
  }

  function ticketState(ins) {
    if (ins.action && ins.action.startsWith("tweak:") && S.status[ins.action.slice(6)] === "applied") return "resolved";
    return ins.action ? "fix" : "open";
  }

  // ------------------------------------------------------------------ render

  function render() {
    const v = $("#v-dashboard");
    const p = S.scan && S.scan.profile;
    const ins = (S.scan && S.scan.insights) || [];
    const sc = scoreOf();
    const fresh = p && p.os.installDate && Date.now() - new Date(p.os.installDate).getTime() < 3 * 864e5 && sc < 50;
    const C = 2 * Math.PI * 64;
    const open = ins.filter((i) => ticketState(i) !== "resolved" && i.severity !== "info").length;

    v.innerHTML = html`
      <div class="page-h"><div><h1>${t("dash.title")}</h1><p>${t("dash.desc")}</p></div>
        <div class="actions"><button class="btn primary" id="dashTune">${t("dash.tune")}${icon("arrow")}</button></div></div>
      ${fresh ? `<div class="callout ok" style="margin-bottom:16px;display:flex;gap:12px;align-items:center">${t("h.fresh")}<button class="btn sm primary" style="margin-left:auto" id="freshTune">${t("dash.tune")}</button></div>` : ""}
      <div class="grid dash">
        <div class="panel">
          <div class="panel-h"><span class="code">H-01</span>${t("dash.health")}<span class="right">${esc(t("prof." + S.profileName))}</span></div>
          <div class="health">
            <div class="ring ${S.busy && !S.scan ? "scanning" : ""}" id="ring">
              <svg viewBox="0 0 160 160"><circle class="ticks" cx="80" cy="80" r="76"/><circle class="track" cx="80" cy="80" r="64"/><circle class="val" cx="80" cy="80" r="64" stroke-dasharray="${C}" stroke-dashoffset="${C * (1 - sc / 100)}"/></svg>
              <div class="c"><b>${S.scan ? sc : "—"}<small>%</small></b><span>${t("h.index")}</span></div>
            </div>
            <div class="subs">${Object.entries(GROUPS).map(([k, cats]) => { const v2 = scoreOf(cats); return `<div class="sub"><span class="k">${t("h." + k)}</span><span class="bar"><i style="width:${v2}%"></i></span><span class="p">${S.scan ? v2 : "—"}%</span></div>`; }).join("")}</div>
          </div>
          <div class="health-foot"><button class="btn sm" id="dashScan">${icon("refresh")}${t("h.scan")}</button><span class="meta">${S.scan ? `${t("h.lastScan")} ${fmtTime(p.scannedAt)}` : t("h.never")}</span></div>
        </div>

        <div class="panel">
          <div class="panel-h"><span class="code">S-02</span>${t("dash.sensors")}<span class="right"><span class="led"></span>${t("live")}</span></div>
          <div class="sensors" id="sensors">${sensors()}</div>
        </div>

        <div class="panel">
          <div class="panel-h"><span class="code">C-03</span>${t("dash.system")}<span class="right">${esc((S.init && S.init.machine) || "")}</span></div>
          ${p ? html`<div class="sys-lines">
            <div><b>Windows</b><span>${esc(p.os.caption.replace("Microsoft Windows ", "").replace("Microsoft ", ""))} ${esc(p.os.displayVersion)} · ${p.os.build}.${p.os.ubr}</span></div>
            <div><b>${t("sys.cpu")}</b><span>${esc(p.cpu.name)} · ${p.cpu.cores}/${p.cpu.threads}${p.cpu.isHybrid ? ` · ${p.cpu.pCores}P+${p.cpu.eCores}E` : ""}</span></div>
            <div><b>${t("sys.gpu")}</b><span>${esc(p.gpus.map((g) => g.name).join(" + ") || "—")}</span></div>
            <div><b>${t("sys.ram")}</b><span>${p.memory.totalGb} GB ${esc(p.memory.type)} · ${p.memory.configuredSpeed} MHz · ${p.memory.modules.length}×</span></div>
            <div><b>${t("sys.disk")}</b><span>${esc(p.disks.map((d) => d.model).join(" · "))}</span></div>
            <div><b>${t("sys.net")}</b><span>${esc(p.network.map((n) => `${n.description}${n.connected ? ` · ${n.speedMbps} Mbps` : ""}`).join(" · ") || "—")}</span></div>
            <div><b>${t("sys.screen")}</b><span>${esc(p.displays.map((d) => `${d.width}×${d.height} · ${d.refreshHz}/${d.maxRefreshHz} Hz`).join(" · ") || "—")}</span></div>
            <div><b>${t("sys.board")}</b><span>${esc(p.board.manufacturer)} ${esc(p.board.product)}</span></div>
          </div>` : `<div class="empty" style="margin:0 16px 16px">${t("scanFirst")}</div>`}
          <div class="panel-h" style="border-top:1px solid var(--line)"><span class="code">Q</span>${t("dash.quick")}</div>
          <div class="quick">
            <button class="btn sm" data-q="purge">${t("q.purge")}</button><button class="btn sm" data-q="dns">${t("q.dns")}</button>
            <button class="btn sm" data-q="net">${t("q.net")}</button><button class="btn sm" data-q="clean">${t("q.clean")}</button>
            <button class="btn sm" data-q="explorer">${t("q.explorer")}</button><button class="btn sm" data-q="rp">${t("backup.rp")}</button>
          </div>
        </div>

        <div class="panel">
          <div class="panel-h"><span class="code">T-04</span>${t("dash.issues")}<span class="right">${open ? `${open} ${t("dash.open")}` : ""}</span></div>
          <div class="issues">${ins.length ? ins.map((x, n) => issue(x, n)).join("") : `<div class="empty">${S.scan ? t("tk.none") : t("scanFirst")}</div>`}</div>
        </div>

        <div class="panel">
          <div class="panel-h"><span class="code">P-05</span>${t("dash.progress")}<span class="right">${t("mx.applied")}</span></div>
          ${bars()}
        </div>

        <div class="panel">
          <div class="panel-h"><span class="code">E-06</span>${t("dash.stream")}<span class="right"><span class="filters" id="logFilters">${["all", "warn", "err"].map((f) => `<button data-f="${f}" class="${logFilter === f ? "on" : ""}">${t(f)}</button>`).join("")}</span></span></div>
          <div class="log" id="stream">${S.log.filter(passFilter).slice(0, 150).map(logRow).join("")}</div>
        </div>

        <div class="panel span-3 flow">
          <div class="panel-h"><span class="code">M-07</span>${t("dash.monitor")}<span class="right"><button class="btn sm" id="exportReport">${icon("download")}${t("dash.export")}</button></span></div>
          <div class="flow-stats">
            <div class="c1">${t("flow.cpu")}<b id="fCpu">—</b></div><div class="c2">GPU<b id="fGpu">—</b></div><div>${t("flow.ram")}<b id="fRam">—</b></div><div class="c3">${t("flow.ping")}<b id="fPing">—</b></div><div>${t("flow.proc")}<b id="fProc">—</b></div>
          </div>
          <canvas id="flowCanvas"></canvas>
        </div>
      </div>`;

    $("#dashScan").onclick = () => App.scan();
    $("#dashTune").onclick = () => go("tune");
    if ($("#freshTune")) $("#freshTune").onclick = () => go("tune");
    $("#exportReport").onclick = async () => { const path = await call("report.export"); toast(t("toast.report") + ": " + path, "ok"); };
    $("#logFilters").onclick = (e) => { const b = e.target.closest("button"); if (!b) return; logFilter = b.dataset.f; render(); };
    v.querySelectorAll("[data-tk]").forEach((b) => (b.onclick = () => ticketAction(ins.find((x) => x.id === b.dataset.tk))));
    v.querySelectorAll("[data-q]").forEach((b) => (b.onclick = () => quick(b.dataset.q)));
    bindSensorHints();
    drawFlow();
  }

  async function quick(q) {
    if (q === "net" || q === "clean") return go("tools");
    if (q === "purge") { const r = await call("memory.purge"); toast(`${t("mem.freed")}: ${(r.freedBytes / 1048576).toFixed(0)} MB`, "ok"); }
    if (q === "dns") { await call("net.flushdns"); toast(t("toast.dns"), "ok"); }
    if (q === "explorer") { await call("explorer.restart"); toast(t("toast.explorer"), "ok"); }
    if (q === "rp") { const b = busy(t("backup.rp")); try { await call("restore.create"); toast(t("toast.done"), "ok"); } finally { b.done(); } }
  }

  // ------------------------------------------------------------------ sensors

  function tile(key, value, unit, sub, level, gaugePct, hint) {
    return `<div class="sensor ${level || ""}"><div class="k">${key}</div><div class="v">${value ?? "—"}${value != null && unit ? `<small>${unit}</small>` : ""}</div><div class="s" ${hint ? `title="${esc(hint)}"` : ""}>${sub || "&nbsp;"}</div><div class="gauge"><i style="width:${Math.max(0, Math.min(100, gaugePct || 0))}%"></i></div></div>`;
  }
  const lvl = (v, warn, crit) => (v == null ? "" : v >= crit ? "crit" : v >= warn ? "warn" : "");

  function sensors() {
    const s = S.sensors || {};
    const tl = S.telemetry[S.telemetry.length - 1] || {};
    const p = S.scan && S.scan.profile;
    const disk = (s.disks || []).find((d) => d.tempC != null) || (s.disks || [])[0];
    const round = (x) => (x == null ? null : Math.round(x));
    return [
      tile(t("sn.cpuTemp"), round(s.cpuTempC), "°", s.cpuTempC != null ? esc(s.cpuTempSource || "") : `<span class="hint" data-hint="lhm">${t("sn.noCpuTemp")}</span>`, lvl(s.cpuTempC, 85, 95), (s.cpuTempC || 0) / 1.05),
      tile(t("sn.gpuTemp"), round(s.gpuTempC), "°", s.gpuPowerPercent != null ? `PWR ${Math.round(s.gpuPowerPercent)}%` : esc(p && p.gpus[0] ? p.gpus[0].name.replace(/NVIDIA GeForce |AMD Radeon /, "") : ""), lvl(s.gpuTempC, 80, 88), s.gpuTempC || 0),
      tile(t("sn.gpuLoad"), round(s.gpuLoad), "%", "3D", "", s.gpuLoad || 0),
      tile(t("sn.gpuFan"), s.gpuFanRpm, "rpm", "", "", (s.gpuFanRpm || 0) / 35),
      tile(t("sn.disk"), disk ? round(disk.tempC) : null, "°", disk ? `${esc(disk.name.slice(0, 22))}${disk.wearPercent != null ? ` · ${t("sn.wear")} ${disk.wearPercent}%` : ""}` : "", lvl(disk && disk.tempC, 65, 75), disk && disk.tempC ? disk.tempC / 0.85 : 0),
      tile(t("sn.timer"), s.timerResolutionMs != null ? (+s.timerResolutionMs).toFixed(s.timerResolutionMs < 1 ? 2 : 1) : null, "ms", t("sn.timerHint"), "", s.timerResolutionMs ? 100 - (s.timerResolutionMs / 15.625) * 100 : 0, t("sn.timerHint")),
      tile(t("sn.ping"), tl.pingMs, "ms", esc(S.init.settings.pingHost), lvl(tl.pingMs, 60, 120), (tl.pingMs || 0) / 1.5),
      tile(t("sn.cpu"), tl.cpu != null ? Math.round(tl.cpu) : null, "%", tl.processes ? `${tl.processes} proc` : "", lvl(tl.cpu, 90, 98), tl.cpu || 0),
      tile(t("sn.ram"), tl.ram, "%", tl.ramTotalGb ? `${tl.ramUsedGb}/${tl.ramTotalGb} GB` : "", lvl(tl.ram, 85, 95), tl.ram || 0),
    ].join("");
  }

  function bindSensorHints() {
    $$("[data-hint=lhm]").forEach((h) => (h.onclick = () => call("shell.open", { url: "https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases" })));
  }

  // ------------------------------------------------------------------ events / issues / charts

  const passFilter = (e) => logFilter === "all" || (logFilter === "warn" ? e.level === "warn" || e.level === "error" : e.level === "error");
  const logRow = (e) => `<div class="row"><span class="t">${fmtTime(e.time)}</span><span class="lv ${e.level}">${e.level === "error" ? "ERR" : e.level.toUpperCase()}</span><span class="msg"><b>${esc(e.source)}</b>${esc(e.text)}</span></div>`;

  function issue(i, n = 0) {
    const st = ticketState(i);
    const act = i.action || "";
    const label = act.startsWith("tweak:") ? t("tk.go.tweak") : act ? t("tk.go." + act) : "";
    return html`<div class="issue ${st === "resolved" ? "resolved" : i.severity}">
      <div class="issue-top"><span class="code">TCK-${String(n + 1).padStart(3, "0")}</span><span class="sev">${t("sev." + (st === "resolved" ? "resolved" : i.severity))}</span></div>
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
    if (!cats.length) return `<div class="empty" style="margin:0 16px 16px">${t("scanFirst")}</div>`;
    return `<div class="bars">${cats.map((c) => `<div class="bar-row"><div><div class="n">${esc(c.name)}</div><div class="track"><i style="width:${Math.round((100 * c.done) / c.total)}%"></i></div></div><span class="p">${c.done}/${c.total}</span></div>`).join("")}</div>`;
  }

  // ------------------------------------------------------------------ monitoring canvas

  function drawFlow() {
    const c = $("#flowCanvas");
    if (!c) return;
    const dpr = window.devicePixelRatio || 1;
    const w = c.clientWidth, h = c.clientHeight;
    if (c.width !== Math.round(w * dpr)) { c.width = Math.round(w * dpr); c.height = Math.round(h * dpr); }
    const g = c.getContext("2d");
    g.setTransform(dpr, 0, 0, dpr, 0, 0);
    g.clearRect(0, 0, w, h);
    const css = getComputedStyle(document.documentElement);
    const col = (n) => css.getPropertyValue(n).trim();
    const data = S.telemetry.slice(-120);
    const n = 120, step = w / (n - 1), off = n - data.length;
    g.strokeStyle = col("--line"); g.lineWidth = 1; g.setLineDash([]);
    for (let i = 1; i < 4; i++) { g.beginPath(); g.moveTo(0, (h * i) / 4); g.lineTo(w, (h * i) / 4); g.stroke(); }
    g.setLineDash([]);
    g.fillStyle = col("--ok");
    data.forEach((d, i) => { if (d.pingMs == null) return; const bh = Math.min(1, d.pingMs / 100) * h * 0.35; g.globalAlpha = 0.35; g.fillRect((i + off) * step - 1.5, h - bh, 3, bh); });
    g.globalAlpha = 1;
    const line = (key, color, dash) => {
      g.strokeStyle = color; g.lineWidth = 1.8; g.setLineDash(dash || []); g.beginPath();
      let started = false;
      data.forEach((d, i) => { if (d[key] == null) return; const x = (i + off) * step, yy = h - (d[key] / 100) * (h - 8) - 4; started ? g.lineTo(x, yy) : g.moveTo(x, yy); started = true; });
      g.stroke(); g.setLineDash([]);
    };
    line("ram", col("--muted"), [4, 3]);
    line("gpu", col("--info"));
    line("cpu", col("--accent"));
  }

  window.Dash = {
    appendLog(e) {
      const s = $("#stream");
      if (!s || !passFilter(e)) return;
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
      const box = $("#sensors");
      if (box) { box.innerHTML = sensors(); bindSensorHints(); }
      drawFlow();
    },
    sensors() {
      const box = $("#sensors");
      if (box) { box.innerHTML = sensors(); bindSensorHints(); }
    },
  };

  window.addEventListener("resize", () => { if (S.view === "dashboard") drawFlow(); });
  (window.Views ||= {}).dashboard = render;
})();
