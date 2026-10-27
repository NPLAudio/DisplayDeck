'use strict';
// Per-profile options kept next to the layout, in <name>.options.json:
//   hotkey   a global shortcut that applies the profile (registered by the tray)
//   run      programs started after the profile is applied
//   extras   HDR, scaling and wallpaper captured from the screens (read and written by the helper)
// Only this PC may change them: a program path ends up being executed.

const fs = require('fs');
const path = require('path');
const { spawn } = require('child_process');

const SECTIONS = ['hdr', 'dpi', 'wallpaper'];
const MAX_RUN = 5;
const PROGRAM_TYPES = /\.(exe|com|lnk|bat|cmd|ps1|vbs|js|py|ahk)$/i;
const SHELL_SPECIAL = /[&|<>^%]/;

const fail = (message, status = 400) => Object.assign(new Error(message), { status });

// ---- hotkeys ----

const KEY_NAMES = {
  up: 'Up', down: 'Down', left: 'Left', right: 'Right', home: 'Home', end: 'End', insert: 'Insert', ins: 'Insert',
  delete: 'Delete', del: 'Delete', pageup: 'PageUp', pagedown: 'PageDown', space: 'Space',
};

// Turns what a person typed into the one spelling the tray understands, or throws with a plain reason.
function parseHotkey(text) {
  const parts = String(text || '').split('+').map((p) => p.trim()).filter(Boolean);
  if (!parts.length) throw fail('Press the keys you want to use.');
  const mods = new Set();
  let key = null;
  for (const raw of parts) {
    const p = raw.toLowerCase();
    if (p === 'ctrl' || p === 'control') mods.add('Ctrl');
    else if (p === 'alt') mods.add('Alt');
    else if (p === 'shift') mods.add('Shift');
    else if (p === 'win' || p === 'windows' || p === 'meta' || p === 'cmd') mods.add('Win');
    else if (key) throw fail('Use one key with Ctrl, Alt, Shift or Win.');
    else if (/^[a-z0-9]$/.test(p)) key = p.toUpperCase();
    else if (/^f([1-9]|1\d|2[0-4])$/.test(p)) key = p.toUpperCase();
    else if (/^(numpad|num)([0-9])$/.test(p)) key = 'Numpad' + p.slice(-1);
    else if (KEY_NAMES[p]) key = KEY_NAMES[p];
    else throw fail(`"${raw}" cannot be used as a shortcut key.`);
  }
  if (!key) throw fail('Add a key as well as Ctrl, Alt, Shift or Win.');
  if (!mods.has('Ctrl') && !mods.has('Alt') && !mods.has('Win')) throw fail('Include Ctrl, Alt or Win so the shortcut does not get in the way of typing.');
  if (mods.has('Ctrl') && mods.has('Alt') && key === 'Delete') throw fail('Windows keeps Ctrl+Alt+Delete for itself.');
  return ['Ctrl', 'Alt', 'Shift', 'Win'].filter((m) => mods.has(m)).concat(key).join('+');
}

// ---- reading and writing ----

const fileFor = (cfg, name) => path.join(cfg.profilesDir, `${name}.options.json`);

function emptyOptions() {
  return { hotkey: '', run: [], extras: {}, extrasAt: null };
}

function read(cfg, name) {
  const out = emptyOptions();
  let j;
  try { j = JSON.parse(fs.readFileSync(fileFor(cfg, name), 'utf8').replace(/^﻿/, '')); } catch (_) { return out; }
  if (!j || typeof j !== 'object') return out;
  if (typeof j.hotkey === 'string') { try { out.hotkey = j.hotkey ? parseHotkey(j.hotkey) : ''; } catch (_) { /* ignore a hand-edited bad value */ } }
  if (Array.isArray(j.run)) {
    out.run = j.run
      .filter((r) => r && typeof r.path === 'string' && r.path)
      .map((r) => ({ path: r.path, args: typeof r.args === 'string' ? r.args : '' }))
      .slice(0, MAX_RUN);
  }
  if (j.extras && typeof j.extras === 'object') {
    for (const s of SECTIONS) if (j.extras[s]) out.extras[s] = j.extras[s];
  }
  if (typeof j.extrasAt === 'string') out.extrasAt = j.extrasAt;
  return out;
}

