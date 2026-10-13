/* Game setup: one plan for maximum FPS that asks about every important switch-off, the temporary Game Mode and the by-hand list. */
(function () {
  const App = (window.App ||= {});
  const catName = (id) => L((S.init.categories.find((c) => c.id === id) || {}).name) || id;

  // ------------------------------------------------------------------ questions

  /**
   * One question per important change ("switch off" vs "keep", with what each side gives), then the built-in apps
   * to remove, then a summary. questions: [{ tw, keep, hint }]. Resolves { off, kept, remove } or null when cancelled.
   */
  window.askQuestions = (questions, apps = [], opts = {}) => {
    const answer = new Map(questions.map((q) => [q.tw.id, q.keep ? "keep" : "off"]));
    const remove = new Set(apps.filter((a) => !a.important).map((a) => a.package));
    const summary = opts.summary !== false;
    const steps = questions.length + (apps.length ? 1 : 0) + (summary ? 1 : 0);
    let i = 0;
    const result = () => ({
      off: new Set(questions.filter((q) => answer.get(q.tw.id) === "off").map((q) => q.tw.id)),
      kept: new Set(questions.filter((q) => answer.get(q.tw.id) === "keep").map((q) => q.tw.id)),
      remove: [...remove],
    });

    return modal({
      title: opts.title || t("q.title"),
      wide: true,
      hazard: true,
      body: `<div class="qz"></div>`,
      onOpen: (ov, close) => {
        const box = $(".qz", ov);
        const next = () => { if (i < steps - 1) { i++; render(); } else close(result()); };
        const render = () => {
          const prog = html`<div class="qz-top"><div class="qz-prog"><i style="width:${steps > 1 ? (i / (steps - 1)) * 100 : 100}%"></i></div><span class="qz-n">${i + 1} / ${steps}</span></div>`;
          let body = "", foot = "";
          if (i < questions.length) {
            const { tw, hint } = questions[i];
            const a = answer.get(tw.id);
            body = html`<div class="qz-q">
              <div class="qz-cat">${esc(catName(tw.category))}</div>
              <div class="qz-name">${esc(L(tw.name))}</div>
              <div class="qz-desc">${esc(L(tw.description))}</div>
              ${hint ? `<div class="qz-hint">${icon("alert")}${esc(hint)}</div>` : ""}
              <div class="qz-opts">
                <button class="qz-opt off ${a === "off" ? "on" : ""}" data-a="off">
                  <span class="qz-oh">${icon("power")}${t("q.ifOff")}</span>
                  <span class="qz-pro">${esc(L(tw.gain) || L(tw.description))}</span>
                  ${tw.confirmText ? `<span class="qz-con">${esc(L(tw.confirmText))}</span>` : ""}
                  <span class="qz-ob">${t("q.off")}</span>
                </button>
                <button class="qz-opt keep ${a === "keep" ? "on" : ""}" data-a="keep">
                  <span class="qz-oh">${icon("check")}${t("q.ifKeep")}</span>
                  <span class="qz-pro">${esc(L(tw.keep) || t("q.keepDefault"))}</span>
                  <span class="qz-con">${t("q.noGain")}</span>
                  <span class="qz-ob">${t("q.keep")}</span>
                </button>
              </div>
              <div class="qz-meta">${badges(tw)}</div></div>`;
            foot = summary ? `<button class="btn ghost sm" data-go="sum">${t("q.acceptRest")}</button>` : "";
          } else if (apps.length && i === questions.length) {
            body = html`<div class="qz-q"><div class="qz-cat">${t("nav.debloat")}</div><div class="qz-name">${t("q.appsT")}</div><div class="qz-desc">${t("q.appsD")}</div>
              <div class="qz-apps">${apps.map((p) => html`<label class="qz-app ${remove.has(p.package) ? "on" : ""}"><input type="checkbox" data-pkg="${esc(p.package)}" ${remove.has(p.package) ? "checked" : ""}>
                <span class="box"></span><span><b>${esc(L(p.name))}</b>${p.warning ? `<small>${esc(L(p.warning))}</small>` : ""}</span></label>`).join("")}</div></div>`;
            foot = `<button class="btn primary" data-go="next">${t("q.next")}${icon("arrow")}</button>`;
          } else {
            const r = result();
            const keptNames = questions.filter((q) => r.kept.has(q.tw.id)).map((q) => L(q.tw.name));
            body = html`<div class="qz-q"><div class="qz-cat">${t("q.sumK")}</div><div class="qz-name">${t("q.sumT")}</div>
              <div class="metrics qz-sum">
                <div class="metric"><div class="k">${t("q.sumAll")}</div><div class="v">${(opts.total || questions.length) - r.kept.size}</div></div>
                <div class="metric ok"><div class="k">${t("q.sumOff")}</div><div class="v">${r.off.size}</div></div>
                <div class="metric"><div class="k">${t("q.sumKeep")}</div><div class="v">${r.kept.size}</div></div>
                ${apps.length ? `<div class="metric"><div class="k">${t("q.sumApps")}</div><div class="v">${r.remove.length}</div></div>` : ""}
              </div>
              ${keptNames.length ? `<div class="qz-desc">${t("q.keptList")}</div><div class="qz-chips">${keptNames.map((n) => `<span class="chip">${esc(n)}</span>`).join("")}</div>` : ""}
              <div class="qz-desc">${t("q.sumNote")}</div></div>`;
            foot = `<button class="btn hazard" data-go="done"><span>${t("q.apply")}</span></button>`;
          }
          const back = i > 0 ? `<button class="btn ghost sm" data-go="back">${icon("arrow", "flip")}${t("q.back")}</button>` : "<span></span>";
          box.innerHTML = html`${prog}${body}${i > 0 || foot ? `<div class="qz-foot">${back}<div class="qz-foot-r">${foot}</div></div>` : ""}`;
          box.querySelectorAll("[data-a]").forEach((b) => (b.onclick = () => {
            answer.set(questions[i].tw.id, b.dataset.a);
            box.querySelectorAll("[data-a]").forEach((x) => x.classList.toggle("on", x === b));
            setTimeout(next, 160);
          }));
          box.querySelectorAll("input[data-pkg]").forEach((cb) => (cb.onchange = () => { cb.checked ? remove.add(cb.dataset.pkg) : remove.delete(cb.dataset.pkg); cb.closest(".qz-app").classList.toggle("on", cb.checked); }));
          box.querySelectorAll("[data-go]").forEach((b) => (b.onclick = () => {
            const go = b.dataset.go;
            if (go === "back") { i--; render(); } else if (go === "sum") { i = steps - 1; render(); } else if (go === "next") next(); else close(result());
          }));
        };
        render();
      },
    });
  };

  /** Which tweaks get a question: anything that switches off something people use, plus every "What do you use?" feature. */
  App.loadFeatures = async () => {
    if (!S.features) { try { S.features = await call("features.list", {}, { silent: true }); } catch { S.features = []; } }
    const map = new Map();
    S.features.forEach((f) => f.tweaks.forEach((id) => map.set(id, f)));
    return map;
  };

  App.questionFor = (tw, feats) => {
    if (!tw || !(tw.confirm || tw.risk === "high" || feats.has(tw.id))) return null;
    const f = feats.get(tw.id);
    const keep = !!(f && (f.used || f.keep));
    return { tw, keep, hint: f && f.used ? t("q.used") : f && f.keep ? t("q.keepSafe") : "" };
  };

  /** The one big button: asks, removes the built-in apps the user let go, then applies everything else in one run. */
  App.gameSetup = async (opts = {}) => {
    if (S.busy) return;
    const ids = [...new Set([...(opts.ids || [...S.selected]), ...(opts.extra || [])])].filter((id) => S.byId[id] && S.status[id] !== "applied");
    if (!ids.length) { toast(t("tune.nothing"), "ok"); return; }
    const feats = await App.loadFeatures();
    const qs = ids.map((id) => App.questionFor(S.byId[id], feats)).filter(Boolean);
    let apps = [];
    if (opts.apps !== false) {
      try { S.appx ||= await call("appx.list", {}, { silent: true }); apps = S.appx.packages.filter((a) => a.recommended && !a.keep); } catch { /* the tweaks still run */ }
    }
    const res = await askQuestions(qs, apps, { total: ids.length });
    if (!res) return;
    if (res.remove.length) {
      const b = busy(t("busy.appx"));
      let n = 0;
      const off = API.on("appx.progress", (name) => b.progress(++n / res.remove.length, name));
      try { await call("appx.remove", { names: res.remove }); } catch { /* toast */ } finally { off(); b.done(); }
      S.appx = null;
    }
    await App.apply(ids.filter((id) => !res.kept.has(id)), { restorePoint: opts.restorePoint ?? S.init.settings.createRestorePoint, preconfirmed: res.off });
  };

  // ------------------------------------------------------------------ Game Mode (temporary)

  App.gmButton = () => {
    const b = $("#gmBtn");
    if (!b) return;
    b.classList.toggle("on", !!S.gameMode);
    $(".gm-state", b).textContent = S.gameMode ? t("gm.on") : t("gm.off");
    if (S.view === "tune" && Views.tune) Views.tune();
  };

  App.gmRefresh = async () => {
    try { S.gameMode = (await call("gamemode.state", {}, { silent: true })).active; } catch { S.gameMode = false; }
    App.gmButton();
  };

  App.gameMode = async () => {
    if (S.busy) return;
    let st;
    try { st = await call("gamemode.state"); } catch { return; }
    if (st.active) {
      const r = await modal({
        title: t("gm.title"),
        body: html`<div class="gm-live">${icon("gamepad")}<div><b>${t("gm.isOn")}</b><div>${t("gm.since")} ${fmtTime(st.started)}</div></div></div>
          <div class="metrics"><div class="metric"><div class="k">${t("gm.paused")}</div><div class="v">${st.services.length}</div></div>
            <div class="metric"><div class="k">${t("gm.closed")}</div><div class="v">${st.apps.length}</div></div></div>
          <div class="td" style="color:var(--muted)">${t("gm.stopNote")}</div>`,
        buttons: [{ label: t("close"), value: false, cls: "ghost" }, { label: t("gm.stop"), value: true, cls: "danger" }],
      });
      if (r !== true) return;
      const b = busy(t("gm.stopping"));
      try { await call("gamemode.stop"); toast(t("gm.stopped"), "ok"); } catch { /* toast */ } finally { b.done(); }
      S.gameMode = false;
      App.gmButton();
      return;
    }
    const on = new Set(st.options.map((o) => o.id));
    const row = (o) => html`<button class="gm-opt ${on.has(o.id) ? "on" : ""}" data-o="${o.id}">
      <span><b>${t("gm." + o.id)}</b><small>${o.id === "power" ? t("gm.power." + o.items[0]) : o.items.length ? esc([...new Set(o.items)].join(", ")) : t("gm." + o.id + "D")}</small></span>
      <span class="switch ${on.has(o.id) ? "on" : ""}"></span></button>`;
    const r = await modal({
      title: t("gm.title"),
      wide: true,
      body: html`<div>${t("gm.desc")}</div><div class="gm-opts">${st.options.map(row).join("")}</div>
        <div class="td" style="color:var(--muted)">${icon("shield")} ${t("gm.safe")}</div>`,
      buttons: [{ label: t("cancel"), value: false, cls: "ghost" }, { label: t("gm.start"), value: true, cls: "primary" }],
      onOpen: (ov) => ov.querySelectorAll("[data-o]").forEach((b) => (b.onclick = () => {
        on.has(b.dataset.o) ? on.delete(b.dataset.o) : on.add(b.dataset.o);
        b.classList.toggle("on", on.has(b.dataset.o));
        $(".switch", b).classList.toggle("on", on.has(b.dataset.o));
      })),
    });
    if (r !== true || !on.size) return;
    const b = busy(t("gm.starting"));
    let res = null;
    try { res = await call("gamemode.start", { groups: [...on] }); } catch { /* toast */ } finally { b.done(); }
    if (!res) return;
    S.gameMode = !!res.active;
    App.gmButton();
    toast(t("gm.started").replace("{s}", res.services.length).replace("{a}", res.apps.length).replace("{m}", res.freedMb), "ok", 7000);
  };

  // ------------------------------------------------------------------ by hand

  /** What only the user can do (BIOS, in-game settings, the NVIDIA/AMD apps, hardware), each with a way to get there. */
  App.manualList = () => {
    const f = (S.scan && S.scan.facts) || {};
    return [
      { id: "xmp", go: () => go("bios") },
      { id: "rebar", go: () => go("bios") },
      f["gpu.nvidia"] ? { id: "nvapp", go: () => go("apps", { guide: "nvidia-app" }) } : null,
      f["gpu.amd"] ? { id: "amdapp", go: () => go("apps", { guide: "amd-software" }) } : null,
      { id: "reflex" },
      { id: "cap" },
      { id: "monitor", go: () => go("tools") },
      { id: "mouse" },
      { id: "overlays", go: () => go("apps", { guide: "discord" }) },
      f["system.laptop"] && f["gpu.discrete"] ? { id: "gpupick", go: () => go("apps", { guide: "windows-graphics" }) } : null,
      { id: "dnd", url: "ms-settings:notifications" },
      { id: "update", url: "ms-settings:windowsupdate" },
      { id: "defender", url: "windowsdefender://threatsettings" },
    ].filter(Boolean);
  };
})();
