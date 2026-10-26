'use strict';
// Monitor controls over DDC/CI (the channel a display listens on in its DisplayPort / HDMI cable): input source,
// brightness, contrast, volume, power, color preset. The Windows helper does the talking; this layer names things,
// checks what may be sent, caches the slow capabilities query, and takes turns (a monitor handles one request at a time).
const system = require('./system');
const { displayName, readJson } = require('./profiles');

// Feature codes the page offers. Anything else is the raw command, which only this PC may use.
const CODES = {
  brightness: 0x10, contrast: 0x12, preset: 0x14, gainRed: 0x16, gainGreen: 0x18, gainBlue: 0x1a,
  volume: 0x62, sharpness: 0x87, mute: 0x8d, input: 0x60, power: 0xd6,
};
const FRIENDLY = new Set(Object.values(CODES));

// One request at a time, across the whole server: two overlapping requests would garble each other on the cable.
let chain = Promise.resolve();
function exclusive(fn) {
  const run = chain.then(fn, fn);
  chain = run.catch(() => {});
  return run;
}

const capsCache = new Map(); // target id -> { at, value }
const CAPS_TTL = 10 * 60 * 1000;

function parseCode(v) {
  const s = String(v == null ? '' : v).trim().replace(/^0x/i, '');
  if (!/^[0-9a-f]{1,2}$/i.test(s)) throw Object.assign(new Error('A feature code is a hex number from 00 to FF (for example 0x60).'), { status: 400 });
  return parseInt(s, 16);
}

function parseTarget(id) {
  const n = Number(id);
  if (!Number.isInteger(n) || n < 0) throw Object.assign(new Error('Unknown display.'), { status: 404 });
  return n;
}

const pick = (features, code) => {
  const f = features && features[String(code)];
  return f ? { current: f.current, max: f.max } : null;
};

// Monitors that are on, with what each reports right now.
async function list(cfg) {
  const { monitors, notes } = await exclusive(() => system.ddcList(cfg));
  const overrides = (readJson(cfg.namesFile).value) || {};
  const out = [];
  for (const m of monitors || []) {
    const name = await displayName({ hwId: m.hwId, xmlName: m.name, targetId: m.targetId }, overrides);
    const controls = {};
    for (const [key, code] of Object.entries(CODES)) {
      const v = pick(m.features, code);
      if (v) controls[key] = v;
    }
    out.push({ id: m.targetId, name, hwId: m.hwId, ddc: !!m.ddc, backend: m.backend || 'windows', controls });
  }
  return { monitors: out, notes: notes || [] };
}

async function capabilities(cfg, id, refresh) {
  const target = parseTarget(id);
  const hit = capsCache.get(target);
  if (!refresh && hit && Date.now() - hit.at < CAPS_TTL) return hit.value;
  const raw = await exclusive(() => system.ddcCaps(cfg, target));
  const codes = raw.codes || {};
  const hex = (n) => '0x' + Number(n).toString(16).toUpperCase().padStart(2, '0');
  const value = {
    raw: raw.raw, model: raw.model || '', type: raw.type || '', mccsVersion: raw.mccsVersion || '',
    supported: Object.keys(codes).map(Number).sort((a, b) => a - b).map(hex),
    inputs: codes[String(CODES.input)] || [],
    presets: codes[String(CODES.preset)] || [],
    power: codes[String(CODES.power)] || [],
    has: Object.fromEntries(Object.entries(CODES).map(([k, c]) => [k, Object.prototype.hasOwnProperty.call(codes, String(c))])),
  };
  capsCache.set(target, { at: Date.now(), value });
  return value;
}

function checkValue(v) {
  const n = typeof v === 'number' || (typeof v === 'string' && v.trim() !== '') ? Number(v) : NaN;
  if (!Number.isInteger(n) || n < 0 || n > 65535) throw Object.assign(new Error('The value must be a whole number from 0 to 65535.'), { status: 400 });
  return n;
}

// Sends one command. The page may only use the known features; raw: true (this PC only, checked by the caller) allows any.
async function set(cfg, id, code, value, { raw = false } = {}) {
  const target = parseTarget(id);
  const c = parseCode(code);
  const v = checkValue(value);
  if (!raw && !FRIENDLY.has(c)) throw Object.assign(new Error('That feature cannot be changed from here.'), { status: 403 });
  if ([0x04, 0x05, 0x06, 0x08, 0x0a].includes(c)) throw Object.assign(new Error('That command resets the display to factory settings, so DisplayDeck will not send it.'), { status: 403 });
  await exclusive(() => system.ddcSet(cfg, target, c, v));
  return { ok: true, code: c, value: v };
}

async function get(cfg, id, code) {
  const target = parseTarget(id);
  const c = parseCode(code);
  const r = await exclusive(() => system.ddcGet(cfg, target, c));
  return { code: c, current: r.current, max: r.max };
}

module.exports = { list, capabilities, set, get, CODES, parseCode };
