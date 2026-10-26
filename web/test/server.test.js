'use strict';
// Starts the real server on a spare port with scratch folders and checks the pages it serves.
const test = require('node:test');
const assert = require('node:assert/strict');
const { spawn } = require('child_process');
const fs = require('fs');
const http = require('http');
const net = require('net');
const os = require('os');
const path = require('path');

const web = path.resolve(__dirname, '..');

function freePort() {
  return new Promise((resolve, reject) => {
    const s = net.createServer();
    s.listen(0, '127.0.0.1', () => { const { port } = s.address(); s.close(() => resolve(port)); });
    s.on('error', reject);
  });
}

function get(port, p) {
  return new Promise((resolve, reject) => {
    http.get({ host: '127.0.0.1', port, path: p }, (res) => {
      let body = '';
      res.on('data', (c) => (body += c));
      res.on('end', () => resolve({ status: res.statusCode, type: res.headers['content-type'] || '', body }));
    }).on('error', reject);
  });
}

async function withServer(fn) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'dd-srv-'));
  const port = await freePort();
  const child = spawn(process.execPath, ['server.js'], {
    cwd: web,
    env: { ...process.env, PORT: String(port), HOST: '127.0.0.1', PROFILES_DIR: path.join(dir, 'profiles'), STATE_FILE: path.join(dir, 'state.json') },
    stdio: 'ignore',
  });
  try {
    for (let i = 0; i < 50; i++) {
      try { await get(port, '/app.css'); break; } catch (_) { await new Promise((r) => setTimeout(r, 100)); }
    }
    await fn(port);
  } finally {
    child.kill();
  }
}

test('serves the three pages and the shared assets with the right types', async () => {
  await withServer(async (port) => {
    for (const [p, type, marker] of [
      ['/', 'text/html', 'id="profiles"'],
      ['/phone', 'text/html', 'id="phoneBody"'],
      ['/settings', 'text/html', 'id="setForm"'],
      ['/app.css', 'text/css', '--primary'],
      ['/app.js', 'text/javascript', 'function renderShell'],
      ['/favicon.svg', 'image/svg+xml', '<svg'],
    ]) {
      const r = await get(port, p);
      assert.equal(r.status, 200, p);
      assert.ok(r.type.startsWith(type), `${p} content type was ${r.type}`);
      assert.ok(r.body.includes(marker), `${p} should contain ${marker}`);
    }
  });
});

test('pages use the shadcn token names, so a generated theme can be pasted in', async () => {
  await withServer(async (port) => {
    const css = (await get(port, '/app.css')).body;
    for (const token of ['--background', '--foreground', '--card', '--primary', '--secondary', '--muted', '--accent', '--destructive', '--border', '--input', '--ring', '--radius']) {
      assert.ok(css.includes(token + ':'), `missing ${token}`);
    }
    assert.ok(css.includes(':root.dark'), 'has a dark theme');
  });
});

test('only the listed files can be fetched; traversal and unknown paths are refused', async () => {
  await withServer(async (port) => {
    for (const p of ['/package.json', '/server.js', '/lib/system.js', '/../server.js', '/%2e%2e/server.js', '/public/index.html', '/nope']) {
      const r = await get(port, p);
      assert.equal(r.status, 404, `${p} should be 404 but was ${r.status}`);
    }
  });
});

test('settings report the app version and this PC as editable', async () => {
  await withServer(async (port) => {
    const s = JSON.parse((await get(port, '/api/settings')).body);
    assert.equal(s.canEdit, true);
    assert.match(s.version, /^\d+\.\d+\.\d+$/);
  });
});

test('/api/current reports the saved profile names and never fails when the helper is missing', async () => {
  await withServer(async (port) => {
    const r = await get(port, '/api/current');
    assert.equal(r.status, 200);
    const j = JSON.parse(r.body);
    assert.ok(Array.isArray(j.profiles));
    assert.ok('current' in j && 'lastApplied' in j && 'liveAvailable' in j);
  });
});

test('/api/audio/apply validates its input', async () => {
  await withServer(async (port) => {
    const post = (body) => new Promise((resolve, reject) => {
      const req = http.request({ host: '127.0.0.1', port, path: '/api/audio/apply', method: 'POST', headers: { 'Content-Type': 'application/json' } }, (res) => {
        let b = ''; res.on('data', (c) => (b += c)); res.on('end', () => resolve({ status: res.statusCode, body: b }));
      });
      req.on('error', reject); req.end(JSON.stringify(body));
    });
    assert.equal((await post({})).status, 400);
    assert.equal((await post({ device: '   ' })).status, 400);
    assert.equal((await post({ device: 'Nope\Device\X\Render' })).status, 500); // helper reports no such device
  });
});
