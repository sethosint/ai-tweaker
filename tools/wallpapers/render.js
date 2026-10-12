// Rasterizes tools/wallpapers/svg/*.svg (from make.py) with headless Chromium:
// 4K JPGs for Windows (embedded into AITweaker.exe) and small WebP previews for the UI.
//   NODE_PATH=$(npm root -g) node tools/wallpapers/render.js
const { chromium } = require("playwright");
const fs = require("fs");
const path = require("path");
const root = path.resolve(__dirname, "..", "..");
const svgDir = path.join(__dirname, "svg");
const outJpg = path.join(root, "src", "AITweaker.App", "Assets", "Wallpapers");
const outPng = path.join(__dirname, "png");
(async () => {
  fs.mkdirSync(outJpg, { recursive: true });
  fs.mkdirSync(outPng, { recursive: true });
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 3840, height: 2160 }, deviceScaleFactor: 1 });
  for (const f of fs.readdirSync(svgDir).filter((x) => x.endsWith(".svg"))) {
    const name = f.replace(/\.svg$/, "");
    await page.setContent(`<html><body style="margin:0;background:#000">${fs.readFileSync(path.join(svgDir, f), "utf8")}</body></html>`);
    await page.waitForTimeout(300);
    await page.screenshot({ path: path.join(outPng, name + ".png"), clip: { x: 0, y: 0, width: 3840, height: 2160 } });
    console.log("rendered", name);
  }
  await browser.close();
})();
