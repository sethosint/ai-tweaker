/* Line icons drawn for AI Tweaker (24×24 grid, 1.75 stroke, currentColor) and the brand mark. */
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
    gamepad: '<path d="M7.5 8h9a4 4 0 0 1 4 4.2l-.3 3.3a2.6 2.6 0 0 1-4.6 1.4L14.2 15H9.8l-1.4 1.9a2.6 2.6 0 0 1-4.6-1.4l-.3-3.3A4 4 0 0 1 7.5 8Z"/><path d="M8 10.5v3M6.5 12h3"/><path d="M15.5 11.2h.1M17 12.8h.1"/>',
    shield: '<path d="M12 3.5 5 6v5.5c0 4.2 3 7.6 7 9 4-1.4 7-4.8 7-9V6l-7-2.5Z"/>',
    hand: '<path d="M8 13V6.5a1.5 1.5 0 0 1 3 0V12"/><path d="M11 11.5V5a1.5 1.5 0 0 1 3 0v6.5"/><path d="M14 11.5V7a1.5 1.5 0 0 1 3 0v7a6 6 0 0 1-6 6h-.6a5 5 0 0 1-3.9-1.9L4 15a1.5 1.5 0 0 1 2.3-1.9L8 15"/>',
    // navigation
    dashboard: '<path d="M4.5 17.5a8 8 0 1 1 15 0"/><path d="m12 14 3.5-4.5"/><circle cx="12" cy="14" r="1.2"/>',
    tune: '<path d="M13 3.5 6 13.5h5.5L10.5 20.5 18 10h-5.5Z"/>',
    tweaks: '<path d="M4 6.5h10M18 6.5h2M4 12h3M11 12h9M4 17.5h12M20 17.5h0"/><circle cx="16" cy="6.5" r="2"/><circle cx="9" cy="12" r="2"/><circle cx="18" cy="17.5" r="2"/>',
    debloat: '<path d="M5 7h14"/><path d="M9.5 7V5h5v2"/><path d="M7 7l.8 12h8.4L17 7"/><path d="M10.5 10.5v5M13.5 10.5v5"/>',
    install: '<path d="M4 15v3.5h16V15"/><path d="M12 4v10"/><path d="m8 10 4 4 4-4"/>',
    tools: '<path d="M14.5 5.5a3.8 3.8 0 0 0 4.9 4.9l-8.9 8.9a2 2 0 0 1-2.8-2.8l8.9-8.9Z"/><path d="M14.5 5.5 16.6 3.4"/>',
    services: '<rect x="4" y="4.5" width="16" height="6" rx="1"/><rect x="4" y="13.5" width="16" height="6" rx="1"/><path d="M7.5 7.5h.01M7.5 16.5h.01M11 7.5h5M11 16.5h5"/>',
    copy: '<rect x="8.5" y="8.5" width="11" height="11" rx="2"/><path d="M15.5 8.5V6a1.5 1.5 0 0 0-1.5-1.5H6A1.5 1.5 0 0 0 4.5 6v8A1.5 1.5 0 0 0 6 15.5h2.5"/>',
    drivers: '<rect x="7" y="7" width="10" height="10" rx="1"/><path d="M10 3.5V7M14 3.5V7M10 17v3.5M14 17v3.5M3.5 10H7M3.5 14H7M17 10h3.5M17 14h3.5"/>',
    apps: '<rect x="4" y="4" width="6.5" height="6.5" rx="1"/><rect x="13.5" y="4" width="6.5" height="6.5" rx="1"/><rect x="4" y="13.5" width="6.5" height="6.5" rx="1"/><rect x="13.5" y="13.5" width="6.5" height="6.5" rx="1"/>',
    manual: '<path d="M8 13V6.5a1.5 1.5 0 0 1 3 0V12"/><path d="M11 11.5V5a1.5 1.5 0 0 1 3 0v6.5"/><path d="M14 11.5V7a1.5 1.5 0 0 1 3 0v7a6 6 0 0 1-6 6h-.6a5 5 0 0 1-3.9-1.9L4 15a1.5 1.5 0 0 1 2.3-1.9L8 15"/>',
    monitor: '<rect x="3.5" y="4.5" width="17" height="11.5" rx="1.2"/><path d="M9 20h6M12 16v4"/>',
    bios: '<rect x="3.5" y="5" width="17" height="14" rx="1"/><path d="M3.5 9h17"/><path d="M7 13h4M7 16h7"/>',
    image: '<circle cx="12" cy="12" r="8"/><circle cx="12" cy="12" r="2.2"/><path d="M12 4a8 8 0 0 1 8 8"/>',
    ai: '<path d="M12 4.5v3M12 16.5v3M4.5 12h3M16.5 12h3"/><circle cx="12" cy="12" r="4.5"/>',
    backup: '<path d="M4.5 12a7.5 7.5 0 1 0 2.2-5.3"/><path d="M4.5 4.5v4h4"/><path d="M12 8v4.5l3 2"/>',
  };
  window.icon = (name, cls = "") => `<svg class="ico ${cls}" viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="1.75" stroke-linecap="round" stroke-linejoin="round">${P[name] || ""}</svg>`;

  /** Brand mark: corner brackets (like the panels) around a gauge needle turned to the top. */
  window.brandMark = () => `<svg class="brand-mark" viewBox="0 0 32 32" aria-hidden="true" fill="none" stroke="currentColor"><path d="M3 10V3h7M22 3h7v7M29 22v7h-7M10 29H3v-7" stroke-width="2.4" stroke-linecap="square"/><path d="M10.5 21.5 20 12" stroke-width="2.8" stroke-linecap="round"/><circle cx="21.6" cy="10.4" r="3.1" fill="var(--signal)" stroke="none"/></svg>`;
})();
