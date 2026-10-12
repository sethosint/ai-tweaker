/* A small set of line icons drawn for AI Tweaker (24×24 grid, 1.75 stroke, currentColor). */
(function () {
  const P = {
    search: '<circle cx="11" cy="11" r="6.5"/><path d="m16 16 4 4"/>',
    settings: '<path d="M4 7h10M18 7h2M4 17h4M12 17h8"/><circle cx="16" cy="7" r="2"/><circle cx="10" cy="17" r="2"/>',
    refresh: '<path d="M19.5 12a7.5 7.5 0 1 1-2.2-5.3"/><path d="M19.5 4.5v4h-4"/>',
    check: '<path d="m5 12.5 4.2 4.2L19 7"/>',
    x: '<path d="M6 6l12 12M18 6 6 18"/>',
    external: '<path d="M14 5h5v5"/><path d="M19 5l-8 8"/><path d="M18 14v4a1 1 0 0 1-1 1H6a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1h4"/>',
    alert: '<path d="M12 4 3 19.5h18L12 4Z"/><path d="M12 10v4.5"/><path d="M12 17.2v.1"/>',
    arrow: '<path d="M5 12h14"/><path d="m13 6 6 6-6 6"/>',
    download: '<path d="M12 4v11"/><path d="m7 10 5 5 5-5"/><path d="M5 19.5h14"/>',
    power: '<path d="M12 4v7"/><path d="M7.2 6.8a7 7 0 1 0 9.6 0"/>',
  };
  window.icon = (name, cls = "") => `<svg class="ico ${cls}" viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="1.75" stroke-linecap="round" stroke-linejoin="round">${P[name] || ""}</svg>`;
  /** Brand mark: two tuning sliders in a rounded square. */
  window.brandMark = () => `<svg class="brand-mark" viewBox="0 0 28 28" aria-hidden="true"><rect width="28" height="28" rx="8" fill="var(--accent)"/><g stroke="var(--accent-ink)" stroke-width="2" stroke-linecap="round"><path d="M7.5 10.5h13M7.5 17.5h13" opacity=".45"/></g><g fill="var(--accent-ink)"><circle cx="17" cy="10.5" r="2.6"/><circle cx="11" cy="17.5" r="2.6"/></g></svg>`;
})();
