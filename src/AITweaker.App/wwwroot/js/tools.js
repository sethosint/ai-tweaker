/* Tools — network quality, memory, display mode (stretched resolutions), startup apps, cleanup. */
(function () {
  const st = { net: null, custom: "", startup: null, clean: null, cleanSel: new Set(["temp-user", "temp-windows", "shader-directx", "crash-dumps"]), freed: null, displays: null, dev: null, res: null, hz: null, dns: null, dnsBench: null, dnsPick: null };
  const DNS = [["auto", ""], ["cloudflare", "1.1.1.1"], ["google", "8.8.8.8"], ["quad9", "9.9.9.9"]];
  const gcd = (a, b) => (b ? gcd(b, a % b) : a);
  function aspect(w, h) {
    const r = w / h;
    for (const [n, v] of [["4:3", 4 / 3], ["5:4", 5 / 4], ["16:10", 16 / 10], ["16:9", 16 / 9], ["21:9", 21 / 9], ["32:9", 32 / 9]]) if (Math.abs(r - v) < 0.02) return n;
    const g = gcd(w, h);
    return `${w / g}:${h / g}`;
  }
  const mb = (b) => (b >= 1073741824 ? (b / 1073741824).toFixed(2) + " GB" : (b / 1048576).toFixed(1) + " MB");

  function verdict(results) {
    const worst = results.reduce((a, r) => ({ jitter: Math.max(a.jitter, r.jitterMs || 0), loss: Math.max(a.loss, r.lossPercent || 0), avg: Math.max(a.avg, r.avgMs || 0) }), { jitter: 0, loss: 0, avg: 0 });
    if (worst.loss >= 2 || worst.jitter >= 15) return ["crit", t("net.bad")];
    if (worst.loss > 0 || worst.jitter >= 5 || worst.avg >= 60) return ["warn", t("net.ok")];
    return ["ok", t("net.good")];
  }

  async function render() {
    const v = $("#v-tools");
    if (!st.startup) call("startup.list", {}, { silent: true }).then((x) => { st.startup = x; if (S.view === "tools") render(); }).catch(() => {});
    if (!st.dns) call("net.dns", {}, { silent: true }).then((x) => { st.dns = x; if (S.view === "tools") render(); }).catch(() => {});
    const dnsCur = st.dns ? st.dns.current : null;
    const dnsPick = st.dnsPick || dnsCur || "auto";
    const bench = st.dnsBench || {};
    const fastest = Object.entries(bench).filter(([, ms]) => ms >= 0).sort((a, b) => a[1] - b[1])[0];
    if (!st.displays) call("display.modes", {}, { silent: true }).then((x) => { st.displays = x; if (S.view === "tools") render(); }).catch(() => {});
    const disp = st.displays && (st.displays.find((d) => d.device === st.dev) || st.displays[0]);
    const resList = disp ? [...new Map(disp.modes.map((m) => [`${m.width}x${m.height}`, m])).values()] : [];
    const resKey = st.res || (disp ? `${disp.current.width}x${disp.current.height}` : "");
    const hzList = disp ? disp.modes.filter((m) => `${m.width}x${m.height}` === resKey).map((m) => m.hz) : [];
    const onCurrent = disp && resKey === `${disp.current.width}x${disp.current.height}`;
    const hz = hzList.includes(st.hz) ? st.hz : onCurrent && hzList.includes(disp.current.hz) ? disp.current.hz : Math.max(0, ...hzList);
    const junk = st.startup ? st.startup.filter((e) => e.enabled && e.suggest === "off").length : 0;
    v.innerHTML = html`
      <div class="page-h"><div><h1>${t("tools.title")}</h1><p>${t("tools.desc")}</p></div></div>
      <div class="grid cols-2" style="align-items:start">
        <div class="panel"><div class="panel-h">${t("net.title")}<span class="right"><button class="btn sm danger" id="netReset">${t("net.reset")}</button></span></div>
          <div class="panel-b" style="display:grid;gap:10px"><div class="td" style="color:var(--muted)">${t("net.desc")}</div>
            <div style="display:flex;gap:8px"><input class="input" id="netCustom" placeholder="${t("net.custom")}" value="${esc(st.custom)}"><button class="btn primary" id="netRun">${t("net.run")}</button></div></div>
          ${st.net ? html`<table class="net-table"><tr><th>${t("net.host")}</th><th>${t("net.avg")}</th><th>${t("net.min")}</th><th>${t("net.max")}</th><th>${t("net.jitter")}</th><th>${t("net.loss")}</th></tr>
            ${st.net.map((r) => `<tr><td><b>${esc(r.label)}</b> <span class="tid">${esc(r.host)}</span></td><td>${r.avgMs ?? "—"} ms</td><td>${r.minMs ?? "—"}</td><td>${r.maxMs ?? "—"}</td><td style="color:${(r.jitterMs || 0) >= 15 ? "var(--crit)" : (r.jitterMs || 0) >= 5 ? "var(--warn)" : "var(--ok)"}">${r.jitterMs ?? "—"} ms</td><td style="color:${r.lossPercent > 0 ? "var(--crit)" : "var(--ok)"}">${r.lossPercent}%</td></tr>`).join("")}</table>
            <div class="verdict ${verdict(st.net)[0]}">${verdict(st.net)[1]}</div>` : ""}
        </div>

        <div class="panel"><div class="panel-h">${t("mem.title")}</div>
          <div class="panel-b" style="display:grid;gap:10px"><div class="td" style="color:var(--muted)">${t("mem.desc")}</div>
            <div style="display:flex;gap:10px;align-items:center;flex-wrap:wrap"><button class="btn primary" id="memPurge">${t("mem.purge")}</button>${st.freed != null ? `<span class="chip ok">${t("mem.freed")}: ${mb(st.freed)}</span>` : ""}
              <button class="btn ghost" id="dnsFlush">${t("q.dns")}</button><button class="btn ghost" id="expRestart">${t("q.explorer")}</button></div></div>
        </div>

        <div class="panel"><div class="panel-h">DNS<span class="right">${dnsCur ? `<span class="chip">${t("dns.now")}: ${t("dns." + dnsCur)}</span>` : ""}</span></div>
          <div class="panel-b" style="display:grid;gap:10px"><div class="td" style="color:var(--muted)">${t("dns.desc")}</div>
            <div class="dns-list">${DNS.map(([k, ip]) => { const ms = k === "auto" ? bench.current : bench[k]; return `<label class="dns-opt ${dnsPick === k ? "on" : ""}"><input type="radio" name="dns" value="${k}" ${dnsPick === k ? "checked" : ""}><span><b>${t("dns." + k)}</b>${ip ? ` <span class="tid">${ip}</span>` : ""}<br><span class="td">${t("dns." + k + "D")}</span></span><span class="dns-ms">${ms != null ? (ms < 0 ? "—" : `${ms} ms`) : ""}${fastest && ((k === "auto" && fastest[0] === "current") || fastest[0] === k) ? `<span class="chip ok">${t("dns.fastest")}</span>` : ""}</span></label>`; }).join("")}</div>
            <div style="display:flex;gap:8px;flex-wrap:wrap"><button class="btn primary" id="dnsApply" ${dnsPick === dnsCur ? "disabled" : ""}>${t("dns.apply")}</button><button class="btn ghost" id="dnsBench">${t("dns.bench")}</button></div>
          </div>
        </div>

        <div class="panel"><div class="panel-h">${t("disp.title")}<span class="right">${disp ? `${disp.current.width}×${disp.current.height} · ${disp.current.hz} Hz` : ""}</span></div>
          <div class="panel-b" style="display:grid;gap:12px"><div class="td" style="color:var(--muted)">${t("disp.desc")}</div>
            ${disp ? html`
              ${st.displays.length > 1 ? `<label class="field">${t("disp.monitor")}<select class="input" id="dispDev">${st.displays.map((d) => `<option value="${esc(d.device)}" ${d.device === disp.device ? "selected" : ""}>${esc(d.name)}</option>`).join("")}</select></label>` : ""}
              <div class="grid cols-2" style="gap:10px">
                <label class="field">${t("disp.res")}<select class="input" id="dispRes">${resList.map((m) => { const k = `${m.width}x${m.height}`; return `<option value="${k}" ${k === resKey ? "selected" : ""}>${m.width} × ${m.height} · ${aspect(m.width, m.height)}${m.width === disp.current.width && m.height === disp.current.height ? " · " + t("disp.now") : ""}</option>`; }).join("")}</select></label>
                <label class="field">${t("disp.hz")}<select class="input" id="dispHz">${hzList.map((h) => `<option value="${h}" ${h === hz ? "selected" : ""}>${h} Hz</option>`).join("")}</select></label>
              </div>
              <div style="display:flex;gap:8px;align-items:center;flex-wrap:wrap"><button class="btn primary" id="dispApply">${t("disp.apply")}</button><button class="btn ghost" id="dispCru">${icon("download")}CRU</button><button class="btn ghost" id="dispCruSite">${icon("external")}monitortests.com</button></div>` : `<div class="empty">…</div>`}
          </div>
        </div>

        <div class="panel"><div class="panel-h">${t("startup.title")}<span class="right">${st.startup ? st.startup.filter((e) => e.enabled).length + "/" + st.startup.length : ""}</span></div>
          <div class="panel-b" style="padding-bottom:0;display:grid;gap:10px"><div class="td" style="color:var(--muted)">${t("startup.desc")}</div>
            <div style="display:flex;gap:8px;flex-wrap:wrap">${junk ? `<button class="btn primary sm" id="suClean">${t("startup.clean")} (${junk})</button>` : `<button class="btn done sm" disabled>${icon("check")}${t("startup.clean0")}</button>`}<button class="btn ghost sm" id="suAutoruns">${icon("external")}Autoruns</button></div></div>
          <div>${st.startup ? (st.startup.length ? st.startup.map((e) => `<div class="tool-row"><div><div class="tn">${esc(e.name)} ${e.suggest === "off" ? `<span class="chip warn">${t("startup.off")}</span>` : e.suggest === "keep" ? `<span class="chip ok">${t("startup.keep")}</span>` : ""}</div><div class="tdsc">${esc(e.location)} · ${esc(e.scope)} · ${esc(e.command)}</div></div><div class="switch ${e.enabled ? "on" : ""}" data-sid="${esc(e.id)}" role="switch" aria-checked="${e.enabled}"></div></div>`).join("") : `<div class="empty" style="margin:12px">—</div>`) : `<div class="empty" style="margin:12px">…</div>`}</div>
        </div>

        <div class="panel"><div class="panel-h">${t("clean.title")}<span class="right">${st.clean ? mb(st.clean.filter((c) => st.cleanSel.has(c.id)).reduce((a, c) => a + c.bytes, 0)) : ""}</span></div>
          <div class="panel-b" style="display:grid;gap:10px"><div class="td" style="color:var(--muted)">${t("clean.desc")}</div>
            ${st.clean ? st.clean.map((c) => `<label class="check" style="justify-content:space-between;display:flex"><span style="display:flex;gap:8px;align-items:center"><input type="checkbox" data-cl="${c.id}" ${st.cleanSel.has(c.id) ? "checked" : ""}><span class="box"></span>${t("cl." + c.id)}</span><span class="tid">${mb(c.bytes)} · ${c.files}</span></label>`).join("") : ""}
            <div style="display:flex;gap:8px"><button class="btn" id="clAnalyze">${t("clean.analyze")}</button><button class="btn primary" id="clRun" ${st.clean ? "" : "disabled"}>${t("clean.run")}</button></div></div>
        </div>
      </div>`;

    $("#netCustom").oninput = (e) => (st.custom = e.target.value);
    if ($("#dispDev")) $("#dispDev").onchange = (e) => { st.dev = e.target.value; st.res = st.hz = null; render(); };
    if ($("#dispRes")) $("#dispRes").onchange = (e) => { st.res = e.target.value; st.hz = null; render(); };
    if ($("#dispHz")) $("#dispHz").onchange = (e) => (st.hz = +e.target.value);
    if ($("#dispCru")) $("#dispCru").onclick = () => go("install", { focus: "cru" });
    if ($("#dispCruSite")) $("#dispCruSite").onclick = () => call("shell.open", { url: "https://www.monitortests.com/forum/Thread-Custom-Resolution-Utility-CRU" });
    if ($("#dispApply")) $("#dispApply").onclick = () => applyMode(disp, resKey, +($("#dispHz").value || hz));
    $("#netRun").onclick = async () => {
      const b = busy(t("net.title"));
      const off = API.on("net.progress", (p) => b.progress(p.i / p.n, `${p.host} · ${p.i}/${p.n}`));
      try { st.net = await call("net.test", { count: 30, custom: st.custom.trim() }); } catch { /* toast */ } finally { off(); b.done(); }
      render();
    };
    $("#netReset").onclick = async () => {
      if (!(await confirmBox(t("net.reset"), t("net.resetQ"), t("net.reset"), "danger", true))) return;
      const b = busy(t("net.reset"));
      try { await call("net.reset"); } catch { return; } finally { b.done(); }
      await App.restartPrompt("reboot", `<div class="callout ok">${icon("check")}${t("net.resetDone")}</div>`);
    };
    $("#memPurge").onclick = async () => { const r = await call("memory.purge"); st.freed = r.freedBytes; render(); };
    v.querySelectorAll("input[name=dns]").forEach((r) => (r.onchange = () => { st.dnsPick = r.value; render(); }));
    $("#dnsBench").onclick = async () => {
      const b = busy(t("dns.bench"));
      try { st.dnsBench = await call("net.dnsBench"); } catch { /* toast */ } finally { b.done(); }
      render();
    };
    $("#dnsApply").onclick = async () => {
      const preset = st.dnsPick;
      try { await call("net.dnsSet", { preset }); toast(`DNS: ${t("dns." + preset)}`, "ok"); } catch { /* toast */ }
      st.dns = null; st.dnsPick = null; render();
    };
    $("#dnsFlush").onclick = async () => { await call("net.flushdns"); toast(t("toast.dns"), "ok"); };
    $("#expRestart").onclick = async () => { await call("explorer.restart"); toast(t("toast.explorer"), "ok"); };
    if ($("#suClean")) $("#suClean").onclick = async () => {
      let n = 0;
      try { n = await call("startup.cleanup"); } catch { return; }
      toast(t("startup.cleaned").replace("{n}", n), "ok");
      st.startup = await call("startup.list");
      render();
    };
    $("#suAutoruns").onclick = () => go("apps", { guide: "autoruns" });
    v.querySelectorAll("[data-sid]").forEach((sw) => (sw.onclick = async () => {
      const e = st.startup.find((x) => x.id === sw.dataset.sid);
      await call("startup.set", { id: e.id, enabled: !e.enabled });
      st.startup = await call("startup.list");
      render();
    }));
    v.querySelectorAll("[data-cl]").forEach((cb) => (cb.onchange = () => { cb.checked ? st.cleanSel.add(cb.dataset.cl) : st.cleanSel.delete(cb.dataset.cl); render(); }));
    $("#clAnalyze").onclick = async () => { const b = busy(t("clean.analyze")); try { st.clean = await call("clean.analyze"); } finally { b.done(); } render(); };
    $("#clRun").onclick = async () => {
      const b = busy(t("clean.title"));
      try { const freed = await call("clean.run", { ids: [...st.cleanSel] }); toast(`${t("clean.freed")}: ${mb(freed)}`, "ok"); st.clean = await call("clean.analyze"); } catch { /* toast */ } finally { b.done(); }
      render();
    };
  }

  /** Switches the mode; the app switches back by itself after 15 s unless the user keeps it. */
  async function applyMode(disp, resKey, hz) {
    const [width, height] = resKey.split("x").map(Number);
    if (width === disp.current.width && height === disp.current.height && hz === disp.current.hz) return;
    const ok = await call("display.set", { device: disp.device, width, height, hz }).catch(() => false);
    if (!ok) { toast(t("disp.refused"), "error", 6000); return; }
    let left = 15, timer = null;
    const res = await modal({
      title: t("disp.keepTitle"),
      body: html`<div>${t("disp.keepQ")}</div><div class="callout"><b id="dispLeft">${left}</b> ${t("rs.sec")} — ${t("disp.autoBack")}</div>`,
      buttons: [{ label: t("disp.back"), value: false, cls: "ghost" }, { label: t("disp.keep"), value: true, cls: "primary" }],
      onOpen: (ov, close) => { timer = setInterval(() => { left--; const el = $("#dispLeft", ov); if (el) el.textContent = left; if (left <= 0) { clearInterval(timer); close(null); } }, 1000); },
    });
    clearInterval(timer);
    if (res === true) { await call("display.keep", { device: disp.device }); toast(t("disp.kept"), "ok"); }
    else await call("display.revert", { device: disp.device }).catch(() => {}); // a no-op if the app already switched back
    st.displays = null; st.res = st.hz = null;
    setTimeout(render, 600);
  }

  (window.Views ||= {}).tools = render;
})();
