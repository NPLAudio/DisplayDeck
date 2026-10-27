'use strict';
// Everything that touches Windows goes through DisplayDeckCtl.exe (display layouts, playback devices,
// default device, spatial format) or reg.exe (monitor names). Child processes get a timeout and no window.

const { execFile } = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');

function tmpFile(ext) {
  return path.join(os.tmpdir(), `dd-${process.pid}-${Date.now()}-${Math.random().toString(36).slice(2)}${ext}`);
}

// Runs the helper and returns its parsed JSON. The helper reports failures as {"error": "..."} on stdout
// with a non-zero exit code; those become Errors carrying the helper's message.
function ctl(cfg, args, timeoutMs = 30000) {
  return new Promise((resolve, reject) => {
    execFile(cfg.ctl, args, { windowsHide: true, encoding: 'utf8', timeout: timeoutMs, maxBuffer: 16 * 1024 * 1024 }, (err, stdout) => {
      let out = null;
      try { out = stdout ? JSON.parse(stdout) : null; } catch (_) { /* fall through */ }
      if (out && out.error) {
        const e = new Error(out.error);
        e.needsElevation = !!out.needsElevation;
        return reject(e);
      }
      if (err) {
        return reject(err.code === 'ENOENT'
          ? new Error(`The DisplayDeck helper was not found (${cfg.ctl}). Reinstall DisplayDeck.`)
          : new Error(`The DisplayDeck helper failed: ${err.message}`));
      }
      resolve(out || {});
    });
  });
}

// ---- display layouts ----

async function saveLiveXml(cfg) {
  const out = tmpFile('.xml');
  try {
    await ctl(cfg, ['display', 'save', out]);
    return fs.readFileSync(out, 'utf8');
  } finally {
    fs.rm(out, { force: true }, () => {});
  }
}

async function saveLayoutTo(cfg, file) {
  return ctl(cfg, ['display', 'save', file]);
}

// Resolves to { matched: [names], skipped: [messages], method }.
async function applyProfile(cfg, file) {
  return ctl(cfg, ['display', 'apply', file], 45000);
}

// ---- layout editor ----

// Connected displays with the modes each supports (displays that are off report none).
async function connectedDisplays(cfg) {
  const { displays } = await ctl(cfg, ['display', 'connected'], 45000);
  return displays || [];
}

// Builds a profile file from an edited layout. templateFiles are saved profiles the helper may copy
// connection details from.
async function buildLayout(cfg, layout, outFile, templateFiles) {
  const spec = tmpFile('.json');
  fs.writeFileSync(spec, JSON.stringify(layout));
  try {
    return await ctl(cfg, ['display', 'build', spec, outFile, ...templateFiles], 45000);
  } finally {
    fs.rm(spec, { force: true }, () => {});
  }
}

// Asks Windows whether it would accept a profile, without changing the screen.
async function validateProfile(cfg, file) {
  return ctl(cfg, ['display', 'apply', file, '--validate'], 45000);
}

// Makes a saved layout the one "Undo layout change" returns to.
async function rememberLayout(cfg, file) {
  return ctl(cfg, ['display', 'remember', file]);
}

// ---- HDR, scaling and wallpaper saved with a profile ----

// Reads the named sections ('hdr', 'dpi', 'wallpaper') from the displays that are on now.
async function captureExtras(cfg, sections) {
  return ctl(cfg, ['extras', 'capture', sections.join(',')], 60000);
}

// Puts saved extras back. Resolves to { applied: [...], skipped: [...] }.
async function applyExtras(cfg, extras) {
  const file = tmpFile('.json');
  fs.writeFileSync(file, JSON.stringify(extras));
  try {
    return await ctl(cfg, ['extras', 'apply', file], 60000);
  } finally {
    fs.rm(file, { force: true }, () => {});
  }
}

// ---- monitor controls (DDC/CI) ----

// Reading every feature of every monitor takes a moment, so these get longer timeouts.
async function ddcList(cfg) {
  return ctl(cfg, ['ddc', 'list'], 60000);
}
async function ddcCaps(cfg, target) {
  return ctl(cfg, ['ddc', 'caps', String(target)], 60000);
}
async function ddcGet(cfg, target, code) {
  return ctl(cfg, ['ddc', 'get', String(target), '0x' + code.toString(16)], 20000);
}
async function ddcSet(cfg, target, code, value) {
  return ctl(cfg, ['ddc', 'set', String(target), '0x' + code.toString(16), String(value)], 20000);
}

