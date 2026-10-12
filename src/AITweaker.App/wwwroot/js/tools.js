/* [05] Tools — network quality, startup apps, cleanup, memory. */
(function () {
  const st = { net: null, custom: "", startup: null, clean: null, cleanSel: new Set(["temp-user", "temp-windows", "shader-directx", "crash-dumps"]), freed: null };
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

        <div class="panel"><div class="panel-h">${t("startup.title")}<span class="right">${st.startup ? st.startup.filter((e) => e.enabled).length + "/" + st.startup.length : ""}</span></div>
          <div class="panel-b" style="padding-bottom:0"><div class="td" style="color:var(--muted)">${t("startup.desc")}</div></div>
          <div>${st.startup ? (st.startup.length ? st.startup.map((e) => `<div class="tool-row"><div><div class="tn">${esc(e.name)}</div><div class="tdsc">${esc(e.location)} · ${esc(e.scope)} · ${esc(e.command)}</div></div><div class="switch ${e.enabled ? "on" : ""}" data-sid="${esc(e.id)}" role="switch" aria-checked="${e.enabled}"></div></div>`).join("") : `<div class="empty" style="margin:12px">—</div>`) : `<div class="empty" style="margin:12px">…</div>`}</div>
        </div>

        <div class="panel"><div class="panel-h">${t("clean.title")}<span class="right">${st.clean ? mb(st.clean.filter((c) => st.cleanSel.has(c.id)).reduce((a, c) => a + c.bytes, 0)) : ""}</span></div>
          <div class="panel-b" style="display:grid;gap:10px"><div class="td" style="color:var(--muted)">${t("clean.desc")}</div>
            ${st.clean ? st.clean.map((c) => `<label class="check" style="justify-content:space-between;display:flex"><span style="display:flex;gap:8px;align-items:center"><input type="checkbox" data-cl="${c.id}" ${st.cleanSel.has(c.id) ? "checked" : ""}><span class="box"></span>${t("cl." + c.id)}</span><span class="tid">${mb(c.bytes)} · ${c.files}</span></label>`).join("") : ""}
            <div style="display:flex;gap:8px"><button class="btn" id="clAnalyze">${t("clean.analyze")}</button><button class="btn primary" id="clRun" ${st.clean ? "" : "disabled"}>${t("clean.run")}</button></div></div>
        </div>
      </div>`;

    $("#netCustom").oninput = (e) => (st.custom = e.target.value);
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
    $("#dnsFlush").onclick = async () => { await call("net.flushdns"); toast(t("toast.dns"), "ok"); };
    $("#expRestart").onclick = async () => { await call("explorer.restart"); toast(t("toast.explorer"), "ok"); };
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

  (window.Views ||= {}).tools = render;
})();
