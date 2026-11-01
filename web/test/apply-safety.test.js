'use strict';
// Things that make screens flash, blank or switch by themselves, run against a stand-in for the Windows helper.
const test = require('node:test');
const assert = require('node:assert/strict');
const { execFileSync, spawn } = require('child_process');
const fs = require('fs');
const http = require('http');
const net = require('net');
const os = require('os');
const path = require('path');

const lock = require('../lib/lock');

const csc = 'C:\\Windows\\Microsoft.NET\\Framework64\\v4.0.30319\\csc.exe';
const work = fs.mkdtempSync(path.join(os.tmpdir(), 'dd-safety-'));
const fake = path.join(work, 'fake-ctl.exe');
const skip = !fs.existsSync(csc) && 'needs the .NET Framework compiler';
if (!skip) execFileSync(csc, ['/nologo', `/out:${fake}`, path.join(__dirname, 'fake-ctl', 'FakeCtl.cs')]);
const fixtures = path.join(__dirname, 'fixtures');

function freePort() {
  return new Promise((resolve, reject) => {
    const s = net.createServer();
    s.listen(0, '127.0.0.1', () => { const { port } = s.address(); s.close(() => resolve(port)); });
    s.on('error', reject);
  });
}
function request(port, method, p, body) {
  return new Promise((resolve, reject) => {
    const req = http.request({ host: '127.0.0.1', port, path: p, method, headers: { 'Content-Type': 'application/json' } }, (res) => {
      let data = '';
      res.on('data', (c) => (data += c));
      res.on('end', () => { let json = null; try { json = JSON.parse(data); } catch (_) { /* html */ } resolve({ status: res.statusCode, json, text: data }); });
    });
    req.on('error', reject);
    req.end(body ? JSON.stringify(body) : undefined);
  });
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// `live` names the fixture the screens "currently" show.
async function withServer(live, env, fn) {
  const dir = fs.mkdtempSync(path.join(work, 'srv-'));
  const profiles = path.join(dir, 'profiles');
  fs.mkdirSync(profiles);
  fs.copyFileSync(path.join(fixtures, 'desk.xml'), path.join(profiles, 'Desk.xml'));
  fs.copyFileSync(path.join(fixtures, 'living-room.xml'), path.join(profiles, 'Living Room.xml'));
  const log = path.join(dir, 'calls.log');
  const port = await freePort();
  const child = spawn(process.execPath, ['server.js'], {
    cwd: path.resolve(__dirname, '..'),
    env: {
      ...process.env, ...env, PORT: String(port), HOST: '127.0.0.1', PROFILES_DIR: profiles, STATE_FILE: path.join(dir, 'state.json'),
      DISPLAYDECK_CTL: fake, FAKE_CTL_LOG: log, FAKE_LIVE_XML: path.join(fixtures, live), EDITOR_MIN_SECONDS: '1',
    },
    stdio: 'ignore',
  });
  const calls = () => (fs.existsSync(log) ? fs.readFileSync(log, 'utf8').split('\n').map((l) => l.trim()).filter(Boolean) : []);
  try {
    for (let i = 0; i < 50; i++) { try { await request(port, 'GET', '/app.css'); break; } catch (_) { await sleep(100); } }
    await fn(port, calls);
  } finally { child.kill(); }
}
const applies = (calls) => calls().filter((l) => l.startsWith('display|apply|'));

// ---------- the same layout again ----------

test('applying the layout the screens already have does not touch the display mode', { skip }, async () => {
  await withServer('desk.xml', {}, async (port, calls) => {
    const r = await request(port, 'POST', '/api/apply/Desk');
    assert.equal(r.status, 200);
    assert.equal(r.json.layout, 'unchanged');
    assert.equal(applies(calls).length, 0, 'no SetDisplayConfig, so no flash and the Undo layout is kept');
    assert.equal((await request(port, 'GET', '/api/current')).json.lastApplied, 'Desk', 'it is still remembered as applied');
  });
});

test('a different layout is applied, and so is the same one when forced', { skip }, async () => {
  await withServer('living-room.xml', {}, async (port, calls) => {
    assert.equal((await request(port, 'POST', '/api/apply/Desk')).json.layout, 'applied');
    assert.equal(applies(calls).length, 1);
    assert.equal((await request(port, 'POST', '/api/apply/Living%20Room')).json.layout, 'unchanged');
    assert.equal(applies(calls).length, 1);
    assert.equal((await request(port, 'POST', '/api/apply/Living%20Room?force=1')).json.layout, 'applied');
    assert.equal(applies(calls).length, 2);
  });
});

test('an unchanged layout still applies the profile audio, extras and programs', { skip }, async () => {
  await withServer('desk.xml', {}, async (port, calls) => {
    await request(port, 'POST', '/api/audio-config/Desk', { device: 'Studio\\Device\\Main\\Render', spatial: 'off' });
    const r = await request(port, 'POST', '/api/apply/Desk');
    assert.equal(r.json.layout, 'unchanged');
    assert.equal(r.json.audio, 'applied');
    assert.ok(calls().some((l) => l.startsWith('audio|default|')));
  });
});

// ---------- two changes at once ----------

test('work that changes the screens runs one piece at a time, in order, even after a failure', async () => {
  const order = [];
  const piece = (name, ms, fail) => lock.exclusive(async () => {
    order.push('start ' + name); await sleep(ms); order.push('end ' + name);
    if (fail) throw new Error('boom');
  });
  const results = await Promise.allSettled([piece('a', 60), piece('b', 10, true), piece('c', 10)]);
  assert.deepEqual(order, ['start a', 'end a', 'start b', 'end b', 'start c', 'end c']);
  assert.deepEqual(results.map((r) => r.status), ['fulfilled', 'rejected', 'fulfilled']);
});

test('two profiles applied at the same moment never overlap', { skip }, async () => {
  await withServer('living-room.xml', { FAKE_APPLY_DELAY_MS: '400' }, async (port, calls) => {
    const [a, b] = await Promise.all([request(port, 'POST', '/api/apply/Desk'), request(port, 'POST', '/api/apply/Desk?force=1')]);
    assert.equal(a.status, 200);
    assert.equal(b.status, 200);
    const seq = calls().filter((l) => l.startsWith('display|apply|') || l === 'apply-done').map((l) => (l === 'apply-done' ? 'done' : 'start'));
    assert.deepEqual(seq, ['start', 'done', 'start', 'done'], 'the second change waited for the first to finish');
  });
});

// ---------- a layout being tried ----------

test('applying a profile cancels a layout being tried, so its timer cannot switch back over it', { skip }, async () => {
  await withServer('living-room.xml', {}, async (port, calls) => {
    const state = (await request(port, 'GET', '/api/editor/state?from=Desk')).json;
    const started = await request(port, 'POST', '/api/editor/test', { layout: { displays: state.displays }, base: 'Desk', seconds: 1 });
    assert.equal(started.status, 200, started.text);
    assert.equal((await request(port, 'GET', '/api/editor/test')).json.pending, true);
    const before = applies(calls).length;

    assert.equal((await request(port, 'POST', '/api/apply/Desk?force=1')).status, 200);
    assert.equal((await request(port, 'GET', '/api/editor/test')).json.pending, false, 'nothing is waiting to revert');
    await sleep(1800); // past the time the revert would have fired
    assert.equal(applies(calls).length, before + 1, 'only the explicit apply changed the screens');
  });
});

test('Undo also cancels a layout being tried', { skip }, async () => {
  await withServer('living-room.xml', {}, async (port) => {
    const state = (await request(port, 'GET', '/api/editor/state?from=Desk')).json;
    await request(port, 'POST', '/api/editor/test', { layout: { displays: state.displays }, base: 'Desk', seconds: 30 });
    assert.equal((await request(port, 'POST', '/api/undo-layout')).status, 200);
    assert.equal((await request(port, 'GET', '/api/editor/test')).json.pending, false);
  });
});
