/* Live 3D hardware models (three.js r150, built procedurally — no model files).
   One WebGL context for the whole app: a fixed transparent canvas, every .hw3d slot is drawn into its own
   scissor rectangle (the three.js "multiple elements" technique). Models idle-rotate, react to load
   (fans spin faster, lights pulse) and can be turned with the mouse. */
(function () {
  const HW = (window.HW3D = {});
  const slots = new Set();
  let renderer = null, canvas = null, envMap = null, clock = null, failed = false, solo = false;
  // Software rendering (no GPU driver, RDP, some VMs): draw a few frames a second so the rest of the UI stays smooth.
  let slow = false, lastDraw = 0, lastFrame = 0, gapAvg = 16, frames = 0;
  const accentColor = () => new THREE.Color(getComputedStyle(document.documentElement).getPropertyValue("--accent").trim() || "#f5a524");

  // ------------------------------------------------------------------ geometry & materials

  /** Rounded box: an extruded rounded rectangle with a bevel, centred. */
  function rbox(w, h, d, r = 0.06, seg = 3) {
    r = Math.min(r, w / 2 - 0.001, h / 2 - 0.001, d / 2 - 0.001);
    const s = new THREE.Shape(), x = -w / 2 + r, y = -h / 2 + r, W = w - 2 * r, H = h - 2 * r;
    const q = Math.max(0.0005, Math.min(r * 0.8, W / 2, H / 2)); // corner radius of the inner outline; the bevel adds r
    s.moveTo(x + q, y); s.lineTo(x + W - q, y); s.absarc(x + W - q, y + q, q, -Math.PI / 2, 0);
    s.lineTo(x + W, y + H - q); s.absarc(x + W - q, y + H - q, q, 0, Math.PI / 2);
    s.lineTo(x + q, y + H); s.absarc(x + q, y + H - q, q, Math.PI / 2, Math.PI);
    s.lineTo(x, y + q); s.absarc(x + q, y + q, q, Math.PI, Math.PI * 1.5);
    const g = new THREE.ExtrudeGeometry(s, { depth: Math.max(0.001, d - 2 * r), bevelEnabled: true, bevelSegments: seg, steps: 1, bevelSize: r, bevelThickness: r, curveSegments: seg * 2 });
    g.center();
    return g;
  }
  const box = (w, h, d) => new THREE.BoxGeometry(w, h, d);
  const M = {};
  function materials() {
    if (M.ready) return M;
    const std = (o) => new THREE.MeshStandardMaterial(o);
    Object.assign(M, {
      ready: true,
      shroud: std({ color: 0x1b1e23, metalness: 0.75, roughness: 0.34 }),
      plastic: std({ color: 0x0e1013, metalness: 0.25, roughness: 0.5 }),
      gloss: std({ color: 0x0a0b0d, metalness: 0.4, roughness: 0.18 }),
      alu: std({ color: 0xb8bdc6, metalness: 1, roughness: 0.27 }),
      aluDark: std({ color: 0x5a6069, metalness: 1, roughness: 0.32 }),
      nickel: std({ color: 0xd2d6dc, metalness: 1, roughness: 0.18 }),
      pcb: std({ color: 0x101418, metalness: 0.2, roughness: 0.62 }),
      pcbGreen: std({ color: 0x1c5a3b, metalness: 0.15, roughness: 0.6 }),
      gold: std({ color: 0xd8a94c, metalness: 1, roughness: 0.28 }),
      chip: std({ color: 0x15171a, metalness: 0.3, roughness: 0.45 }),
      hole: std({ color: 0x050607, metalness: 0, roughness: 1 }),
      blade: std({ color: 0x14171b, metalness: 0.2, roughness: 0.42, side: THREE.DoubleSide }),
      glow: std({ color: 0xffffff, emissive: 0xffffff, emissiveIntensity: 1, metalness: 0, roughness: 0.4 }),
      ledGreen: std({ color: 0x50ff8a, emissive: 0x50ff8a, emissiveIntensity: 2.4 }),
    });
    return M;
  }
  /** A per-model glow material, tinted with the current accent. */
  const glowMat = () => { const m = materials().glow.clone(); m.color = accentColor(); m.emissive = accentColor(); return m; };
  function instanced(geo, mat, positions) {
    const im = new THREE.InstancedMesh(geo, mat, positions.length);
    const o = new THREE.Object3D();
    positions.forEach((p, i) => { o.position.set(p[0], p[1], p[2]); o.rotation.set(p[3] || 0, p[4] || 0, p[5] || 0); o.updateMatrix(); im.setMatrixAt(i, o.matrix); });
    return im;
  }
  function labelTexture(lines, w = 512, h = 128, bg = "#0d0f12", fg = "#d9d6cf") {
    const c = document.createElement("canvas"); c.width = w; c.height = h;
    const g = c.getContext("2d");
    g.fillStyle = bg; g.fillRect(0, 0, w, h);
    g.fillStyle = fg; g.textBaseline = "middle";
    lines.forEach((l, i) => { g.font = `${l.weight || 600} ${l.size || 40}px "Plex Mono", Consolas, monospace`; g.globalAlpha = l.alpha ?? 1; g.fillText(l.text, l.x ?? 24, l.y ?? h / 2 + i * 44); });
    const t = new THREE.CanvasTexture(c); t.encoding = THREE.sRGBEncoding; t.anisotropy = 4;
    return t;
  }

  // ------------------------------------------------------------------ models

  function fan(radius, glow) {
    const m = materials(), g = new THREE.Group(), rotor = new THREE.Group();
    const ring = new THREE.Mesh(new THREE.TorusGeometry(radius, radius * 0.08, 10, 48), m.gloss);
    const well = new THREE.Mesh(new THREE.CircleGeometry(radius * 0.98, 40), m.hole);
    well.position.z = -0.02;
    g.add(well, ring);
    for (let k = 0; k < 9; k++) {
      const b = new THREE.Mesh(box(radius * 0.78, radius * 0.26, 0.012), m.blade);
      b.position.x = radius * 0.46; b.rotation.x = 0.42;
      const arm = new THREE.Group(); arm.rotation.z = (k / 9) * Math.PI * 2; arm.add(b); rotor.add(arm);
    }
    const hub = new THREE.Mesh(new THREE.CylinderGeometry(radius * 0.28, radius * 0.28, 0.06, 32), m.plastic);
    hub.rotation.x = Math.PI / 2; hub.position.z = 0.02;
    const hubRing = new THREE.Mesh(new THREE.TorusGeometry(radius * 0.29, radius * 0.025, 8, 40), glow);
    hubRing.position.z = 0.05;
    rotor.add(hub);
    g.add(rotor, hubRing);
    g.userData.rotor = rotor;
    return g;
  }

  const MODELS = {
    gpu() {
      const m = materials(), g = new THREE.Group(), glow = glowMat(), fans = [];
      g.add(new THREE.Mesh(rbox(3.4, 1.32, 0.5, 0.09), m.shroud));
      const back = new THREE.Mesh(rbox(3.32, 1.26, 0.04, 0.015), m.aluDark); back.position.z = -0.29; g.add(back);
      [-1.08, 0, 1.08].forEach((x) => { const f = fan(0.44, glow); f.position.set(x, -0.02, 0.27); g.add(f); fans.push(f.userData.rotor); });
      const fins = []; for (let i = 0; i < 46; i++) fins.push([-1.6 + i * 0.07, 0.69, 0]);
      g.add(instanced(box(0.018, 0.08, 0.42), m.alu, fins));
      const strip = new THREE.Mesh(rbox(1.5, 0.06, 0.06, 0.02), glow); strip.position.set(-0.6, 0.62, 0.2); g.add(strip);
      const line = new THREE.Mesh(box(3.0, 0.012, 0.01), glow); line.position.set(0, -0.6, 0.255); g.add(line);
      const bracket = new THREE.Mesh(box(0.05, 1.55, 0.56), m.alu); bracket.position.set(1.76, -0.05, 0); g.add(bracket);
      const pins = []; for (let i = 0; i < 26; i++) pins.push([-0.55 + i * 0.045, -0.73, -0.22]);
      g.add(instanced(box(0.03, 0.1, 0.02), m.gold, pins));
      return { group: g, fans, glow, scale: 1 };
    },
    cpu() {
      const m = materials(), g = new THREE.Group(), glow = glowMat();
      g.add(new THREE.Mesh(rbox(1.6, 1.6, 0.08, 0.02), m.pcbGreen));
      const ihs = new THREE.Mesh(rbox(1.22, 1.22, 0.16, 0.06), m.nickel); ihs.position.z = 0.12; g.add(ihs);
      const tex = labelTexture([{ text: "AI TWEAKER", size: 46, y: 46 }, { text: "x64 · 8C/16T", size: 34, y: 96, alpha: 0.7 }], 512, 140, "#c9cdd3", "#3b3f45");
      const label = new THREE.Mesh(new THREE.PlaneGeometry(0.9, 0.25), new THREE.MeshStandardMaterial({ map: tex, metalness: 0.6, roughness: 0.3 }));
      label.position.set(0, 0.1, 0.205); g.add(label);
      const mark = new THREE.Mesh(new THREE.CircleGeometry(0.04, 3), glow); mark.position.set(-0.68, -0.68, 0.045); g.add(mark);
      const caps = []; for (let i = 0; i < 12; i++) { caps.push([-0.6 + i * 0.11, 0.72, 0.05]); caps.push([-0.6 + i * 0.11, -0.72, 0.05]); }
      g.add(instanced(box(0.05, 0.03, 0.025), m.gold, caps));
      const pads = []; for (let i = 0; i < 22; i++) for (let j = 0; j < 22; j++) pads.push([-0.73 + i * 0.07, -0.73 + j * 0.07, -0.042, Math.PI]);
      g.add(instanced(new THREE.CircleGeometry(0.022, 8), m.gold, pads));
      return { group: g, glow, scale: 1.25, tilt: 0.55 };
    },
    ram() {
      const m = materials(), g = new THREE.Group(), glow = glowMat();
      const stick = (z) => {
        const s = new THREE.Group();
        s.add(new THREE.Mesh(box(3.4, 1.0, 0.04), m.pcb));
        const shape = new THREE.Shape();
        shape.moveTo(-1.7, -0.3); shape.lineTo(1.7, -0.3); shape.lineTo(1.7, 0.38); shape.lineTo(0.95, 0.38); shape.lineTo(0.72, 0.52); shape.lineTo(-1.7, 0.52); shape.lineTo(-1.7, -0.3);
        const plate = new THREE.ExtrudeGeometry(shape, { depth: 0.035, bevelEnabled: true, bevelSize: 0.01, bevelThickness: 0.01, bevelSegments: 2 });
        const f = new THREE.Mesh(plate, m.shroud); f.position.z = 0.03; s.add(f);
        const b = new THREE.Mesh(plate, m.shroud); b.position.z = -0.065; s.add(b);
        const bar = new THREE.Mesh(rbox(3.36, 0.11, 0.12, 0.03), glow); bar.position.set(0, 0.6, 0); s.add(bar);
        const etch = new THREE.Mesh(box(1.9, 0.012, 0.01), glow); etch.position.set(-0.6, 0.0, 0.08); s.add(etch);
        const pins = []; for (let i = 0; i < 64; i++) { const x = -1.6 + i * 0.05 + (i > 30 ? 0.08 : 0); pins.push([x, -0.45, 0]); }
        s.add(instanced(box(0.032, 0.12, 0.05), m.gold, pins));
        s.position.z = z;
        return s;
      };
      g.add(stick(0.16), stick(-0.16));
      return { group: g, glow, scale: 0.92 };
    },
    ssd() {
      const m = materials(), g = new THREE.Group(), glow = glowMat();
      g.add(new THREE.Mesh(box(2.8, 0.76, 0.035), m.pcb));
      const sink = new THREE.Mesh(rbox(2.2, 0.7, 0.14, 0.04), m.aluDark); sink.position.set(0.15, 0, 0.09); g.add(sink);
      const grooves = []; for (let i = 0; i < 14; i++) grooves.push([-0.85 + i * 0.145, 0, 0.165]);
      g.add(instanced(box(0.07, 0.66, 0.03), m.alu, grooves));
      const tex = labelTexture([{ text: "NVMe · PCIe 4.0", size: 40 }], 512, 96, "#16181c", "#c9c5bd");
      const label = new THREE.Mesh(new THREE.PlaneGeometry(1.0, 0.19), new THREE.MeshStandardMaterial({ map: tex, roughness: 0.6 }));
      label.position.set(0.15, -0.22, 0.183); g.add(label);
      const stripe = new THREE.Mesh(box(2.0, 0.025, 0.01), glow); stripe.position.set(0.15, 0.25, 0.186); g.add(stripe);
      const pins = []; for (let i = 0; i < 30; i++) pins.push([-1.36, -0.3 + i * 0.02, 0]);
      g.add(instanced(box(0.07, 0.012, 0.04), m.gold, pins));
      const screw = new THREE.Mesh(new THREE.TorusGeometry(0.06, 0.02, 8, 20, Math.PI), m.gold); screw.position.set(1.4, 0, 0.02); screw.rotation.z = Math.PI / 2; g.add(screw);
      return { group: g, glow, scale: 1.05 };
    },
    board() {
      const m = materials(), g = new THREE.Group(), glow = glowMat();
      g.add(new THREE.Mesh(rbox(3.0, 3.4, 0.06, 0.03), m.pcb));
      const socket = new THREE.Mesh(rbox(0.86, 0.86, 0.08, 0.02), m.alu); socket.position.set(0.15, 0.75, 0.07); g.add(socket);
      const cap = new THREE.Mesh(rbox(0.6, 0.6, 0.08, 0.04), m.nickel); cap.position.set(0.15, 0.75, 0.13); g.add(cap);
      for (let i = 0; i < 4; i++) { const s = new THREE.Mesh(box(0.07, 1.7, 0.1), m.plastic); s.position.set(0.95 + i * 0.13, 0.55, 0.08); g.add(s); }
      const vrmTop = new THREE.Mesh(rbox(1.3, 0.32, 0.26, 0.05), m.shroud); vrmTop.position.set(0.1, 1.42, 0.16); g.add(vrmTop);
      const vrmLeft = new THREE.Mesh(rbox(0.32, 1.2, 0.26, 0.05), m.shroud); vrmLeft.position.set(-0.75, 0.75, 0.16); g.add(vrmLeft);
      const io = new THREE.Mesh(rbox(0.55, 1.3, 0.38, 0.06), m.shroud); io.position.set(-1.15, 0.95, 0.22); g.add(io);
      const ioGlow = new THREE.Mesh(box(0.03, 1.1, 0.03), glow); ioGlow.position.set(-0.86, 0.95, 0.4); g.add(ioGlow);
      for (let i = 0; i < 2; i++) { const p = new THREE.Mesh(rbox(2.2, 0.12, 0.14, 0.02), i === 0 ? m.alu : m.plastic); p.position.set(0.1, -0.35 - i * 0.62, 0.1); g.add(p); }
      const m2 = new THREE.Mesh(rbox(1.7, 0.36, 0.1, 0.03), m.aluDark); m2.position.set(0.0, -0.66, 0.1); g.add(m2);
      const chipset = new THREE.Mesh(rbox(0.75, 0.75, 0.14, 0.06), m.shroud); chipset.position.set(0.8, -1.25, 0.12); g.add(chipset);
      const logo = new THREE.Mesh(new THREE.RingGeometry(0.12, 0.16, 32), glow); logo.position.set(0.8, -1.25, 0.195); g.add(logo);
      return { group: g, glow, scale: 0.78, tilt: 0.65 };
    },
    router() {
      const m = materials(), g = new THREE.Group(), glow = glowMat(), leds = [];
      const body = new THREE.Mesh(rbox(2.3, 0.36, 1.4, 0.12), m.gloss); g.add(body);
      const top = new THREE.Mesh(box(1.4, 0.01, 0.06), glow); top.position.set(0, 0.185, 0.45); g.add(top);
      [-0.9, -0.3, 0.3, 0.9].forEach((x, i) => {
        const a = new THREE.Mesh(new THREE.CylinderGeometry(0.045, 0.06, 1.3, 16), m.plastic);
        a.position.set(x, 0.75, -0.62); a.rotation.z = (i - 1.5) * 0.18; a.rotation.x = -0.12; g.add(a);
      });
      for (let i = 0; i < 6; i++) { const l = new THREE.Mesh(new THREE.CircleGeometry(0.025, 12), m.ledGreen.clone()); l.position.set(-0.5 + i * 0.2, 0, 0.705); g.add(l); leds.push(l); }
      return { group: g, glow, leds, scale: 1, tilt: 0.25 };
    },
    monitor() {
      const m = materials(), g = new THREE.Group(), glow = glowMat();
      const panel = new THREE.Mesh(rbox(3.3, 1.92, 0.08, 0.03), m.gloss); panel.position.y = 0.3; g.add(panel);
      const c = document.createElement("canvas"); c.width = 640; c.height = 360;
      const ctx = c.getContext("2d");
      const acc = "#" + accentColor().getHexString();
      const grd = ctx.createLinearGradient(0, 0, 640, 360); grd.addColorStop(0, "#07080b"); grd.addColorStop(1, "#14161b");
      ctx.fillStyle = grd; ctx.fillRect(0, 0, 640, 360);
      ctx.strokeStyle = acc; ctx.globalAlpha = 0.85; ctx.lineWidth = 3;
      ctx.beginPath(); for (let x = 0; x <= 640; x += 8) { const y = 230 - 70 * Math.exp(-((x - 330) ** 2) / 9000) - 10 * Math.sin(x / 23); x ? ctx.lineTo(x, y) : ctx.moveTo(x, y); } ctx.stroke();
      ctx.globalAlpha = 1; ctx.fillStyle = "#e9e6de"; ctx.font = '600 54px "Plex Cond", Bahnschrift, sans-serif'; ctx.fillText("180 Hz", 40, 90);
      const tex = new THREE.CanvasTexture(c); tex.encoding = THREE.sRGBEncoding;
      const screen = new THREE.Mesh(new THREE.PlaneGeometry(3.16, 1.78), new THREE.MeshBasicMaterial({ map: tex, toneMapped: false }));
      screen.position.set(0, 0.3, 0.042); g.add(screen);
      const neck = new THREE.Mesh(rbox(0.22, 0.9, 0.12, 0.04), m.aluDark); neck.position.set(0, -0.95, -0.12); g.add(neck);
      const base = new THREE.Mesh(rbox(1.3, 0.06, 0.7, 0.03), m.aluDark); base.position.set(0, -1.38, 0.05); g.add(base);
      const lip = new THREE.Mesh(box(0.6, 0.012, 0.01), glow); lip.position.set(0, -0.68, 0.045); g.add(lip);
      return { group: g, glow, scale: 0.82, tilt: 0.12 };
    },
    usb() {
      const m = materials(), g = new THREE.Group(), glow = glowMat();
      g.add(new THREE.Mesh(rbox(1.9, 0.62, 0.24, 0.1), m.shroud));
      const stripe = new THREE.Mesh(box(1.2, 0.02, 0.01), glow); stripe.position.set(-0.15, 0.12, 0.125); g.add(stripe);
      const plug = new THREE.Mesh(box(0.62, 0.46, 0.17), m.alu); plug.position.set(1.25, 0, 0); g.add(plug);
      [[1.3, 0.1], [1.3, -0.1]].forEach(([x, y]) => { const h = new THREE.Mesh(box(0.12, 0.08, 0.02), m.hole); h.position.set(x, y, 0.09); g.add(h); });
      const led = new THREE.Mesh(new THREE.CircleGeometry(0.035, 16), glow); led.position.set(-0.8, -0.16, 0.122); g.add(led);
      return { group: g, glow, scale: 1.1, tilt: 0.3 };
    },
  };

  // ------------------------------------------------------------------ renderer

  function makeEnv() {
    const pm = new THREE.PMREMGenerator(renderer), sc = new THREE.Scene();
    const sky = new THREE.Mesh(new THREE.SphereGeometry(10, 32, 16), new THREE.MeshBasicMaterial({ side: THREE.BackSide, vertexColors: true }));
    const col = [], pos = sky.geometry.attributes.position;
    for (let i = 0; i < pos.count; i++) { const k = (pos.getY(i) / 10 + 1) / 2; col.push(0.04 + 0.5 * k ** 2, 0.045 + 0.52 * k ** 2, 0.05 + 0.58 * k ** 2); }
    sky.geometry.setAttribute("color", new THREE.Float32BufferAttribute(col, 3));
    sc.add(sky);
    const soft = (x, y, z, w, h, v) => { const p = new THREE.Mesh(new THREE.PlaneGeometry(w, h), new THREE.MeshBasicMaterial({ toneMapped: false, side: THREE.DoubleSide })); p.material.color.setScalar(v); p.position.set(x, y, z); p.lookAt(0, 0, 0); sc.add(p); };
    soft(0, 6, 4, 8, 3, 5); soft(-7, 2, 2, 2, 6, 2.2); soft(7, 1, -2, 2, 6, 1.6);
    const tex = pm.fromScene(sc, 0.03).texture;
    pm.dispose();
    return tex;
  }

  function ensure() {
    if (renderer || failed) return !!renderer;
    if (!window.THREE) { failed = true; return false; }
    canvas = document.createElement("canvas");
    canvas.className = "hw3d-canvas";
    document.body.append(canvas);
    try {
      renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true, powerPreference: "low-power" });
    } catch {
      failed = true; canvas.remove(); return false;
    }
    renderer.setPixelRatio(Math.min(2, window.devicePixelRatio || 1));
    renderer.outputEncoding = THREE.sRGBEncoding;
    renderer.toneMapping = THREE.ACESFilmicToneMapping;
    renderer.toneMappingExposure = 1.1;
    renderer.setClearColor(0x000000, 0);
    try {
      const gl = renderer.getContext(), info = gl.getExtension("WEBGL_debug_renderer_info");
      slow = /swiftshader|llvmpipe|software|basic render/i.test(info ? gl.getParameter(info.UNMASKED_RENDERER_WEBGL) : "");
    } catch { /* keep defaults */ }
    if (slow) renderer.setPixelRatio(1);
    envMap = makeEnv();
    clock = new THREE.Clock();
    requestAnimationFrame(loop);
    return true;
  }

  function loop(now) {
    requestAnimationFrame(loop);
    if (!slots.size || document.hidden) return;
    // a dialog covers the page: keep the last frame under its blur
    if (!solo && document.querySelector(".overlay, .busy-veil")) return;
    if (lastFrame && !slow) {
      gapAvg = gapAvg * 0.95 + (now - lastFrame) * 0.05;
      if (++frames > 180 && gapAvg > 45) { slow = true; renderer.setPixelRatio(1); }
    }
    lastFrame = now;
    if (slow && now - lastDraw < 125) return;
    lastDraw = now;
    const dt = Math.min(slow ? 0.2 : 0.05, clock.getDelta()), t = clock.elapsedTime;
    const w = window.innerWidth, h = window.innerHeight;
    if (canvas.width !== Math.floor(w * renderer.getPixelRatio()) || canvas.height !== Math.floor(h * renderer.getPixelRatio())) renderer.setSize(w, h, false);
    renderer.setScissorTest(false);
    renderer.clear();
    renderer.setScissorTest(true);
    for (const s of slots) {
      // A re-rendered view may hand the same element back (see data-key), so give it a moment before freeing.
      if (!s.el.isConnected) { s.goneAt ||= now; if (now - s.goneAt > 1500) dispose(s); continue; }
      s.goneAt = 0;
      if (solo && !s.el.closest(".apply-stage")) continue;
      const r = s.el.getBoundingClientRect();
      const clip = s.el.closest(".view, .apply-stage, .modal") || document.body;
      const c = clip.getBoundingClientRect();
      const left = Math.max(r.left, c.left), right = Math.min(r.right, c.right), top = Math.max(r.top, c.top), bottom = Math.min(r.bottom, c.bottom);
      if (right <= left || bottom <= top || r.width < 4 || !s.el.offsetParent) continue;
      animate(s, dt, t);
      s.camera.aspect = r.width / r.height;
      s.camera.updateProjectionMatrix();
      renderer.setViewport(r.left, h - r.bottom, r.width, r.height);
      renderer.setScissor(left, h - bottom, right - left, bottom - top);
      renderer.render(s.scene, s.camera);
    }
  }

  function animate(s, dt, t) {
    const load = Math.max(0, Math.min(1, s.load()));
    const speed = s.boost ? 3 : 1;
    if (!s.drag) s.yaw += dt * 0.35 * speed * (s.spin ?? 1);
    s.pivot.rotation.y = s.yaw + s.tiltY;
    s.pivot.rotation.x = s.baseTilt + s.tiltX;
    s.pivot.position.y = Math.sin(t * 1.2 + s.phase) * 0.06;
    (s.model.fans || []).forEach((f) => (f.rotation.z -= dt * (4 + load * 38) * speed));
    if (s.model.glow) s.model.glow.emissiveIntensity = 0.75 + Math.sin(t * (2 + load * 4) + s.phase) * 0.3 + (s.boost ? 0.8 : 0);
    (s.model.leds || []).forEach((l, i) => (l.material.emissiveIntensity = Math.sin(t * 9 + i * 1.7) > -0.2 + load ? 2.6 : 0.3));
  }

  function dispose(s) {
    slots.delete(s);
    const shared = new Set(Object.values(M));
    s.scene.traverse((o) => {
      if (o.geometry) o.geometry.dispose();
      if (o.material && !shared.has(o.material)) { if (o.material.map) o.material.map.dispose(); o.material.dispose(); }
    });
    if (s.el._hw && s.el._hw.slot === s) s.el._hw = null;
  }

  /**
   * Draws a model into `el`. kind: gpu | cpu | ram | ssd | board | router | monitor | usb.
   * opts.load: () => 0..1 (fans and lights follow it); opts.spin: idle speed factor.
   */
  HW.mount = (el, kind, opts = {}) => {
    if (!el || !MODELS[kind] || !ensure()) return null;
    for (const s of slots) if (s.el === el) dispose(s);
    const scene = new THREE.Scene();
    scene.environment = envMap;
    const key = new THREE.DirectionalLight(0xffffff, 2.2); key.position.set(3, 4, 5);
    const rim = new THREE.DirectionalLight(accentColor(), 2.6); rim.position.set(-5, 2, -4);
    const fill = new THREE.HemisphereLight(0xbfc7d5, 0x0b0c0f, 0.5);
    scene.add(key, rim, fill);
    const model = MODELS[kind]();
    model.kind = kind;
    const pivot = new THREE.Group();
    model.group.scale.setScalar(model.scale || 1);
    pivot.add(model.group);
    scene.add(pivot);
    const camera = new THREE.PerspectiveCamera(30, 1, 0.1, 100);
    camera.position.set(0, 0.45, opts.distance || 5.6);
    camera.lookAt(0, 0, 0);
    const s = { el, key: opts.key, scene, camera, pivot, model, rim, load: opts.load || (() => 0.15), spin: opts.spin, yaw: opts.yaw ?? -0.5, tiltX: 0, tiltY: 0, baseTilt: model.tilt ?? 0.18, phase: Math.random() * 6, drag: false, boost: false };
    // drag to turn, hover to lean
    let lastX = 0, lastY = 0;
    el.addEventListener("pointerdown", (e) => { s.drag = true; lastX = e.clientX; lastY = e.clientY; el.setPointerCapture(e.pointerId); });
    el.addEventListener("pointermove", (e) => {
      if (s.drag) { s.yaw += (e.clientX - lastX) * 0.012; s.tiltX = Math.max(-0.6, Math.min(0.6, s.tiltX + (e.clientY - lastY) * 0.006)); lastX = e.clientX; lastY = e.clientY; }
      else { const r = el.getBoundingClientRect(); s.tiltY = ((e.clientX - r.left) / r.width - 0.5) * 0.35; }
    });
    const stop = () => { s.drag = false; };
    el.addEventListener("pointerup", stop);
    el.addEventListener("pointerleave", () => { s.tiltY = 0; stop(); });
    slots.add(s);
    return { slot: s, boost(on) { s.boost = on; }, dispose: () => dispose(s) };
  };

  /** Mounts every [data-hw] element inside root (data-hw="gpu", optional data-load="gpu|cpu|ram|ping"). */
  HW.scan = (root, loads = {}) => {
    if (!root) return;
    root.querySelectorAll("[data-hw]").forEach((el) => {
      if (el._hw) return;
      // data-key: when a view re-renders, the model that was already spinning there is moved into the new markup.
      const key = el.dataset.key;
      const old = key && [...slots].find((s) => s.key === key && !s.el.isConnected && s.model.kind === el.dataset.hw);
      if (old) { old.el.className = el.className; old.el.style.cssText = el.style.cssText; el.replaceWith(old.el); return; }
      el._hw = HW.mount(el, el.dataset.hw, { key, load: loads[el.dataset.load || el.dataset.hw], yaw: +(el.dataset.yaw || -0.5), spin: +(el.dataset.spin || 1), distance: +(el.dataset.dist || 0) || undefined });
    });
  };

  /** While the apply sequence runs, the canvas sits above its veil and draws only the models on that stage. */
  HW.solo = (on) => { solo = on; if (canvas) canvas.classList.toggle("solo", on); };

  /** Re-tints glow and rim lights after an accent or theme change. */
  HW.retint = () => {
    if (!slots.size) return;
    const c = accentColor();
    for (const s of slots) {
      s.rim.color.copy(c);
      if (s.model.glow) { s.model.glow.color.copy(c); s.model.glow.emissive.copy(c); }
    }
  };
  HW.available = () => ensure();
})();
