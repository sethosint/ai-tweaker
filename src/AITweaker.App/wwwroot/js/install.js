/* Install tab: official installers straight from the vendors (winget only as a fallback), Chrome extensions. */
(function () {
  const V = (window.Views ||= {});
  const GROUPS = ["browsers", "communication", "gaming", "media", "tools", "runtimes", "look", "store"];
  const st = { data: null, sel: null };

  async function load() {
    st.data = await call("programs.list");
    if (!st.sel) st.sel = new Set(st.data.programs.filter((p) => p.recommended && !p.installed).map((p) => p.id));
  }

  V.install = async (opts = {}) => {
    const v = $("#v-install");
    if (!st.data) {
      v.innerHTML = `<div class="empty">${t("loading")}</div>`;
      try { await load(); } catch { return; }
    }
    // Opened for one program (from its guide or the display tools): tick it so one click installs it.
    if (opts.focus && st.data.programs.some((p) => p.id === opts.focus && !p.installed)) st.sel.add(opts.focus);
    const d = st.data;
    const extTweak = S.byId["apps.chrome.extensions"];
    const extApplied = S.status["apps.chrome.extensions"] === "applied";
    const sel = d.programs.filter((p) => st.sel.has(p.id));

    v.innerHTML = html`
      <div class="page-h"><div><h1>${t("inst.title")}</h1><p>${t("inst.desc")}</p></div>
        <div class="actions"><button class="btn ghost sm" id="iRec">${t("inst.selectRec")}</button><button class="btn ghost sm" id="iNone">${t("tune.selectNone")}</button></div></div>
      ${d.winget || d.programs.every((p) => p.source || !p.winget) ? "" : html`<div class="callout warn" style="margin-bottom:14px;display:flex;gap:12px;align-items:center;justify-content:space-between"><span>${t("inst.noWinget")}</span><button class="btn sm" id="iRepair">${t("inst.repair")}</button></div>`}
      ${GROUPS.map((g) => { const list = d.programs.filter((p) => p.group === g); if (!list.length) return ""; return html`<div class="cat-group ${g === "store" ? "store-group" : ""}">
        <div class="cat-h">${t("inst.g." + g)}<span class="n">${list.filter((p) => st.sel.has(p.id)).length}/${list.length}</span></div>
        ${g === "store" ? html`<div class="callout ${d.store ? "" : "warn"}" style="margin:10px 0;display:flex;gap:12px;align-items:center;justify-content:space-between">
          <span>${d.store ? t("inst.storeD") : t("inst.storeMissing")}</span>${d.store ? "" : `<button class="btn sm" id="iStore">${t("inst.storeRestore")}</button>`}</div>` : ""}
        ${list.map((p) => html`<div class="trow inst ${p.installed ? "is-installed" : ""}" data-row="${p.id}">
          <label class="check"><input type="checkbox" data-id="${p.id}" ${st.sel.has(p.id) ? "checked" : ""} ${p.store && !d.store ? "disabled" : ""}><span class="box"></span></label>
          ${brandIcon(p.id, p.name, "sm")}
          <div><div class="tt">${esc(p.name)} ${p.paid ? `<span class="chip warn">${t("inst.paid")}</span>` : ""}</div>
            ${p.note ? `<div class="td">${esc(L(p.note))}</div>` : ""}
            <div class="tm"><span class="tid">${p.store ? `Microsoft Store · ${esc(p.store)}` : p.direct ? `${esc(p.source || "")} · ${t("inst.portable")}` : p.source ? `${esc(p.source)} · ${t(p.silent ? "inst.silent" : "inst.wizard")}` : p.winget ? "winget · " + esc(p.winget) : p.direct ? t("inst.official") : esc(p.url || "")}</span></div></div>
          <div class="tr inst-tr">${p.installed ? `<span class="status applied">${t("inst.installed")}</span>${p.winget ? `<button class="btn sm ghost" data-un="${p.id}">${t("inst.uninstall")}</button>` : ""}` : ""}</div></div>`).join("")}
      </div>`; }).join("")}

      <div class="cat-group">
        <div class="cat-h">${t("inst.ext")}<span class="n">${d.extensions.length}</span></div>
        <div class="callout" style="margin:10px 0;display:flex;gap:12px;align-items:center;justify-content:space-between">
          <span>${t("inst.extDesc")}</span>
          ${extTweak ? (extApplied ? `<span class="status applied">${t("inst.extOn")}</span>` : `<button class="btn primary sm" id="iExt">${t("inst.extAuto")}</button>`) : ""}
        </div>
        ${d.extensions.map((e) => html`<div class="trow inst">
          <span></span>${brandIcon(e.id, e.name, "sm")}
          <div><div class="tt">${esc(e.name)}</div><div class="td">${esc(L(e.note))}</div>
            <div class="tm"><span class="tid">${e.minVersion ? esc(e.minVersion) + "+" : t("inst.latest")}</span></div></div>
          <div class="tr"><button class="btn ghost sm" data-url="${esc(e.storeUrl)}">${t("inst.store")}</button></div></div>`).join("")}
      </div>

      <div class="sticky-apply">
        <button class="btn primary" id="iGo" ${sel.length ? "" : "disabled"}>${t("inst.go")} ${sel.length ? `(${sel.length})` : ""}</button>
        <div class="sum">${t("inst.hint")}</div>
      </div>`;

    if (opts.focus) { const row = v.querySelector(`[data-row="${CSS.escape(opts.focus)}"]`); if (row) { row.scrollIntoView({ block: "center" }); row.classList.add("hl"); } }
    v.querySelectorAll("[data-id]").forEach((c) => (c.onchange = () => { c.checked ? st.sel.add(c.dataset.id) : st.sel.delete(c.dataset.id); V.install(); }));
    v.querySelectorAll("[data-un]").forEach((b) => (b.onclick = () => uninstall(b.dataset.un)));
    v.querySelectorAll("[data-url]").forEach((b) => (b.onclick = () => call("shell.open", { url: b.dataset.url })));
    if ($("#iStore")) $("#iStore").onclick = () => go("debloat");
    $("#iRec").onclick = () => { st.sel = new Set(d.programs.filter((p) => p.recommended && !p.installed).map((p) => p.id)); V.install(); };
    $("#iNone").onclick = () => { st.sel = new Set(); V.install(); };
    if ($("#iRepair")) $("#iRepair").onclick = async () => {
      const b = busy(t("inst.repair"));
      try { const ok = await call("programs.repairWinget"); toast(ok ? t("inst.repairOk") : t("inst.repairFail"), ok ? "ok" : "error", 6000); } catch { /* toast */ } finally { b.done(); }
      st.data = null; V.install();
    };
    if ($("#iExt")) $("#iExt").onclick = () => App.apply(["apps.chrome.extensions", "apps.chrome.privacy", "apps.chrome.background"].filter((id) => S.byId[id] && S.status[id] !== "applied"));
    $("#iGo").onclick = () => install(sel.map((p) => p.id));
  };

  async function install(ids) {
    if (S.busy || !ids.length) return;
    const names = st.data.programs.filter((p) => ids.includes(p.id));
    const ok = await confirmBox(t("inst.go"), html`${t("inst.confirm")}<div class="pre" style="margin-top:10px">${names.map((p) => esc(p.name)).join("\n")}</div>`, `${t("inst.go")} (${ids.length})`);
    if (!ok) return;
    const b = busy(t("inst.busy"));
    const off = API.on("install.progress", (p) => b.progress((p.i - 0.5) / p.n, `${p.i}/${p.n} · ${p.name}`));
    let summary;
    try { summary = await call("programs.install", { ids }); } catch { return; } finally { off(); b.done(); }
    st.data = null;
    await load().catch(() => {});
    const good = summary.results.filter((r) => r.ok), bad = summary.results.filter((r) => !r.ok);
    good.forEach((r) => st.sel.delete(r.id));
    V.install();
    App.scan(true); // newly installed apps (Open-Shell, Chrome…) unlock their tweaks

    const res = await modal({
      title: t("inst.done"),
      body: html`<div class="metrics" style="grid-template-columns:repeat(2,1fr);border:1px solid var(--line)">
          <div class="metric ok"><div class="k">${t("inst.ok")}</div><div class="v">${good.length}</div></div>
          <div class="metric ${bad.length ? "crit" : ""}" style="border-right:0"><div class="k">${t("inst.failed")}</div><div class="v">${bad.length}</div></div></div>
        ${bad.length ? `<div class="pre">${bad.map((r) => `${esc(r.name)} — ${esc(r.method === "page" ? t("inst.pageOpened") : r.error || "")}`).join("\n")}</div>` : ""}`,
      buttons: bad.some((r) => r.method !== "page") ? [{ label: t("close"), value: false, cls: "ghost" }, { label: t("inst.retry"), value: true, cls: "primary" }] : [{ label: t("ok"), value: false, cls: "primary" }],
    });
    if (res === true) return install(bad.filter((r) => r.method !== "page").map((r) => r.id));
    if (summary.rebootRequired) await App.restartPrompt("reboot");
    if (good.some((r) => r.id === "openshell")) offerOpenShell();
  }

  async function uninstall(id) {
    const p = st.data.programs.find((x) => x.id === id);
    if (!(await confirmBox(t("inst.uninstall"), html`${t("inst.unQ")} <b>${esc(p.name)}</b>?`, t("inst.uninstall")))) return;
    const b = busy(`${t("inst.uninstall")} · ${p.name}`);
    let r;
    try { r = await call("programs.uninstall", { id }); } catch { return; } finally { b.done(); }
    toast(r.ok ? `${p.name}: ${t("inst.removed")}` : `${p.name}: ${r.error || ""}`, r.ok ? "ok" : "error", 6000);
    st.data = null;
    V.install();
    App.scan(true);
  }

  /** Right after Open-Shell is installed, offer the Start menu from the reference. */
  async function offerOpenShell() {
    const ids = ["look.openshell", "look.dark", "look.accent"];
    for (let i = 0; i < 20 && !(S.byId["look.openshell"] && S.status["look.openshell"] && S.status["look.openshell"] !== "n/a"); i++) await new Promise((r) => setTimeout(r, 500));
    if (S.status["look.openshell"] === "applied") return;
    if (await confirmBox(t("inst.osTitle"), t("inst.osQ"), t("inst.osGo"))) App.apply(ids.filter((id) => S.status[id] !== "applied"));
  }
})();
