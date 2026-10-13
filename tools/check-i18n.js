#!/usr/bin/env node
/* Fails when the UI asks for a string that RU or EN doesn't have.
   Checks: RU/EN have the same keys, every literal t("key") exists, and every
   NVIDIA tweak shown on the Drivers page has its one-line summary (nv.sub.<id>).
   Usage: node tools/check-i18n.js */
const fs = require("fs");
const path = require("path");
const vm = require("vm");

const js = path.join(__dirname, "..", "src", "AITweaker.App", "wwwroot", "js");
const sandbox = { window: {} };
vm.runInNewContext(fs.readFileSync(path.join(js, "i18n.js"), "utf8"), sandbox);
vm.runInNewContext(fs.readFileSync(path.join(js, "mock-data.js"), "utf8"), sandbox);
const { ru, en } = sandbox.window.I18N;
const problems = [];

for (const k of Object.keys(ru)) if (!(k in en)) problems.push(`EN is missing "${k}"`);
for (const k of Object.keys(en)) if (!(k in ru)) problems.push(`RU is missing "${k}"`);

for (const file of fs.readdirSync(js).filter((f) => f.endsWith(".js") && f !== "i18n.js" && f !== "mock-data.js")) {
  const src = fs.readFileSync(path.join(js, file), "utf8");
  for (const m of src.matchAll(/\bt\("([A-Za-z0-9_.-]+)"\)/g))
    if (!(m[1] in ru)) problems.push(`${file}: t("${m[1]}") has no text`);
}

for (const tw of sandbox.window.MOCK_DATA.tweaks.filter((x) => x.id.startsWith("gaming.nvidia-")))
  if (!(`nv.sub.${tw.id}` in ru)) problems.push(`no Drivers-page summary nv.sub.${tw.id}`);

if (problems.length) { console.error(problems.join("\n")); process.exit(1); }
console.log(`i18n OK: ${Object.keys(ru).length} keys in RU and EN`);
