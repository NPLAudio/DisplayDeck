'use strict';
// The layout editor's server side: what the editor starts from, checking a layout with Windows, trying it
// with an automatic revert, and saving it as a profile.
const fs = require('fs');
const path = require('path');
const { parseProfile, displayKey } = require('./parser');
const system = require('./system');
const { scanFiles, displayName, readJson } = require('./profiles');

const MIN_TEST_SECONDS = Number(process.env.EDITOR_MIN_SECONDS) || 5;
const ROTATIONS = { identity: 1, rotate90: 2, rotate180: 3, rotate270: 4 };
const rotationOf = (v) => {
  if (v == null) return 1;
  const n = Number(v);
  if (Number.isInteger(n) && n >= 1 && n <= 4) return n;
  return ROTATIONS[String(v).toLowerCase().replace(/\s+/g, '')] || 1;
};

// Sizes offered for a display that is switched off (so Windows cannot list its modes).
const COMMON = [[3840, 2160], [2560, 1440], [1920, 1080], [1600, 900], [1280, 720]];
const COMMON_RATES = [24, 30, 50, 60, 75, 100, 120, 144];

function addMode(map, w, h, hz) {
  if (!w || !h) return;
  const k = `${w}x${h}`;
  if (!map.has(k)) map.set(k, { width: w, height: h, rates: new Set() });
  if (hz) map.get(k).rates.add(Math.round(hz));
}

function modesList(map) {
  return [...map.values()]
    .map((m) => ({ width: m.width, height: m.height, refresh: [...m.rates].sort((a, b) => a - b) }))
    .sort((a, b) => b.width * b.height - a.width * a.height || b.width - a.width);
}

function readParsedFile(file) {
  try { return parseProfile(fs.readFileSync(file, 'utf8')); } catch (_) { return null; }
}

