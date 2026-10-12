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

  /** Applies tweaks after asking about every important one; shows a summary, or the restart window when needed. */
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
    const body = html`<div class="metrics" style="grid-template-columns:repeat(2,1fr);border:1px solid var(--line)">
        <div class="metric ok"><div class="k">${t("done.applied")}</div><div class="v">${ok.length}</div></div>
        <div class="metric ${bad.length ? "crit" : ""}" style="border-right:0"><div class="k">${t("done.failed")}</div><div class="v">${bad.length}</div></div></div>
      ${bad.length ? `<div class="pre">${bad.map((r) => `${esc(r.id)}: ${esc(r.error || "")}`).join("\n")}</div>` : ""}`;
    if (summary.rebootRequired || summary.logoffRequired) await App.restartPrompt(summary.rebootRequired ? "reboot" : "logoff", body);
    else await modal({ title: t("done.title"), body, buttons: [{ label: t("ok"), value: false, cls: "primary" }] });
  };

  /**
   * Automatic restart window: counts down and restarts (or signs out) by itself unless the user postpones.
   * A postponed reboot stays visible as a chip in the top bar until Windows has actually restarted.
   */
  App.restartPrompt = async (kind = "reboot", body = "") => {
    if (kind === "reboot") App.setRebootPending(true);
    const total = Math.max(0, +(S.init.settings.autoRestartSeconds ?? 60));
    let left = total, timer = null;
    const verb = kind === "reboot" ? t("rs.reboot") : t("rs.logoff");
    const res = await modal({
      title: kind === "reboot" ? t("rs.title") : t("rs.titleLogoff"),
      hazard: true,
      body: html`${body}
        <div class="restart-box">
          <div class="rs-ring"><svg viewBox="0 0 120 120"><circle cx="60" cy="60" r="52" class="rs-track"/><circle cx="60" cy="60" r="52" class="rs-arc" id="rsArc"/></svg><div class="rs-n" id="rsN">${total || "⟳"}</div></div>
          <div class="rs-t"><b>${kind === "reboot" ? t("done.reboot") : t("done.logoff")}</b>
            <div>${total ? `${verb} ${t("rs.in")} <b id="rsLeft">${total}</b> ${t("rs.sec")}.` : t("rs.manual")}</div>
            <div class="td" style="color:var(--muted)">${t("rs.save")}</div></div>
        </div>`,
      buttons: [{ label: t("rs.later"), value: false, cls: "ghost" }, { label: kind === "reboot" ? t("done.rebootNow") : t("rs.logoffNow"), value: true, cls: "danger" }],
      onOpen: (ov, close) => {
        if (!total) return;
        const arc = $("#rsArc", ov), C = 2 * Math.PI * 52;
        arc.style.strokeDasharray = C;
        const tick = () => {
          $("#rsN", ov).textContent = left;
          $("#rsLeft", ov).textContent = left;
          arc.style.strokeDashoffset = C * (1 - left / total);
          if (left-- <= 0) { clearInterval(timer); close(true); }
        };
        tick();
        timer = setInterval(tick, 1000);
      },
    });
    clearInterval(timer);
    if (res !== true) { toast(kind === "reboot" ? t("rs.postponed") : t("done.logoff"), "warn", 6000); return false; }
    await call(kind === "reboot" ? "system.reboot" : "system.logoff").catch(() => {});
    toast(kind === "reboot" ? t("rs.rebooting") : t("rs.loggingOff"), "warn", 8000);
    return true;
  };

  App.setRebootPending = (on) => {
    S.rebootPending = on;
    const chip = $("#rebootChip");
    if (chip) chip.hidden = !on;
  };

  App.revert = async (ids) => {
    if (S.busy || !ids.length) return;
    const b = busy(t("busy.revert"));
    let res = null;
    try {
      res = await call("tweaks.revert", { ids });
      const bad = res.filter((r) => !r.ok);
      toast(`${res.length - bad.length}/${res.length} ↺`, bad.length ? "warn" : "ok");
    } catch { /* toast */ } finally { b.done(); }
    await App.refreshStatus();
    go(S.view);
    // Rolling back a tweak that needed a reboot needs one as well.
    const done = (res || []).filter((r) => r.ok).map((r) => S.byId[r.id]).filter(Boolean);
    if (done.some((tw) => tw.reboot)) await App.restartPrompt("reboot");
    else if (done.some((tw) => tw.logoff)) await App.restartPrompt("logoff");
  };

  async function boot() {
    S.demo = API.demo;
    API.on("log", pushLog);
    API.on("telemetry", pushTelemetry);
    API.on("sensors", pushSensors);
    API.on("system.theme", (sys) => { S.system = sys; applyPalette(); if (S.view === "dashboard") Views.dashboard(); });
    try {
      S.init = await API.call("app.init");
    } catch (e) {
      document.body.innerHTML = `<pre style="padding:20px">${esc(e.message)}</pre>`;
      return;
    }
    S.lang = S.init.settings.language || "ru";
    S.themeMode = S.init.settings.theme || "auto";
    S.accentMode = S.init.settings.accent || "system";
    if (S.init.system) S.system = S.init.system;
    S.profileName = S.init.settings.profile || "Performance";
    S.rebootPending = !!S.init.rebootPending;
    (S.init.log || []).reverse().forEach((e) => S.log.push(e));
    S.catalog = await API.call("tweaks.catalog");
    S.byId = Object.fromEntries(S.catalog.map((x) => [x.id, x]));
    renderShell();
    go("dashboard");
    API.start();
    await App.scan();
    if (S.init.firstRun) App.firstRun();
  }

  /**
   * First launch after a clean install (autounattend starts us with --first-run, or Windows came from our image):
   * scan → auto-tune for this exact hardware → AI Tweaker look → automatic restart.
   */
  App.firstRun = async () => {
    const img = S.init.image;
    const prof = { v: "Performance" };
    const go1 = await modal({
      title: t("fr.title"),
      wide: true,
      body: html`<div class="fr-hero"><img src="img/aitweaker-dark.webp" alt=""><div>
          <div class="tt" style="font:700 17px/1.3 var(--font-head);letter-spacing:.08em">${t("fr.hello")}</div>
          <div style="color:var(--ink-2);margin-top:6px">${t("fr.desc")}</div>
          ${img ? `<div class="chip ok" style="margin-top:10px">✓ ${t("fr.fromImage")} · Windows 11 ${esc(img.windows || "")} ${esc(img.build || "")}</div>` : ""}</div></div>
        <ol class="fr-steps">
          <li><b>${t("fr.s1")}</b><span>${esc(S.scan ? `${S.scan.profile.cpu.name} · ${(S.scan.profile.gpus[0] || {}).name || ""}` : "")}</span></li>
          <li><b>${t("fr.s2")}</b><span>${t("fr.s2d")}</span></li>
          <li><b>${t("fr.s3")}</b><span>${t("fr.s3d")}</span></li>
          <li><b>${t("fr.s4")}</b><span>${t("fr.s4d")}</span></li>
        </ol>
        <label class="field">${t("fr.profile")}<select class="input" id="frProf">${["Balanced", "Performance", "Extreme"].map((p) => `<option value="${p}" ${p === prof.v ? "selected" : ""}>${t("prof." + p)}${p === "Performance" ? " — " + t("fr.rec") : ""}</option>`).join("")}</select></label>`,
      buttons: [{ label: t("fr.later"), value: "later", cls: "ghost" }, { label: t("fr.never"), value: "never", cls: "ghost" }, { label: "▶ " + t("fr.start"), value: "go", cls: "primary" }],
      onOpen: (ov) => { $("#frProf", ov).onchange = (e) => (prof.v = e.target.value); },
    });
    if (go1 === "never") { call("firstrun.done", {}, { silent: true }).catch(() => {}); return; }
    if (go1 !== "go") return;
    call("firstrun.done", {}, { silent: true }).catch(() => {});
    await App.setProfile(prof.v);
    // Everything recommended for this machine that isn't applied yet (the image already carries the offline part), plus the look.
    const ids = S.plan.items.filter((i) => (i.recommended || S.byId[i.id].category === "look") && S.status[i.id] !== "applied").map((i) => i.id);
    go("tune");
    await App.apply(ids, { restorePoint: true });
  };

  document.addEventListener("DOMContentLoaded", boot);
})();
