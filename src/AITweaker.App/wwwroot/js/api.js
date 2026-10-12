/* Bridge to the C# host (WebView2). Outside WebView2 the same API is served by a built-in simulator
   so the interface can be previewed in any browser. */
(function () {
  const listeners = {};
  const on = (event, fn) => {
    (listeners[event] ||= []).push(fn);
    return () => { listeners[event] = listeners[event].filter((f) => f !== fn); };
  };
  const emit = (event, data) => (listeners[event] || []).forEach((fn) => { try { fn(data); } catch (e) { console.error(e); } });

  const webview = window.chrome && window.chrome.webview;
  let seq = 0;
  const pending = new Map();

  function realCall(method, params) {
    return new Promise((resolve, reject) => {
      const id = ++seq;
      pending.set(id, { resolve, reject });
      webview.postMessage({ id, method, params: params || {} });
    });
  }

  if (webview) {
    webview.addEventListener("message", (e) => {
      const m = e.data;
      if (m && m.event) return emit(m.event, m.data);
      const p = pending.get(m.id);
      if (!p) return;
      pending.delete(m.id);
      m.ok ? p.resolve(m.result) : p.reject(new Error(m.error || "error"));
    });
  }

  // ------------------------------------------------------------------ simulator

  const D = window.MOCK_DATA || { categories: [], tweaks: [], apps: [], guides: [], bios: [] };
  const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
  const now = () => new Date().toISOString();
  const log = (level, source, text) => emit("log", { time: now(), level, source, text });
  const mock = { applied: new Set(), sessions: [], settings: { language: "ru", theme: "auto", accent: "system", profile: "Performance", model: "claude-opus-5-5", hasKey: false, pingHost: "1.1.1.1", createRestorePoint: true } };

  const profile = {
    os: { caption: "Microsoft Windows 11 Pro", build: 26200, ubr: 6584, edition: "Professional", displayVersion: "25H2" },
    cpu: { name: "AMD Ryzen 7 9850X3D 8-Core Processor", vendor: "AMD", cores: 8, threads: 16, maxClockMhz: 4700, isHybrid: false, pCores: 8, eCores: 0, isX3D: true, microcodeRevision: 192, topology: [] },
    gpus: [{ name: "NVIDIA GeForce RTX 5070 Ti", vendor: "NVIDIA", driverVersion: "32.0.15.8157", driverDate: "2026-03-02T00:00:00", vramMb: 16303, isDiscrete: true }],
    memory: { totalGb: 32, type: "DDR5", configuredSpeed: 4800, xmpLikelyOff: true, modules: [{ capacityGb: 16, speed: 6000, configuredSpeed: 4800 }, { capacityGb: 16, speed: 6000, configuredSpeed: 4800 }] },
    disks: [{ model: "Samsung SSD 990 PRO 2TB", sizeGb: 2000, mediaType: "SSD", busType: "NVMe", isSystem: true }, { model: "WDC WD20EZBX", sizeGb: 2000, mediaType: "HDD", busType: "SATA", isSystem: false }],
    network: [{ name: "Ethernet", description: "Realtek Gaming 2.5GbE Family Controller", kind: "ethernet", speedMbps: 1000, connected: true }],
    displays: [{ name: "ASUS VG27AQ3A", device: "\\\\.\\DISPLAY1", width: 2560, height: 1440, refreshHz: 60, maxRefreshHz: 180 }],
    board: { manufacturer: "ASUSTeK COMPUTER INC.", product: "TUF GAMING B850-PLUS WIFI", biosVersion: "1402", biosDate: "2026-02-11T00:00:00", vendorKey: "asus" },
    machine: { manufacturer: "ASUS", model: "System Product Name", isLaptop: false, hasBattery: false, isVirtualMachine: false },
    security: { secureBoot: true, tpm: true, vbsRunning: true, hvciRunning: true, hypervisorPresent: true },
    problemDevices: [],
    hasBluetooth: true, hasPrinter: false,
    apps: ["steam", "discord", "chrome", "edge", "obs", "spotify", "nvidiaApp", "exitlag", "epic", "store", "onedrive"],
    systemDriveFreePercent: 41,
  };
  const facts = {
    "os.build": 26200, "os.win11": true, "cpu.vendor": "AMD", "cpu.name": profile.cpu.name, "cpu.cores": 8, "cpu.threads": 16, "cpu.hybrid": false,
    "cpu.x3d": true, "cpu.x3dMultiCcd": false, "cpu.intelGen": 0, "gpu.nvidia": true, "gpu.amd": false, "gpu.intel": false, "gpu.discrete": true,
    "ram.gb": 32, "ram.type": "DDR5", "ram.speed": 4800, "ram.xmpOff": true, "disk.systemSsd": true, "disk.systemNvme": true, "disk.hdd": true,
    "system.laptop": false, "system.desktop": true, "system.battery": false, "system.vm": false, "net.wifi": false, "net.ethernet": true, "net.wifiOnly": false,
    "bluetooth": true, "printer": false, "display.maxHzUnused": true, "sec.secureBoot": true, "sec.tpm": true, "sec.vbs": true, "sec.hvci": true,
  };
  profile.apps.forEach((a) => (facts["app." + a] = true));

  const insights = [
    { id: "display.hz.1", severity: "critical", title: { ru: "Монитор работает на 60 Гц вместо 180 Гц", en: "Monitor runs at 60 Hz instead of 180 Hz" }, detail: { ru: "ASUS VG27AQ3A: включите максимальную частоту обновления — это самый заметный прирост плавности.", en: "ASUS VG27AQ3A: switch to the maximum refresh rate — the most noticeable smoothness upgrade." }, action: "tweak:display.maxhz", metric: "60→180 Hz" },
    { id: "ram.xmp", severity: "warning", title: { ru: "Память работает на 4800 МГц", en: "RAM runs at 4800 MHz" }, detail: { ru: "Похоже, профиль XMP/EXPO выключен в BIOS — вы теряете 5–15% FPS в CPU-зависимых играх.", en: "XMP/EXPO looks disabled in BIOS — that costs 5–15% FPS in CPU-bound games." }, action: "bios", metric: "4800 MHz" },
    { id: "sec.hvci", severity: "warning", title: { ru: "Включена целостность памяти (HVCI/VBS)", en: "Memory integrity (HVCI/VBS) is on" }, detail: { ru: "Защита на уровне гипервизора стоит до 5–10% FPS. Можно отключить в TWEAKS → Безопасность (с подтверждением).", en: "Hypervisor-level protection costs up to 5–10% FPS. Can be disabled in TWEAKS → Security (asks first)." }, action: "tweak:security.vbs.off" },
    { id: "gpu.driver.old", severity: "warning", title: { ru: "Драйвер видеокарты от 02.03.2026", en: "GPU driver from 2026-03-02" }, detail: { ru: "Драйверу NVIDIA GeForce RTX 5070 Ti больше полугода. Свежие драйверы дают прирост FPS в новых играх.", en: "The NVIDIA GeForce RTX 5070 Ti driver is over six months old." }, action: "drivers", metric: "32.0.15.8157" },
  ];

  function evalWhen(expr) {
    if (!expr) return true;
    try {
      const js = expr.replace(/[A-Za-z_][A-Za-z0-9_.]*/g, (id) => (["true", "false", "null"].includes(id) ? id : `F(${JSON.stringify(id)})`)).replace(/~/g, "&&");
      // eslint-disable-next-line no-new-func
      return !!Function("F", `return (${js});`)((k) => facts[k]);
    } catch { return true; }
  }

  const levels = { Safe: 1, Balanced: 2, Performance: 3, Extreme: 4 };
  const statusOf = (t) => (mock.applied.has(t.id) ? "applied" : evalWhen(t.when) ? "off" : "n/a");

  const handlers = {
    "app.init": async () => ({ version: "1.0.0", isAdmin: true, machine: "DEMO-RIG", system: { accent: "#0078D4", dark: false }, settings: { ...mock.settings }, models: ["claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5"], categories: D.categories, log: [], demo: true }),
    "system.scan": async () => {
      log("info", "scan", "scanning hardware and Windows configuration…");
      await sleep(1400);
      log("ok", "scan", `scan finished in 1386 ms: ${profile.cpu.name}, ${profile.gpus[0].name}, 32 GB`);
      insights.filter((i) => i.severity !== "info").forEach((i) => log(i.severity === "critical" ? "error" : "warn", "diag", i.title.en));
      return { profile, facts, insights };
    },
    "plan.build": async ({ profile: p }) => {
      const lvl = levels[p] || 3;
      const items = D.tweaks.filter((t) => evalWhen(t.when)).map((t) => ({
        id: t.id, recommended: t.level > 0 && t.level <= lvl, needsConfirm: !!t.confirm || t.risk === "high",
        reason: t.why || { ru: "Рекомендовано для вашей системы.", en: "Recommended for your system." },
      }));
      return { profile: p, items, skipped: D.tweaks.filter((t) => !evalWhen(t.when)).map((t) => t.id) };
    },
    "tweaks.catalog": async () => D.tweaks,
    "tweaks.status": async () => Object.fromEntries(D.tweaks.map((t) => [t.id, statusOf(t)])),
    "tweaks.apply": async ({ ids, restorePoint }) => {
      const list = D.tweaks.filter((t) => ids.includes(t.id));
      if (restorePoint) { log("info", "restore", "creating a system restore point…"); await sleep(700); log("ok", "restore", "restore point created"); }
      const results = [];
      for (let i = 0; i < list.length; i++) {
        const t = list[i];
        emit("apply.progress", { i: i + 1, n: list.length, id: t.id });
        log("info", t.id, "applying: " + t.name.en);
        await sleep(90 + Math.random() * 160);
        mock.applied.add(t.id);
        log("ok", t.id, `done in ${Math.round(8 + Math.random() * 60)} ms`);
        results.push({ id: t.id, ok: true });
      }
      mock.sessions.unshift({ id: Math.random().toString(16).slice(2, 14), time: now(), kind: "apply", profile: mock.settings.profile, applied: ids, failed: [], restorePoint });
      return { results, restorePointCreated: !!restorePoint, rebootRequired: list.some((t) => t.reboot), logoffRequired: list.some((t) => t.logoff) };
    },
    "tweaks.revert": async ({ ids }) => {
      for (const id of ids) { await sleep(80); mock.applied.delete(id); log("ok", id, "rolled back"); }
      return ids.map((id) => ({ id, ok: true }));
    },
    "backup.list": async () => ({
      applied: [...mock.applied].map((id) => ({ tweakId: id, appliedAt: now(), changes: 2, name: (D.tweaks.find((t) => t.id === id) || {}).name })),
      sessions: mock.sessions, folder: "C:\\ProgramData\\AITweaker\\backups",
    }),
    "restore.create": async () => { await sleep(800); log("ok", "restore", "restore point created"); return true; },
    "appx.list": async () => {
      await sleep(600);
      const installed = new Set(D.apps.map((a) => a.package).filter((_, i) => i % 7 !== 3));
      return {
        packages: D.apps.filter((a) => installed.has(a.package)).map((a) => ({ ...a, installed: true, keep: a.keepWhen ? evalWhen(a.keepWhen) : false })),
        others: [{ name: "SpotifyAB.SpotifyMusic", version: "1.2.60" }, { name: "5319275A.WhatsAppDesktop", version: "2.2540" }, { name: "Microsoft.MinecraftUWP", version: "1.21" }],
      };
    },
    "appx.remove": async ({ names }) => {
      const r = {};
      for (const n of names) { emit("appx.progress", n); await sleep(250); log("ok", "debloat", "removed " + n); r[n] = true; }
      return r;
    },
    "drivers.list": async () => [
      { category: "gpu", name: profile.gpus[0].name, vendor: "NVIDIA", version: "32.0.15.8157", date: "2026-03-02T00:00:00", status: "old", links: [{ title: "NVIDIA App", url: "https://www.nvidia.com/en-us/software/nvidia-app/" }, { title: "GeForce drivers", url: "https://www.nvidia.com/en-us/drivers/" }] },
      { category: "chipset", name: profile.cpu.name, vendor: "AMD", status: "check", links: [{ title: "AMD Chipset Drivers", url: "https://www.amd.com/en/support/download/drivers.html" }] },
      { category: "network", name: profile.network[0].description, vendor: "ethernet", status: "ok", links: [{ title: "ASUS Support", url: "https://www.asus.com/support/download-center/" }] },
    ],
    "drivers.rescan": async () => { await sleep(900); return { exitCode: 0 }; },
    "guides.list": async () => D.guides.filter((g) => evalWhen(g.when)).map((g) => ({ ...g, installed: !g.detect || evalWhen(g.detect) })).sort((a, b) => b.installed - a.installed),
    "bios.list": async () => ({
      board: { manufacturer: profile.board.manufacturer, product: profile.board.product, biosVersion: profile.board.biosVersion, biosDate: profile.board.biosDate, vendor: "asus" },
      cpu: { name: profile.cpu.name, microcode: "0xC0" }, ram: { type: "DDR5", speed: 4800, rated: 6000 }, security: profile.security,
      advice: D.bios.filter((b) => evalWhen(b.when)).map((b) => ({ id: b.id, title: b.title, detail: b.detail, value: b.value, priority: b.priority, path: b.paths.asus || b.paths.other, status: b.id === "xmp" ? "fix" : ["secureboot", "tpm"].includes(b.id) ? "ok" : "check" }))
        .sort((a, b) => ({ high: 0, medium: 1, low: 2 }[a.priority] - { high: 0, medium: 1, low: 2 }[b.priority])),
    }),
    "bios.reboot": async () => { log("warn", "system", "demo: reboot into BIOS skipped"); return true; },
    "system.reboot": async () => { log("warn", "system", "demo: reboot skipped"); return true; },
    "ame.run": async ({ playbook }) => {
      for (let v = 0; v <= 1; v += 0.1) { emit("ame.progress", { step: v < 0.45 ? "AME Wizard" : playbook, value: v }); await sleep(180); }
      log("ok", "ame", "demo: AME Wizard would launch now");
      return "C:\\Users\\demo\\AppData\\Local\\AITweaker\\downloads\\ame\\AtlasPlaybook.apbx";
    },
    "usb.list": async () => [{ number: 3, name: "SanDisk Ultra USB 3.0", sizeBytes: 30752000000, partitionStyle: "MBR" }],
    "usb.pickIso": async () => "C:\\Users\\demo\\Downloads\\Win11_25H2_Russian_x64.iso",
    "usb.downloadIso": async () => { for (let v = 0; v <= 1; v += 0.05) { emit("usb.progress", { step: "iso", value: v }); await sleep(120); } return "C:\\Users\\demo\\Downloads\\Win11_Russian_x64.iso"; },
    "usb.preview": async ({ options }) => `<?xml version="1.0" encoding="utf-8"?>\n<!-- Generated by AI Tweaker (demo preview) -->\n<unattend xmlns="urn:schemas-microsoft-com:unattend">\n  <!-- local account: ${(options && options.userName) || "User"} -->\n  ...\n</unattend>`,
    "usb.create": async () => {
      for (const [s, v] of [["clean", 0.02], ["format", 0.05], ["mount", 0.08], ["copy", 0.3], ["copy", 0.6], ["split", 0.85], ["unattend", 0.97], ["done", 1]]) { emit("usb.progress", { step: s, value: v }); log("info", "usb", s); await sleep(500); }
      return true;
    },
    "task.cancel": async () => true,
    "ai.analyze": async () => {
      if (!mock.settings.hasKey) throw new Error(mock.settings.language === "en" ? "Add your Claude API key in AI settings first." : "Сначала добавьте API-ключ Claude в настройках AI.");
      await sleep(1800);
      return {
        summary: mock.settings.language === "en" ? "Strong gaming desktop held back by 60 Hz, disabled EXPO and HVCI." : "Мощный игровой ПК, которому мешают 60 Гц на мониторе, выключенный EXPO и HVCI.",
        recommendations: [{ id: "display.maxhz", reason: "180 Hz monitor at 60 Hz." }, { id: "devices.interrupt-affinity", reason: "8 cores: GPU/NIC/USB interrupts off core 0." }, { id: "gaming.hags", reason: "RTX 50: needed for DLSS frame generation." }],
        avoid: [{ id: "security.cpu-mitigations.off", reason: "Zen 5 has near-zero mitigation overhead." }],
        tips: ["Enable EXPO I in BIOS (Ai Tweaker → Ai Overclock Tuner).", "Cap FPS at 177 with G-SYNC + Reflex."],
        model: mock.settings.model,
      };
    },
    "settings.set": async (p) => { Object.assign(mock.settings, p); if ("apiKey" in p) mock.settings.hasKey = !!p.apiKey; delete mock.settings.apiKey; return { hasKey: mock.settings.hasKey }; },
    "report.export": async () => { log("ok", "report", "demo: report would be saved to the desktop"); return "Desktop\\AI-Tweaker-report.html"; },
    "shell.open": async ({ url }) => { window.open(url, "_blank", "noopener"); return true; },
    "shell.folder": async () => true,
    "restore.open": async () => true,
    "window.theme": async () => true,
    "system.theme": async () => ({ accent: "#0078D4", dark: false }),
    "net.test": async ({ custom }) => {
      const hosts = [["1.1.1.1", "Cloudflare", 9], ["8.8.8.8", "Google DNS", 14], ["9.9.9.9", "Quad9", 21]].concat(custom ? [[custom, custom, 38]] : []);
      for (let i = 1; i <= 30; i++) { emit("net.progress", { host: hosts[i % hosts.length][0], i, n: 30 }); await sleep(45); }
      return hosts.map(([host, label, base]) => ({ host, label, sent: 30, received: 30, avgMs: base + 0.4, minMs: base - 2, maxMs: base + 6, jitterMs: +(1 + Math.random() * 3).toFixed(1), lossPercent: 0 }));
    },
    "net.flushdns": async () => ({ exitCode: 0 }),
    "net.reset": async () => true,
    "startup.list": async () => mock.startup,
    "startup.set": async ({ id, enabled }) => { const e = mock.startup.find((x) => x.id === id); if (e) e.enabled = enabled; log("info", "startup", `${id} -> ${enabled}`); return true; },
    "clean.analyze": async () => { await sleep(500); return [["temp-user", 912e6, 4210], ["temp-windows", 214e6, 388], ["shader-nvidia", 1.9e9, 1630], ["shader-amd", 0, 0], ["shader-directx", 640e6, 2210], ["windows-update", 3.1e9, 77], ["crash-dumps", 88e6, 12]].map(([id, bytes, files]) => ({ id, path: "", bytes, files })); },
    "clean.run": async ({ ids }) => { await sleep(900); return ids.length * 3.2e8; },
    "memory.purge": async () => { await sleep(300); log("ok", "memory", "standby list purged"); return { freedBytes: 2.7e9, availableBytes: 24e9 }; },
    "explorer.restart": async () => true,
  };

  mock.startup = [
    { id: "HKCU|Run|Discord", name: "Discord", command: "C:\\Users\\demo\\AppData\\Local\\Discord\\Update.exe --processStart Discord.exe", location: "HKCU Run", enabled: true, scope: "user" },
    { id: "HKCU|Run|Steam", name: "Steam", command: "\"C:\\Program Files (x86)\\Steam\\steam.exe\" -silent", location: "HKCU Run", enabled: true, scope: "user" },
    { id: "HKLM|Run|RtkAudUService", name: "RtkAudUService", command: "C:\\Windows\\System32\\RtkAudUService64.exe -background", location: "HKLM Run", enabled: true, scope: "all users" },
    { id: "HKCU|Run|OneDrive", name: "OneDrive", command: "\"C:\\Program Files\\Microsoft OneDrive\\OneDrive.exe\" /background", location: "HKCU Run", enabled: false, scope: "user" },
  ];

  function startMockTelemetry() {
    let cpu = 6, ram = 38, ping = 14, gpu = 3, gpuT = 41, cpuT = 46, tick = 0;
    setInterval(() => {
      cpu = Math.max(1, Math.min(100, cpu + (Math.random() - 0.5) * 8 + (Math.random() < 0.06 ? 25 : 0) * (cpu < 40 ? 1 : -1)));
      ram = Math.max(20, Math.min(90, ram + (Math.random() - 0.5) * 1.2));
      ping = Math.max(6, Math.min(60, ping + (Math.random() - 0.5) * 4 + (Math.random() < 0.04 ? 18 : 0)));
      gpu = Math.max(0, Math.min(100, gpu + (Math.random() - 0.5) * 10 + (Math.random() < 0.05 ? 45 : 0) * (gpu < 30 ? 1 : -1)));
      gpuT += ((38 + gpu * 0.42) - gpuT) * 0.15; cpuT += ((42 + cpu * 0.45) - cpuT) * 0.2;
      if (++tick % 2 === 0) emit("sensors", { time: now(), cpuTempC: +cpuT.toFixed(1), cpuTempSource: "LibreHardwareMonitor", gpuTempC: +gpuT.toFixed(1), gpuLoad: +gpu.toFixed(1), gpuFanRpm: gpuT > 50 ? Math.round(900 + (gpuT - 50) * 60) : null, gpuPowerPercent: +(12 + gpu * 0.8).toFixed(1), timerResolutionMs: gpu > 30 ? 0.5 : 15.625, disks: [{ name: "Samsung SSD 990 PRO 2TB", tempC: 44, tempMaxC: 61, wearPercent: 2 }, { name: "WDC WD20EZBX", tempC: 36, tempMaxC: 44, wearPercent: null }] });
      emit("telemetry", { time: now(), cpu: +cpu.toFixed(1), ram: Math.round(ram), ramUsedGb: +(ram * 0.32).toFixed(1), ramTotalGb: 32, pingMs: Math.round(ping), processes: 180 + Math.round(Math.random() * 12) });
    }, 1000);
  }

  async function mockCall(method, params) {
    const h = handlers[method];
    if (!h) throw new Error("Unknown method " + method);
    await sleep(60);
    return h(params || {});
  }

  window.API = {
    demo: !webview,
    call: webview ? realCall : mockCall,
    on,
    start() { if (!webview) startMockTelemetry(); },
  };
})();