async function undoLayout(cfg) {
  return ctl(cfg, ['display', 'undo'], 45000);
}

// ---- audio ----

async function listPlaybackDevices(cfg) {
  const { devices } = await ctl(cfg, ['audio', 'list']);
  return devices.map((d) => ({
    name: d.name,
    deviceName: d.adapter,
    id: d.id,
    isDefault: !!d.isDefault,
    state: 'Active',
    spatialGuid: d.spatialGuid || '',
    formats: d.formats || [],
  }));
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const DEVICE_MISSING = /No active playback device matches/;

// Switches the default playback device, then (when the profile says so) its spatial format.
// `waitMs` keeps looking for a device that has not shown up yet. Right after a layout change, the sound device of a
// display (an HDMI or DisplayPort output) can take a few seconds to appear, and asking too early finds nothing.
async function applyAudio(cfg, audio, { waitMs = 0 } = {}) {
  if (!audio || !audio.device) return;
  const deadline = Date.now() + waitMs;
  for (;;) {
    try {
      await ctl(cfg, ['audio', 'default', audio.device]);
      break;
    } catch (e) {
      if (!DEVICE_MISSING.test(e.message) || Date.now() >= deadline) throw e;
      await sleep(Math.min(750, Math.max(0, deadline - Date.now())));
    }
  }
  if (typeof audio.spatial === 'string') {
    await ctl(cfg, ['audio', 'spatial', audio.device, audio.spatial], 120000); // may wait on a UAC prompt
  }
}

// ---- dialogs and names ----

// Native folder dialog (or file dialog with `file: true`) on this PC, brought to the front. Resolves to the chosen path, or null if cancelled.
function pickPath({ initial = '', title = 'Choose a folder', file = false } = {}) {
  return new Promise((resolve, reject) => {
    execFile('powershell.exe',
      ['-NoProfile', '-NonInteractive', '-STA', '-ExecutionPolicy', 'Bypass', '-File', path.join(__dirname, 'pick-path.ps1')],
      {
        windowsHide: true, timeout: 5 * 60 * 1000, encoding: 'utf8',
        env: { ...process.env, DD_INITIAL: initial, DD_TITLE: title, DD_MODE: file ? 'file' : 'folder' },
      },
      (err, stdout) => (err ? reject(new Error('Folder dialog failed: ' + err.message)) : resolve(stdout.trim() || null)));
  });
}

// Monitor name from the EDID block in the registry (descriptor tag 0xFC).
const edidCache = new Map();
function edidName(hwId) {
  if (!/^[A-Z0-9]{7}$/.test(hwId || '')) return Promise.resolve(null);
  if (edidCache.has(hwId)) return Promise.resolve(edidCache.get(hwId));
  return new Promise((resolve) => {
    execFile('reg', ['query', `HKLM\\SYSTEM\\CurrentControlSet\\Enum\\DISPLAY\\${hwId}`, '/s', '/v', 'EDID'],
      { windowsHide: true, timeout: 5000 }, (err, stdout) => {
        let name = null;
        const m = !err && /EDID\s+REG_BINARY\s+([0-9A-Fa-f]+)/.exec(stdout || '');
        if (m) {
          const b = Buffer.from(m[1], 'hex');
          for (let o = 54; o + 18 <= Math.min(b.length, 126); o += 18) {
            if (b[o] === 0 && b[o + 1] === 0 && b[o + 2] === 0 && b[o + 3] === 0xfc) {
              name = b.toString('latin1', o + 5, o + 18).split('\n')[0].trim() || null;
              break;
            }
          }
        }
        edidCache.set(hwId, name);
        resolve(name);
      });
  });
}

module.exports = { saveLiveXml, saveLayoutTo, applyProfile, captureExtras, applyExtras, undoLayout, connectedDisplays, ddcList, ddcCaps, ddcGet, ddcSet, buildLayout, validateProfile, rememberLayout, tmpFile, listPlaybackDevices, applyAudio, pickPath, edidName };