// Everything the editor needs: the displays Windows knows about (connected ones plus any a saved profile has
// used), laid out as in the profile `from`, or all switched off when starting from scratch.
async function editorState(cfg, from, useLive) {
  const files = scanFiles(cfg);
  const overrides = (readJson(cfg.namesFile).value) || {};
  let connected = [];
  let connectedError = null;
  try { connected = await system.connectedDisplays(cfg); } catch (e) { connectedError = e.message; }

  let baseParsed = null;
  if (useLive) {
    try { baseParsed = parseProfile(await system.saveLiveXml(cfg)); } catch (_) { baseParsed = null; }
  } else if (from) {
    const entry = files.find((f) => f.name === from);
    if (!entry) throw Object.assign(new Error(`No such profile: ${from}`), { status: 404 });
    baseParsed = readParsedFile(entry.file);
    if (!baseParsed) throw Object.assign(new Error(`"${from}" could not be read.`), { status: 400 });
  }

  const entries = new Map(); // displayKey -> editor entry
  const seenModes = new Map(); // displayKey -> Map of modes seen in saved profiles
  const noteModes = (key, d) => {
    if (!seenModes.has(key)) seenModes.set(key, new Map());
    addMode(seenModes.get(key), d.width, d.height, d.refreshHz);
  };

  for (const f of files) {
    const parsed = !useLive && f.name === from ? baseParsed : readParsedFile(f.file);
    if (!parsed) continue;
    for (const d of parsed.displays) {
      if (!d.active || d.width == null) continue;
      const key = displayKey(d);
      if (!key) continue;
      noteModes(key, d);
      const isBase = !useLive && f.name === from;
      if (entries.has(key) && !isBase) continue;
      if (entries.has(key) && isBase && entries.get(key).fromBase) continue;
      entries.set(key, {
        key, targetId: d.targetId, hwId: d.hwId || '',
        name: await displayName(d, overrides),
        enabled: isBase, primary: isBase && d.primary,
        width: d.width, height: d.height, x: d.x, y: d.y,
        refresh: d.refreshHz || 0, rotation: rotationOf(d.rotation),
        connected: false, fromBase: isBase, manufacturer: 0, product: 0, modes: [],
      });
    }
  }

  if (useLive && baseParsed) {
    for (const d of baseParsed.displays) {
      if (!d.active || d.width == null) continue;
      const key = displayKey(d);
      if (!key) continue;
      noteModes(key, d);
      entries.set(key, {
        key, targetId: d.targetId, hwId: d.hwId || '',
        name: await displayName(d, overrides),
        enabled: true, primary: d.primary,
        width: d.width, height: d.height, x: d.x, y: d.y,
        refresh: d.refreshHz || 0, rotation: rotationOf(d.rotation),
        connected: false, fromBase: true, manufacturer: 0, product: 0, modes: [],
      });
    }
  }

  for (const c of connected) {
    const probe = { hwId: c.hwId, xmlName: c.name, targetId: c.targetId };
    const key = displayKey(probe);
    let e = entries.get(key);
    if (!e) {
      const pref = c.preferred || (c.modes && c.modes[0]) || { width: 1920, height: 1080, refresh: [60] };
      e = {
        key, targetId: c.targetId, hwId: c.hwId || '',
        name: await displayName(probe, overrides),
        enabled: false, primary: false,
        width: pref.width, height: pref.height, x: null, y: null,
        refresh: (pref.refresh && pref.refresh[pref.refresh.length - 1]) || 60, rotation: 1,
        connected: true, fromBase: false, manufacturer: 0, product: 0, modes: [],
      };
      entries.set(key, e);
    }
    e.connected = true;
    e.manufacturer = c.manufacturer || 0;
    e.product = c.product || 0;
    const m = new Map();
    for (const mode of c.modes || []) for (const hz of mode.refresh) addMode(m, mode.width, mode.height, hz);
    e.deviceModes = m.size > 0;
    if (m.size === 0) {
      if (c.preferred) for (const hz of c.preferred.refresh) addMode(m, c.preferred.width, c.preferred.height, hz);
    }
    e.modeMap = m;
  }

  const list = [...entries.values()];
  for (const e of list) {
    const m = e.modeMap || new Map();
    if (!e.deviceModes) {
      for (const [k, v] of seenModes.get(e.key) || []) for (const hz of v.rates) addMode(m, v.width, v.height, hz);
      for (const [w, h] of COMMON) for (const hz of COMMON_RATES) addMode(m, w, h, hz);
    }
    addMode(m, e.width, e.height, e.refresh);
    e.modes = modesList(m);
    delete e.modeMap;
    delete e.fromBase;
  }

  // Keep the layout's own order: enabled displays left to right, then the rest by name.
  list.sort((a, b) => (b.enabled - a.enabled) || ((a.x ?? 1e9) - (b.x ?? 1e9)) || a.name.localeCompare(b.name));
  return { displays: list, connectedError, from: useLive ? null : from || null, live: !!useLive, profiles: files.map((f) => f.name), test: testStatus() };
}

// ---- layouts -> profile files ----

const num = (v, def = 0) => (Number.isFinite(Number(v)) ? Number(v) : def);

function cleanLayout(layout) {
  if (!layout || !Array.isArray(layout.displays)) throw new Error('The layout has no displays.');
  if (layout.displays.length > 16) throw new Error('Too many displays.');
  return {
    displays: layout.displays.map((d) => ({
      targetId: Math.trunc(num(d.targetId)),
      name: typeof d.name === 'string' ? d.name.slice(0, 80) : '',
      manufacturer: Math.trunc(num(d.manufacturer)),
      product: Math.trunc(num(d.product)),
      enabled: d.enabled !== false,
      primary: !!d.primary,
      width: Math.trunc(num(d.width)),
      height: Math.trunc(num(d.height)),
      x: Math.trunc(num(d.x)),
      y: Math.trunc(num(d.y)),
      refresh: Math.max(0, Math.min(1000, num(d.refresh))),
      rotation: rotationOf(d.rotation),
    })),
  };
}

