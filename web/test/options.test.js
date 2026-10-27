'use strict';
// Per-profile shortcuts, programs to run and saved HDR/scaling/wallpaper, against a stand-in for the Windows helper.
const test = require('node:test');
const assert = require('node:assert/strict');
const { execFileSync, spawn } = require('child_process');
const fs = require('fs');
const http = require('http');
const net = require('net');
const os = require('os');
const path = require('path');

const options = require('../lib/options');
const system = require('../lib/system');

const csc = 'C:\\Windows\\Microsoft.NET\\Framework64\\v4.0.30319\\csc.exe';
const work = fs.mkdtempSync(path.join(os.tmpdir(), 'dd-options-'));
const fake = path.join(work, 'fake-ctl.exe');
const skip = !fs.existsSync(csc) && 'needs the .NET Framework compiler';
if (!skip) execFileSync(csc, ['/nologo', `/out:${fake}`, path.join(__dirname, 'fake-ctl', 'FakeCtl.cs')]);

const profilesDir = path.join(work, 'profiles');
fs.mkdirSync(profilesDir, { recursive: true });
const cfg = { profilesDir, namesFile: path.join(work, 'names.json'), ctl: fake, stateFile: path.join(work, 'state.json') };
const script = path.join(work, 'ok.bat');
fs.writeFileSync(script, '@echo off\r\necho %1 > "%~dp0marker.txt"\r\n');

// ---------- shortcuts ----------

test('a shortcut is written one way whatever was typed', () => {
  assert.equal(options.parseHotkey('ctrl+alt+1'), 'Ctrl+Alt+1');
  assert.equal(options.parseHotkey('Shift + Control + f5'), 'Ctrl+Shift+F5');
  assert.equal(options.parseHotkey('win+up'), 'Win+Up');
  assert.equal(options.parseHotkey('alt+num3'), 'Alt+Numpad3');
  assert.equal(options.parseHotkey('Ctrl+Alt+PgDn'.replace('PgDn', 'PageDown')), 'Ctrl+Alt+PageDown');
});

test('shortcuts that would get in the way of typing, or cannot work, are refused with a reason', () => {
  for (const [bad, why] of [
    ['', /Press the keys/], ['Ctrl', /Add a key/], ['A', /Include Ctrl, Alt or Win/], ['Shift+A', /Include Ctrl, Alt or Win/],
    ['Ctrl+A+B', /one key/], ['Ctrl+Alt+Delete', /Windows keeps/], ['Ctrl+Banana', /cannot be used/], ['Ctrl+F25', /cannot be used/],
  ]) assert.throws(() => options.parseHotkey(bad), why, bad);
});

// ---------- starting programs ----------

test('arguments are split on spaces except inside quotes', () => {
  assert.deepEqual(options.splitArgs('-a "b c" d'), ['-a', 'b c', 'd']);
  assert.deepEqual(options.splitArgs('   '), []);
});

