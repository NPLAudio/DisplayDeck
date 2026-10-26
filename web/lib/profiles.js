'use strict';
const fs = require('fs');
const path = require('path');
const { parseProfile, displayKey, layoutSignature } = require('./parser');
const system = require('./system');

const parseCache = new Map(); // file -> { mtimeMs, size, parsed, error }

function readParsed(file, st) {
  const hit = parseCache.get(file);
  if (hit && hit.mtimeMs === st.mtimeMs && hit.size === st.size) return hit;
  const entry = { mtimeMs: st.mtimeMs, size: st.size, parsed: null, error: null };
  try {
    entry.parsed = parseProfile(fs.readFileSync(file, 'utf8'));
  } catch (e) {
    entry.error = e.message;
  }
  parseCache.set(file, entry);
  return entry;
}

function readJson(file) {
  try {
    return { value: JSON.parse(fs.readFileSync(file, 'utf8').replace(/^﻿/, '')) };
  } catch (e) {
    return e.code === 'ENOENT' ? { value: null } : { error: e.message };
  }
}

let liveCache = { at: 0, value: null };
async function getLive(cfg) {
  if (Date.now() - liveCache.at < 3000) return liveCache.value;
  let value = null;
  try {
    const parsed = parseProfile(await system.saveLiveXml(cfg));
    value = { parsed, signature: layoutSignature(parsed) };
  } catch (_) { /* live state unavailable: caller falls back to "last applied" */ }
  liveCache = { at: Date.now(), value };
  return value;
}

function readLastApplied(cfg) {
  const s = readJson(cfg.stateFile).value;
  return s && typeof s.lastApplied === 'string' ? s.lastApplied : null;
}

function writeLastApplied(cfg, name) {
  fs.writeFileSync(cfg.stateFile, JSON.stringify({ lastApplied: name, at: new Date().toISOString() }));
}

function scanFiles(cfg) {
  let names = [];
  try { names = fs.readdirSync(cfg.profilesDir); } catch (_) { /* missing dir -> empty list */ }
  return names
    .filter((f) => /\.xml$/i.test(f))
    .map((f) => {
      const file = path.join(cfg.profilesDir, f);
      try { return { name: f.replace(/\.xml$/i, ''), file, st: fs.statSync(file) }; } catch (_) { return null; }
    })
    .filter((x) => x && x.st.isFile())
    .sort((a, b) => a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }));
}

async function displayName(d, overrides) {
  return (d.hwId && overrides[d.hwId])
    || (d.xmlName && overrides[d.xmlName])
    || (d.targetId !== null && overrides[`target:${d.targetId}`])
    || d.xmlName
    || (await system.edidName(d.hwId))
    || d.hwId
    || (d.targetId !== null ? `Display ${d.targetId}` : 'Unknown display');
}

async function buildProfiles(cfg) {
  const files = scanFiles(cfg);
  const entries = files.map((f) => ({ ...f, ...readParsed(f.file, f.st) }));
  const overrides = (readJson(cfg.namesFile).value) || {};
  const live = await getLive(cfg);
  const lastApplied = readLastApplied(cfg);

  // The XML only records displays that are on, so "disabled" means: a display
  // seen in another profile (or live) that this profile does not enable.
  const known = new Map();
  const remember = (parsed) => {
    for (const d of parsed.displays) {
      const k = displayKey(d);
      if (d.active && k && !known.has(k)) known.set(k, d);
    }
  };
  entries.forEach((e) => e.parsed && remember(e.parsed));
  if (live) remember(live.parsed);

  const profiles = [];
  for (const e of entries) {
    const audioFile = path.join(cfg.profilesDir, `${e.name}.audio.json`);
    const audioRead = readJson(audioFile);
    const p = {
      name: e.name,
      mtime: new Date(e.st.mtimeMs).toISOString(),
      error: e.error,
      warnings: e.parsed ? e.parsed.warnings : [],
      displays: [],
      disabled: [],
      audio: audioRead.error ? { error: audioRead.error } : audioRead.value,
      current: false,
      lastApplied: e.name === lastApplied,
    };
    if (e.parsed) {
      const activeKeys = new Set();
      for (const d of e.parsed.displays) {
        if (!d.active) continue;
        activeKeys.add(displayKey(d));
        p.displays.push({
          name: await displayName(d, overrides),
          width: d.width, height: d.height, x: d.x, y: d.y,
          refreshHz: d.refreshHz, primary: d.primary, output: d.output,
        });
      }
      for (const [k, d] of known) {
        if (!activeKeys.has(k)) p.disabled.push({ name: await displayName(d, overrides) });
      }
      const sig = layoutSignature(e.parsed);
      p.current = !!(live && live.signature && sig && sig === live.signature);
    }
    profiles.push(p);
  }
  return {
    profiles,
    live: live ? { available: true, matched: profiles.some((p) => p.current) } : { available: false, matched: false },
  };
}

module.exports = { buildProfiles, scanFiles, writeLastApplied };