// Writes the layout as a profile at outFile. Saved profiles (the base one first) supply connection details.
async function buildTo(cfg, layout, base, outFile) {
  const clean = cleanLayout(layout);
  const files = scanFiles(cfg);
  const baseEntry = base ? files.find((f) => f.name === base) : null;
  const templates = [...(baseEntry ? [baseEntry] : []), ...files.filter((f) => f !== baseEntry)].map((f) => f.file);
  return system.buildLayout(cfg, clean, outFile, templates);
}

// Checks the layout with Windows without changing anything. Resolves to { ok, method } or throws with Windows' reason.
async function validate(cfg, layout, base) {
  const tmp = system.tmpFile('.xml');
  try {
    await buildTo(cfg, layout, base, tmp);
    const r = await system.validateProfile(cfg, tmp);
    return { ok: true, method: r.method, skipped: r.skipped || [] };
  } finally {
    fs.rm(tmp, { force: true }, () => {});
  }
}

// ---- try it, with an automatic way back ----

let pending = null; // { before, timer, until, cfg }

function testStatus() {
  return pending ? { pending: true, secondsLeft: Math.max(0, Math.round((pending.until - Date.now()) / 1000)) } : { pending: false, secondsLeft: 0 };
}

function dropPending() {
  if (!pending) return;
  clearTimeout(pending.timer);
  fs.rm(pending.before, { force: true }, () => {});
  pending = null;
}

async function startTest(cfg, layout, base, seconds = 15) {
  const secs = Math.max(MIN_TEST_SECONDS, Math.min(60, Math.round(num(seconds, 15))));
  const tmp = system.tmpFile('.xml');
  const startedHere = !pending;
  let before = pending && pending.before;
  if (startedHere) {
    before = system.tmpFile('.xml');
    await system.saveLayoutTo(cfg, before); // what to go back to
  }
  try {
    await buildTo(cfg, layout, base, tmp);
    await system.applyProfile(cfg, tmp);
  } catch (e) {
    if (startedHere) fs.rm(before, { force: true }, () => {});
    throw e;
  } finally {
    fs.rm(tmp, { force: true }, () => {});
  }
  if (pending) clearTimeout(pending.timer);
  const until = Date.now() + secs * 1000;
  pending = { before, until, cfg, timer: setTimeout(() => { revert().catch(() => {}); }, secs * 1000) };
  pending.timer.unref();
  return { ok: true, secondsLeft: secs };
}

// Keeps the tried layout. "Undo layout change" then goes back to what was there before the test.
async function keep() {
  if (!pending) return { ok: true };
  const { before, cfg } = pending;
  try { await system.rememberLayout(cfg, before); } finally { dropPending(); }
  return { ok: true };
}

async function revert() {
  if (!pending) return { ok: true };
  const { before, cfg } = pending;
  clearTimeout(pending.timer);
  try {
    await system.applyProfile(cfg, before);
  } finally {
    dropPending();
  }
  return { ok: true };
}

// ---- saving ----

async function save(cfg, { name, layout, base, overwrite, copyAudioFrom }, validName) {
  if (!validName(name)) throw Object.assign(new Error('Use 1 to 60 characters, without \\ / : * ? " < > | and not ending in a dot.'), { status: 400 });
  fs.mkdirSync(cfg.profilesDir, { recursive: true });
  const file = path.join(cfg.profilesDir, `${name}.xml`);
  if (fs.existsSync(file) && !overwrite) throw Object.assign(new Error(`A profile named "${name}" already exists.`), { status: 409 });
  const tmp = system.tmpFile('.xml');
  try {
    const built = await buildTo(cfg, layout, base, tmp);
    fs.copyFileSync(tmp, file);
    if (copyAudioFrom) {
      const from = path.join(cfg.profilesDir, `${copyAudioFrom}.audio.json`);
      const to = path.join(cfg.profilesDir, `${name}.audio.json`);
      if (from !== to && fs.existsSync(from) && !fs.existsSync(to)) fs.copyFileSync(from, to);
    }
    return { ok: true, name, displays: built.displays };
  } finally {
    fs.rm(tmp, { force: true }, () => {});
  }
}

module.exports = { editorState, validate, startTest, keep, revert, save, testStatus, cleanLayout, rotationOf };
