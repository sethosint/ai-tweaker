/* "By hand": everything only the user can do — BIOS, the GPU app, in-game settings, Windows switches, hardware. */
(function () {
  const App = (window.App ||= {});
  const V = (window.Views ||= {});
  const KEY = "aitweaker.manual.done";

  // Ticks are a per-viewer convenience: kept in this browser profile, never needed for anything to work.
  const loadDone = () => { try { return new Set(JSON.parse(localStorage.getItem(KEY) || "[]")); } catch { return new Set(); } };
  const saveDone = (set) => { try { localStorage.setItem(KEY, JSON.stringify([...set])); } catch { /* private mode */ } };
  let done = loadDone();

  /** Sections and their items. BIOS items come from the firmware check; "auto" means the scan already sees it done. */
  App.manualSections = () => {
    const f = (S.scan && S.scan.facts) || {};
    const fw = (S.bios && S.bios.firmware) || {};
    // the BIOS update item opens the maker's download page for this exact model
    const bios = S.bios ? S.bios.advice.map((a) => ({ id: "bios." + a.id, bios: a, auto: a.status === "ok", url: a.id === "bios-update" && fw.support ? fw.support : undefined })) : [];
    return [
      { id: "bios", icon: "bios", items: bios },
      { id: "gpu", icon: "drivers", items: [
        f["gpu.nvidia"] ? { id: "nvapp", go: () => go("apps", { guide: "nvidia-app" }) } : null,
        f["gpu.amd"] ? { id: "amdapp", go: () => go("apps", { guide: "amd-software" }) } : null,
        { id: "vrr" },
        { id: "undervolt" },
        { id: "ddu", url: "https://www.wagnardsoft.com/display-driver-uninstaller-ddu-" },
      ] },
      { id: "games", icon: "gamepad", items: [{ id: "reflex" }, { id: "cap" }, { id: "ingame" }, ...gameItems(), { id: "gamebar" }, { id: "overlays", go: () => go("apps", { guide: "discord" }) }] },
      { id: "windows", icon: "settings", items: [
        { id: "dnd", url: "ms-settings:notifications" },
        { id: "update", url: "ms-settings:windowsupdate" },
        { id: "audio", url: "ms-settings:sound" },
        { id: "defender", url: "windowsdefender://threatsettings" },
      ] },
      { id: "hardware", icon: "monitor", items: [
        { id: "monitor", go: () => go("tools") },
        { id: "mouse" },
        f["net.wifiOnly"] ? { id: "cable" } : null,
        { id: "router", url: "https://www.waveform.com/tools/bufferbloat" },
        f["net.wifi"] ? { id: "wifi" } : null,
        { id: "testufo", url: "https://www.testufo.com/frameskipping" },
        { id: "stress" },
      ] },
    ].map((s) => ({ ...s, items: s.items.filter(Boolean) })).filter((s) => s.items.length || s.id === "bios");
  };

  /** Competitive settings for the popular games this PC actually has (found by the Games panel). */
  const gameItems = () => {
    const names = ((S.games && S.games.games) || []).map((g) => g.name.toLowerCase());
    return [["cs2", /counter-strike 2|cs2/], ["valorant", /valorant/], ["fortnite", /fortnite/]]
      .filter(([, rx]) => names.some((n) => rx.test(n))).map(([id]) => ({ id }));
  };

  const isDone = (it) => it.auto || done.has(it.id);

  /** How to get into this maker's setup: keys, its own tools, and the exact model to look up. */
  const firmwareKeys = (fw) => {
    const keys = [["fw.enter", fw.enter], ["fw.advanced", fw.advanced], ["fw.defaults", fw.defaults], ["fw.save", fw.save]].filter(([, k]) => k);
    return html`<div class="fw">
      <div class="fw-keys">${fw.name ? `<b>${esc(fw.name)}</b>` : ""}${keys.map(([label, k]) => `<span><span class="kbd">${esc(k)}</span>${t(label)}</span>`).join("")}</div>
      ${fw.hint ? `<div class="fw-hint">${esc(L(fw.hint))}</div>` : ""}
      <div class="fw-hint">${t("fw.or")}${fw.app ? " " + t("fw.app").replace("{app}", `<b>${esc(fw.app)}</b>`) : ""}${fw.model ? ` <button class="btn sm ghost" data-copy="${esc(fw.model)}" title="${t("man.copy")}">${esc(fw.model)}${icon("copy")}</button>` : ""}</div>
    </div>`;
  };

  /** How many by-hand items are still open (for the Game setup card and the nav badge). */
  App.manualLeft = () => {
    const all = App.manualSections().flatMap((s) => s.items);
    return { left: all.filter((it) => !isDone(it)).length, total: all.length };
  };

  V.manual = async (opts = {}) => {
    const v = $("#v-manual");
    if (!S.scan) { v.innerHTML = `<div class="empty">${t("scanFirst")}</div>`; return; }
    if (!S.bios) { try { S.bios = await call("bios.list", {}, { silent: true }); } catch { S.bios = { advice: [], board: {}, cpu: {}, ram: {}, security: {} }; } }
    if (!S.games && App.loadGames) await App.loadGames();
    const secs = App.manualSections();
    const all = secs.flatMap((s) => s.items);
    const n = all.filter(isDone).length;
    const b = S.bios;

    const row = (it, i) => {
      const a = it.bios;
      const title = a ? esc(L(a.title)) : t("man." + it.id);
      const desc = a ? esc(L(a.detail)) : t("man." + it.id + "D");
      const action = it.go || it.url ? `<button class="btn sm ghost" data-act="${esc(it.id)}">${t(it.url ? "man.open" : "man.go")}${icon(it.url ? "external" : "arrow")}</button>` : "";
      return html`<div class="mn-row ${isDone(it) ? "done" : ""}">
        <label class="check" title="${t("man.mark")}"><input type="checkbox" data-done="${esc(it.id)}" ${isDone(it) ? "checked" : ""} ${it.auto ? "disabled" : ""}><span class="box"></span></label>
        <div><div class="mn-t">${title}${a ? ` <span class="chip ${a.priority === "high" ? "crit" : a.priority === "medium" ? "warn" : ""}">${t("bios.p." + a.priority)}</span>` : ""}${it.auto ? ` <span class="chip ok">${t("man.seen")}</span>` : ""}</div>
          <div class="mn-d">${desc}</div>
          ${a ? `<div class="mn-path"><code>${esc(a.path)}</code><span class="val">${esc(a.value)}</span></div>` : ""}</div>
        <div class="mn-a">${action}</div></div>`;
    };

    v.innerHTML = html`
      <div class="page-h"><div><h1>${t("man.title")}</h1><p>${t("man.pageD")}</p></div></div>
      <div class="mn-progress"><div><b>${n}</b> ${t("man.of")} ${all.length} ${t("man.doneN")}</div><div class="mn-bar"><i style="width:${all.length ? (n / all.length) * 100 : 0}%"></i></div></div>
      ${secs.map((s) => html`<section class="mn-sec" id="mn-${s.id}">
        <div class="mn-h">${icon(s.icon)}<div><b>${t("man.sec." + s.id + (s.id === "bios" && b.firmware && b.firmware.laptop ? "L" : ""))}</b><span>${t("man.sec." + s.id + (s.id === "bios" && b.firmware && b.firmware.laptop ? "LD" : "D"))}</span></div>
          <span class="n">${s.items.filter(isDone).length}/${s.items.length}</span>
          ${s.id === "bios" ? `<button class="btn sm danger" id="toBios">${icon("power")}${t("bios.reboot")}</button>` : ""}</div>
        ${s.id === "bios" && b.board && b.board.manufacturer ? html`<div class="mn-board">
          <span><b>${esc(b.board.manufacturer)}</b> ${esc(b.board.product || "")} · BIOS ${esc(b.board.biosVersion || "")}${b.board.biosDate ? " · " + fmtDate(b.board.biosDate) : ""}</span>
          <span>${esc(b.ram.type || "")} ${b.ram.speed || ""} / ${b.ram.rated || ""} MHz</span>
          <span>Secure Boot ${b.security.secureBoot ? t("bios.on") : t("bios.off")} · TPM ${b.security.tpm ? t("bios.on") : t("bios.off")}</span></div>` : ""}
        ${s.id === "bios" && b.firmware ? firmwareKeys(b.firmware) : ""}
        <div class="mn-list">${s.items.map(row).join("") || `<div class="mn-d" style="padding:12px 0">${t("man.none")}</div>`}</div>
      </section>`).join("")}`;

    v.querySelectorAll("[data-copy]").forEach((btn) => (btn.onclick = () => {
      navigator.clipboard.writeText(btn.dataset.copy).then(() => toast(t("man.copied")), () => {});
    }));
    v.querySelectorAll("[data-done]").forEach((cb) => (cb.onchange = () => {
      cb.checked ? done.add(cb.dataset.done) : done.delete(cb.dataset.done);
      saveDone(done);
      V.manual();
    }));
    v.querySelectorAll("[data-act]").forEach((btn) => (btn.onclick = () => {
      const it = all.find((x) => x.id === btn.dataset.act);
      if (it.url) call("shell.open", { url: it.url }).catch(() => {}); else it.go();
    }));
    const toBios = $("#toBios");
    if (toBios) toBios.onclick = async () => { if (await confirmBox(t("bios.reboot"), t("bios.rebootQ"), t("bios.reboot"), "danger", true)) call("bios.reboot"); };
    if (opts.section) { const el = $("#mn-" + opts.section); if (el) el.scrollIntoView({ block: "start" }); }
  };
})();
