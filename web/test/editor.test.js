'use strict';
// The layout editor's server side, run against a stand-in for the Windows helper (nothing touches the screens).
const test = require('node:test');
const assert = require('node:assert/strict');
const { execFileSync, spawn } = require('child_process');
const fs = require('fs');
const http = require('http');
const net = require('net');
const os = require('os');
const path = require('path');

process.env.EDITOR_MIN_SECONDS = '1';
const editor = require('../lib/editor');

const csc = 'C:\\Windows\\Microsoft.NET\\Framework64\\v4.0.30319\\csc.exe';
const work = fs.mkdtempSync(path.join(os.tmpdir(), 'dd-editor-'));
const fake = path.join(work, 'fake-ctl.exe');
const log = path.join(work, 'calls.log');
process.env.FAKE_CTL_LOG = log;
const skip = !fs.existsSync(csc) && 'needs the .NET Framework compiler';
if (!skip) execFileSync(csc, ['/nologo', `/out:${fake}`, path.join(__dirname, 'fake-ctl', 'FakeCtl.cs')]);

const fixtures = path.join(__dirname, 'fixtures');
function newCfg() {
  const profilesDir = fs.mkdtempSync(path.join(work, 'profiles-'));
  fs.copyFileSync(path.join(fixtures, 'desk.xml'), path.join(profilesDir, 'Desk.xml'));
  fs.copyFileSync(path.join(fixtures, 'living-room.xml'), path.join(profilesDir, 'Living Room.xml'));
  fs.writeFileSync(path.join(profilesDir, 'Desk.audio.json'), JSON.stringify({ label: 'Speakers', device: 'X\\Device\\Y\\Render', spatial: 'off' }));
  return { profilesDir, namesFile: path.join(profilesDir, 'names.json'), ctl: fake, stateFile: path.join(profilesDir, 'state.json') };
}
const calls = () => (fs.existsSync(log) ? fs.readFileSync(log, 'utf8').trim().split('\n').filter(Boolean) : []);
const resetLog = () => fs.rmSync(log, { force: true });
const byName = (state, n) => state.displays.find((d) => d.name === n);
const layoutOf = (state) => ({ displays: state.displays });
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const validName = (n) => /^[^\\/:*?"<>|\x00-\x1f]{1,60}$/.test(n);

test('starting from a profile lays out its displays and lists the others as off', { skip }, async () => {
  const s = await editor.editorState(newCfg(), 'Desk');
  const on = s.displays.filter((d) => d.enabled).map((d) => d.name).sort();
  assert.deepEqual(on, ['C49RG9x', 'MPG 491C OLED', 'SONY TV *30']);
  const mpg = byName(s, 'MPG 491C OLED');
  assert.deepEqual([mpg.x, mpg.y, mpg.width, mpg.height, mpg.refresh, mpg.primary, mpg.rotation], [0, 0, 5120, 1440, 144, true, 1]);
  assert.equal(byName(s, 'C49RG9x').y, -1440, 'negative positions survive');
  const spare = s.displays.find((d) => d.targetId === 4400);
  assert.equal(spare.enabled, false, 'a connected display the profile does not use is offered as off');
  assert.equal(spare.connected, true);
  assert.deepEqual(s.profiles, ['Desk', 'Living Room']);
});

test('a connected display that is on offers the modes Windows lists; one that is off offers saved and common sizes', { skip }, async () => {
  const s = await editor.editorState(newCfg(), 'Desk');
  const mpg = byName(s, 'MPG 491C OLED');
  assert.equal(mpg.deviceModes, true);
  assert.deepEqual(mpg.modes.map((m) => `${m.width}x${m.height}`), ['5120x1440', '2560x1440']);
  assert.deepEqual(mpg.modes[0].refresh, [60, 120, 144]);
  const tv = byName(s, 'SONY TV *30');
  assert.notEqual(tv.deviceModes, true);
  const uhd = tv.modes.find((m) => m.width === 3840 && m.height === 2160);
  assert.ok(uhd.refresh.includes(60) && uhd.refresh.includes(120), 'rates seen in saved profiles are offered');
  assert.ok(tv.modes.some((m) => m.width === 1920 && m.height === 1080), 'common sizes are offered');
});

test('starting from scratch has every display off and nothing placed', { skip }, async () => {
  const s = await editor.editorState(newCfg(), null);
  assert.ok(s.displays.length >= 4);
  assert.ok(s.displays.every((d) => !d.enabled && !d.primary));
  assert.equal(s.from, null);
});

test('starting from the current screens uses the live layout', { skip }, async () => {
  const s = await editor.editorState(newCfg(), null, true);
  assert.equal(s.live, true);
});

test('an unknown starting profile is a 404', { skip }, async () => {
  await assert.rejects(editor.editorState(newCfg(), 'Nope'), (e) => e.status === 404);
});

test('validate builds the layout and asks Windows to check it, changing nothing', { skip }, async () => {
  const cfg = newCfg();
  const s = await editor.editorState(cfg, 'Desk'); resetLog();
  const r = await editor.validate(cfg, layoutOf(s), 'Desk');
  assert.equal(r.ok, true);
  const c = calls();
  assert.ok(c.some((l) => l.startsWith('display|build|')), 'built');
  assert.ok(c.some((l) => /^display\|apply\|.*\|--validate$/.test(l)), 'validated');
  assert.ok(!c.some((l) => l.startsWith('display|apply|') && !l.endsWith('--validate')), 'never applied');
  const build = c.find((l) => l.startsWith('display|build|')).split('|');
  assert.ok(build[4].endsWith('Desk.xml'), 'the base profile is the first template');
});

test('when Windows refuses a layout, its reason comes back', { skip }, async () => {
  const cfg = newCfg();
  const s = await editor.editorState(cfg, 'Desk');
  byName(s, 'MPG 491C OLED').width = 9900;
  await assert.rejects(editor.validate(cfg, layoutOf(s), 'Desk'), /Windows rejected the layout/);
});

test('the layout is cleaned before it reaches the helper', () => {
  const clean = editor.cleanLayout({ displays: [{ targetId: '4353.7', name: 'x'.repeat(200), enabled: false, primary: 1, width: '1920', height: 1080, x: 'abc', y: -5.5, refresh: 99999, rotation: 'Rotate90', extra: 'ignored' }] });
  const d = clean.displays[0];
  assert.deepEqual([d.targetId, d.name.length, d.enabled, d.primary, d.width, d.x, d.y, d.refresh, d.rotation, 'extra' in d], [4353, 80, false, true, 1920, 0, -5, 1000, 2, false]);
  assert.throws(() => editor.cleanLayout({}), /no displays/);
  assert.throws(() => editor.cleanLayout({ displays: new Array(17).fill({}) }), /Too many/);
});

test('saving writes the profile and copies the audio settings from the one it started from', { skip }, async () => {
  const cfg = newCfg();
  const s = await editor.editorState(cfg, 'Desk');
  const r = await editor.save(cfg, { name: 'Desk copy', layout: layoutOf(s), base: 'Desk', copyAudioFrom: 'Desk' }, validName);
  assert.equal(r.ok, true);
  assert.ok(fs.existsSync(path.join(cfg.profilesDir, 'Desk copy.xml')));
  assert.equal(JSON.parse(fs.readFileSync(path.join(cfg.profilesDir, 'Desk copy.audio.json'), 'utf8')).label, 'Speakers');
});

test('saving refuses bad names, and will not replace a profile unless asked', { skip }, async () => {
  const cfg = newCfg();
  const s = await editor.editorState(cfg, 'Desk');
  await assert.rejects(editor.save(cfg, { name: 'a/b', layout: layoutOf(s) }, validName), (e) => e.status === 400);
  await assert.rejects(editor.save(cfg, { name: 'Desk', layout: layoutOf(s) }, validName), (e) => e.status === 409);
  const again = await editor.save(cfg, { name: 'Desk', layout: layoutOf(s), overwrite: true }, validName);
  assert.equal(again.ok, true);
});

test('Try layout applies it; Keep makes "undo" return to what was there before', { skip }, async () => {
  const cfg = newCfg();
  const s = await editor.editorState(cfg, 'Desk'); resetLog();
  const r = await editor.startTest(cfg, layoutOf(s), 'Desk', 30);
  assert.equal(r.ok, true);
  assert.equal(editor.testStatus().pending, true);
  const c = calls();
  assert.deepEqual(c.map((l) => l.split('|').slice(0, 2).join('|')), ['display|save', 'display|build', 'display|apply']);
  await editor.keep();
  assert.equal(editor.testStatus().pending, false);
  const last = calls().pop();
  assert.ok(last.startsWith('display|remember|'), 'the earlier layout is remembered for Undo');
  assert.equal(last.split('|')[2], c[0].split('|')[2], 'it is the layout saved before the test');
});

test('Go back applies the layout saved before the test', { skip }, async () => {
  const cfg = newCfg();
  const s = await editor.editorState(cfg, 'Desk'); resetLog();
  await editor.startTest(cfg, layoutOf(s), 'Desk', 30);
  const before = calls()[0].split('|')[2];
  await editor.revert();
  assert.equal(editor.testStatus().pending, false);
  const last = calls().pop().split('|');
  assert.deepEqual([last[0], last[1], last[2]], ['display', 'apply', before]);
});

test('an untouched test reverts by itself', { skip }, async () => {
  const cfg = newCfg();
  const s = await editor.editorState(cfg, 'Desk'); resetLog();
  await editor.startTest(cfg, layoutOf(s), 'Desk', 1);
  await sleep(1800);
  assert.equal(editor.testStatus().pending, false);
  const applies = calls().filter((l) => l.startsWith('display|apply|'));
  assert.equal(applies.length, 2, 'the test, then the automatic way back');
});

test('trying again while a test is running still goes back to the original layout', { skip }, async () => {
  const cfg = newCfg();
  const s = await editor.editorState(cfg, 'Desk'); resetLog();
  await editor.startTest(cfg, layoutOf(s), 'Desk', 30);
  await editor.startTest(cfg, layoutOf(s), 'Desk', 30);
  assert.equal(calls().filter((l) => l.startsWith('display|save|')).length, 1, 'the starting layout is captured once');
  await editor.revert();
});

test('a failed test leaves nothing pending', { skip }, async () => {
  const cfg = newCfg();
  const s = await editor.editorState(cfg, 'Desk');
  byName(s, 'MPG 491C OLED').width = 9900;
  await assert.rejects(editor.startTest(cfg, layoutOf(s), 'Desk', 30), /Windows rejected/);
  assert.equal(editor.testStatus().pending, false);
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

test('the editor page and its API are served, and the API refuses cross-origin writes', { skip }, async () => {
  const cfg = newCfg();
  const port = await freePort();
  const child = spawn(process.execPath, ['server.js'], {
    cwd: path.resolve(__dirname, '..'),
    env: { ...process.env, PORT: String(port), HOST: '127.0.0.1', PROFILES_DIR: cfg.profilesDir, STATE_FILE: cfg.stateFile, DISPLAYDECK_CTL: fake },
    stdio: 'ignore',
  });
  try {
    for (let i = 0; i < 50; i++) { try { await request(port, 'GET', '/app.css'); break; } catch (_) { await sleep(100); } }
    const page = await request(port, 'GET', '/editor');
    assert.equal(page.status, 200);
    assert.match(page.text, /Arrange your displays/);
    const st = await request(port, 'GET', '/api/editor/state?from=Desk');
    assert.equal(st.status, 200);
    assert.equal(st.json.displays.filter((d) => d.enabled).length, 3);
    assert.equal((await request(port, 'GET', '/api/editor/state?from=Nope')).status, 404);
    const evil = await request(port, 'POST', '/api/editor/save', { name: 'x', layout: { displays: st.json.displays } }, { Origin: 'http://evil.example' });
    assert.equal(evil.status, 403);
    const ok = await request(port, 'POST', '/api/editor/save', { name: 'Made here', layout: { displays: st.json.displays }, base: 'Desk' });
    assert.equal(ok.status, 200);
    assert.ok(fs.existsSync(path.join(cfg.profilesDir, 'Made here.xml')));
    const dupe = await request(port, 'POST', '/api/editor/save', { name: 'Made here', layout: { displays: st.json.displays } });
    assert.equal(dupe.status, 409);
  } finally {
    child.kill();
  }
});