function isEmpty(o) {
  return !o.hotkey && !o.run.length && !Object.keys(o.extras).length;
}

function write(cfg, name, opts) {
  const file = fileFor(cfg, name);
  if (isEmpty(opts)) { fs.rmSync(file, { force: true }); return; }
  fs.mkdirSync(cfg.profilesDir, { recursive: true });
  fs.writeFileSync(file, JSON.stringify({ hotkey: opts.hotkey, run: opts.run, extras: opts.extras, extrasAt: opts.extrasAt }, null, 2) + '\n');
}

function remove(cfg, name) {
  fs.rmSync(fileFor(cfg, name), { force: true });
}

// Duplicating a profile keeps its programs and extras; a shortcut can only belong to one profile.
function copy(cfg, from, to) {
  if (from === to) return;
  const o = read(cfg, from);
  o.hotkey = '';
  if (!isEmpty(o) && !fs.existsSync(fileFor(cfg, to))) write(cfg, to, o);
}

// What the pages and the tray need to know, without the captured details.
function summary(o) {
  const x = o.extras;
  return {
    hotkey: o.hotkey,
    run: o.run,
    extras: {
      hdr: x.hdr ? x.hdr.length : 0,
      dpi: x.dpi ? x.dpi.length : 0,
      wallpaper: x.wallpaper ? x.wallpaper.mode || 'picture' : null,
      at: o.extrasAt,
    },
  };
}

function allHotkeys(cfg, names) {
  return names.map((name) => ({ name, hotkey: read(cfg, name).hotkey })).filter((h) => h.hotkey);
}

// ---- changing options ----

