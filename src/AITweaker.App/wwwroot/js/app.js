/* Bootstrap and cross-view actions (scan, apply, revert). */
(function () {
  const App = (window.App = {});

  App.refreshStatus = async () => {
    S.status = await call("tweaks.status", {}, { silent: true }).catch(() => S.status);
  };

  App.buildPlan = async () => {
    S.plan = await call("plan.build", { profile: S.profileName });
    S.selected = new Set(S.plan.items.filter((i) => i.recommended && S.status[i.id] !== "applied").map((i) => i.id));
  };

  App.scan = async (quiet) => {
    const b = quiet ? null : busy(t("busy.scan"));
    try {
      b && b.progress(0.2, "WMI · CPU · GPU · RAM · DISK · NET");
      S.scan = await call("system.scan");
      b && b.progress(0.7, "status");
      S.guides = S.bios = S.drivers = S.appx = null;
      await App.refreshStatus();
      await App.buildPlan();
    } catch { /* toast shown */ } finally { b && b.done(); }
    updateBadges();
    go(S.view);
  };

  App.setProfile = async (p) => {
    S.profileName = p;
    $("#profileChip").textContent = t("prof." + p);
    call("settings.set", { profile: p }, { silent: true }).catch(() => {});
    await App.buildPlan();
    go(S.view);
  };

  /** Applies tweaks after asking about every important one; shows a summary with reboot prompt. */
  App.apply = async (ids, opts = {}) => {
    if (S.busy || !ids.length) return;
    const accepted = await confirmImportant(ids);
    if (!accepted.length) return;
    const b = busy(t("busy.apply"));
    const off = API.on("apply.progress", (p) => b.progress(p.i / p.n, `${p.i}/${p.n} · ${p.id}`));
    let summary;
    try {
      summary = await call("tweaks.apply", { ids: accepted, restorePoint: opts.restorePoint ?? S.init.settings.createRestorePoint, profile: S.profileName });
    } catch { return; } finally { off(); b.done(); }
    await App.refreshStatus();
    accepted.forEach((id) => S.selected.delete(id));
    go(S.view);
    const ok = summary.results.filter((r) => r.ok), bad = summary.results.filter((r) => !r.ok);
    const res = await modal({
      title: t("done.title"),
      body: html`<div class="metrics" style="grid-template-columns:repeat(2,1fr);border:1px solid var(--line)">
          <div class="metric ok"><div class="k">${t("done.applied")}</div><div class="v">${ok.length}</div></div>
          <div class="metric ${bad.length ? "crit" : ""}" style="border-right:0"><div class="k">${t("done.failed")}</div><div class="v">${bad.length}</div></div></div>
        ${bad.length ? `<div class="pre">${bad.map((r) => `${esc(r.id)}: ${esc(r.error || "")}`).join("\n")}</div>` : ""}
        ${summary.rebootRequired ? `<div class="callout crit">${t("done.reboot")}</div>` : ""}
        ${summary.logoffRequired && !summary.rebootRequired ? `<div class="callout">${t("done.logoff")}</div>` : ""}`,
      buttons: summary.rebootRequired ? [{ label: t("done.later"), value: false, cls: "ghost" }, { label: t("done.rebootNow"), value: true, cls: "primary" }] : [{ label: t("ok"), value: false, cls: "primary" }],
    });
    if (res === true) call("system.reboot");
  };

  App.revert = async (ids) => {
    if (S.busy || !ids.length) return;
    const b = busy(t("busy.revert"));
    try {
      const res = await call("tweaks.revert", { ids });
      const bad = res.filter((r) => !r.ok);
      toast(`${res.length - bad.length}/${res.length} ↺`, bad.length ? "warn" : "ok");
    } catch { /* toast */ } finally { b.done(); }
    await App.refreshStatus();
    go(S.view);
  };

  async function boot() {
    S.demo = API.demo;
    API.on("log", pushLog);
    API.on("telemetry", pushTelemetry);
    try {
      S.init = await API.call("app.init");
    } catch (e) {
      document.body.innerHTML = `<pre style="padding:20px">${esc(e.message)}</pre>`;
      return;
    }
    S.lang = S.init.settings.language || "ru";
    S.theme = S.init.settings.theme || "day";
    S.profileName = S.init.settings.profile || "Performance";
    (S.init.log || []).reverse().forEach((e) => S.log.push(e));
    S.catalog = await API.call("tweaks.catalog");
    S.byId = Object.fromEntries(S.catalog.map((x) => [x.id, x]));
    renderShell();
    go("dashboard");
    API.start();
    App.scan();
  }

  document.addEventListener("DOMContentLoaded", boot);
})();
