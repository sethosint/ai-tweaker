/* Services: every Windows service, its start type and state; switch any of them back on (or off) by hand. */
(function () {
  const V = (window.Views ||= {});
  const START = ["auto", "delayed", "manual", "disabled"];
  const st = { list: null, q: "", filter: "all" };

  const tweakName = (id) => (S.byId[id] ? L(S.byId[id].name) : id);
  const appliedTweaks = (s) => s.tweaks.filter((id) => S.status[id] === "applied");

  function match(s) {
    const q = st.q.trim().toLowerCase();
    if (q && !(s.displayName.toLowerCase().includes(q) || s.name.toLowerCase().includes(q) || s.description.toLowerCase().includes(q))) return false;
    if (st.filter === "changed") return s.changed || appliedTweaks(s).length > 0;
    if (st.filter === "disabled") return s.start === "disabled";
    if (st.filter === "running") return s.state === "running";
    return true;
  }

  V.services = async (opts = {}) => {
    const v = $("#v-services");
    if (opts.q != null) { st.q = opts.q; st.filter = "all"; }
    if (!st.list) {
      v.innerHTML = `<div class="empty">${t("loading")}</div>`;
      try { st.list = await call("services.list"); } catch { return; }
    }
    const list = st.list.filter(match);
    const count = (f) => st.list.filter((s) => (f === "all" ? true : f === "changed" ? s.changed || appliedTweaks(s).length : f === "disabled" ? s.start === "disabled" : s.state === "running")).length;
    v.innerHTML = html`
      <div class="page-h"><div><h1>${t("svc.title")}</h1><p>${t("svc.desc")}</p></div>
        <div class="actions"><button class="btn ghost sm" id="svcReload" title="${t("refresh")}">${icon("refresh")}</button></div></div>
      <div class="toolbar">
        <input class="input" id="svcQ" placeholder="${t("svc.search")}" value="${esc(st.q)}">
        <div class="tabs" id="svcF">${["all", "changed", "disabled", "running"].map((f) => `<button data-f="${f}" class="${st.filter === f ? "on" : ""}">${t("svc.f." + f)}<span class="n">${count(f)}</span></button>`).join("")}</div>
      </div>
      <div class="cat-group">
        ${list.slice(0, 400).map((s) => { const by = appliedTweaks(s); return html`<div class="trow svc-row" data-n="${esc(s.name)}">
          <span class="svc-dot ${s.state}" title="${t("svc.state." + s.state)}"></span>
          <div><div class="tt">${esc(s.displayName)} ${s.essential ? `<span class="chip">${t("svc.essential")}</span>` : ""}</div>
            ${s.description ? `<div class="td svc-desc">${esc(s.description)}</div>` : ""}
            <div class="tm"><span class="tid">${esc(s.name)}</span>
              ${by.map((id) => `<span class="chip warn">${t("svc.byTweak")}: ${esc(tweakName(id))}</span><button class="link" data-revert="${id}">${t("revert")}</button>`).join("")}
              ${s.changed ? `<span class="chip info">${t("svc.manual")}</span><button class="link" data-restore="${esc(s.name)}">${t("svc.restore")}</button>` : ""}</div></div>
          <div class="tr svc-ctl">
            ${s.start === "system" ? `<span class="status">${t("svc.start.system")}</span>` : `<select class="input sm" data-start="${esc(s.name)}">${START.map((m) => `<option value="${m}" ${s.start === m ? "selected" : ""} ${m === "disabled" && s.essential ? "disabled" : ""}>${t("svc.start." + m)}</option>`).join("")}</select>`}
            <button class="btn sm ${s.state === "running" ? "ghost" : ""}" data-run="${esc(s.name)}" data-to="${s.state === "running" ? "0" : "1"}" ${s.start === "disabled" && s.state !== "running" ? "disabled" : ""}>${s.state === "running" ? t("svc.stop") : t("svc.startNow")}</button>
          </div></div>`; }).join("") || `<div class="empty">${t("res.none")}</div>`}
      </div>`;

    const q = $("#svcQ");
    // redraw once typing pauses, not on every key (the list has hundreds of rows)
    q.oninput = () => { clearTimeout(st.qt); st.qt = setTimeout(() => { st.q = q.value; const pos = q.selectionStart; V.services(); const n = $("#svcQ"); n.focus(); n.setSelectionRange(pos, pos); }, 150); };
    $("#svcReload").onclick = () => { st.list = null; V.services(); };
    v.querySelectorAll("#svcF [data-f]").forEach((b) => (b.onclick = () => { st.filter = b.dataset.f; V.services(); }));
    v.querySelectorAll("[data-start]").forEach((sel) => (sel.onchange = () => setStart(sel.dataset.start, sel.value)));
    v.querySelectorAll("[data-run]").forEach((b) => (b.onclick = () => run(b.dataset.run, b.dataset.to === "1")));
    v.querySelectorAll("[data-restore]").forEach((b) => (b.onclick = () => restore(b.dataset.restore)));
    v.querySelectorAll("[data-revert]").forEach((b) => (b.onclick = async () => { await App.revert([b.dataset.revert]); st.list = null; V.services(); }));
  };

  async function refresh(name) {
    try { st.list = await call("services.list", {}, { silent: true }); } catch { /* keep the old list */ }
    if (S.view === "services") V.services();
    const s = st.list && st.list.find((x) => x.name === name);
    return s;
  }

  async function setStart(name, start) {
    const s = st.list.find((x) => x.name === name);
    if (start === "disabled" && !(await confirmBox(t("svc.disableQ"), html`<b>${esc(s.displayName)}</b><div class="td" style="margin-top:6px">${esc(s.description || s.name)}</div>`, t("svc.start.disabled")))) { V.services(); return; }
    try { await call("services.setStart", { name, start }); toast(`${s.displayName}: ${t("svc.start." + start).toLowerCase()}`, "ok"); } catch { /* toast */ }
    refresh(name);
  }

  async function run(name, start) {
    const b = busy(start ? t("svc.startNow") : t("svc.stop"));
    try { await call("services.run", { name, start }); } catch { /* toast */ } finally { b.done(); }
    refresh(name);
  }

  async function restore(name) {
    try { await call("services.restore", { name }); toast(t("svc.restored"), "ok"); } catch { /* toast */ }
    refresh(name);
  }
})();