function checkProgram(entry) {
  if (!entry || typeof entry.path !== 'string') throw fail('Choose a program or script to run.');
  const p = entry.path.trim();
  if (!/^[A-Za-z]:\\/.test(p)) throw fail(`Use the full path of a file on this PC: ${p || '(empty)'}`);
  if (!PROGRAM_TYPES.test(p)) throw fail('That file type cannot be started. Use .exe, .lnk, .bat, .cmd, .ps1, .vbs, .js, .py or .ahk.');
  let st;
  try { st = fs.statSync(p); } catch (_) { throw fail(`File not found: ${p}`); }
  if (!st.isFile()) throw fail(`Not a file: ${p}`);
  const args = typeof entry.args === 'string' ? entry.args.trim() : '';
  if (args.length > 400) throw fail('The arguments are too long.');
  if (/[\x00-\x1f]/.test(args)) throw fail('The arguments cannot contain line breaks.');
  if ((args.match(/"/g) || []).length % 2) throw fail('The arguments have a quote mark without its pair.');
  if (needsShell(p) && SHELL_SPECIAL.test(args)) throw fail('For this file type, the arguments cannot contain & | < > ^ or %.');
  return { path: path.normalize(p), args };
}

function validateRun(list) {
  if (!Array.isArray(list)) throw fail('The programs to run must be a list.');
  if (list.length > MAX_RUN) throw fail(`At most ${MAX_RUN} programs can run.`);
  return list.map(checkProgram);
}

// body: { hotkey?: string, run?: [{path, args}], extras?: { hdr|dpi|wallpaper: 'capture' | 'remove' } }
// Anything left out is unchanged.
async function update(cfg, name, body, system, otherNames) {
  const o = read(cfg, name);

  if ('hotkey' in body) {
    const hk = body.hotkey ? parseHotkey(body.hotkey) : '';
    if (hk) {
      const taken = otherNames.find((n) => n !== name && read(cfg, n).hotkey === hk);
      if (taken) throw fail(`${hk} is already used by "${taken}".`, 409);
    }
    o.hotkey = hk;
  }
  if ('run' in body) o.run = validateRun(body.run);

  const notes = [];
  if (body.extras && typeof body.extras === 'object') {
    const want = [];
    for (const s of SECTIONS) {
      const action = body.extras[s];
      if (action === 'remove') delete o.extras[s];
      else if (action === 'capture') want.push(s);
      else if (action !== undefined) throw fail(`Unknown action for ${s}.`);
    }
    if (want.length) {
      const got = await system.captureExtras(cfg, want);
      notes.push(...(got.notes || []));
      const missing = want.filter((s) => !got[s] || (Array.isArray(got[s]) && !got[s].length));
      if (missing.length) throw fail(notes.join(' ') || `Could not read ${missing.join(', ')} from the displays.`);
      for (const s of want) o.extras[s] = got[s];
      o.extrasAt = new Date().toISOString();
    }
    if (!Object.keys(o.extras).length) o.extrasAt = null;
  }

  write(cfg, name, o);
  return { ok: true, options: summary(o), notes };
}

// ---- running programs ----

function splitArgs(text) {
  const out = [];
  const re = /"([^"]*)"|(\S+)/g;
  let m;
  while ((m = re.exec(text || ''))) out.push(m[1] !== undefined ? m[1] : m[2]);
  return out;
}

const needsShell = (p) => /\.(bat|cmd|lnk|py|ahk)$/i.test(p);

// How to start one entry: the program to spawn and its arguments.
function launchSpec(entry, env = process.env) {
  const p = entry.path;
  const args = splitArgs(entry.args);
  const cwd = path.dirname(p);
  const sys32 = path.join(env.SystemRoot || 'C:\\Windows', 'System32');
  if (/\.(exe|com)$/i.test(p)) return { file: p, args, options: { cwd } };
  if (/\.ps1$/i.test(p)) {
    return { file: path.join(sys32, 'WindowsPowerShell', 'v1.0', 'powershell.exe'), args: ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', p, ...args], options: { cwd, windowsHide: true } };
  }
  if (/\.(vbs|js)$/i.test(p)) return { file: path.join(sys32, 'wscript.exe'), args: ['//nologo', p, ...args], options: { cwd, windowsHide: true } };
  // .bat and .cmd run in a hidden command window; .lnk, .py, .ahk open with whatever Windows has for them.
  const quoted = args.map((a) => `"${a}"`).join(' ');
  const line = /\.(bat|cmd)$/i.test(p) ? `"${p}" ${quoted}` : `start "" "${p}" ${quoted}`;
  return { file: path.join(sys32, 'cmd.exe'), args: ['/d', '/s', '/c', `"${line.trim()}"`], options: { cwd, windowsHide: true, windowsVerbatimArguments: true } };
}

// Starts each program and does not wait for it. Returns one message per program that could not be started.
async function runPrograms(list, { spawnFn = spawn, env = process.env } = {}) {
  const errors = [];
  for (const entry of list) {
    if (!fs.existsSync(entry.path)) { errors.push(`${path.basename(entry.path)} was not found.`); continue; }
    const spec = launchSpec(entry, env);
    const err = await new Promise((resolve) => {
      let child;
      try {
        child = spawnFn(spec.file, spec.args, { ...spec.options, detached: true, stdio: 'ignore' });
      } catch (e) { return resolve(e.message); }
      child.once('error', (e) => resolve(e.message));
      child.once('spawn', () => { child.unref(); resolve(null); });
    });
    if (err) errors.push(`${path.basename(entry.path)} could not be started: ${err}`);
  }
  return errors;
}

// ---- what the tray reported about shortcuts it could not register ----

let hotkeyProblems = {};
const reportHotkeys = (failed) => {
  hotkeyProblems = {};
  for (const [name, why] of Object.entries(failed || {})) if (typeof why === 'string') hotkeyProblems[name] = why.slice(0, 200);
};
const hotkeyProblem = (name) => hotkeyProblems[name] || null;

module.exports = {
  SECTIONS, parseHotkey, read, write, remove, copy, summary, allHotkeys, update, validateRun,
  splitArgs, launchSpec, runPrograms, reportHotkeys, hotkeyProblem, isEmpty,
};
