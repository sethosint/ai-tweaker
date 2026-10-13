/* "What do you use?" — switch off every Windows feature the user doesn't need, after asking about each one. */
(function () {
  const App = (window.App ||= {});

  /**
   * Shows the questionnaire. Resolves with the tweak ids for the features the user doesn't use
   * (already applied ones are skipped), or null when cancelled.
   */
  App.featureWizard = async () => {
    let list;
    try { list = await call("features.list"); } catch { return null; }
    const used = Object.fromEntries(list.map((f) => [f.id, f.used]));
    const loses = (f) => f.tweaks.map((id) => S.byId[id]).filter(Boolean).map((tw) => L(tw.confirmText) || L(tw.description)).find(Boolean) || "";
    const pending = (f) => f.tweaks.filter((id) => S.status[id] !== "applied");
    const count = () => list.filter((f) => !used[f.id]).reduce((n, f) => n + pending(f).length, 0);

    const res = await modal({
      title: t("fw.title"),
      wide: true,
      body: html`<div>${t("fw.desc")}</div>
        <div class="fw-list">${list.map((f) => html`<div class="fw-row ${pending(f).length ? "" : "is-done"}">
          <div><div class="fw-name">${esc(L(f.name))}</div><div class="fw-lose">${esc(loses(f))}</div></div>
          ${pending(f).length ? `<button class="fw-use" data-f="${f.id}"><span>${t("fw.use")}</span><span class="switch ${used[f.id] ? "on" : ""}"></span></button>` : `<span class="status applied">${t("fw.off")}</span>`}
        </div>`).join("")}</div>`,
      buttons: [{ label: t("cancel"), value: false, cls: "ghost" }, { label: `${t("fw.go")} (${count()})`, value: true, cls: "primary" }],
      onOpen: (ov) => {
        const go = ov.querySelector(".modal-f [data-i='1']");
        ov.querySelectorAll("[data-f]").forEach((b) => (b.onclick = () => {
          used[b.dataset.f] = !used[b.dataset.f];
          $(".switch", b).classList.toggle("on", used[b.dataset.f]);
          go.textContent = `${t("fw.go")} (${count()})`;
        }));
      },
    });
    if (res !== true) return null;
    return list.filter((f) => !used[f.id]).flatMap(pending);
  };

  /** From the Auto-tune tab: ask, then apply right away (the answers count as the confirmations). */
  App.disableUnused = async () => {
    const ids = await App.featureWizard();
    if (ids && ids.length) await App.apply(ids, { preconfirmed: new Set(ids) });
  };
})();