test('each kind of file is started the way Windows needs', () => {
  const env = { SystemRoot: 'C:\\Windows' };
  const exe = options.launchSpec({ path: 'C:\\Tools\\a.exe', args: '-x "y z"' }, env);
  assert.deepEqual([exe.file, exe.args], ['C:\\Tools\\a.exe', ['-x', 'y z']]);
  const ps = options.launchSpec({ path: 'C:\\Tools\\a.ps1', args: '-Name Desk' }, env);
  assert.match(ps.file, /powershell\.exe$/);
  assert.deepEqual(ps.args.slice(0, 4), ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File']);
  assert.deepEqual(ps.args.slice(4), ['C:\\Tools\\a.ps1', '-Name', 'Desk']);
  assert.match(options.launchSpec({ path: 'C:\\Tools\\a.vbs', args: '' }, env).file, /wscript\.exe$/);
  const bat = options.launchSpec({ path: 'C:\\Tools\\a.bat', args: 'one "t w o"' }, env);
  assert.match(bat.file, /cmd\.exe$/);
  assert.equal(bat.args[3], '""C:\\Tools\\a.bat" "one" "t w o""');
  assert.equal(bat.options.windowsVerbatimArguments, true);
  const lnk = options.launchSpec({ path: 'C:\\Tools\\Game.lnk', args: '' }, env);
  assert.equal(lnk.args[3], '"start "" "C:\\Tools\\Game.lnk""');
});

test('only full paths to real program files are accepted', () => {
  const good = options.validateRun([{ path: script, args: 'Desk' }]);
  assert.deepEqual(good, [{ path: script, args: 'Desk' }]);
  for (const [bad, why] of [
    [{ path: 'ok.bat' }, /full path/], [{ path: '\\\\server\\share\\a.bat' }, /full path/], [{ path: 'C:\\x\\a.txt' }, /cannot be started/],
    [{ path: path.join(work, 'missing.bat') }, /not found/], [{ path: work + '\\' }, /cannot be started/],
    [{ path: script, args: 'a & b' }, /cannot contain/], [{ path: script, args: 'say "hi' }, /quote mark/], [{ path: script, args: 'a\nb' }, /line breaks/],
  ]) assert.throws(() => options.validateRun([bad]), why, JSON.stringify(bad));
  assert.throws(() => options.validateRun(new Array(6).fill({ path: script })), /At most 5/);
  assert.throws(() => options.validateRun('x'), /list/);
});

test('programs are started without waiting, and one that cannot start is reported', async () => {
  const started = [];
  const spawnFn = (file, args, opts) => {
    const handlers = {};
    const child = { once: (ev, fn) => { handlers[ev] = fn; }, unref: () => {} };
    setImmediate(() => (file.endsWith('bad.exe') ? handlers.error(new Error('spawn EACCES')) : handlers.spawn()));
    started.push({ file, detached: opts.detached, stdio: opts.stdio });
    return child;
  };
  const bad = path.join(work, 'bad.exe');
  fs.writeFileSync(bad, '');
  const errors = await options.runPrograms([{ path: bad, args: '' }, { path: path.join(work, 'gone.exe'), args: '' }, { path: script, args: '' }], { spawnFn });
  assert.equal(errors.length, 2);
  assert.match(errors[0], /bad\.exe could not be started: spawn EACCES/);
  assert.match(errors[1], /gone\.exe was not found/);
  assert.ok(started.every((s) => s.detached === true && s.stdio === 'ignore'));
  assert.equal(started.length, 2, 'the missing file is never spawned');
});

// ---------- saving and reading ----------

const fakeSystem = {
  captureExtras: async (_cfg, sections) => JSON.parse(execFileSync(fake, ['extras', 'capture', sections.join(',')], { encoding: 'utf8' })),
};

test('options are stored beside the profile and an empty set leaves no file', async () => {
  fs.writeFileSync(path.join(profilesDir, 'Desk.xml'), 'x');
  fs.writeFileSync(path.join(profilesDir, 'Den.xml'), 'x');
  const r = await options.update(cfg, 'Desk', { hotkey: 'ctrl+alt+1', run: [{ path: script, args: 'Desk' }], extras: { hdr: 'capture', dpi: 'capture', wallpaper: 'capture' } }, fakeSystem, ['Desk', 'Den']);
  assert.equal(r.options.hotkey, 'Ctrl+Alt+1');
  assert.deepEqual(r.options.extras, { hdr: 1, dpi: 1, wallpaper: 'picture', at: r.options.extras.at });
  assert.ok(r.options.extras.at);
  assert.ok(fs.existsSync(path.join(profilesDir, 'Desk.options.json')));
  assert.deepEqual(options.allHotkeys(cfg, ['Desk', 'Den']), [{ name: 'Desk', hotkey: 'Ctrl+Alt+1' }]);

  await options.update(cfg, 'Desk', { hotkey: '', run: [], extras: { hdr: 'remove', dpi: 'remove', wallpaper: 'remove' } }, fakeSystem, ['Desk', 'Den']);
  assert.ok(!fs.existsSync(path.join(profilesDir, 'Desk.options.json')), 'nothing left to store');
});

test('a shortcut can belong to one profile only', async () => {
  await options.update(cfg, 'Desk', { hotkey: 'Ctrl+Alt+2' }, fakeSystem, ['Desk', 'Den']);
  await assert.rejects(options.update(cfg, 'Den', { hotkey: 'alt+ctrl+2' }, fakeSystem, ['Desk', 'Den']), (e) => e.status === 409 && /already used by "Desk"/.test(e.message));
  await options.update(cfg, 'Desk', { hotkey: 'Ctrl+Alt+2' }, fakeSystem, ['Desk', 'Den']); // its own shortcut again is fine
  options.remove(cfg, 'Desk');
});

test('a setting the displays cannot give is refused with the reason and nothing is saved', { skip }, async () => {
  process.env.FAKE_NO_HDR = '1';
  try {
    await assert.rejects(options.update(cfg, 'Den', { hotkey: 'Ctrl+Alt+3', extras: { hdr: 'capture' } }, fakeSystem, ['Desk', 'Den']), /support HDR/);
  } finally { delete process.env.FAKE_NO_HDR; }
  assert.ok(!fs.existsSync(path.join(profilesDir, 'Den.options.json')), 'the shortcut was not saved either');
});

test('a duplicate keeps the programs and extras but not the shortcut', async () => {
  await options.update(cfg, 'Desk', { hotkey: 'Ctrl+Alt+4', run: [{ path: script, args: '' }], extras: { dpi: 'capture' } }, fakeSystem, ['Desk', 'Den']);
  options.copy(cfg, 'Desk', 'Den');
  const den = options.read(cfg, 'Den');
  assert.equal(den.hotkey, '');
  assert.equal(den.run.length, 1);
  assert.ok(den.extras.dpi);
  options.remove(cfg, 'Desk'); options.remove(cfg, 'Den');
});

test('a hand-edited file with nonsense in it is read as far as it makes sense', () => {
  fs.writeFileSync(path.join(profilesDir, 'Odd.options.json'), JSON.stringify({ hotkey: 'Banana', run: [{ path: 5 }, { path: script }], extras: { hdr: [], bogus: 1 } }));
  const o = options.read(cfg, 'Odd');
  assert.equal(o.hotkey, '');
  assert.equal(o.run.length, 1);
  assert.deepEqual(Object.keys(o.extras), ['hdr']);
  fs.writeFileSync(path.join(profilesDir, 'Odd.options.json'), '{ not json');
  assert.deepEqual(options.read(cfg, 'Odd').run, []);
  options.remove(cfg, 'Odd');
});

// ---------- over HTTP ----------

function freePort() {
  return new Promise((resolve, reject) => {
    const s = net.createServer();
    s.listen(0, '127.0.0.1', () => { const { port } = s.address(); s.close(() => resolve(port)); });
    s.on('error', reject);
  });
}
function request(port, method, p, body, headers = {}) {
  return new Promise((resolve, reject) => {
    const req = http.request({ host: '127.0.0.1', port, path: p, method, headers: { 'Content-Type': 'application/json', ...headers } }, (res) => {
      let data = '';
      res.on('data', (c) => (data += c));
      res.on('end', () => { let json = null; try { json = JSON.parse(data); } catch (_) { /* html */ } resolve({ status: res.statusCode, json, text: data }); });
    });
    req.on('error', reject);
    req.end(body ? JSON.stringify(body) : undefined);
  });
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function withServer(env, fn) {
  const port = await freePort();
  const dir = fs.mkdtempSync(path.join(work, 'srv-'));
  const profiles = path.join(dir, 'profiles');
  fs.mkdirSync(profiles);
  fs.writeFileSync(path.join(profiles, 'Desk.xml'), 'LAYOUT');
  fs.writeFileSync(path.join(profiles, 'Den.xml'), 'LAYOUT');
  const child = spawn(process.execPath, ['server.js'], {
    cwd: path.resolve(__dirname, '..'),
    env: { ...process.env, ...env, PORT: String(port), HOST: '127.0.0.1', PROFILES_DIR: profiles, STATE_FILE: path.join(dir, 'state.json'), DISPLAYDECK_CTL: fake },
    stdio: 'ignore',
  });
  try {
    for (let i = 0; i < 50; i++) { try { await request(port, 'GET', '/app.css'); break; } catch (_) { await sleep(100); } }
    await fn(port, profiles);
  } finally { child.kill(); }
}

test('applying a profile applies its extras and starts its programs, and reports what went wrong', { skip }, async () => {
  const marker = path.join(work, 'marker.txt');
  fs.rmSync(marker, { force: true });
  await withServer({}, async (port, profiles) => {
    const set = await request(port, 'POST', '/api/options/Desk', { hotkey: 'Ctrl+Alt+1', run: [{ path: script, args: 'applied' }], extras: { dpi: 'capture' } });
    assert.equal(set.status, 200, set.text);

    const list = await request(port, 'GET', '/api/profiles');
    const desk = list.json.profiles.find((p) => p.name === 'Desk');
    assert.equal(desk.options.hotkey, 'Ctrl+Alt+1');
    assert.equal(desk.options.extras.dpi, 1);
    assert.equal(list.json.profiles.length, 2, 'the options file is not a profile');
    assert.deepEqual((await request(port, 'GET', '/api/hotkeys')).json.hotkeys, [{ name: 'Desk', hotkey: 'Ctrl+Alt+1' }]);

    const applied = await request(port, 'POST', '/api/apply/Desk');
    assert.equal(applied.status, 200, applied.text);
    assert.deepEqual(applied.json.warnings, ['C49RG9x is not on, so its HDR setting was left alone.']);
    assert.equal(applied.json.ran, 1);
    assert.deepEqual(applied.json.runErrors, []);
    for (let i = 0; i < 50 && !fs.existsSync(marker); i++) await sleep(100);
    assert.match(fs.readFileSync(marker, 'utf8'), /applied/, 'the program ran with its arguments');

    const plain = await request(port, 'POST', '/api/apply/Den');
    assert.equal(plain.json.ran, undefined);
    assert.deepEqual(plain.json.warnings, []);
  });
});

test('a failing extra is a warning: the layout still applied', { skip }, async () => {
  await withServer({ FAKE_EXTRAS_FAIL: '1' }, async (port) => {
    await request(port, 'POST', '/api/options/Desk', { extras: { dpi: 'capture' } });
    const r = await request(port, 'POST', '/api/apply/Desk');
    assert.equal(r.status, 200);
    assert.equal(r.json.ok, true);
    assert.match(r.json.warnings[0], /HDR, scaling or wallpaper: Windows would not change the scaling/);
  });
});

test('options can only be changed from this PC and same-origin, for profiles that exist', { skip }, async () => {
  await withServer({}, async (port, profiles) => {
    assert.equal((await request(port, 'POST', '/api/options/Desk', { hotkey: 'Ctrl+Alt+1' }, { Origin: 'http://evil.example' })).status, 403);
    assert.equal((await request(port, 'POST', '/api/options/Nope', { hotkey: 'Ctrl+Alt+1' })).status, 404);
    assert.equal((await request(port, 'POST', '/api/options/Desk', { hotkey: 'Ctrl+Z' })).status, 200);
    assert.equal((await request(port, 'POST', '/api/options/Den', { hotkey: 'Ctrl+Z' })).status, 409);
    assert.equal((await request(port, 'POST', '/api/options/Den', { run: [{ path: 'notepad.exe' }] })).status, 400, 'a bare name is not a full path');
    assert.equal((await request(port, 'POST', '/api/hotkeys/report', { failed: { Desk: 'Another app uses Ctrl+Z.' } })).status, 200);
    const desk = (await request(port, 'GET', '/api/profiles')).json.profiles.find((p) => p.name === 'Desk');
    assert.equal(desk.options.hotkeyError, 'Another app uses Ctrl+Z.');
    assert.equal((await request(port, 'DELETE', '/api/profiles/Desk')).status, 200);
    assert.ok(!fs.existsSync(path.join(profiles, 'Desk.options.json')), 'deleting a profile removes its options');
  });
});

test('a profile cannot be named so that it collides with an options file', { skip }, async () => {
  await withServer({}, async (port) => {
    const r = await request(port, 'POST', '/api/profiles/save', { name: 'Desk.options' });
    assert.equal(r.status, 400);
  });
});

test('system.js passes the extras to the helper as a file', { skip }, async () => {
  const log = path.join(work, 'calls.log');
  process.env.FAKE_CTL_LOG = log;
  fs.rmSync(log, { force: true });
  const r = await system.applyExtras(cfg, { dpi: [{ targetId: 4353, percent: 125 }] });
  assert.deepEqual(r.applied, ['MPG 491C OLED scaling 125%']);
  const c = await system.captureExtras(cfg, ['hdr', 'dpi']);
  assert.equal(c.hdr.length, 1);
  assert.match(fs.readFileSync(log, 'utf8'), /extras\|capture\|hdr,dpi/);
  delete process.env.FAKE_CTL_LOG;
});
