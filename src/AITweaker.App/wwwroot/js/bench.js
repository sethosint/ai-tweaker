/* Bench: numbers before and after — the idle system (background load, DPC/ISR, timer, ping) and frame times in a game (PresentMon).
   The comparison itself (first vs newest, noise floors) comes from the C# side; this file only draws it. */
(function () {
  const App = (window.App ||= {});
  const V = (window.Views ||= {});
  const st = { seconds: 60 };

  /** Unit and decimals per metric. */
  const UNIT = {
    cpuIdlePercent: ["%", 1], dpcPercent: ["%", 2], interruptPercent: ["%", 2], dpcPerSec: ["/s", 0], interruptsPerSec: ["/s", 0], contextSwitchesPerSec: ["/s", 0],
    processes: ["", 0], threads: ["", 0], services: ["", 0], startupItems: ["", 0], ramUsedGb: ["GB", 2], timerMs: ["ms", 3], sleepAvgMs: ["ms", 3], sleepP99Ms: ["ms", 3],
    routerPingMs: ["ms", 1], routerJitterMs: ["ms", 1], pingMs: ["ms", 1], jitterMs: ["ms", 1], lossPercent: ["%", 1], bootSeconds: ["s", 1],
    avgFps: ["", 0], low1Fps: ["", 0], low01Fps: ["", 0], p99FrameMs: ["ms", 2], stuttersPerMin: ["/min", 1], latencyMs: ["ms", 1], pcLatencyMs: ["ms", 1],
  };
  const UNIT_KEY = { "/s": "bench.perSec", "/min": "bench.perMin", GB: "bench.gb", s: "bench.sec", ms: "bench.ms" };

  const fmt = (k, v) => {
    if (v == null) return "—";
    const [unit, d] = UNIT[k] || ["", 1];
    const u = UNIT_KEY[unit] ? t(UNIT_KEY[unit]) : unit;
    return `${Number(v).toFixed(d)}${u ? `<small>${u}</small>` : ""}`;
  };

  const when = (e) => `${fmtDate(e.at)} ${new Date(e.at).toLocaleTimeString(S.lang === "ru" ? "ru-RU" : "en-US", { hour: "2-digit", minute: "2-digit" })}`;

  /** One table for a pair: with an "after" it shows before / after / change, otherwise the single measurement. */
  function table(pair, singleLabel) {
    const has = !!pair.after;
    const head = has
      ? `<th>${t("bench.before")}<br><span class="tid">${when(pair.before)}</span></th><th>${t("bench.after")}<br><span class="tid">${when(pair.after)}</span></th><th>Δ</th>`
      : `<th>${singleLabel}<br><span class="tid">${when(pair.before)}</span></th><th></th><th></th>`;
    return `<table class="net-table bn-table"><tr><th>${t("bench.metric")}</th>${head}</tr>${pair.rows.map((r) => {
      if (!has) return `<tr><td>${t("bench.m." + r.key)}</td><td>${fmt(r.key, r.before)}</td><td></td><td></td></tr>`;
      const d = (UNIT[r.key] || [0, 1])[1];
      const sign = r.delta > 0 ? "+" : "";
      const change = r.delta == null ? "" : r.better == null ? "≈"
        : `${sign}${Number(r.delta).toFixed(d)}${r.deltaPercent != null && Math.abs(r.deltaPercent) >= 1 ? ` <span>${sign}${r.deltaPercent.toFixed(0)}%</span>` : ""}`;
      return `<tr><td>${t("bench.m." + r.key)}</td><td>${fmt(r.key, r.before)}</td><td>${fmt(r.key, r.after)}</td><td class="${r.better == null ? "" : r.better ? "up" : "down"}">${change}</td></tr>`;
    }).join("")}</table>`;
  }

  function verdict(rows) {
    const scored = rows.filter((r) => r.delta != null);
    const better = scored.filter((r) => r.better === true).length, worse = scored.filter((r) => r.better === false).length;
    return `<div class="verdict ${better > worse ? "ok" : worse > better ? "warn" : ""}">${t("bench.verdict").replace("{b}", better).replace("{w}", worse).replace("{n}", scored.length - better - worse)}</div>`;
  }

  function systemPanel(sys) {
    return html`<div class="panel"><div class="panel-h">${t("bench.system")}<span class="right"><button class="btn sm primary" id="bnSystem">${icon("gauge")}${t(sys ? "bench.measureAfter" : "bench.measureBefore")}</button></span></div>
      <div class="panel-b" style="display:grid;gap:12px">
        <div class="td" style="color:var(--muted)">${t("bench.systemD")}</div>
        ${!sys ? `<div class="callout">${t("bench.systemNone")}</div>`
          : table(sys, t("bench.before")) + (sys.after ? verdict(sys.rows) : `<div class="callout">${t("bench.afterHint")}</div>`)}
      </div></div>`;
  }

  function gamePanel(games, presentMon) {
    return html`<div class="panel"><div class="panel-h">${t("bench.game")}<span class="right"><span class="chip">PresentMon</span></span></div>
      <div class="panel-b" style="display:grid;gap:12px">
        <div class="td" style="color:var(--muted)">${t("bench.gameD")}</div>
        <div style="display:flex;gap:10px;align-items:center;flex-wrap:wrap">
          <div class="seg" id="bnSec">${[30, 60, 120].map((s) => `<button data-v="${s}" class="${st.seconds === s ? "on" : ""}">${s} ${t("bench.sec")}</button>`).join("")}</div>
          <button class="btn primary" id="bnGame">${icon("gamepad")}${t("bench.start")}</button>
        </div>
        ${presentMon ? "" : `<div class="tid">${t("bench.pmDownload")}</div>`}
        ${games.map(gameCard).join("") || `<div class="callout">${t("bench.gameNone")}</div>`}
      </div></div>`;
  }

  function gameCard(pair) {
    const f = (pair.after || pair.before).frames;
    return html`<div class="bn-game">
      <div class="bn-game-h"><b>${esc(f.app.replace(/\.exe$/i, ""))}</b><span class="tid">${f.seconds} ${t("bench.sec")} · ${f.frames} ${t("bench.frames")}</span></div>
      <div class="metrics bn-big">${["avgFps", "low1Fps", "low01Fps"].map((k) => `<div class="metric"><div class="k">${t("bench.m." + k)}</div><div class="v">${fmt(k, f[k])}</div></div>`).join("")}</div>
      ${f.bound ? `<div class="callout ${f.bound === "gpu" ? "" : "warn"}">${t("bench.bound." + f.bound).replace("{p}", Math.round(f.gpuBusyPercent))}</div>` : ""}
      ${table(pair, t("bench.now"))}
    </div>`;
  }

  function historyPanel(entries) {
    if (!entries.length) return "";
    return html`<div class="panel" style="margin-top:22px"><div class="panel-h">${t("bench.history")}<span class="right"><button class="btn sm ghost" id="bnFolder">${t("bench.folder")}</button><button class="btn sm ghost" id="bnClear">${t("bench.clear")}</button></span></div>
      <div class="panel-b">${[...entries].reverse().map((e) => `<div class="tool-row"><div><div class="tn">${e.kind === "system" ? t("bench.system") : esc(e.frames.app)}</div>
        <div class="td" style="color:var(--muted);font-size:12px">${when(e)} · ${e.kind === "system" ? `CPU ${e.system.cpuIdlePercent}% · DPC ${e.system.dpcPercent}% · ping ${e.system.pingMs ?? "—"} ms` : `${e.frames.avgFps} FPS · 1% ${e.frames.low1Fps} · 0.1% ${e.frames.low01Fps}`}</div></div>
        <button class="btn sm ghost" data-del="${esc(e.id)}" title="${t("bench.remove")}">${icon("x")}</button></div>`).join("")}</div></div>`;
  }

  V.bench = async () => {
    const v = $("#v-bench");
    if (!S.bench) { v.innerHTML = `<div class="empty">…</div>`; try { S.bench = await call("bench.list"); } catch { S.bench = { entries: [], presentMon: false, system: null, games: [] }; } }
    const b = S.bench;
    v.innerHTML = html`
      <div class="page-h"><div><h1>${t("bench.title")}</h1><p>${t("bench.desc")}</p></div></div>
      <div class="grid cols-2" style="align-items:start">${systemPanel(b.system)}${gamePanel(b.games, b.presentMon)}</div>
      ${historyPanel(b.entries)}`;
    $("#bnSystem").onclick = () => App.benchSystem(b.system ? "after" : "before");
    $("#bnGame").onclick = benchGame;
    $$("#bnSec button").forEach((x) => (x.onclick = () => { st.seconds = +x.dataset.v; V.bench(); }));
    $$("#v-bench [data-del]").forEach((x) => (x.onclick = async () => { await call("bench.remove", { id: x.dataset.del }); S.bench = null; V.bench(); }));
    if ($("#bnClear")) $("#bnClear").onclick = async () => { if (await confirmBox(t("bench.clear"), t("bench.clearQ"), t("bench.clear"), "danger")) { await call("bench.clear"); S.bench = null; V.bench(); } };
    if ($("#bnFolder")) $("#bnFolder").onclick = () => call("shell.folder", { name: "bench" });
  };

  /** Idle measurement with a progress card. */
  App.benchSystem = async (label) => {
    if (S.busy) return;
    const b = busy(t(label === "before" ? "bench.busyBefore" : "bench.busy"), { cancel: true });
    const off = API.on("bench.progress", (x) => b.progress(x.value / 100, t("bench.step." + x.step)));
    try { await call("bench.system", { label }); toast(t("bench.saved"), "ok"); } catch { /* toast shown */ } finally { off(); b.done(); S.bench = null; }
    if (S.view === "bench") V.bench();
  };

  /** Runs once, right before the first optimization, so there is always a "before" to compare with. */
  App.benchBefore = async () => {
    try {
      S.bench ||= await call("bench.list", {}, { silent: true });
      if (!S.bench.system) await App.benchSystem("before");
    } catch { /* measuring must never block optimizing */ }
  };

  async function benchGame() {
    if (S.busy) return;
    const b = busy(t("bench.busyGame"), { cancel: true });
    const off = API.on("bench.progress", (x) => b.progress(x.value / 100, t("bench.step." + x.step)));
    try {
      const e = await call("bench.capture", { seconds: st.seconds, delay: 10 }, { silent: true });
      toast(`${e.frames.app}: ${e.frames.avgFps} FPS · 1% ${e.frames.low1Fps}`, "ok");
    } catch (err) {
      toast(/no-game/.test(err.message) ? t("bench.noGame") : err.message, "error", 8000);
    } finally { off(); b.done(); S.bench = null; }
    if (S.view === "bench") V.bench();
  }
})();
