'use strict';
// Monitor controls (DDC/CI), run against a stand-in for the Windows helper: nothing talks to a real display.
const test = require('node:test');
const assert = require('node:assert/strict');
const { execFileSync, spawn } = require('child_process');
const fs = require('fs');
const http = require('http');
const net = require('net');
const os = require('os');
const path = require('path');

const monitors = require('../lib/monitors');

const csc = 'C:\\Windows\\Microsoft.NET\\Framework64\\v4.0.30319\\csc.exe';
const work = fs.mkdtempSync(path.join(os.tmpdir(), 'dd-monitors-'));
const fake = path.join(work, 'fake-ctl.exe');
const log = path.join(work, 'calls.log');
process.env.FAKE_CTL_LOG = log;
const skip = !fs.existsSync(csc) && 'needs the .NET Framework compiler';
if (!skip) execFileSync(csc, ['/nologo', `/out:${fake}`, path.join(__dirname, 'fake-ctl', 'FakeCtl.cs')]);

const cfg = { profilesDir: path.join(work, 'profiles'), namesFile: path.join(work, 'names.json'), ctl: fake, stateFile: path.join(work, 'state.json') };
const calls = () => (fs.existsSync(log) ? fs.readFileSync(log, 'utf8').trim().split('\n').filter(Boolean) : []);
const resetLog = () => fs.rmSync(log, { force: true });

test('lists each display with what it can do right now', { skip }, async () => {
  const r = await monitors.list(cfg);
  assert.equal(r.monitors.length, 3);
  const mpg = r.monitors.find((m) => m.id === 4353);
  assert.equal(mpg.name, 'MPG 491C OLED');
  assert.equal(mpg.ddc, true);
  assert.equal(mpg.backend, 'nvidia');
  assert.deepEqual(mpg.controls.brightness, { current: 70, max: 100 });
  assert.equal(mpg.controls.input.current, 15, 'DisplayPort 1');
  assert.equal(mpg.controls.mute.current, 2);
  assert.equal(mpg.controls.power.current, 1);
  assert.ok(!('sharpness' in mpg.controls), 'a feature the display does not report is not offered');
});

test('a display that cannot be reached is listed as not controllable', { skip }, async () => {
  const tv = (await monitors.list(cfg)).monitors.find((m) => m.id === 4355);
  assert.equal(tv.ddc, false);
  assert.equal(tv.backend, 'none');
  assert.deepEqual(tv.controls, {});
});

test('capabilities give the inputs, presets and power states the display itself lists', { skip }, async () => {
  const c = await monitors.capabilities(cfg, 4353, true);
  assert.deepEqual(c.inputs, [17, 18, 15, 16]);
  assert.deepEqual(c.presets, [1, 5, 8]);
  assert.deepEqual(c.power, [1, 4]);
  assert.equal(c.has.input, true);
  assert.equal(c.has.sharpness, false);
  assert.ok(c.supported.includes('0x60') && c.supported.includes('0xD6'));
  assert.equal(c.model, 'FALCON');
});

test('the slow capabilities query is remembered, and can be refreshed', { skip }, async () => {
  await monitors.capabilities(cfg, 4353, true);
  resetLog();
  await monitors.capabilities(cfg, 4353);
  assert.equal(calls().length, 0, 'served from memory');
  await monitors.capabilities(cfg, 4353, true);
  assert.equal(calls().filter((l) => l.startsWith('ddc|caps|')).length, 1, 'asked again on request');
});

test('a display that will not report capabilities gives its reason', { skip }, async () => {
  await assert.rejects(monitors.capabilities(cfg, 4356, true), /did not report its capabilities/);
});

test('changing the input sends exactly that feature and value', { skip }, async () => {
  resetLog();
  const r = await monitors.set(cfg, 4353, '0x60', 17);
  assert.deepEqual(r, { ok: true, code: 0x60, value: 17 });
  assert.deepEqual(calls(), ['ddc|set|4353|0x60|17']);
});

test('the page may only change the features it knows about', { skip }, async () => {
  resetLog();
  for (const code of ['0x10', '0x12', '0x14', '0x60', '0x62', '0x8d', '0xd6', '87']) await monitors.set(cfg, 4353, code, 1);
  assert.equal(calls().length, 8);
  await assert.rejects(monitors.set(cfg, 4353, '0x42', 1), (e) => e.status === 403);
  assert.equal(calls().length, 8, 'nothing was sent for the unknown feature');
});

test('raw commands reach any feature, except the factory-reset ones', { skip }, async () => {
  resetLog();
  await monitors.set(cfg, 4353, '0xE0', 3, { raw: true });
  assert.deepEqual(calls(), ['ddc|set|4353|0xe0|3']);
  for (const code of ['0x04', '0x05', '0x06', '0x08', '0x0A']) {
    await assert.rejects(monitors.set(cfg, 4353, code, 1, { raw: true }), (e) => e.status === 403 && /factory/.test(e.message), code);
  }
  assert.equal(calls().length, 1);
});

