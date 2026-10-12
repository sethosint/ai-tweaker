/* [01] Dashboard — the OPS terminal: metrics + radar, event stream, tickets, chart, matrix, telemetry flow. */
(function () {
  const CAT_CODE = { privacy: "P", gaming: "G", input: "I", network: "N", power: "W", devices: "D", system: "S", interface: "U", apps: "A", security: "X" };
  let logFilter = "all";

  const planItems = () => (S.plan ? S.plan.items : []);
  const applicable = () => planItems().map((i) => S.byId[i.id]).filter(Boolean);

  function score() {
    const rec = planItems().filter((i) => i.recommended);
    if (!rec.length) return 0;
    return Math.round((100 * rec.filter((i) => S.status[i.id] === "applied").length) / rec.length);
  }

  function ticketState(ins) {
    if (ins.action && ins.action.startsWith("tweak:") && S.status[ins.action.slice(6)] === "applied") return "resolved";
    return ins.action ? "fix" : "open";
  }

  function hash(s) { let h = 2166136261; for (const c of s) h = Math.imul(h ^ c.charCodeAt(0), 16777619); return (h >>> 0) / 4294967295; }

  // ------------------------------------------------------------------ render

  function render() {
    const v = $("#v-dashboard");
    const p = S.scan && S.scan.profile;
    const ins = (S.scan && S.scan.insights) || [];
    const crit = ins.filter((i) => i.severity === "critical").length;
    const appliedCount = applicable().filter((tw) => S.status[tw.id] === "applied").length;
    const last = S.telemetry[S.telemetry.length - 1];
    const sc = score();

    v.innerHTML = html`
      <div class="page-h"><div><span class="code">SEC-01 // OPS</span><h1>${t("dash.title")}</h1><p>${t("dash.desc")}</p></div>
        <div class="actions"><button class="btn" id="dashScan">⟳ ${t("radar.scan")}</button><button class="btn primary" id="dashTune">${t("nav.tune")} →</button></div></div>
      <div class="grid dash">
        <div class="panel span-2 wide">
          <div class="panel-h"><span class="code">M-01</span>${t("dash.metrics")}<span class="right"><span class="led ${crit ? "crit" : ""}"></span>LIVE</span></div>
          <div class="metrics">
            <div class="metric ${sc >= 80 ? "ok" : ""}"><div class="k">${t("m.score")}</div><div class="v" id="mScore">${sc}<small>%</small></div><div class="score-bar"><i style="width:${sc}%"></i></div></div>
            <div class="metric"><div class="k">${t("m.applied")}</div><div class="v">${appliedCount}<small>/ ${applicable().length}</small></div><div class="s">${esc(S.profileName ? t("prof." + S.profileName) : "")}</div></div>
            <div class="metric ${crit ? "crit" : ""}"><div class="k">${t("m.issues")}</div><div class="v">${ins.filter((i) => i.severity !== "info").length}<small>${crit} ${t("m.critical")}</small></div><div class="s">${ins.length} ${t("radar.checks")}</div></div>
            <div class="metric"><div class="k">${t("m.ping")}</div><div class="v" id="mPing">${last && last.pingMs != null ? last.pingMs : "—"}<small>ms</small></div><div class="s">${t("m.host")} ${esc(S.init.settings.pingHost)}</div></div>
            <div class="metric"><div class="k">${t("m.cpu")}</div><div class="v" id="mCpu">${last ? Math.round(last.cpu) : "—"}<small>%</small></div><div class="s">${esc(p ? p.cpu.name : "")}</div></div>
            <div class="metric"><div class="k">${t("m.ram")}</div><div class="v" id="mRam">${last ? last.ram : "—"}<small>%</small></div><div class="s">${p ? `${p.memory.totalGb} GB ${p.memory.type} @ ${p.memory.configuredSpeed} MHz` : ""}</div></div>
          </div>
          ${p ? html`<div class="sys-lines">
            <div><b>OS</b><span>${esc(p.os.caption)} ${esc(p.os.displayVersion)} · ${p.os.build}.${p.os.ubr}</span></div>
            <div><b>GPU</b><span>${esc(p.gpus.map((g) => g.name).join(" + ") || "—")}</span></div>
            <div><b>DISK</b><span>${esc(p.disks.map((d) => `${d.model} (${d.busType} ${d.mediaType})`).join(" · "))}</span></div>
            <div><b>NET</b><span>${esc(p.network.map((n) => `${n.description}${n.connected ? ` · ${n.speedMbps} Mbps` : ""}`).join(" · ") || "—")}</span></div>
            <div><b>BOARD</b><span>${esc(p.board.manufacturer)} ${esc(p.board.product)} · BIOS ${esc(p.board.biosVersion)}</span></div>
          </div>` : ""}
        </div>

        <div class="panel">
          <div class="panel-h"><span class="code">R-02</span>${t("dash.radar")}<span class="right">${ins.length} ${t("radar.checks")}</span></div>
          <div class="radar-wrap">
            <div class="radar ${S.scan ? "" : "idle"}" id="radar"><div class="sweep"></div>
              ${ins.map((i) => { const a = hash(i.id) * Math.PI * 2; const r = i.severity === "critical" ? 0.28 : i.severity === "warning" ? 0.52 : 0.76; return `<span class="blip ${i.severity}" data-id="${esc(i.id)}" title="${esc(L(i.title))}" style="left:${50 + Math.cos(a) * r * 50}%;top:${50 + Math.sin(a) * r * 50}%"></span>`; }).join("")}
              <div class="center"><button id="radarScan">${t("radar.scan")}</button></div>
            </div>
            <div class="radar-meta"><span>${t("radar.last")}: ${S.scan ? fmtTime(S.scan.profile.scannedAt) : t("radar.never")}</span><span>${esc((S.init && S.init.machine) || "")}</span></div>
          </div>
        </div>

        <div class="panel">
          <div class="panel-h"><span class="code">E-03</span>${t("dash.stream")}<span class="right"><span class="filters" id="logFilters">${["all", "warn", "err"].map((f) => `<button data-f="${f}" class="${logFilter === f ? "on" : ""}">${t(f)}</button>`).join("")}</span></span></div>
          <div class="stream" id="stream">${S.log.filter(passFilter).slice(0, 150).map(logRow).join("")}</div>
        </div>

        <div class="panel">
          <div class="panel-h"><span class="code">T-04</span>${t("dash.tickets")}<span class="right">${ins.filter((i) => ticketState(i) !== "resolved").length} OPEN</span></div>
          <div class="tickets">${ins.length ? ins.map(ticket).join("") : `<div class="empty">${S.scan ? t("tk.none") : t("scanFirst")}</div>`}</div>
        </div>

        <div class="panel chart">
          <div class="panel-h"><span class="code">C-05</span>${t("dash.chart")}<span class="right" title="P·G·I·N·W·D·S·U·A·X">■ ${t("mx.applied")} ─ %</span></div>
          <div class="panel-b flush">${chart()}</div>
        </div>

        <div class="panel span-3">
          <div class="panel-h"><span class="code">X-06</span>${t("dash.matrix")}<span class="right">${applicable().length} NODES</span></div>
          <div class="matrix" id="matrix">${matrix()}</div>
          <div class="legend"><span><span class="led"></span>${t("mx.applied")}</span><span><span class="led warn"></span>${t("mx.pending")}</span><span><span class="led crit"></span>${t("mx.confirm")}</span><span><span class="led off"></span>${t("mx.off")}</span></div>
        </div>

        <div class="panel span-3 flow">
          <div class="panel-h"><span class="code">F-07</span>${t("dash.flow")}<span class="right"><button class="btn sm primary" id="exportReport">⇩ ${t("dash.export")}</button></span></div>
          <div class="flow-stats">
            <div class="c1">${t("flow.cpu")}<b id="fCpu">—</b></div><div class="c2">${t("flow.ram")}<b id="fRam">—</b></div><div class="c3">${t("flow.ping")}<b id="fPing">—</b></div><div>${t("flow.proc")}<b id="fProc">—</b></div>
          </div>
          <canvas id="flowCanvas"></canvas>
        </div>
      </div>`;

    $("#dashScan").onclick = $("#radarScan").onclick = () => App.scan();
    $("#dashTune").onclick = () => go("tune");
    $("#exportReport").onclick = async () => { const path = await call("report.export"); toast(t("toast.report") + ": " + path, "ok"); };
    $("#logFilters").onclick = (e) => { const b = e.target.closest("button"); if (!b) return; logFilter = b.dataset.f; render(); };
    v.querySelectorAll(".blip").forEach((b) => (b.onclick = () => { const i = ins.find((x) => x.id === b.dataset.id); if (i) ticketAction(i); }));
    v.querySelectorAll("[data-tk]").forEach((b) => (b.onclick = () => ticketAction(ins.find((x) => x.id === b.dataset.tk))));
    v.querySelectorAll(".cell").forEach((c) => (c.onclick = () => Views.tweakModal(c.dataset.id)));
    drawFlow();
  }

  const passFilter = (e) => logFilter === "all" || (logFilter === "warn" ? e.level === "warn" || e.level === "error" : e.level === "error");
  const logRow = (e) => `<div class="row"><span class="t">${fmtTime(e.time)}</span><span class="lv ${e.level}">${e.level.toUpperCase()}</span><span class="msg"><b>${esc(e.source)}</b> ${esc(e.text)}</span></div>`;

  function ticket(i, n) {
    const st = ticketState(i);
    const id = "TCK-" + String(n + 1).padStart(4, "0");
    const act = i.action || "";
    const label = act.startsWith("tweak:") ? t("tk.go.tweak") : act ? t("tk.go." + act) : "";
    return html`<div class="ticket ${st === "resolved" ? "resolved" : i.severity}">
      <div class="tk-top"><span class="tk-id">${id}</span><span>${i.severity}</span><span class="chip ${st === "resolved" ? "ok" : i.severity === "critical" ? "crit" : "warn"}" style="margin-left:auto">${t("tk." + st)}</span></div>
      <div class="tk-title">${esc(L(i.title))}</div>
      <div class="tk-detail">${esc(L(i.detail))}</div>
      <div class="tk-foot">${label && st !== "resolved" ? `<button class="btn sm" data-tk="${esc(i.id)}">${label} →</button>` : ""}${i.metric ? `<span class="tk-metric">${esc(i.metric)}</span>` : ""}</div>
    </div>`;
  }

  async function ticketAction(i) {
    if (!i || !i.action) return;
    if (i.action.startsWith("tweak:")) {
      const id = i.action.slice(6);
      if (!S.byId[id]) return;
      if (S.status[id] === "applied") return;
      await App.apply([id]);
      return;
    }
    go(i.action);
  }

  function chart() {
    const cats = S.init.categories.map((c) => {
      const items = planItems().filter((i) => S.byId[i.id] && S.byId[i.id].category === c.id);
      const done = items.filter((i) => S.status[i.id] === "applied").length;
      return { id: c.id, total: items.length, done };
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
    s += `<text x="${pl - 4}" y="${pt - 6}">n</text></svg>`;
    return s;
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

  // ------------------------------------------------------------------ live updates

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
    // ping bars (bottom), scaled to 100 ms
    g.fillStyle = col("--ok");
    data.forEach((d, i) => { if (d.pingMs == null) return; const bh = Math.min(1, d.pingMs / 100) * h * 0.35; g.globalAlpha = 0.35; g.fillRect((i + off) * step - 1.5, h - bh, 3, bh); });
    g.globalAlpha = 1;
    const line = (key, color) => {
      g.strokeStyle = color; g.lineWidth = 1.8; g.beginPath();
      data.forEach((d, i) => { const x = (i + off) * step, yy = h - (d[key] / 100) * (h - 8) - 4; i ? g.lineTo(x, yy) : g.moveTo(x, yy); });
      g.stroke();
    };
    line("ram", col("--info"));
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
      const set = (id, v) => { const el = $(id); if (el) el.firstChild.nodeValue = v; };
      set("#mCpu", Math.round(sm.cpu));
      set("#mRam", sm.ram);
      set("#mPing", sm.pingMs ?? "—");
      const f = (id, v) => { const el = $(id); if (el) el.textContent = v; };
      f("#fCpu", Math.round(sm.cpu) + "%");
      f("#fRam", `${sm.ram}% · ${sm.ramUsedGb}/${sm.ramTotalGb} GB`);
      f("#fPing", sm.pingMs != null ? sm.pingMs + " ms" : "—");
      f("#fProc", sm.processes);
      drawFlow();
    },
  };

  window.addEventListener("resize", () => { if (S.view === "dashboard") drawFlow(); });
  (window.Views ||= {}).dashboard = render;
})();
