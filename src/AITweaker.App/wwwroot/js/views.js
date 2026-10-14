/* Views 02–10. */
(function () {
  const V = (window.Views ||= {});
  const catName = (id) => L((S.init.categories.find((c) => c.id === id) || {}).name) || id;
  const needScan = (el) => { if (S.scan) return false; el.innerHTML = `<div class="empty">${t("scanFirst")}<br><br><button class="btn primary" onclick="App.scan()">${t("radar.scan")}</button></div>`; return true; };
  const statusChip = (id) => { const st = S.status[id] || "n/a"; return `<span class="status ${st}">${t(st === "n/a" ? "st.na" : "st." + st)}</span>`; };

  // ------------------------------------------------------------------ [02] game setup

  V.tune = () => {
    const v = $("#v-tune");
    if (needScan(v)) return;
    const items = S.plan.items;
    const groups = S.init.categories.map((c) => ({ c, list: items.filter((i) => S.byId[i.id] && S.byId[i.id].category === c.id) })).filter((g) => g.list.length);
    const sel = [...S.selected].filter((id) => items.some((i) => i.id === id));
    const feats = new Map();
    (S.features || []).forEach((f) => f.tweaks.forEach((id) => feats.set(id, f)));
    const asks = sel.filter((id) => App.questionFor(S.byId[id], feats)).length;
    const applied = items.filter((i) => S.status[i.id] === "applied").length;
    const man = App.manualLeft();

    v.innerHTML = html`
      <div class="page-h"><div><h1>${t("tune.title")}</h1><p>${t("tune.desc")}</p></div></div>
      <div class="tune-hero">
        <div class="th-l">
          <div class="th-k">${t("tune.heroK")}</div>
          <div class="th-t">${t("tune.heroT")}</div>
          <div class="th-d">${t("tune.heroD")}</div>
          <div class="th-stats">
            <div><b>${sel.length}</b><span>${t("tune.changes")}</span></div>
            <div><b>${asks}</b><span>${t("tune.questions")}</span></div>
            <div><b>${applied}</b><span>${t("tune.done")}</span></div>
          </div>
        </div>
        <div class="th-r">
          <button class="btn hazard th-go" id="gameSetup" ${sel.length ? "" : "disabled"}><span>${sel.length ? t("tune.go") : t("tune.allDone")}</span></button>
          <label class="check"><input type="checkbox" id="rpCheck" ${S.init.settings.createRestorePoint ? "checked" : ""}><span class="box"></span>${t("tune.rp")}</label>
          <div class="th-note">${t("tune.goNote")}</div>
        </div>
      </div>
      <div class="tune-cards">
        <div class="gm-card ${S.gameMode ? "on" : ""}">
          <div class="gm-ico">${icon("gamepad")}</div>
          <div class="gm-t">${t("gm.title")}<span class="chip ${S.gameMode ? "ok dot" : ""}">${S.gameMode ? t("gm.on") : t("gm.off")}</span></div>
          <div class="gm-d">${t("gm.cardD")}</div>
          <button class="btn ${S.gameMode ? "danger" : "primary"}" id="gmCard">${S.gameMode ? t("gm.stop") : t("gm.start")}</button>
        </div>
        <div class="mt-card">
          <div class="gm-ico">${icon("manual")}</div>
          <div class="gm-t">${t("man.title")}<span class="chip ${man.left ? "warn" : "ok"}">${man.left ? t("man.leftN").replace("{n}", man.left) : t("man.allDone")}</span></div>
          <div class="gm-d">${t("man.teaser")}</div>
          <button class="btn ghost" id="toManual">${t("man.openTab")}${icon("arrow")}</button>
        </div>
      </div>
      <div id="tuneGames">${App.gamesPanel()}</div>
      <div class="tune-list-h"><div><b>${t("tune.listT")}</b><span>${t("tune.listD")}</span></div>
        <div class="actions"><button class="btn ghost sm" id="selRec">${t("tune.selectRec")}</button><button class="btn ghost sm" id="selNone">${t("tune.selectNone")}</button></div></div>
      ${groups.map(({ c, list }) => html`<div class="cat-group">
        <div class="cat-h">${esc(L(c.name))}<span class="n">${list.filter((i) => S.selected.has(i.id)).length}/${list.length}</span></div>
        ${list.map((i) => { const tw = S.byId[i.id]; const ask = App.questionFor(tw, feats); return html`<div class="trow">
          <label class="check"><input type="checkbox" data-id="${tw.id}" ${S.selected.has(tw.id) ? "checked" : ""}><span class="box"></span></label>
          <div><div class="tt">${esc(L(tw.name))} ${ask ? `<span class="chip warn">${t("tune.asks")}</span>` : ""}</div>
            <div class="td">${esc(L(tw.description))}</div>
            ${tw.gain ? `<div class="why">${esc(L(tw.gain))}</div>` : `<div class="why">${esc(L(i.reason))}</div>`}
            <div class="tm">${badges(tw)}<span class="tid">${tw.id}</span></div></div>
          <div class="tr">${statusChip(tw.id)}</div></div>`; }).join("")}
      </div>`).join("")}
      <div class="sticky-apply">
        <button class="btn hazard" id="applyPlan" ${sel.length ? "" : "disabled"}><span>${t("tune.go")}</span></button>
        <div class="sum"><b>${sel.length}</b> ${t("tune.selected")} · <b>${asks}</b> ${t("tune.confirmCount")}</div>
      </div>`;

    const run = () => App.gameSetup({ restorePoint: $("#rpCheck").checked });
    $("#gameSetup").onclick = run;
    $("#applyPlan").onclick = run;
    $("#gmCard").onclick = () => App.gameMode();
    $("#toManual").onclick = () => go("manual");
    const gamesBox = $("#tuneGames");
    const drawGames = () => { gamesBox.innerHTML = App.gamesPanel(); App.bindGames(gamesBox, drawGames); };
    App.bindGames(gamesBox, drawGames);
    if (!S.games) App.loadGames().then(() => { if (S.view === "tune" && document.body.contains(gamesBox)) drawGames(); });
    if (!S.bios) call("bios.list", {}, { silent: true }).then((b) => { S.bios = b; if (S.view === "tune") V.tune(); }).catch(() => {});
    v.querySelectorAll("input[data-id]").forEach((cb) => (cb.onchange = () => { cb.checked ? S.selected.add(cb.dataset.id) : S.selected.delete(cb.dataset.id); V.tune(); }));
    $("#selRec").onclick = async () => { await App.buildPlan(); V.tune(); };
    $("#selNone").onclick = () => { S.selected.clear(); V.tune(); };
  };

  // ------------------------------------------------------------------ [03] tweaks catalog

  let tweakFilter = "", tweakCat = "all", onlyApplicable = true;
  const picked = new Set();

  V.tweaks = (opts = {}) => {
    const v = $("#v-tweaks");
    if (opts.focus) { tweakFilter = ""; tweakCat = "all"; onlyApplicable = false; }
    const planIds = new Set(S.plan ? S.plan.items.map((i) => i.id) : S.catalog.map((x) => x.id));
    const q = tweakFilter.toLowerCase();
    const list = S.catalog.filter((tw) => (tweakCat === "all" || tw.category === tweakCat) && (!onlyApplicable || planIds.has(tw.id))
      && (!q || (tw.id + " " + L(tw.name) + " " + L(tw.description)).toLowerCase().includes(q)));
    const cats = S.init.categories.filter((c) => list.some((tw) => tw.category === c.id));

    v.innerHTML = html`
      <div class="page-h"><div><h1>${t("tweaks.title")}</h1><p>${t("tweaks.desc")}</p></div>
        <div class="actions"><button class="btn sm" id="bulkApply">${t("tweaks.applySel")}</button><button class="btn sm danger" id="bulkRevert">${t("tweaks.revertSel")}</button></div></div>
      <div class="toolbar">
        <input class="input" id="tf" placeholder="${t("tweaks.filter")}" value="${esc(tweakFilter)}">
        <div class="tabs" id="catTabs"><button data-c="all" class="${tweakCat === "all" ? "on" : ""}">${t("all")}<span class="n">${S.catalog.length}</span></button>${S.init.categories.map((c) => `<button data-c="${c.id}" class="${tweakCat === c.id ? "on" : ""}">${esc(L(c.name).split(" ")[0])}<span class="n">${S.catalog.filter((x) => x.category === c.id).length}</span></button>`).join("")}</div>
        <label class="check"><input type="checkbox" id="onlyApp" ${onlyApplicable ? "checked" : ""}><span class="box"></span>${t("tweaks.onlyApplicable")}</label>
      </div>
      ${cats.map((c) => html`<div class="cat-group"><div class="cat-h">${esc(L(c.name))}<span class="n">${list.filter((x) => x.category === c.id).length}</span></div>
        ${list.filter((x) => x.category === c.id).map((tw) => row(tw, planIds.has(tw.id))).join("")}</div>`).join("") || `<div class="empty">${t("res.none")}</div>`}`;

    const tf = $("#tf");
    tf.oninput = () => { tweakFilter = tf.value; const pos = tf.selectionStart; V.tweaks(); const n = $("#tf"); n.focus(); n.setSelectionRange(pos, pos); };
    $("#catTabs").onclick = (e) => { const b = e.target.closest("button"); if (b) { tweakCat = b.dataset.c; V.tweaks(); } };
    $("#onlyApp").onchange = (e) => { onlyApplicable = e.target.checked; V.tweaks(); };
    v.querySelectorAll("input[data-pick]").forEach((cb) => (cb.onchange = () => (cb.checked ? picked.add(cb.dataset.pick) : picked.delete(cb.dataset.pick))));
    v.querySelectorAll("[data-apply]").forEach((b) => (b.onclick = () => App.apply([b.dataset.apply])));
    v.querySelectorAll("[data-revert]").forEach((b) => (b.onclick = () => App.revert([b.dataset.revert])));
    $("#bulkApply").onclick = () => picked.size && App.apply([...picked]);
    $("#bulkRevert").onclick = () => picked.size && App.revert([...picked]);
    if (opts.focus) { const el = v.querySelector(`[data-row="${CSS.escape(opts.focus)}"]`); if (el) { el.scrollIntoView({ block: "center" }); el.classList.add("hl"); } }
  };

  function row(tw, ok) {
    const st = S.status[tw.id];
    return html`<div class="trow" data-row="${tw.id}" style="${ok ? "" : "opacity:.55"}">
      <label class="check"><input type="checkbox" data-pick="${tw.id}" ${picked.has(tw.id) ? "checked" : ""} ${ok ? "" : "disabled"}><span class="box"></span></label>
      <div><div class="tt">${esc(L(tw.name))}</div><div class="td">${esc(L(tw.description))}</div>
        ${tw.confirmText ? `<div class="td" style="color:var(--crit)">${esc(L(tw.confirmText))}</div>` : ""}
        <div class="tm">${badges(tw)}<span class="tid">${tw.id}</span>${ok ? "" : `<span class="tid">· ${t("tweaks.notApplicable")}</span>`}</div>
        <details><summary>${t("details")}</summary>${tw.actions.map((a) => `<code>› ${esc(a)}</code>`).join("")}${tw.when ? `<code>when: ${esc(tw.when)}</code>` : ""}</details></div>
      <div class="tr">${statusChip(tw.id)}${ok && st !== "applied" ? `<button class="btn sm" data-apply="${tw.id}">${t("apply")}</button>` : ""}${st === "applied" || st === "partial" ? `<button class="btn sm danger" data-revert="${tw.id}">${t("revert")}</button>` : ""}</div>
    </div>`;
  }

  V.tweakModal = async (id) => {
    const tw = S.byId[id];
    if (!tw) return;
    const item = S.plan && S.plan.items.find((i) => i.id === id);
    const st = S.status[id];
    const res = await modal({
      title: `${catName(tw.category)} · ${tw.id}`,
      body: html`<div style="font:700 16px/1.3 var(--font-head);letter-spacing:.05em">${esc(L(tw.name))}</div>
        <div>${esc(L(tw.description))}</div>
        ${item ? `<div class="why">${esc(L(item.reason))}</div>` : ""}
        ${tw.confirmText ? `<div class="callout crit">${esc(L(tw.confirmText))}</div>` : ""}
        <div style="display:flex;gap:6px;flex-wrap:wrap">${statusChip(id)}${badges(tw)}</div>
        <div class="pre">${tw.actions.map(esc).join("\n")}</div>`,
      buttons: [{ label: t("close"), value: null, cls: "ghost" }, ...(st === "applied" || st === "partial" ? [{ label: t("revert"), value: "revert", cls: "danger" }] : []), ...(st !== "applied" && st !== "n/a" ? [{ label: t("apply"), value: "apply", cls: "primary" }] : [])],
    });
    if (res === "apply") App.apply([id]);
    if (res === "revert") App.revert([id]);
  };

  // ------------------------------------------------------------------ [04] debloat

  const removeSel = new Set();
  V.debloat = async () => {
    const v = $("#v-debloat");
    if (needScan(v)) return;
    if (!S.appx) {
      v.innerHTML = `<div class="empty">${t("debloat.loading")}</div>`;
      try { S.appx = await call("appx.list"); } catch { return; }
      removeSel.clear();
      S.appx.packages.filter((a) => a.recommended && !a.keep).forEach((a) => removeSel.add(a.package));
    }
    const groups = ["bloat", "ai", "communication", "xbox", "store", "media"];
    v.innerHTML = html`
      <div class="page-h"><div><h1>${t("debloat.title")}</h1><p>${t("debloat.desc")}</p></div>
        <div class="actions"><button class="btn ghost sm" id="appxReload" title="${t("refresh")}">${icon("refresh")}</button><button class="btn danger" id="appxRemove">${t("debloat.remove")} (${removeSel.size})</button></div></div>
      ${groups.map((g) => { const list = S.appx.packages.filter((a) => a.group === g); if (!list.length) return ""; return html`<div class="cat-group"><div class="cat-h">${t("grp." + g)}<span class="n">${list.length}</span></div>
        ${list.map((a) => html`<div class="trow">
          <label class="check"><input type="checkbox" data-pkg="${a.package}" ${removeSel.has(a.package) ? "checked" : ""} ${a.keep ? "disabled" : ""}><span class="box"></span></label>
          <div><div class="tt">${esc(L(a.name))} ${a.important ? `<span class="chip warn">${t("debloat.important")}</span>` : ""} ${a.keep ? `<span class="chip ok">${t("debloat.keep")}</span>` : ""}</div>
            ${a.warning ? `<div class="td">${esc(L(a.warning))}</div>` : ""}${a.keep && a.keepReason ? `<div class="why">${esc(L(a.keepReason))}</div>` : ""}
            <div class="tm"><span class="tid">${esc(a.package)}</span></div></div><div></div></div>`).join("")}</div>`; }).join("")}
      ${S.appx.removed && S.appx.removed.length ? html`<div class="cat-group"><div class="cat-h">${t("debloat.removedT")}<span class="n">${S.appx.removed.length}</span></div>
        <div class="td" style="padding:10px 16px 0;color:var(--muted)">${t("debloat.removedD")}</div>
        ${S.appx.removed.map((a) => `<div class="trow"><span></span><div><div class="tt">${esc(L(a.name))}</div><div class="tm"><span class="tid">${esc(a.package)}</span></div></div><div class="tr"><button class="btn sm" data-restore="${esc(a.package)}">${t("debloat.restore")}</button></div></div>`).join("")}</div>` : ""}
      ${S.appx.others.length ? html`<details class="cat-group"><summary class="cat-h" style="cursor:pointer">${t("debloat.others")}<span class="n">${S.appx.others.length}</span></summary>
        ${S.appx.others.map((o) => `<div class="trow"><label class="check"><input type="checkbox" data-pkg="${esc(o.name)}" data-other="1" ${removeSel.has(o.name) ? "checked" : ""}><span class="box"></span></label><div><div class="tt">${esc(o.name)}</div><div class="tm"><span class="tid">${esc(o.version)}</span></div></div><div></div></div>`).join("")}</details>` : ""}`;

    v.querySelectorAll("input[data-pkg]").forEach((cb) => (cb.onchange = () => { cb.checked ? removeSel.add(cb.dataset.pkg) : removeSel.delete(cb.dataset.pkg); $("#appxRemove").textContent = `${t("debloat.remove")} (${removeSel.size})`; }));
    $("#appxReload").onclick = () => { S.appx = null; V.debloat(); };
    v.querySelectorAll("[data-restore]").forEach((b) => (b.onclick = async () => {
      const bz = busy(t("debloat.restoring"));
      try { const r = await call("appx.restore", { name: b.dataset.restore }); toast(r.ok ? t("debloat.restored") : t("debloat.storeOpened"), r.ok ? "ok" : "warn", 6000); } catch { /* toast */ } finally { bz.done(); }
      S.appx = null; V.debloat();
    }));
    $("#appxRemove").onclick = removeApps;
  };

  async function removeApps() {
    if (!removeSel.size) return;
    const pk = (name) => S.appx.packages.find((a) => a.package === name);
    const accepted = [];
    const sel = [...removeSel];
    const risky = sel.filter((n) => (pk(n) && pk(n).important) || !pk(n));
    for (const n of sel) if (!risky.includes(n)) accepted.push(n);
    for (let i = 0; i < risky.length; i++) {
      const a = pk(risky[i]);
      const ok = await modal({
        title: `${t("confirm.title")} · ${i + 1} ${t("confirm.of")} ${risky.length}`, hazard: true,
        body: html`<div style="font:700 15px var(--font-head)">${esc(a ? L(a.name) : risky[i])}</div><div class="tid">${esc(risky[i])}</div>
          ${a && a.warning ? `<div class="callout crit"><b>${t("confirm.lose")}</b><br>${esc(L(a.warning))}</div>` : ""}`,
        buttons: [{ label: t("confirm.no"), value: false, cls: "ghost" }, { label: t("confirm.remove"), value: true, cls: "danger" }],
      });
      if (ok === true) accepted.push(risky[i]);
    }
    if (!accepted.length) return;
    const b = busy(t("busy.appx"));
    let n = 0;
    const off = API.on("appx.progress", (name) => b.progress(++n / accepted.length, name));
    try {
      const res = await call("appx.remove", { names: accepted });
      const okN = Object.values(res).filter(Boolean).length;
      toast(`${t("debloat.removed")}: ${okN}/${accepted.length}`, okN === accepted.length ? "ok" : "warn");
    } catch { /* toast */ } finally { off(); b.done(); }
    S.appx = null;
    V.debloat();
  }

  // ------------------------------------------------------------------ [05] drivers

  V.drivers = async () => {
    const v = $("#v-drivers");
    if (needScan(v)) return;
    if (!S.drivers) { v.innerHTML = `<div class="empty">…</div>`; S.drivers = await call("drivers.list"); }
    const nv = S.scan.profile.gpus.some((g) => g.vendor === "NVIDIA");
    const nvTweaks = S.catalog.filter((tw) => tw.id.startsWith("gaming.nvidia-") && (S.plan ? S.plan.items.some((i) => i.id === tw.id) : true));
    v.innerHTML = html`
      <div class="page-h"><div><h1>${t("drivers.title")}</h1><p>${t("drivers.desc")}</p></div>
        <div class="actions"><button class="btn sm" id="drvRescan">${t("drivers.rescan")}</button><button class="btn sm" id="drvOptional">${t("drivers.optional")}${icon("external")}</button></div></div>
      ${nv ? html`<div class="grid cols-2" style="margin-bottom:30px">
        <div class="panel"><div class="panel-h">${t("nv.title")}<span class="right" id="nvState"><span class="led busy"></span>${t("nv.checking")}</span></div>
          <div class="panel-b" id="nvBody">${nvVersions(null)}</div></div>
        <div class="panel"><div class="panel-h">${t("nv.settings")}</div>
          <div class="panel-b" style="display:grid;gap:12px"><div class="td" style="color:var(--muted)">${t("nv.settingsD")}</div>
            <div>${nvTweaks.map((tw) => `<div class="tool-row"><div><div class="tn">${esc(L(tw.name))}</div><div class="td" style="color:var(--muted);font-size:12px">${t("nv.sub." + tw.id)}</div></div><span class="status ${S.status[tw.id] === "applied" ? "applied" : "off"}">${t(S.status[tw.id] === "applied" ? "st.applied" : "st.off")}</span></div>`).join("")}</div>
            <div style="display:flex;gap:8px;flex-wrap:wrap"><button class="btn primary" id="nvApply" ${nvTweaks.every((tw) => S.status[tw.id] === "applied") ? "disabled" : ""}>${t("nv.applySettings")}</button>
              <button class="btn ghost" data-guide="nvidia-app">${t("nv.appGuide")}${icon("arrow")}</button></div>
          </div></div>
      </div>` : ""}
      ${drvSummary(S.drivers)}
      <div class="cards">${S.drivers.map((d) => html`<div class="card drv ${d.category === "oem" ? "drv-oem" : ""}" style="cursor:default">
        <div class="cn">${t("drv." + d.category)}</div>
        <div style="font-weight:700">${esc(d.name)}${d.category === "oem" ? ` <button class="btn sm ghost icon-only" data-copy="${esc(d.name)}" title="${t("man.copy")}">${icon("copy")}</button>` : ""}</div>
        ${d.category === "oem" ? "" : `<div class="cs">${esc(d.vendor || "")}${d.version ? " · " + esc(d.version) : ""}${d.date ? " · " + fmtDate(d.date) : ""}</div>`}
        ${d.category === "oem" ? "" : `<div class="cf"><span class="chip ${drvChip(d)}">${t("drv." + d.status)}</span>${d.provider && d.status === "inbox" ? `<span class="drv-by">${t("drv.by")} ${esc(d.provider)}</span>` : ""}</div>`}
        ${d.note ? `<div class="drv-note">${esc(L(d.note))}</div>` : ""}
        <div class="cf">${d.links.map((l) => `<button class="btn sm" data-url="${esc(l.url)}">${esc(l.title)}${icon("external")}</button>`).join("")}</div>
      </div>`).join("")}</div>`;
    v.querySelectorAll("[data-copy]").forEach((b) => (b.onclick = () => navigator.clipboard.writeText(b.dataset.copy).then(() => toast(t("man.copied")), () => {})));
    v.querySelectorAll("[data-url]").forEach((b) => (b.onclick = () => call("shell.open", { url: b.dataset.url })));
    v.querySelectorAll("[data-guide]").forEach((b) => (b.onclick = () => go("apps", { guide: b.dataset.guide })));
    $("#drvRescan").onclick = async () => { await call("drivers.rescan"); S.drivers = null; await App.scan(true); V.drivers(); };
    $("#drvOptional").onclick = () => call("shell.open", { url: "ms-settings:windowsupdate-optionalupdates" });
    if (!nv) return;
    $("#nvApply").onclick = () => App.apply(nvTweaks.filter((tw) => S.status[tw.id] !== "applied").map((tw) => tw.id));
    if (!S.nvidia) S.nvidia = await call("nvidia.check", {}, { silent: true }).catch((e) => ({ error: e.message }));
    if (S.view === "drivers" && $("#nvBody")) nvRender(nvTweaks);
  };

  /** ok = green, Windows' generic driver or "check" = neutral, low-priority "old" = amber, the rest = red. */
  const drvChip = (d) => d.status === "ok" ? "ok" : d.status === "check" || (d.status === "inbox" && d.priority === "low") ? "info"
    : d.priority === "high" || d.status === "missing" || d.status === "error" ? "crit" : "warn";

  const drvSummary = (list) => {
    const todo = list.filter((d) => d.category !== "oem" && d.status !== "ok" && d.priority !== "low");
    return html`<div class="callout ${todo.length ? "warn" : ""}" style="margin-bottom:18px">${todo.length
      ? t("drv.todo").replace("{n}", todo.length) + " " + todo.map((d) => `<b>${t("drv." + d.category)}</b>`).join(", ")
      : t("drv.allGood")}</div>`;
  };

  const nvDate = (d) => { const x = d ? new Date(d) : null; return x && !isNaN(x) ? fmtDate(x) : esc(d || ""); };

  function nvVersions(n) {
    const g = S.scan.profile.gpus.find((x) => x.vendor === "NVIDIA");
    const inst = n && n.installed ? n.installed : g.driverVersion;
    return html`<div class="nv-vers">
      <div><span class="k">${t("nv.installed")}</span><b>${esc(inst || "—")}</b><span class="s">${g.driverDate ? fmtDate(g.driverDate) : ""}</span></div>
      <div class="arrow">${icon("arrow")}</div>
      <div class="${n && n.updateAvailable ? "new" : ""}"><span class="k">${t("nv.latest")}</span><b>${n && n.latest ? esc(n.latest.version) : "…"}</b><span class="s">${n && n.latest ? `${nvDate(n.latest.releaseDate)}${n.latest.size ? " · " + esc(n.latest.size) : ""}` : ""}</span></div>
    </div><div class="td" style="color:var(--muted);margin-top:4px">${esc(g.name)}</div>`;
  }

  function nvRender(nvTweaks) {
    const n = S.nvidia || {};
    const st = $("#nvState");
    if (n.latest) st.innerHTML = `<span class="chip ${n.updateAvailable ? "warn" : "ok"} dot">${t(n.updateAvailable ? "nv.update" : "nv.upToDate")}</span>`;
    else st.innerHTML = `<span class="chip crit dot">${t("nv.unknown")}</span>`;
    const panelOn = nvTweaks.length > 0;
    $("#nvBody").innerHTML = html`${nvVersions(n)}
      ${n.latest ? html`<div style="display:grid;gap:9px;margin:16px 0 4px">
          <label class="check"><input type="checkbox" id="nvAudio" checked><span class="box"></span>${t("nv.hdAudio")}</label>
          <label class="check"><input type="checkbox" id="nvPhysx" checked><span class="box"></span>${t("nv.physx")}</label>
          <label class="check"><input type="checkbox" id="nvClean" checked><span class="box"></span>${t("nv.clean")}</label>
          ${panelOn ? `<label class="check"><input type="checkbox" id="nvPanel" checked><span class="box"></span>${t("nv.panel")}</label>` : ""}
        </div>
        <div class="callout" style="margin:10px 0 14px">${t("nv.note")}</div>
        <div style="display:flex;gap:8px;flex-wrap:wrap"><button class="btn ${n.updateAvailable ? "primary" : ""}" id="nvInstall">${icon("download")}${t(n.updateAvailable ? "nv.install" : "nv.reinstall").replace("{v}", esc(n.latest.version))}</button>
          ${n.latest.detailsUrl ? `<button class="btn ghost" data-nvurl="${esc(n.latest.detailsUrl)}">${t("nv.details")}${icon("external")}</button>` : ""}
          <button class="btn ghost" data-nvurl="https://www.nvidia.com/en-us/software/nvidia-app/">NVIDIA App${icon("external")}</button></div>`
      : html`<div class="callout warn" style="margin-top:14px">${t("nv.lookupFail")}${n.error ? `<div class="tid" style="margin-top:6px">${esc(n.error)}</div>` : ""}</div>
        <div style="margin-top:12px"><button class="btn" data-nvurl="https://www.nvidia.com/en-us/drivers/">${t("nv.page")}${icon("external")}</button></div>`}`;
    $$("#nvBody [data-nvurl]").forEach((b) => (b.onclick = () => call("shell.open", { url: b.dataset.nvurl })));
    if ($("#nvInstall")) $("#nvInstall").onclick = () => nvInstall(nvTweaks);
  }

  async function nvInstall(nvTweaks) {
    const n = S.nvidia, ver = n.latest.version;
    const opts = { hdAudio: $("#nvAudio").checked, physX: $("#nvPhysx").checked, clean: $("#nvClean").checked };
    const panel = $("#nvPanel") ? $("#nvPanel").checked : false;
    const ok = await confirmBox(t("nv.confirmT"), t("nv.confirmD").replace("{v}", esc(ver)).replace("{size}", esc(n.latest.size || "")), t("nv.install").replace("{v}", esc(ver)));
    if (!ok) return;
    const before = Object.keys(S.status).filter((id) => S.status[id] === "applied");
    const span = { download: [0, 0.72], extract: [0.72, 0.1], install: [0.84, 0.14] };
    const b = busy(t("nv.busy").replace("{v}", esc(ver)));
    const off = API.on("nvidia.progress", (p) => { const [a, w] = span[p.step] || [0, 0]; b.progress(a + w * p.value, t("nv.step." + p.step) + (p.step === "download" ? ` · ${Math.round(p.value * 100)}%` : "")); });
    let r;
    try { r = await call("nvidia.install", opts); } catch { return; } finally { off(); b.done(); }
    if (!r.ok) { toast(t("nv.failed").replace("{e}", r.error || r.exitCode), "error", 9000); return; }
    toast(t("nv.done").replace("{v}", esc(r.version)), "ok", 6000);
    S.nvidia = null;
    S.drivers = null;
    await App.scan(true);
    // a clean install resets the driver profile and GPU device settings: put back what was applied before, plus the panel settings
    const lost = before.filter((id) => S.byId[id] && S.status[id] !== "applied");
    const extra = panel ? nvTweaks.map((tw) => tw.id).filter((id) => S.status[id] !== "applied") : [];
    const ids = [...new Set([...lost, ...extra])];
    if (ids.length) await App.apply(ids, { preconfirmed: new Set(lost), restorePoint: false });
    App.setRebootPending(true);
    await App.restartPrompt("reboot");
  }

  // ------------------------------------------------------------------ [06] app guides

  /** Full-size screenshot over the page; click anywhere or Esc closes it. */
  function zoomShot(src) {
    const o = document.createElement("div");
    o.className = "shot-zoom";
    o.innerHTML = `<img src="${esc(src)}" alt="">`;
    const close = () => { o.remove(); document.removeEventListener("keydown", onKey); };
    const onKey = (e) => { if (e.key === "Escape") close(); };
    o.onclick = close;
    document.addEventListener("keydown", onKey);
    document.body.append(o);
  }

  const autoOf = (g) => (g.autoTweaks || []).filter((id) => S.byId[id] && S.status[id] !== "n/a");
  const pendingOf = (g) => autoOf(g).filter((id) => S.status[id] !== "applied");
  const manualN = (g) => g.sections.reduce((a, s) => a + s.settings.length, 0);

  V.apps = async (opts = {}) => {
    const v = $("#v-apps");
    if (needScan(v)) return;
    if (!S.guides) S.guides = await call("guides.list");
    const g = opts.guide && S.guides.find((x) => x.id === opts.guide);
    if (!g) {
      const installed = S.guides.filter((x) => x.installed);
      const allPending = [...new Set(installed.flatMap(pendingOf))];
      v.innerHTML = html`
        <div class="page-h"><div><h1>${t("apps.title")}</h1><p>${t("apps.desc")}</p></div>
          <div class="actions">${allPending.length ? `<button class="btn primary" id="appsAuto">${t("apps.autoAll")} (${allPending.length})</button>` : `<button class="btn done" disabled>${icon("check")}${t("apps.autoAllDone")}</button>`}</div></div>
        <div class="apps-legend"><span><i class="lg-auto"></i>${t("apps.legendAuto")}</span><span><i class="lg-man"></i>${t("apps.legendManual")}</span></div>
        <div class="cards">${S.guides.map((x) => { const a = autoOf(x), pend = pendingOf(x); return html`<div class="card ${x.installed ? "" : "dim"}" data-g="${x.id}">
          <div class="cn">${brandIcon(x.id, x.name)}${esc(x.name)}</div>
          <div class="cs">${esc(L(x.summary))}</div>
          <div class="cf">${x.installed ? "" : `<span class="chip">${t("apps.notInstalled")}</span>`}
            ${a.length ? `<span class="chip ${pend.length ? "info" : "ok"}">${t("apps.autoN")} ${a.length - pend.length}/${a.length}</span>` : ""}
            <span class="chip">${t("apps.manualN")} ${manualN(x)}</span></div>
        </div>`; }).join("")}</div>`;
      v.querySelectorAll("[data-g]").forEach((c) => (c.onclick = () => V.apps({ guide: c.dataset.g })));
      if ($("#appsAuto")) $("#appsAuto").onclick = () => App.apply(allPending);
      return;
    }
    const auto = autoOf(g), pend = pendingOf(g);
    v.innerHTML = html`
      <div class="page-h">${brandIcon(g.id, g.name, "lg")}<div><h1>${esc(g.name)}</h1><p>${esc(L(g.summary))}</p></div>
        <div class="actions">${!g.installed && (S.init.programIds || []).includes(g.id) ? `<button class="btn primary sm" id="gInstall">${icon("download")}${t("apps.install")}</button>` : ""}<button class="btn ghost sm" id="gBack">${icon("arrow", "flip")}${t("apps.back")}</button></div></div>
      ${auto.length ? html`<div class="app-block auto">
        <div class="ab-h">${icon("tune")}<div><b>${t("apps.autoT")}</b><span>${t("apps.autoD")}</span></div>
          ${pend.length ? `<button class="btn primary sm" id="gAuto">${t("apps.auto")} (${pend.length})</button>` : `<span class="chip ok">${icon("check")}${t("apps.autoDone")}</span>`}</div>
        ${auto.map((id) => { const tw = S.byId[id]; return html`<div class="ab-row"><div><div class="tt">${esc(L(tw.name))}</div><div class="td">${esc(L(tw.description))}</div></div>${statusChip(id)}</div>`; }).join("")}
      </div>` : ""}
      <div class="app-block man">
        <div class="ab-h">${icon("manual")}<div><b>${t("apps.manualT")}</b><span>${t(auto.length ? "apps.manualD" : "apps.manualD0").replaceAll("{app}", esc(g.name))}</span></div></div>
      </div>
      ${g.sections.map((s, si) => html`<div class="guide-sec ${(s.images || []).length ? "" : "noimg"}">
        <div class="gs-l"><h3><span class="gs-n">${String(si + 1).padStart(2, "0")}</span>${esc(L(s.title))}</h3><div class="path">${esc(s.path)}</div>
          ${s.settings.map((st) => { const vv = String(st.value); const cls = vv === "ON" ? "on" : vv === "OFF" ? "off" : ""; return html`<div class="setting"><div><div class="sn">${esc(st.name)}</div>${st.note ? `<div class="snote">${esc(L(st.note))}</div>` : ""}</div><span class="val ${cls}" title="${esc(vv)}">${esc(vv)}</span></div>`; }).join("")}
          ${s.note ? `<div class="note-box">${esc(L(s.note))}</div>` : ""}</div>
        ${(s.images || []).length ? `<div class="gs-r"><div class="gs-shots">${s.images.map((im) => `<button class="gs-shot" data-shot="guides/${esc(im)}" title="${t("apps.zoom")}"><img src="guides/${esc(im)}" alt="${esc(L(s.title))}" loading="lazy"></button>`).join("")}</div></div>` : ""}
      </div>`).join("")}`;
    v.querySelectorAll("[data-shot]").forEach((b) => (b.onclick = () => zoomShot(b.dataset.shot)));
    $("#gBack").onclick = () => V.apps();
    if ($("#gInstall")) $("#gInstall").onclick = () => go("install", { focus: g.id });
    if ($("#gAuto")) $("#gAuto").onclick = () => App.apply(pend);
  };

  // ------------------------------------------------------------------ [09] image (own AI Tweaker image + USB)

  const usb = { iso: "", built: null, source: "built", disk: null, disks: null, lang: "Russian", typed: "", opt: { userName: "User", password: "", computerName: "", timeZone: "Russian Standard Time", locale: "ru-RU", bypass: true, copyTweaker: true, runAfterSetup: true, disableBitLocker: true } };
  // Without a USB stick the English Windows 11 is offered; files and apps can only be kept with an image in the system's own language.
  const rein = { source: "dl", mode: "clean", lang: "English" };
  const img = { level: 2, look: true, updates: true, removeApps: true, removeComponents: true, drivers: false, cleanup: true, extraApps: [] };
  const EXTRA = [["Microsoft.WindowsStore", "Microsoft Store"], ["Microsoft.GamingApp", "Xbox"], ["Microsoft.XboxGamingOverlay", "Xbox Game Bar"], ["Microsoft.YourPhone", "Phone Link"], ["Microsoft.OutlookForWindows", "Outlook"]];
  const TZ = [["Kaliningrad Standard Time", "UTC+2 Калининград"], ["Russian Standard Time", "UTC+3 Москва"], ["Belarus Standard Time", "UTC+3 Минск"], ["FLE Standard Time", "UTC+2 Киев"], ["Samara Standard Time", "UTC+4 Самара"], ["Ekaterinburg Standard Time", "UTC+5 Екатеринбург"], ["West Asia Standard Time", "UTC+5 Ташкент"], ["Omsk Standard Time", "UTC+6 Омск"], ["Central Asia Standard Time", "UTC+6 Астана"], ["N. Central Asia Standard Time", "UTC+7 Новосибирск"], ["North Asia Standard Time", "UTC+7 Красноярск"], ["North Asia East Standard Time", "UTC+8 Иркутск"], ["Yakutsk Standard Time", "UTC+9 Якутск"], ["Vladivostok Standard Time", "UTC+10 Владивосток"], ["Magadan Standard Time", "UTC+11 Магадан"], ["Russia Time Zone 11", "UTC+12 Камчатка"], ["GMT Standard Time", "UTC+0 London"], ["W. Europe Standard Time", "UTC+1 Berlin"], ["Eastern Standard Time", "UTC−5 New York"], ["Pacific Standard Time", "UTC−8 Los Angeles"]];
  const imgOptions = () => ({ level: img.level, look: img.look, updates: img.updates, removeApps: img.removeApps, removeComponents: img.removeComponents, drivers: img.drivers, cleanup: img.cleanup, extraApps: img.extraApps });

  V.image = async () => {
    const v = $("#v-image");
    if (usb.disks === null) {
      usb.disks = [];
      call("usb.list", {}, { silent: true }).then((d) => { usb.disks = d; if (S.view === "image") V.image(); }).catch(() => {});
      call("image.existing", {}, { silent: true }).then((f) => { if (f) { usb.built = usb.built || { folder: f }; if (S.view === "image") V.image(); } }).catch(() => {});
    }
    const o = usb.opt;
    const src = usb.source === "built" && usb.built ? usb.built.folder : usb.iso;
    v.innerHTML = html`
      <div class="page-h"><div><h1>${t("image.title")}</h1><p>${t("image.desc")}</p></div></div>
      ${reinstallPanel()}
      <div class="grid cols-2" style="align-items:start">
        <div class="panel"><div class="panel-h">${t("ib.title")}</div><div class="panel-b steps">
          <div class="callout">${t("ib.desc")}</div>
          <div class="step"><h4>${t("usb.iso")}</h4>
            <div style="display:flex;gap:8px;flex-wrap:wrap"><button class="btn sm" id="isoPick">${t("usb.pick")}</button>
              <select class="input" id="isoLang" style="width:auto">${["Russian", "English", "English International", "Ukrainian", "Kazakh"].map((l) => `<option ${usb.lang === l ? "selected" : ""}>${l}</option>`).join("")}</select>
              <button class="btn sm" id="isoDl">${icon("download")}${t("usb.download")}</button></div>
            <div class="tid" style="margin-top:8px;word-break:break-all">${esc(usb.iso || "—")}</div>
            <div class="td" style="margin-top:6px;color:var(--muted)">${t("usb.fidoNote")}</div></div>
          <div class="step"><h4>${t("ib.contents")}</h4>
            <label class="field">${t("ib.level")}<select class="input" id="ibLevel">${[1, 2, 3].map((l) => `<option value="${l}" ${img.level === l ? "selected" : ""}>${t("prof." + ["Safe", "Balanced", "Performance"][l - 1])}</option>`).join("")}</select></label>
            <div style="display:grid;gap:8px;margin-top:10px">
              ${[["look", "ib.look"], ["updates", "ib.updates"], ["removeApps", "ib.apps"], ["removeComponents", "ib.components"], ["cleanup", "ib.cleanup"], ["drivers", "ib.drivers"]].map(([k, l]) => `<label class="check"><input type="checkbox" data-ib="${k}" ${img[k] ? "checked" : ""}><span class="box"></span>${t(l)}</label>`).join("")}</div>
            ${img.look ? `<div class="look-thumbs"><figure><div class="swatch-black"></div><figcaption>${t("look.black")}</figcaption></figure>${["graphite", "lock"].map((n) => `<figure><img src="img/aitweaker-${n}.webp" alt="" loading="lazy"><figcaption>${t("look." + n)}</figcaption></figure>`).join("")}</div>` : ""}
            <div class="cat-h" style="margin-top:12px;font-size:11px">${t("ib.extra")}</div>
            <div style="display:grid;gap:6px;margin-top:8px">${EXTRA.map(([pkg, n]) => `<label class="check"><input type="checkbox" data-extra="${pkg}" ${img.extraApps.includes(pkg) ? "checked" : ""}><span class="box"></span>${n} <span class="chip warn" style="margin-left:4px">${t("debloat.important")}</span></label>`).join("")}</div>
            <div class="td" style="margin-top:8px;color:var(--muted)">${t("ib.extraNote")}</div></div>
          <div class="step"><h4>${t("ib.build")}</h4>
            <div style="display:flex;gap:8px;flex-wrap:wrap"><button class="btn sm ghost" id="ibPreview">${t("ib.preview")}</button><button class="btn hazard" id="ibBuild" ${usb.iso ? "" : "disabled"}><span>${t("ib.build")}</span></button></div>
            ${usb.built ? `<div class="callout ok" style="margin-top:10px">${icon("check")}${t("ib.ready")}: <span class="tid">${esc(usb.built.folder)}</span>${usb.built.tweaks != null ? `<br>${usb.built.version ? `Windows 11 ${esc(usb.built.version)} · build ${esc(usb.built.build)}${usb.built.update ? " · " + esc(usb.built.update) : ""}<br>` : ""}${usb.built.tweaks} tweaks · ${usb.built.apps} apps · install.wim ${(usb.built.wimBytes / 1073741824).toFixed(2)} GB` : ""}</div>` : ""}
            <div class="td" style="margin-top:8px;color:var(--muted)">${t("ib.note")}</div></div>
        </div></div>

        <div class="panel"><div class="panel-h">${t("usb.title")}</div><div class="panel-b steps">
          <div class="step"><h4>${t("usb.source")}</h4>
            <label class="check" style="display:flex;margin:4px 0"><input type="radio" name="usbsrc" value="built" ${usb.source === "built" ? "checked" : ""} ${usb.built ? "" : "disabled"}><span class="box"></span>${t("usb.srcBuilt")}</label>
            <label class="check" style="display:flex;margin:4px 0"><input type="radio" name="usbsrc" value="iso" ${usb.source === "iso" || !usb.built ? "checked" : ""}><span class="box"></span>${t("usb.srcIso")}</label>
            <div class="tid" style="word-break:break-all">${esc(src || "—")}</div></div>
          <div class="step"><h4>${t("usb.disk")}</h4>
            ${usb.disks.length ? usb.disks.map((d) => `<label class="check" style="display:flex;margin:4px 0"><input type="radio" name="disk" value="${d.number}" ${usb.disk === d.number ? "checked" : ""}><span class="box"></span>#${d.number} · ${esc(d.name)} · ${(d.sizeBytes / 1e9).toFixed(1)} GB</label>`).join("") : `<div class="td">${t("usb.noDisk")}</div>`}
            <button class="btn sm ghost" id="diskRefresh" style="margin-top:6px">${t("usb.refresh")}</button></div>
          <div class="step"><h4>${t("usb.options")}</h4><div class="grid cols-2" style="gap:10px">
            <label class="field">${t("usb.user")}<input class="input" data-o="userName" value="${esc(o.userName)}"></label>
            <label class="field">${t("usb.pass")}<input class="input" type="password" data-o="password" value="${esc(o.password)}"></label>
            <label class="field">${t("usb.pc")}<input class="input" data-o="computerName" value="${esc(o.computerName)}"></label>
            <label class="field">${t("usb.tz")}<select class="input" data-o="timeZone">${TZ.map(([id, n]) => `<option value="${id}" ${o.timeZone === id ? "selected" : ""}>${n}</option>`).join("")}</select></label>
            <label class="field">${t("usb.locale")}<select class="input" data-o="locale">${["ru-RU", "en-US", "uk-UA", "kk-KZ", "be-BY"].map((l) => `<option ${o.locale === l ? "selected" : ""}>${l}</option>`).join("")}</select></label></div>
            <div style="display:grid;gap:8px;margin-top:10px">
              ${[["bypass", "usb.bypass"], ["copyTweaker", "usb.copy"], ["runAfterSetup", "usb.runAfter"], ["disableBitLocker", "usb.bitlocker"]].map(([k, l]) => `<label class="check"><input type="checkbox" data-b="${k}" ${o[k] ? "checked" : ""}><span class="box"></span>${t(l)}</label>`).join("")}</div>
            <div class="callout" style="margin-top:10px">${t("usb.note")}</div></div>
          <div class="step"><h4>${t("usb.create")}</h4><div style="display:flex;gap:8px;flex-wrap:wrap"><button class="btn sm ghost" id="xmlPrev">${t("usb.preview")}</button><button class="btn hazard" id="usbGo" ${src && usb.disk !== null ? "" : "disabled"}><span>${t("usb.create")}</span></button></div></div>
        </div></div>
      </div>

      <div class="panel" style="margin-top:16px"><div class="panel-h">${t("rufus.title")}<span class="right"><button class="btn sm ghost" id="rufusSite">${icon("external")}rufus.ie</button></span></div>
        <div class="panel-b" style="display:grid;gap:12px">
          <div class="td" style="color:var(--muted)">${t("rufus.desc")}</div>
          <div style="display:flex;gap:8px;flex-wrap:wrap;align-items:center"><button class="btn primary" id="rufusOpen">${t("rufus.open")}</button><button class="btn" id="rufusCopy">${t("rufus.copy")}</button>
            <span class="tid" style="word-break:break-all">${usb.iso ? "ISO: " + esc(usb.iso) : t("rufus.noIso")}</span></div>
        </div>
      </div>

      <div class="panel" style="margin-top:16px"><div class="panel-h">${t("ame.title")}</div>
        <div class="panel-b" style="display:grid;gap:12px">
          <div class="td" style="color:var(--muted)">${t("ame.desc")}</div>
          <ol class="ame-steps"><li>${t("ame.s1")}</li><li>${t("ame.s2")}</li><li>${t("ame.s3")}</li></ol>
          <div style="display:flex;gap:8px;flex-wrap:wrap"><button class="btn primary" id="ameSave">${icon("download")}${t("ame.save")}</button><button class="btn ghost" id="ameGet">${icon("external")}${t("ame.get")}</button></div>
          ${usb.ame ? html`<div class="callout ${usb.ame.protected ? "ok" : "warn"}" style="display:flex;gap:12px;align-items:center;justify-content:space-between;flex-wrap:wrap">
            <span>${usb.ame.protected ? icon("check") : ""}${t("ame.saved")}: <span class="tid">${esc(usb.ame.path)}</span>${usb.ame.protected ? "" : `<br>${t("ame.plain")}`}</span>
            <button class="btn sm" id="ameShow">${t("ame.show")}</button></div>` : ""}
        </div>
      </div>`;

    $("#isoPick").onclick = async () => { const p = await call("usb.pickIso"); if (p) { usb.iso = p; V.image(); } };
    $("#rufusSite").onclick = () => call("shell.open", { url: "https://rufus.ie/" });
    $("#rufusOpen").onclick = async () => {
      const b = busy(t("rufus.opening"));
      try { const r = await call("usb.rufus", { iso: usb.iso || "" }); toast(r.downloaded ? t("rufus.downloaded") : t("rufus.opened"), "ok"); } catch { /* toast */ } finally { b.done(); }
    };
    $("#rufusCopy").onclick = async () => {
      try { const d = await call("usb.copyTweaker"); toast(d.length ? `${t("rufus.copied")}: ${d.join(", ")}` : t("rufus.noStick"), d.length ? "ok" : "warn", 6000); } catch { /* toast */ }
    };
    $("#ameGet").onclick = () => call("shell.open", { url: "https://ameliorated.io/" });
    $("#ameSave").onclick = async () => {
      const b = busy(t("ame.save"));
      try { usb.ame = await call("image.ame"); toast(t("ame.saved"), "ok"); } catch { /* toast */ } finally { b.done(); }
      V.image();
    };
    if ($("#ameShow")) $("#ameShow").onclick = () => call("shell.reveal", { path: usb.ame.path });
    $("#isoLang").onchange = (e) => (usb.lang = e.target.value);
    $("#isoDl").onclick = async () => {
      const b = busy(t("usb.download"));
      const off = API.on("usb.progress", (p) => b.progress(p.value, `${p.step} · ${Math.round(p.value * 100)}%`));
      try { usb.iso = await call("usb.downloadIso", { language: usb.lang }); } catch { /* toast shown */ } finally { off(); b.done(); }
      V.image();
    };
    $("#ibLevel").onchange = (e) => (img.level = +e.target.value);
    v.querySelectorAll("[data-ib]").forEach((i) => (i.onchange = () => { img[i.dataset.ib] = i.checked; if (i.dataset.ib === "look") V.image(); }));
    v.querySelectorAll("[data-extra]").forEach((i) => (i.onchange = async () => {
      if (i.checked) {
        const a = (window.MOCK_DATA ? MOCK_DATA.apps : []).find((x) => x.package === i.dataset.extra);
        const ok = await confirmBox(t("confirm.title"), a && a.warning ? esc(L(a.warning)) : esc(i.dataset.extra), t("confirm.remove"), "danger", true);
        if (!ok) { i.checked = false; return; }
        img.extraApps.push(i.dataset.extra);
      } else img.extraApps = img.extraApps.filter((x) => x !== i.dataset.extra);
    }));
    $("#ibPreview").onclick = async () => {
      const pv = await call("image.preview", { options: imgOptions() });
      modal({
        title: t("ib.preview"), wide: true,
        body: html`<div class="kv"><b>${t("ib.pvTweaks")}</b><span>${pv.tweaks.length} · ${pv.registryValues} registry · ${pv.services.length} services</span><b>${t("ib.pvApps")}</b><span>${pv.apps.length}</span><b>${t("ib.pvComponents")}</b><span>${pv.capabilities.length}</span><b>${t("ib.pvAfter")}</b><span>${pv.afterInstall}</span></div>
          <div class="pre">${esc(pv.tweaks.map((x) => `+ ${L(x.name)}  [${x.id}]`).join("\n"))}\n\n${esc(pv.apps.map((x) => `− ${x}`).join("\n"))}\n\n${esc(pv.capabilities.map((x) => `− ${x}*`).join("\n"))}</div>`,
        buttons: [{ label: t("close"), cls: "ghost" }],
      });
    };
    $("#ibBuild").onclick = async () => {
      if (!(await confirmBox(t("ib.build"), t("ib.buildQ"), t("ib.build")))) return;
      const b = busy(t("busy.image"));
      const off = API.on("image.progress", (p) => b.progress(p.value, `${t("ib.step." + p.step)} · ${Math.round(p.value * 100)}%`));
      try { usb.built = await call("image.build", { iso: usb.iso, options: imgOptions() }); usb.source = "built"; toast(t("ib.ready"), "ok"); } catch { /* toast */ } finally { off(); b.done(); }
      V.image();
    };
    v.querySelectorAll("input[name=usbsrc]").forEach((r) => (r.onchange = () => { usb.source = r.value; V.image(); }));
    v.querySelectorAll("input[name=disk]").forEach((r) => (r.onchange = () => { usb.disk = +r.value; V.image(); }));
    $("#diskRefresh").onclick = async () => { usb.disks = await call("usb.list"); V.image(); };
    v.querySelectorAll("[data-o]").forEach((i) => (i.onchange = () => (o[i.dataset.o] = i.value)));
    v.querySelectorAll("[data-b]").forEach((i) => (i.onchange = () => (o[i.dataset.b] = i.checked)));
    $("#xmlPrev").onclick = async () => { const xml = await call("usb.preview", { options: o }); modal({ title: "autounattend.xml", wide: true, body: `<div class="pre">${esc(xml)}</div>`, buttons: [{ label: t("close"), cls: "ghost" }] }); };
    $("#usbGo").onclick = () => createUsb(src);
    v.querySelectorAll("input[name=reinsrc]").forEach((r) => (r.onchange = () => { rein.source = r.value; V.image(); }));
    v.querySelectorAll("input[name=reinmode]").forEach((r) => (r.onchange = () => { rein.mode = r.value; V.image(); }));
    $("#reinGo").onclick = startReinstall;
    $("#reinUsb").onclick = () => $(".rein-usb-anchor").scrollIntoView({ behavior: "smooth", block: "start" });
  };

  // ------------------------------------------------------------------ reinstall without a USB stick

  function reinstallPanel() {
    if (rein.source === "built" && !usb.built) rein.source = "dl";
    if (rein.source === "iso" && !usb.iso) rein.source = "dl";
    const osLang = (S.scan && S.scan.profile.os.language) || "";
    const keepBlocked = rein.source === "dl" && osLang && !/^en/i.test(osLang);
    if (keepBlocked && rein.mode !== "clean") rein.mode = "clean";
    const opt = (name, value, label, desc, on, dis) => `<label class="rein-opt ${on ? "on" : ""} ${dis ? "dis" : ""}"><input type="radio" name="${name}" value="${value}" ${on ? "checked" : ""} ${dis ? "disabled" : ""}><span><b>${label}</b>${desc ? `<span>${desc}</span>` : ""}</span></label>`;
    return html`<div class="panel rein" style="margin-bottom:30px">
      <div class="panel-h">${t("rein.title")}<span class="right"><button class="btn sm ghost" id="reinUsb">${t("rein.usbBtn")}${icon("arrow")}</button></span></div>
      <div class="panel-b">
        <div class="td" style="color:var(--muted);margin-bottom:16px;max-width:900px">${t("rein.desc")}</div>
        <div class="grid cols-2" style="gap:22px">
          <div><div class="rein-k">${t("rein.src")}</div><div class="rein-list">
            ${opt("reinsrc", "dl", t("rein.srcDl"), t("rein.srcDlD"), rein.source === "dl")}
            ${opt("reinsrc", "built", t("rein.srcBuilt"), usb.built ? esc(usb.built.folder) : t("rein.srcBuiltNo"), rein.source === "built", !usb.built)}
            ${opt("reinsrc", "iso", t("rein.srcIso"), usb.iso ? esc(usb.iso) : t("rein.srcIsoNo"), rein.source === "iso", !usb.iso)}
          </div></div>
          <div><div class="rein-k">${t("rein.mode")}</div><div class="rein-list">
            ${["clean", "dataonly", "upgrade"].map((m) => opt("reinmode", m, t("rein.m." + m), m !== "clean" && keepBlocked ? t("rein.langNote").replace("{lang}", esc(osLang)) : t("rein.m." + m + "D"), rein.mode === m, m !== "clean" && keepBlocked)).join("")}
          </div></div>
        </div>
        <div style="display:flex;gap:14px;align-items:center;flex-wrap:wrap;margin-top:18px">
          <button class="btn hazard" id="reinGo"><span>${t("rein.start")}</span></button>
          <span class="td" style="color:var(--muted);max-width:720px">${t("rein.note")}</span>
        </div>
      </div></div>
      <div class="rein-usb-anchor"></div>`;
  }

  async function startReinstall() {
    const clean = rein.mode === "clean";
    let typed = "";
    const res = await modal({
      title: t("rein.confirmT"), hazard: true,
      body: html`<div class="confirm-title">${t("rein.m." + rein.mode)}</div><div>${t(clean ? "rein.confirmClean" : "rein.confirm")}</div>
        ${clean ? `<div>${t("rein.typeYes")}</div><input class="input" id="reinTyped" autocomplete="off">` : ""}`,
      buttons: [{ label: t("cancel"), value: false, cls: "ghost" }, { label: t("rein.start"), value: true, cls: clean ? "danger" : "primary" }],
      onOpen: (ov) => { const i = $("#reinTyped", ov); if (i) { i.focus(); ov.querySelector("[data-i='1']").addEventListener("click", () => (typed = i.value), { capture: true }); } },
    });
    if (res !== true) return;
    if (clean && typed.trim().toUpperCase() !== t("rein.yes").toUpperCase()) { toast(t("rein.notTyped"), "warn"); return; }
    const b = busy(t("rein.busy"));
    const off = [API.on("reinstall.progress", (p) => b.progress(p.step === "iso" ? 0.05 + p.value * 0.8 : { fido: 0.03, mount: 0.88, check: 0.92, setup: 0.97 }[p.step] || 0.5, t("rein.step." + p.step) + (p.step === "iso" ? ` · ${Math.round(p.value * 100)}%` : ""))),
      API.on("usb.progress", (p) => b.progress(p.step === "iso" ? 0.05 + p.value * 0.8 : 0.03, t("rein.step." + p.step) + (p.step === "iso" ? ` · ${Math.round(p.value * 100)}%` : "")))];
    let r = null;
    try { r = await call("reinstall.start", { source: rein.source, iso: usb.iso, mode: rein.mode, language: rein.lang }); } catch { /* toast */ } finally { off.forEach((f) => f()); b.done(); }
    if (!r) return;
    await modal({ title: t("rein.startedT"), body: html`<div>${t("rein.started")}</div><div class="callout">${t("rein.after")}</div><div class="tid">${esc(r.setup)} ${esc(r.arguments)}</div>`, buttons: [{ label: t("ok"), cls: "primary" }] });
  }

  async function createUsb(src) {
    const d = usb.disks.find((x) => x.number === usb.disk);
    if (!d || !src) return;
    const typed = await modal({
      title: t("usb.create"), hazard: true,
      body: html`<div><b>#${d.number} · ${esc(d.name)} · ${(d.sizeBytes / 1e9).toFixed(1)} GB</b></div><div>${t("usb.confirm")}</div><input class="input" id="typed" autocomplete="off">`,
      buttons: [{ label: t("cancel"), value: null, cls: "ghost" }, { label: t("usb.create"), value: "go", cls: "danger" }],
      onOpen: (ov) => { const i = $("#typed", ov); i.focus(); ov.querySelector("[data-i='1']").addEventListener("click", () => (usb.typed = i.value), { capture: true }); },
    });
    if (typed !== "go" || String(usb.typed).trim() !== String(d.number)) return;
    const b = busy(t("busy.usb"));
    const off = API.on("usb.progress", (p) => b.progress(p.value, `${p.step} · ${Math.round(p.value * 100)}%`));
    try { await call("usb.create", { disk: d.number, iso: src, options: usb.opt }); toast(t("usb.done"), "ok"); } catch { /* toast */ } finally { off(); b.done(); }
  }

  // ------------------------------------------------------------------ [10] backup

  V.backup = async () => {
    const v = $("#v-backup");
    S.backups = await call("backup.list");
    const b = S.backups;
    v.innerHTML = html`
      <div class="page-h"><div><h1>${t("backup.title")}</h1><p>${t("backup.desc")}</p></div>
        <div class="actions"><button class="btn sm" id="rpNow">${t("backup.rp")}</button><button class="btn sm" id="rstrui">${t("backup.rstrui")}${icon("external")}</button><button class="btn sm ghost" id="bkFolder">${t("backup.folder")}</button><button class="btn sm ghost" id="logFolder">${t("backup.logs")}</button></div></div>
      <div class="grid cols-2" style="align-items:start">
        <div class="panel"><div class="panel-h">${t("backup.applied")}<span class="right">${b.applied.length ? `<button class="btn sm danger" id="revertAll">${t("backup.revertAll")}</button>` : ""}</span></div><div class="panel-b flush">
          ${b.applied.length ? b.applied.map((x) => `<div class="trow" style="grid-template-columns:1fr auto;padding:9px 12px"><div><div class="tt">${esc(L(x.name) || x.tweakId)}</div><div class="tm"><span class="tid">${esc(x.tweakId)} · ${fmtDate(x.appliedAt)} ${fmtTime(x.appliedAt)} · ${x.changes} changes</span></div></div><button class="btn sm danger" data-rv="${esc(x.tweakId)}">${t("revert")}</button></div>`).join("") : `<div class="empty" style="margin:12px">${t("backup.none")}</div>`}
        </div></div>
        <div class="panel"><div class="panel-h">${t("backup.sessions")}</div><div class="panel-b flush">
          ${b.sessions.length ? b.sessions.map((x) => `<div class="setting" style="padding:8px 12px"><div><div class="sn"><b>${x.kind.toUpperCase()}</b> ${esc(x.profile || "")} · ${fmtDate(x.time)} ${fmtTime(x.time)}</div><div class="snote">${x.applied.length} ok · ${x.failed.length} failed${x.restorePoint ? " · restore point" : ""}</div></div><span class="val ${x.failed.length ? "" : "on"}">${x.failed.length ? t("bk.errors") : t("bk.ok")}</span></div>`).join("") : `<div class="empty" style="margin:12px">—</div>`}
        </div></div>
      </div>`;
    v.querySelectorAll("[data-rv]").forEach((x) => (x.onclick = () => App.revert([x.dataset.rv])));
    if ($("#revertAll")) $("#revertAll").onclick = async () => { if (await confirmBox(t("backup.revertAll"), `${b.applied.length} tweaks`, t("revert"), "danger", true)) App.revert(b.applied.map((x) => x.tweakId)); };
    $("#rpNow").onclick = async () => { const bb = busy(t("backup.rp")); try { await call("restore.create"); toast(t("toast.done"), "ok"); } finally { bb.done(); } };
    $("#rstrui").onclick = () => call("restore.open");
    $("#bkFolder").onclick = () => call("shell.folder", { name: "backups" });
    $("#logFolder").onclick = () => call("shell.folder", { name: "logs" });
  };
})();
