/* Updates: a newer release on GitHub shows a button in the top bar; one click downloads it, checks its SHA-256 against
   the release and restarts AI Tweaker on the new version. */
(function () {
  const App = (window.App ||= {});
  const st = { info: null, checking: false };

  /** The top-bar button while a newer version waits; renderShell calls it again after redrawing the bar. */
  App.updatePill = () => {
    const slot = $("#updSlot");
    if (!slot) return;
    const i = st.info;
    slot.innerHTML = i && i.newer ? `<button class="upd-pill" id="updPill" title="${t("upd.pillTitle")}">${icon("update")}<span>${t("upd.pill")}</span><b>v${esc(i.version)}</b></button>` : "";
    if ($("#updPill")) $("#updPill").onclick = () => App.updateDialog();
  };

  /** Asks GitHub for the newest release; manual checks report "up to date" and errors, the start-up check stays quiet. */
  App.checkUpdate = async (manual = false) => {
    if (st.checking) return st.info;
    st.checking = true;
    try {
      st.info = await call("update.check", {}, { silent: true });
    } catch {
      if (manual) toast(t("upd.checkFail"), "error", 6000);
      return null;
    } finally {
      st.checking = false;
    }
    App.updatePill();
    if (manual) st.info.newer ? App.updateDialog() : toast(t("upd.latest").replace("{v}", S.init.version), "ok");
    return st.info;
  };

  /** Release notes in the UI language: the Russian part comes before "---", the English one after it. */
  function notes(md) {
    const parts = String(md || "").split(/\n-{3,}\n/);
    const text = (S.lang === "en" && parts.length > 1 ? parts[parts.length - 1] : parts[0]).trim();
    const inline = (s) => esc(s).replace(/\*\*(.+?)\*\*/g, "<b>$1</b>").replace(/\[([^\]]+)\]\([^)]+\)/g, "$1");
    let out = "", list = false;
    for (const line of text.split("\n")) {
      const l = line.trim();
      const item = /^[-*]\s+(.*)/.exec(l) || /^\d+\.\s+(.*)/.exec(l);
      if (item) { if (!list) { out += "<ul>"; list = true; } out += `<li>${inline(item[1])}</li>`; continue; }
      if (list) { out += "</ul>"; list = false; }
      if (!l || /^#\s|^##\s/.test(l)) continue; // the release title is already the dialog title
      out += /^#{3,}\s/.test(l) ? `<h4>${inline(l.replace(/^#+\s*/, ""))}</h4>` : `<p>${inline(l)}</p>`;
    }
    return out + (list ? "</ul>" : "");
  }

  App.updateDialog = async () => {
    const i = st.info;
    if (!i || !i.newer) return;
    const mb = i.size ? `${(i.size / 1048576).toFixed(0)} ${t("upd.mb")}` : "";
    const res = await modal({
      title: t("upd.title").replace("{v}", i.version),
      body: html`<div class="upd-head">
          <div class="upd-v"><span>${t("upd.current")}</span><b>${esc(S.init.version)}</b></div>${icon("arrow")}
          <div class="upd-v new"><span>${t("upd.new")}</span><b>${esc(i.version)}</b></div>
          <div class="upd-m">${i.published ? fmtDate(i.published) : ""}${mb ? " · " + mb : ""}</div></div>
        <div class="upd-notes">${notes(i.notes) || `<p>${t("upd.noNotes")}</p>`}</div>
        <div class="td" style="color:var(--muted)">${t("upd.how")}</div>`,
      buttons: [{ label: t("upd.later"), value: false, cls: "ghost" }, { label: t("upd.go"), value: true, cls: "primary" }],
    });
    if (res === true) install();
  };

  async function install() {
    if (S.busy) return;
    const b = busy(t("upd.busy"));
    const off = API.on("update.progress", (v) => b.progress(v * 0.95, `${t("upd.download")} ${Math.round(v * 100)}%`));
    try {
      await call("update.install", {}, { silent: true });
    } catch (e) {
      off();
      b.done();
      toast(`${t("upd.fail")}: ${e.message}`, "error", 9000);
      return;
    }
    off();
    b.progress(1, t("upd.restarting")); // the window closes and the new version opens in a moment
    if (S.demo) setTimeout(() => { b.done(); toast(t("upd.demo"), "ok"); }, 1500);
  }

  /** After start: a note when this is a freshly updated version, then the quiet check. */
  App.updateOnStart = () => {
    if (S.init.updated) toast(t("upd.done").replace("{v}", S.init.version), "ok", 7000);
    if (S.init.settings.checkUpdates !== false) setTimeout(() => App.checkUpdate(false), 5000);
  };
})();