test('feature codes and values are checked before anything is sent', { skip }, async () => {
  resetLog();
  for (const bad of ['zz', '0x1FF', '', null, '-1']) await assert.rejects(monitors.set(cfg, 4353, bad, 1), (e) => e.status === 400, String(bad));
  for (const bad of [-1, 65536, 1.5, 'abc', null]) await assert.rejects(monitors.set(cfg, 4353, '0x10', bad), (e) => e.status === 400, String(bad));
  await assert.rejects(monitors.set(cfg, 'x', '0x10', 1), (e) => e.status === 404);
  assert.equal(calls().length, 0);
  assert.equal(monitors.parseCode('0x0F'), 15);
  assert.equal(monitors.parseCode('60'), 0x60);
});

test('a numeric code is the code itself, as the page sends it', { skip }, async () => {
  resetLog();
  await monitors.set(cfg, 4353, 0x60, 17);
  await monitors.set(cfg, 4353, 96, 18);
  await monitors.set(cfg, 4353, 0x10, 50);
  assert.deepEqual(calls(), ['ddc|set|4353|0x60|17', 'ddc|set|4353|0x60|18', 'ddc|set|4353|0x10|50']);
  assert.equal(monitors.parseCode(96), 0x60);
  assert.equal(monitors.parseCode('96'), 0x96, 'text is hex');
  assert.throws(() => monitors.parseCode(256), (e) => e.status === 400);
  assert.throws(() => monitors.parseCode(-1), (e) => e.status === 400);
});

test('every control on the page can be sent, using exactly the codes the page uses', { skip }, async () => {
  // The page keeps its feature codes in one table; pull it out of the page itself so this cannot drift.
  const page = fs.readFileSync(path.join(__dirname, '..', 'public', 'monitors.html'), 'utf8');
  const table = /const KEYS = ({[^}]*});/.exec(page);
  assert.ok(table, 'the page lists its feature codes');
  const keys = Function('return ' + table[1])();
  assert.ok(Object.keys(keys).length >= 8);
  resetLog();
  for (const [name, code] of Object.entries(keys)) {
    for (const form of [code, '0x' + code.toString(16)]) await monitors.set(cfg, 4353, form, 1);
  }
  assert.equal(calls().length, Object.keys(keys).length * 2, 'nothing the page sends is refused');
});

test('reading a feature returns its value and maximum', { skip }, async () => {
  const r = await monitors.get(cfg, 4353, '0x10');
  assert.deepEqual(r, { code: 16, current: 70, max: 100 });
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

test('the Monitors page and its API work, and refuse cross-origin writes', { skip }, async () => {
  const port = await freePort();
  const child = spawn(process.execPath, ['server.js'], {
    cwd: path.resolve(__dirname, '..'),
    env: { ...process.env, PORT: String(port), HOST: '127.0.0.1', PROFILES_DIR: cfg.profilesDir, STATE_FILE: cfg.stateFile, DISPLAYDECK_CTL: fake },
    stdio: 'ignore',
  });
  try {
    for (let i = 0; i < 50; i++) { try { await request(port, 'GET', '/app.css'); break; } catch (_) { await sleep(100); } }
    const page = await request(port, 'GET', '/monitors');
    assert.equal(page.status, 200);
    assert.match(page.text, /About these controls/);
    assert.match(page.text, /apple-touch-icon/);

    const list = await request(port, 'GET', '/api/monitors');
    assert.equal(list.status, 200);
    assert.equal(list.json.monitors.length, 3);

    const caps = await request(port, 'GET', '/api/monitors/4353/capabilities?refresh=1');
    assert.equal(caps.status, 200);
    assert.deepEqual(caps.json.inputs, [17, 18, 15, 16]);
    assert.equal((await request(port, 'GET', '/api/monitors/4356/capabilities?refresh=1')).status, 400);

    const evil = await request(port, 'POST', '/api/monitors/4353/set', { code: '0x60', value: 17 }, { Origin: 'http://evil.example' });
    assert.equal(evil.status, 403);
    const ok = await request(port, 'POST', '/api/monitors/4353/set', { code: '0x60', value: 17 });
    assert.equal(ok.status, 200);
    assert.equal((await request(port, 'POST', '/api/monitors/4353/set', { code: '0x42', value: 1 })).status, 403, 'unknown feature without raw');
    assert.equal((await request(port, 'POST', '/api/monitors/4353/set', { code: '0x42', value: 1, raw: true })).status, 200, 'raw from this PC');
    assert.equal((await request(port, 'GET', '/api/monitors/4353/vcp?code=0x10')).json.current, 70);
    assert.equal((await request(port, 'GET', '/api/monitors/abc/capabilities')).status, 404, 'only numeric display ids');
  } finally {
    child.kill();
  }
});
