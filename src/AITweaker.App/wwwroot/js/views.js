/* Views 02–10. */
(function () {
  const V = (window.Views ||= {});
  const PROFILES = ["Safe", "Balanced", "Performance", "Extreme"];
  const catName = (id) => L((S.init.categories.find((c) => c.id === id) || {}).name) || id;
  const needScan = (el) => { if (S.scan) return false; el.innerHTML = `<div class="empty">${t("scanFirst")}<br><br><button class="btn primary" onclick="App.scan()">${t("radar.scan")}</button></div>`; return true; };
  const statusChip = (id) => { const st = S.status[id] || "n/a"; return `<span class="status ${st}">${t(st === "n/a" ? "st.na" : "st." + st)}</span>`; };

  // ------------------------------------------------------------------ [02] auto-tune

  V.tune = () => {
    const v = $("#v-tune");
    if (needScan(v)) return;
    const items = S.plan.items;
    const groups = S.init.categories.map((c) => ({ c, list: items.filter((i) => S.byId[i.id] && S.byId[i.id].category === c.id) })).filter((g) => g.list.length);
    const sel = [...S.selected].filter((id) => items.some((i) => i.id === id));
    const confirmN = sel.filter((id) => { const tw = S.byId[id]; return tw && (tw.confirm || tw.risk === "high"); }).length;
    const counts = Object.fromEntries(PROFILES.map((p, i) => [p, items.filter((x) => { const tw = S.byId[x.id]; return tw.level > 0 && tw.level <= i + 1; }).length]));

    v.innerHTML = html`
      <div class="page-h"><div><span class="code">SEC-02 // AUTO-TUNE</span><h1>${t("tune.title")}</h1><p>${t("tune.desc")}</p></div>
        <div class="actions"><button class="btn ghost sm" id="selRec">${t("tune.selectRec")}</button><button class="btn ghost sm" id="selNone">${t("tune.selectNone")}</button></div></div>
      <div class="profiles">${PROFILES.map((p, i) => `<div class="prof ${S.profileName === p ? "on" : ""}" data-p="${p}"><div class="pc">L${i + 1}</div><div class="pn">${t("prof." + p)}</div><div class="pd">${t("prof." + p + ".d")}</div><div class="pk">${counts[p]} tweaks</div></div>`).join("")}</div>
      ${groups.map(({ c, list }) => html`<div class="cat-group">
        <div class="cat-h">${esc(L(c.name))}<span class="n">${list.filter((i) => S.selected.has(i.id)).length}/${list.length}</span></div>
        ${list.map((i) => { const tw = S.byId[i.id]; return html`<div class="trow">
          <label class="check"><input type="checkbox" data-id="${tw.id}" ${S.selected.has(tw.id) ? "checked" : ""}><span class="box"></span></label>
          <div><div class="tt">${esc(L(tw.name))} ${i.needsConfirm ? '<span class="chip crit">⚠ CONFIRM</span>' : ""}</div>
            <div class="td">${esc(L(tw.description))}</div>
            <div class="why">▸ ${esc(L(i.reason))}</div>
            <div class="tm">${badges(tw)}<span class="tid">${tw.id}</span></div></div>
          <div class="tr">${statusChip(tw.id)}</div></div>`; }).join("")}
      </div>`).join("")}
      <div class="sticky-apply">
        <button class="btn hazard" id="applyPlan" ${sel.length ? "" : "disabled"}><span>▶ ${t("tune.apply")}</span></button>
        <div class="sum"><b>${sel.length}</b> ${t("tune.selected")} · <b>${confirmN}</b> ${t("tune.confirmCount")}</div>
        <label class="check" style="margin-left:auto"><input type="checkbox" id="rpCheck" ${S.init.settings.createRestorePoint ? "checked" : ""}><span class="box"></span>${t("tune.rp")}</label>
      </div>`;

    v.querySelectorAll(".prof").forEach((el) => (el.onclick = () => App.setProfile(el.dataset.p)));
    v.querySelectorAll("input[data-id]").forEach((cb) => (cb.onchange = () => { cb.checked ? S.selected.add(cb.dataset.id) : S.selected.delete(cb.dataset.id); V.tune(); }));
    $("#selRec").onclick = () => { S.selected = new Set(items.filter((i) => i.recommended).map((i) => i.id)); V.tune(); };
    $("#selNone").onclick = () => { S.selected.clear(); V.tune(); };
    $("#applyPlan").onclick = () => App.apply([...S.selected], { restorePoint: $("#rpCheck").checked });
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
      <div class="page-h"><div><span class="code">SEC-03 // CATALOG</span><h1>${t("tweaks.title")}</h1><p>${t("tweaks.desc")}</p></div>
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
        ${tw.confirmText ? `<div class="td" style="color:var(--crit)">⚠ ${esc(L(tw.confirmText))}</div>` : ""}
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
        ${item ? `<div class="why" style="color:var(--accent)">▸ ${esc(L(item.reason))}</div>` : ""}
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
      <div class="page-h"><div><span class="code">SEC-04 // DEBLOAT</span><h1>${t("debloat.title")}</h1><p>${t("debloat.desc")}</p></div>
        <div class="actions"><button class="btn ghost sm" id="appxReload">⟳</button><button class="btn danger" id="appxRemove">✕ ${t("debloat.remove")} (${removeSel.size})</button></div></div>
      ${groups.map((g) => { const list = S.appx.packages.filter((a) => a.group === g); if (!list.length) return ""; return html`<div class="cat-group"><div class="cat-h">${t("grp." + g)}<span class="n">${list.length}</span></div>
        ${list.map((a) => html`<div class="trow">
          <label class="check"><input type="checkbox" data-pkg="${a.package}" ${removeSel.has(a.package) ? "checked" : ""} ${a.keep ? "disabled" : ""}><span class="box"></span></label>
          <div><div class="tt">${esc(L(a.name))} ${a.important ? `<span class="chip crit">⚠ ${t("debloat.important")}</span>` : ""} ${a.keep ? `<span class="chip ok">🔒 ${t("debloat.keep")}</span>` : ""}</div>
            ${a.warning ? `<div class="td">${esc(L(a.warning))}</div>` : ""}${a.keep && a.keepReason ? `<div class="why">▸ ${esc(L(a.keepReason))}</div>` : ""}
            <div class="tm"><span class="tid">${esc(a.package)}</span></div></div><div></div></div>`).join("")}</div>`; }).join("")}
      ${S.appx.others.length ? html`<details class="cat-group"><summary class="cat-h" style="cursor:pointer">${t("debloat.others")}<span class="n">${S.appx.others.length}</span></summary>
        ${S.appx.others.map((o) => `<div class="trow"><label class="check"><input type="checkbox" data-pkg="${esc(o.name)}" data-other="1" ${removeSel.has(o.name) ? "checked" : ""}><span class="box"></span></label><div><div class="tt">${esc(o.name)}</div><div class="tm"><span class="tid">${esc(o.version)}</span></div></div><div></div></div>`).join("")}</details>` : ""}`;

    v.querySelectorAll("input[data-pkg]").forEach((cb) => (cb.onchange = () => { cb.checked ? removeSel.add(cb.dataset.pkg) : removeSel.delete(cb.dataset.pkg); $("#appxRemove").textContent = `✕ ${t("debloat.remove")} (${removeSel.size})`; }));
    $("#appxReload").onclick = () => { S.appx = null; V.debloat(); };
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
      toast(`${okN}/${accepted.length} ✓`, okN === accepted.length ? "ok" : "warn");
    } catch { /* toast */ } finally { off(); b.done(); }
    S.appx = null;
    V.debloat();
  }

  // ------------------------------------------------------------------ [05] drivers

  V.drivers = async () => {
    const v = $("#v-drivers");
    if (needScan(v)) return;
    if (!S.drivers) { v.innerHTML = `<div class="empty">…</div>`; S.drivers = await call("drivers.list"); }
    v.innerHTML = html`
      <div class="page-h"><div><span class="code">SEC-06 // DRIVERS</span><h1>${t("drivers.title")}</h1><p>${t("drivers.desc")}</p></div>
        <div class="actions"><button class="btn sm" id="drvRescan">⟳ ${t("drivers.rescan")}</button><button class="btn sm" id="drvOptional">${t("drivers.optional")} ↗</button></div></div>
      <div class="cards">${S.drivers.map((d) => html`<div class="card" style="cursor:default">
        <div class="cn"><span class="app-ico">${d.category === "gpu" ? "GPU" : d.category === "chipset" ? "CHP" : d.category === "network" ? "NET" : "!"}</span>${t("drv." + d.category)}</div>
        <div style="font-weight:700">${esc(d.name)}</div>
        <div class="cs">${esc(d.vendor || "")}${d.version ? " · " + esc(d.version) : ""}${d.date ? " · " + fmtDate(d.date) : ""}</div>
        <div class="cf"><span class="chip ${d.status === "ok" ? "ok" : d.status === "check" ? "info" : "crit"}">${t("drv." + d.status)}</span></div>
        <div class="cf">${d.links.map((l) => `<button class="btn sm" data-url="${esc(l.url)}">${esc(l.title)} ↗</button>`).join("")}</div>
      </div>`).join("")}</div>`;
    v.querySelectorAll("[data-url]").forEach((b) => (b.onclick = () => call("shell.open", { url: b.dataset.url })));
    $("#drvRescan").onclick = async () => { await call("drivers.rescan"); S.drivers = null; await App.scan(true); V.drivers(); };
    $("#drvOptional").onclick = () => call("shell.open", { url: "ms-settings:windowsupdate-optionalupdates" });
  };

  // ------------------------------------------------------------------ [06] app guides

  V.apps = async (opts = {}) => {
    const v = $("#v-apps");
    if (needScan(v)) return;
    if (!S.guides) S.guides = await call("guides.list");
    const g = opts.guide && S.guides.find((x) => x.id === opts.guide);
    if (!g) {
      v.innerHTML = html`
        <div class="page-h"><div><span class="code">SEC-07 // APPS</span><h1>${t("apps.title")}</h1><p>${t("apps.desc")}</p></div></div>
        <div class="cards">${S.guides.map((x) => html`<div class="card ${x.installed ? "" : "dim"}" data-g="${x.id}">
          <div class="cn"><span class="app-ico">${esc(x.name.slice(0, 2).toUpperCase())}</span>${esc(x.name)}</div>
          <div class="cs">${esc(L(x.summary))}</div>
          <div class="cf"><span class="chip ${x.installed ? "ok" : ""}">${x.installed ? t("apps.installed") : t("apps.notInstalled")}</span><span class="chip ${x.source === "master" ? "accent" : "info"}">${x.source === "master" ? t("apps.master") : t("apps.ai")}</span><span class="chip">${x.sections.reduce((a, s) => a + s.settings.length, 0)} ⚙</span></div>
        </div>`).join("")}</div>`;
      v.querySelectorAll("[data-g]").forEach((c) => (c.onclick = () => V.apps({ guide: c.dataset.g })));
      return;
    }
    const auto = (g.autoTweaks || []).filter((id) => S.byId[id] && S.status[id] !== "applied" && S.status[id] !== "n/a");
    v.innerHTML = html`
      <div class="page-h"><div><span class="code">SEC-07 // ${esc(g.id.toUpperCase())}</span><h1>${esc(g.name)}</h1><p>${esc(L(g.summary))}</p></div>
        <div class="actions"><button class="btn ghost sm" id="gBack">${t("apps.back")}</button>${auto.length ? `<button class="btn primary sm" id="gAuto">⚡ ${t("apps.auto")} (${auto.length})</button>` : ""}</div></div>
      ${g.sections.map((s) => html`<div class="guide-sec">
        <div class="gs-l"><h3>${esc(L(s.title))}</h3><div class="path">${esc(s.path)}</div>
          ${s.settings.map((st) => { const vv = String(st.value); const cls = vv === "ON" ? "on" : vv === "OFF" ? "off" : ""; return html`<div class="setting"><div><div class="sn">${esc(st.name)}</div>${st.note ? `<div class="snote">▸ ${esc(L(st.note))}</div>` : ""}</div><span class="val ${cls}" title="${esc(vv)}">${esc(vv)}</span></div>`; }).join("")}
          ${s.note ? `<div class="note-box">${esc(L(s.note))}</div>` : ""}</div>
        <div class="gs-r">${s.image ? `<img src="guides/${esc(s.image)}" alt="${esc(L(s.title))}" title="${t("apps.zoom")}" loading="lazy">` : `<div class="empty" style="width:100%">AI TWEAKER</div>`}</div>
      </div>`).join("")}`;
    $("#gBack").onclick = () => V.apps();
    if ($("#gAuto")) $("#gAuto").onclick = () => App.apply(auto);
    v.querySelectorAll(".gs-r img").forEach((img) => (img.onclick = () => {
      const ov = document.createElement("div"); ov.className = "overlay lightbox"; ov.innerHTML = `<img src="${img.src}">`; ov.onclick = () => ov.remove(); document.body.append(ov);
    }));
  };

  // ------------------------------------------------------------------ [07] BIOS

  V.bios = async () => {
    const v = $("#v-bios");
    if (needScan(v)) return;
    if (!S.bios) S.bios = await call("bios.list");
    const b = S.bios;
    v.innerHTML = html`
      <div class="page-h"><div><span class="code">SEC-08 // FIRMWARE</span><h1>${t("bios.title")}</h1><p>${t("bios.desc")}</p></div>
        <div class="actions"><button class="btn danger" id="toBios">⏻ ${t("bios.reboot")}</button></div></div>
      <div class="grid cols-3" style="margin-bottom:14px">
        <div class="panel"><div class="panel-h"><span class="code">MB</span>${esc(b.board.manufacturer)}</div><div class="panel-b kv"><b>MODEL</b><span>${esc(b.board.product)}</span><b>BIOS</b><span>${esc(b.board.biosVersion)} · ${fmtDate(b.board.biosDate)}</span></div></div>
        <div class="panel"><div class="panel-h"><span class="code">CPU</span>${esc(b.cpu.name.split(" ").slice(0, 4).join(" "))}</div><div class="panel-b kv"><b>MICROCODE</b><span>${esc(b.cpu.microcode || "—")}</span><b>RAM</b><span>${esc(b.ram.type)} ${b.ram.speed} / ${b.ram.rated} MHz</span></div></div>
        <div class="panel"><div class="panel-h"><span class="code">SEC</span>SECURITY</div><div class="panel-b kv"><b>SECURE BOOT</b><span>${b.security.secureBoot ? "ON" : "OFF"}</span><b>TPM</b><span>${b.security.tpm ? "ON" : "OFF"}</span><b>VBS/HVCI</b><span>${b.security.vbsRunning ? "ON" : "OFF"} / ${b.security.hvciRunning ? "ON" : "OFF"}</span></div></div>
      </div>
      ${b.advice.map((a) => html`<div class="trow">
        <span class="led ${a.status === "ok" ? "" : a.status === "fix" ? "crit" : "warn"}" style="margin-top:5px"></span>
        <div><div class="tt">${esc(L(a.title))} <span class="chip ${a.priority === "high" ? "crit" : a.priority === "medium" ? "warn" : ""}">${a.priority}</span></div>
          <div class="td">${esc(L(a.detail))}</div>
          <div class="tm"><code style="color:var(--accent)">${esc(a.path)}</code></div></div>
        <div class="tr"><span class="val">${esc(a.value)}</span><span class="status ${a.status === "ok" ? "applied" : a.status === "fix" ? "partial" : ""}">${t("bios." + a.status)}</span></div></div>`).join("")}`;
    $("#toBios").onclick = async () => { if (await confirmBox(t("bios.reboot"), t("bios.rebootQ"), t("bios.reboot"), "danger", true)) call("bios.reboot"); };
  };

  // ------------------------------------------------------------------ [09] image (own AI Tweaker image + USB)

  const usb = { iso: "", built: null, source: "built", disk: null, disks: null, lang: "Russian", typed: "", opt: { userName: "User", password: "", computerName: "", timeZone: "Russian Standard Time", locale: "ru-RU", bypass: true, copyTweaker: true, runAfterSetup: true, disableBitLocker: true } };
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
      <div class="page-h"><div><span class="code">SEC-09 // IMAGE</span><h1>${t("image.title")}</h1><p>${t("image.desc")}</p></div></div>
      <div class="grid cols-2" style="align-items:start">
        <div class="panel"><div class="panel-h"><span class="code">AIT</span>${t("ib.title")}</div><div class="panel-b steps">
          <div class="callout">${t("ib.desc")}</div>
          <div class="step"><h4>${t("usb.iso")}</h4>
            <div style="display:flex;gap:8px;flex-wrap:wrap"><button class="btn sm" id="isoPick">📂 ${t("usb.pick")}</button>
              <select class="input" id="isoLang" style="width:auto">${["Russian", "English", "English International", "Ukrainian", "Kazakh"].map((l) => `<option ${usb.lang === l ? "selected" : ""}>${l}</option>`).join("")}</select>
              <button class="btn sm" id="isoDl">⇩ ${t("usb.download")}</button></div>
            <div class="tid" style="margin-top:8px;word-break:break-all">${esc(usb.iso || "—")}</div>
            <div class="td" style="margin-top:6px;color:var(--muted)">${t("usb.fidoNote")}</div></div>
          <div class="step"><h4>${t("ib.contents")}</h4>
            <label class="field">${t("ib.level")}<select class="input" id="ibLevel">${[1, 2, 3].map((l) => `<option value="${l}" ${img.level === l ? "selected" : ""}>${t("prof." + ["Safe", "Balanced", "Performance"][l - 1])}</option>`).join("")}</select></label>
            <div style="display:grid;gap:8px;margin-top:10px">
              ${[["look", "ib.look"], ["updates", "ib.updates"], ["removeApps", "ib.apps"], ["removeComponents", "ib.components"], ["cleanup", "ib.cleanup"], ["drivers", "ib.drivers"]].map(([k, l]) => `<label class="check"><input type="checkbox" data-ib="${k}" ${img[k] ? "checked" : ""}><span class="box"></span>${t(l)}</label>`).join("")}</div>
            ${img.look ? `<div class="look-thumbs"><figure><div class="swatch-black"></div><figcaption>${t("look.black")}</figcaption></figure>${["graphite", "lock"].map((n) => `<figure><img src="img/aitweaker-${n}.webp" alt="" loading="lazy"><figcaption>${t("look." + n)}</figcaption></figure>`).join("")}</div>` : ""}
            <div class="cat-h" style="margin-top:12px;font-size:11px">${t("ib.extra")}</div>
            <div style="display:grid;gap:6px;margin-top:8px">${EXTRA.map(([pkg, n]) => `<label class="check"><input type="checkbox" data-extra="${pkg}" ${img.extraApps.includes(pkg) ? "checked" : ""}><span class="box"></span>${n} <span class="chip crit" style="margin-left:4px">⚠</span></label>`).join("")}</div>
            <div class="td" style="margin-top:8px;color:var(--muted)">${t("ib.extraNote")}</div></div>
          <div class="step"><h4>${t("ib.build")}</h4>
            <div style="display:flex;gap:8px;flex-wrap:wrap"><button class="btn sm ghost" id="ibPreview">${t("ib.preview")}</button><button class="btn hazard" id="ibBuild" ${usb.iso ? "" : "disabled"}><span>⚙ ${t("ib.build")}</span></button></div>
            ${usb.built ? `<div class="callout ok" style="margin-top:10px">✓ ${t("ib.ready")}: <span class="tid">${esc(usb.built.folder)}</span>${usb.built.tweaks != null ? `<br>${usb.built.version ? `Windows 11 ${esc(usb.built.version)} · build ${esc(usb.built.build)}${usb.built.update ? " · " + esc(usb.built.update) : ""}<br>` : ""}${usb.built.tweaks} tweaks · ${usb.built.apps} apps · install.wim ${(usb.built.wimBytes / 1073741824).toFixed(2)} GB` : ""}</div>` : ""}
            <div class="td" style="margin-top:8px;color:var(--muted)">${t("ib.note")}</div></div>
        </div></div>

        <div class="panel"><div class="panel-h"><span class="code">USB</span>${t("usb.title")}</div><div class="panel-b steps">
          <div class="step"><h4>${t("usb.source")}</h4>
            <label class="check" style="display:flex;margin:4px 0"><input type="radio" name="usbsrc" value="built" ${usb.source === "built" ? "checked" : ""} ${usb.built ? "" : "disabled"}><span class="box" style="border-radius:50%"></span>${t("usb.srcBuilt")}</label>
            <label class="check" style="display:flex;margin:4px 0"><input type="radio" name="usbsrc" value="iso" ${usb.source === "iso" || !usb.built ? "checked" : ""}><span class="box" style="border-radius:50%"></span>${t("usb.srcIso")}</label>
            <div class="tid" style="word-break:break-all">${esc(src || "—")}</div></div>
          <div class="step"><h4>${t("usb.disk")}</h4>
            ${usb.disks.length ? usb.disks.map((d) => `<label class="check" style="display:flex;margin:4px 0"><input type="radio" name="disk" value="${d.number}" ${usb.disk === d.number ? "checked" : ""}><span class="box" style="border-radius:50%"></span>#${d.number} · ${esc(d.name)} · ${(d.sizeBytes / 1e9).toFixed(1)} GB</label>`).join("") : `<div class="td">${t("usb.noDisk")}</div>`}
            <button class="btn sm ghost" id="diskRefresh" style="margin-top:6px">⟳ ${t("usb.refresh")}</button></div>
          <div class="step"><h4>${t("usb.options")}</h4><div class="grid cols-2" style="gap:10px">
            <label class="field">${t("usb.user")}<input class="input" data-o="userName" value="${esc(o.userName)}"></label>
            <label class="field">${t("usb.pass")}<input class="input" type="password" data-o="password" value="${esc(o.password)}"></label>
            <label class="field">${t("usb.pc")}<input class="input" data-o="computerName" value="${esc(o.computerName)}"></label>
            <label class="field">${t("usb.tz")}<select class="input" data-o="timeZone">${TZ.map(([id, n]) => `<option value="${id}" ${o.timeZone === id ? "selected" : ""}>${n}</option>`).join("")}</select></label>
            <label class="field">${t("usb.locale")}<select class="input" data-o="locale">${["ru-RU", "en-US", "uk-UA", "kk-KZ", "be-BY"].map((l) => `<option ${o.locale === l ? "selected" : ""}>${l}</option>`).join("")}</select></label></div>
            <div style="display:grid;gap:8px;margin-top:10px">
              ${[["bypass", "usb.bypass"], ["copyTweaker", "usb.copy"], ["runAfterSetup", "usb.runAfter"], ["disableBitLocker", "usb.bitlocker"]].map(([k, l]) => `<label class="check"><input type="checkbox" data-b="${k}" ${o[k] ? "checked" : ""}><span class="box"></span>${t(l)}</label>`).join("")}</div>
            <div class="callout" style="margin-top:10px">${t("usb.note")}</div></div>
          <div class="step"><h4>${t("usb.create")}</h4><div style="display:flex;gap:8px;flex-wrap:wrap"><button class="btn sm ghost" id="xmlPrev">${t("usb.preview")}</button><button class="btn hazard" id="usbGo" ${src && usb.disk !== null ? "" : "disabled"}><span>⚡ ${t("usb.create")}</span></button></div></div>
        </div></div>
      </div>`;

    $("#isoPick").onclick = async () => { const p = await call("usb.pickIso"); if (p) { usb.iso = p; V.image(); } };
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
          <div class="pre">${esc(pv.tweaks.map((x) => `✓ ${L(x.name)}  [${x.id}]`).join("\n"))}\n\n${esc(pv.apps.map((x) => `✕ ${x}`).join("\n"))}\n\n${esc(pv.capabilities.map((x) => `✕ ${x}*`).join("\n"))}</div>`,
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
  };

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
    try { await call("usb.create", { disk: d.number, iso: src, options: usb.opt }); toast("USB ✓", "ok"); } catch { /* toast */ } finally { off(); b.done(); }
  }

  // ------------------------------------------------------------------ [09] AI

  V.ai = () => {
    const v = $("#v-ai");
    const s = S.init.settings;
    const a = S.ai;
    v.innerHTML = html`
      <div class="page-h"><div><span class="code">SEC-10 // CLAUDE</span><h1>${t("ai.title")}</h1><p>${t("ai.desc")}</p></div></div>
      <div class="grid cols-2" style="align-items:start">
        <div class="panel"><div class="panel-h"><span class="code">KEY</span>${t("ai.key")}<span class="right">${s.hasKey ? `<span class="chip ok">${t("ai.keySaved")}</span>` : ""}</span></div><div class="panel-b" style="display:grid;gap:10px">
          <input class="input" type="password" id="aiKey" placeholder="sk-ant-…" autocomplete="off">
          <div style="display:flex;gap:8px;flex-wrap:wrap"><button class="btn sm primary" id="aiSave">${t("ai.save")}</button>${s.hasKey ? `<button class="btn sm danger" id="aiDel">${t("ai.removeKey")}</button>` : ""}<button class="btn sm ghost" id="aiGet">${t("ai.getKey")} ↗</button></div>
          <label class="field">${t("ai.model")}<select class="input" id="aiModel">${S.init.models.map((m) => `<option ${s.model === m ? "selected" : ""}>${m}</option>`).join("")}</select></label>
          <div class="callout">${t("ai.local")}</div>
          <button class="btn hazard" id="aiRun" ${s.hasKey && S.scan ? "" : "disabled"}><span>◎ ${t("ai.analyze")}</span></button>
        </div></div>
        <div class="panel"><div class="panel-h"><span class="code">OUT</span>${t("ai.summary")}<span class="right">${a ? esc(a.model || "") : ""}</span></div><div class="panel-b" style="display:grid;gap:12px">
          ${a ? html`<div style="font-weight:700">${esc(a.summary)}</div>
            <div><div class="cat-h">${t("ai.recommend")}<span class="n">${a.recommendations.length}</span></div>${a.recommendations.map((r) => `<div class="trow" style="grid-template-columns:1fr auto"><div><div class="tt">${esc(L((S.byId[r.id] || {}).name) || r.id)}</div><div class="td">${esc(r.reason)}</div></div>${statusChip(r.id)}</div>`).join("")}</div>
            ${a.avoid.length ? `<div><div class="cat-h">${t("ai.avoid")}<span class="n">${a.avoid.length}</span></div>${a.avoid.map((r) => `<div class="trow" style="grid-template-columns:1fr"><div><div class="tt">✕ ${esc(L((S.byId[r.id] || {}).name) || r.id)}</div><div class="td">${esc(r.reason)}</div></div></div>`).join("")}</div>` : ""}
            ${a.tips.length ? `<div><div class="cat-h">${t("ai.tips")}</div>${a.tips.map((x) => `<div class="setting"><div class="sn">▸ ${esc(x)}</div></div>`).join("")}</div>` : ""}
            <button class="btn primary" id="aiUse">${t("ai.useInTune")} →</button>` : `<div class="empty">◎</div>`}
        </div></div>
      </div>`;
    $("#aiSave").onclick = async () => { const k = $("#aiKey").value.trim(); if (!k) return; const r = await call("settings.set", { apiKey: k }); s.hasKey = r.hasKey; toast(t("toast.saved"), "ok"); V.ai(); };
    if ($("#aiDel")) $("#aiDel").onclick = async () => { const r = await call("settings.set", { apiKey: null }); s.hasKey = r.hasKey; V.ai(); };
    $("#aiGet").onclick = () => call("shell.open", { url: "https://console.anthropic.com/settings/keys" });
    $("#aiModel").onchange = (e) => { s.model = e.target.value; call("settings.set", { model: s.model }); };
    $("#aiRun").onclick = async () => {
      const b = busy(t("busy.ai"));
      b.progress(0.3, s.model);
      try { S.ai = await call("ai.analyze", { profile: S.profileName }); } catch { /* toast */ } finally { b.done(); }
      V.ai();
    };
    if ($("#aiUse")) $("#aiUse").onclick = () => {
      a.recommendations.forEach((r) => S.selected.add(r.id));
      a.avoid.forEach((r) => S.selected.delete(r.id));
      go("tune");
    };
  };

  // ------------------------------------------------------------------ [10] backup

  V.backup = async () => {
    const v = $("#v-backup");
    S.backups = await call("backup.list");
    const b = S.backups;
    v.innerHTML = html`
      <div class="page-h"><div><span class="code">SEC-11 // ROLLBACK</span><h1>${t("backup.title")}</h1><p>${t("backup.desc")}</p></div>
        <div class="actions"><button class="btn sm" id="rpNow">◷ ${t("backup.rp")}</button><button class="btn sm" id="rstrui">${t("backup.rstrui")} ↗</button><button class="btn sm ghost" id="bkFolder">${t("backup.folder")}</button><button class="btn sm ghost" id="logFolder">${t("backup.logs")}</button></div></div>
      <div class="grid cols-2" style="align-items:start">
        <div class="panel"><div class="panel-h"><span class="code">BK</span>${t("backup.applied")}<span class="right">${b.applied.length ? `<button class="btn sm danger" id="revertAll">${t("backup.revertAll")}</button>` : ""}</span></div><div class="panel-b flush">
          ${b.applied.length ? b.applied.map((x) => `<div class="trow" style="grid-template-columns:1fr auto;padding:9px 12px"><div><div class="tt">${esc(L(x.name) || x.tweakId)}</div><div class="tm"><span class="tid">${esc(x.tweakId)} · ${fmtDate(x.appliedAt)} ${fmtTime(x.appliedAt)} · ${x.changes} changes</span></div></div><button class="btn sm danger" data-rv="${esc(x.tweakId)}">${t("revert")}</button></div>`).join("") : `<div class="empty" style="margin:12px">${t("backup.none")}</div>`}
        </div></div>
        <div class="panel"><div class="panel-h"><span class="code">LOG</span>${t("backup.sessions")}</div><div class="panel-b flush">
          ${b.sessions.length ? b.sessions.map((x) => `<div class="setting" style="padding:8px 12px"><div><div class="sn"><b>${x.kind.toUpperCase()}</b> ${esc(x.profile || "")} · ${fmtDate(x.time)} ${fmtTime(x.time)}</div><div class="snote">${x.applied.length} ok · ${x.failed.length} failed${x.restorePoint ? " · restore point" : ""}</div></div><span class="val ${x.failed.length ? "" : "on"}">${x.failed.length ? "!" : "✓"}</span></div>`).join("") : `<div class="empty" style="margin:12px">—</div>`}
        </div></div>
      </div>`;
    v.querySelectorAll("[data-rv]").forEach((x) => (x.onclick = () => App.revert([x.dataset.rv])));
    if ($("#revertAll")) $("#revertAll").onclick = async () => { if (await confirmBox(t("backup.revertAll"), `${b.applied.length} tweaks`, t("revert"), "danger", true)) App.revert(b.applied.map((x) => x.tweakId)); };
    $("#rpNow").onclick = async () => { const bb = busy(t("backup.rp")); try { await call("restore.create"); toast("✓", "ok"); } finally { bb.done(); } };
    $("#rstrui").onclick = () => call("restore.open");
    $("#bkFolder").onclick = () => call("shell.folder", { name: "backups" });
    $("#logFolder").onclick = () => call("shell.folder", { name: "logs" });
  };
})();
