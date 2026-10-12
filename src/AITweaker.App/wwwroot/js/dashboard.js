/* [01] Dashboard — system index, live sensors, configuration, events, issues, progress, optimization map, monitoring. */
(function () {
  const CAT_CODE = { privacy: "P", gaming: "G", input: "I", network: "N", power: "W", devices: "D", system: "S", interface: "U", apps: "A", security: "X" };
  const GROUPS = { latency: ["input", "network", "devices"], fps: ["gaming", "power", "security"], privacy: ["privacy"], clean: ["system", "interface", "apps"] };
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
    const C = 2 * Math.PI * 76;

    v.innerHTML = html`
      <div class="page-h"><div><span class="code">SEC-01 // OPS</span><h1>${t("dash.title")}</h1><p>${t("dash.desc")}</p></div>
        <div class="actions"><button class="btn primary" id="dashTune">${t("nav.tune")} →</button></div></div>
      ${fresh ? `<div class="callout ok" style="margin-bottom:14px;display:flex;gap:12px;align-items:center">✦ ${t("h.fresh")}<button class="btn sm primary" style="margin-left:auto" id="freshTune">${t("nav.tune")} →</button></div>` : ""}
      <div class="grid dash">
        <div class="panel">
          <div class="panel-h"><span class="code">H-01</span>${t("dash.health")}<span class="right">${esc(t("prof." + S.profileName))}</span></div>
          <div class="health">
            <div class="ring ${S.busy && !S.scan ? "scanning" : ""}" id="ring">
              <svg viewBox="0 0 180 180"><circle class="track" cx="90" cy="90" r="76"/><circle class="val" cx="90" cy="90" r="76" stroke-dasharray="${C}" stroke-dashoffset="${C * (1 - sc / 100)}"/><circle class="ticks" cx="90" cy="90" r="76"/></svg>
              <div class="sweep"></div>
              <div class="c"><b>${S.scan ? sc : "—"}<small>%</small></b><span>${t("h.index")}</span></div>
            </div>
            <div class="subs">${Object.entries(GROUPS).map(([k, cats]) => { const v2 = scoreOf(cats); return `<div class="sub"><span class="k">${t("h." + k)}</span><span class="bar"><i style="width:${v2}%"></i></span><span class="p">${S.scan ? v2 : "—"}%</span></div>`; }).join("")}</div>
          </div>
          <div class="health-foot"><button class="btn sm" id="dashScan">⟳ ${t("h.scan")}</button><span class="meta">${t("radar.last")}: ${S.scan ? fmtTime(p.scannedAt) : t("radar.never")} · ${ins.length} ${t("radar.checks")}</span></div>
        </div>

        <div class="panel">
          <div class="panel-h"><span class="code">S-02</span>${t("dash.sensors")}<span class="right"><span class="led"></span>LIVE</span></div>
          <div class="sensors" id="sensors">${sensors()}</div>
        </div>

        <div class="panel">
          <div class="panel-h"><span class="code">C-03</span>${t("dash.system")}<span class="right">${esc((S.init && S.init.machine) || "")}</span></div>
          ${p ? html`<div class="sys-lines">
            <div><b>OS</b><span>${esc(p.os.caption.replace("Microsoft ", ""))} ${esc(p.os.displayVersion)} · ${p.os.build}.${p.os.ubr}</span></div>
            <div><b>CPU</b><span>${esc(p.cpu.name)} · ${p.cpu.cores}C/${p.cpu.threads}T${p.cpu.isHybrid ? ` · ${p.cpu.pCores}P+${p.cpu.eCores}E` : ""}</span></div>
            <div><b>GPU</b><span>${esc(p.gpus.map((g) => g.name).join(" + ") || "—")}</span></div>
            <div><b>RAM</b><span>${p.memory.totalGb} GB ${esc(p.memory.type)} @ ${p.memory.configuredSpeed} MHz · ${p.memory.modules.length}×</span></div>
            <div><b>DISK</b><span>${esc(p.disks.map((d) => `${d.model} (${d.busType})`).join(" · "))}</span></div>
            <div><b>NET</b><span>${esc(p.network.map((n) => `${n.description}${n.connected ? ` · ${n.speedMbps} Mbps` : ""}`).join(" · ") || "—")}</span></div>
            <div><b>SCREEN</b><span>${esc(p.displays.map((d) => `${d.width}×${d.height} @ ${d.refreshHz}/${d.maxRefreshHz} Hz`).join(" · ") || "—")}</span></div>
            <div><b>BOARD</b><span>${esc(p.board.manufacturer)} ${esc(p.board.product)} · BIOS ${esc(p.board.biosVersion)}</span></div>
          </div>` : `<div class="empty" style="margin:12px">${t("scanFirst")}</div>`}
          <div class="panel-h" style="border-top:1px dashed var(--line-2)"><span class="code">Q</span>${t("dash.quick")}</div>
          <div class="quick">
            <button class="btn sm" data-q="purge">▣ ${t("q.purge")}</button><button class="btn sm" data-q="dns">⇆ ${t("q.dns")}</button>
            <button class="btn sm" data-q="net">◎ ${t("q.net")}</button><button class="btn sm" data-q="clean">✕ ${t("q.clean")}</button>
            <button class="btn sm" data-q="explorer">⌂ ${t("q.explorer")}</button><button class="btn sm" data-q="rp">◷ ${t("backup.rp")}</button>
          </div>
        </div>

        <div class="panel">
          <div class="panel-h"><span class="code">E-04</span>${t("dash.stream")}<span class="right"><span class="filters" id="logFilters">${["all", "warn", "err"].map((f) => `<button data-f="${f}" class="${logFilter === f ? "on" : ""}">${t(f)}</button>`).join("")}</span></span></div>
          <div class="stream" id="stream">${S.log.filter(passFilter).slice(0, 150).map(logRow).join("")}</div>
        </div>

        <div class="panel">
          <div class="panel-h"><span class="code">T-05</span>${t("dash.issues")}<span class="right">${ins.filter((i) => ticketState(i) !== "resolved" && i.severity !== "info").length} OPEN</span></div>
          <div class="tickets">${ins.length ? ins.map(ticket).join("") : `<div class="empty">${S.scan ? t("tk.none") : t("scanFirst")}</div>`}</div>
        </div>

        <div class="panel chart">
          <div class="panel-h"><span class="code">P-06</span>${t("dash.progress")}<span class="right" title="P·G·I·N·W·D·S·U·A·X">■ ${t("mx.applied")} ─ %</span></div>
          <div class="panel-b flush">${chart()}</div>
        </div>

        <div class="panel span-3">
          <div class="panel-h"><span class="code">X-07</span>${t("dash.map")}<span class="right">${applicable().length} NODES</span></div>
          <div class="matrix" id="matrix">${matrix()}</div>
          <div class="legend"><span><span class="led"></span>${t("mx.applied")}</span><span><span class="led warn"></span>${t("mx.pending")}</span><span><span class="led crit"></span>${t("mx.confirm")}</span><span><span class="led off"></span>${t("mx.off")}</span></div>
        </div>

        <div class="panel span-3 flow">
          <div class="panel-h"><span class="code">M-08</span>${t("dash.monitor")}<span class="right"><button class="btn sm primary" id="exportReport">⇩ ${t("dash.export")}</button></span></div>
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
    v.querySelectorAll(".cell").forEach((c) => (c.onclick = () => Views.tweakModal(c.dataset.id)));
    v.querySelectorAll("[data-q]").forEach((b) => (b.onclick = () => quick(b.dataset.q)));
    bindSensorHints();
    drawFlow();
  }

  async function quick(q) {
    if (q === "net" || q === "clean") return go("tools");
    if (q === "purge") { const r = await call("memory.purge"); toast(`${t("mem.freed")}: ${(r.freedBytes / 1048576).toFixed(0)} MB`, "ok"); }
    if (q === "dns") { await call("net.flushdns"); toast("DNS ✓", "ok"); }
    if (q === "explorer") { await call("explorer.restart"); toast("Explorer ✓", "ok"); }
    if (q === "rp") { const b = busy(t("backup.rp")); try { await call("restore.create"); toast("✓", "ok"); } finally { b.done(); } }
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
  const logRow = (e) => `<div class="row"><span class="t">${fmtTime(e.time)}</span><span class="lv ${e.level}">${e.level.toUpperCase()}</span><span class="msg"><b>${esc(e.source)}</b> ${esc(e.text)}</span></div>`;

  function ticket(i, n) {
    const st = ticketState(i);
    const id = "TCK-" + String(n + 1).padStart(4, "0");
    const act = i.action || "";
    const label = act.startsWith("tweak:") ? t("tk.go.tweak") : act ? t("tk.go." + act) : "";
    return html`<div class="ticket ${st === "resolved" ? "resolved" : i.severity}">
      <div class="tk-top"><span class="tk-id">${id}</span><span>${i.severity}</span><span class="chip ${st === "resolved" ? "ok" : i.severity === "critical" ? "crit" : i.severity === "warning" ? "warn" : "info"}" style="margin-left:auto">${t("tk." + st)}</span></div>
      <div class="tk-title">${esc(L(i.title))}</div>
      <div class="tk-detail">${esc(L(i.detail))}</div>
      <div class="tk-foot">${label && st !== "resolved" ? `<button class="btn sm" data-tk="${esc(i.id)}">${label} →</button>` : ""}${i.metric ? `<span class="tk-metric">${esc(i.metric)}</span>` : ""}</div>
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

  function chart() {
    const cats = S.init.categories.map((c) => {
      const items = planItems().filter((i) => S.byId[i.id] && S.byId[i.id].category === c.id);
      return { id: c.id, total: items.length, done: items.filter((i) => S.status[i.id] === "applied").length };
    }).filter((c) => c.total > 0);
    if (!cats.length) return `<div class="empty" style="margin:12px">${t("scanFirst")}</div>`;
    const W = 520, H = 300, pl = 30, pr = 34, pt = 18, pb = 30;
    const max = Math.max(...cats.map((c) => c.total));
    const bw = (W - pl - pr) / cats.length;
    const y = (v) => pt + (H - pt - pb) * (1 - v / max);
    const yp = (pct) => pt + (H - pt - pb) * (1 - pct / 100);
    let s = `<svg viewBox="0 0 ${W} ${H}" preserveAspectRatio="none">`;
    for (let g = 0; g <= 4; g++) { const gy = pt + ((H - pt - pb) * g) / 4; s += `<line class="grid-l" x1="${pl}" x2="${W - pr}" y1="${gy}" y2="${gy}"/><text x="${W - pr + 4}" y="${gy + 3}">${100 - g * 25}%</text>`; }
    const pts = [];
    cats.forEach((c, i) => {
      const x = pl + i * bw + bw * 0.18, w = bw * 0.64;
      s += `<rect class="bar" x="${x}" y="${y(c.total)}" width="${w}" height="${H - pb - y(c.total)}"/>`;
      s += `<rect class="bar-done" x="${x}" y="${y(c.done)}" width="${w}" height="${H - pb - y(c.done)}"/>`;
      s += `<text x="${x + w / 2}" y="${y(c.total) - 4}" text-anchor="middle">${c.done}/${c.total}</text>`;
      s += `<text x="${x + w / 2}" y="${H - pb + 16}" text-anchor="middle" style="font-weight:700"><title>${esc(L(S.init.categories.find((k) => k.id === c.id).name))}</title>${CAT_CODE[c.id] || "?"}</text>`;
      pts.push([x + w / 2, yp((100 * c.done) / c.total)]);
    });
    s += `<polyline class="line" points="${pts.map((p) => p.join(",")).join(" ")}"/>`;
    pts.forEach((p) => (s += `<circle class="dot" cx="${p[0]}" cy="${p[1]}" r="3.5"/>`));
    return s + `</svg>`;
  }

  function matrix() {
    if (!S.plan) return `<div class="empty">${t("scanFirst")}</div>`;
    const counters = {};
    return planItems().map((i) => {
      const tw = S.byId[i.id];
      if (!tw) return "";
      const code = (CAT_CODE[tw.category] || "?") + "-" + String((counters[tw.category] = (counters[tw.category] || 0) + 1)).padStart(2, "0");
      const st = S.status[i.id];
      const cls = st === "applied" ? "applied" : !i.recommended ? "" : i.needsConfirm ? "confirm" : "pending";
      const led = st === "applied" ? "" : !i.recommended ? "off" : i.needsConfirm ? "crit" : "warn";
      return `<div class="cell ${cls}" data-id="${esc(tw.id)}" title="${esc(L(tw.name))}"><span class="cid">${code}</span><span class="led ${led}"></span><span class="cname">${esc(L(tw.name))}</span></div>`;
    }).join("");
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
    g.strokeStyle = col("--line"); g.lineWidth = 1; g.setLineDash([2, 4]);
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
