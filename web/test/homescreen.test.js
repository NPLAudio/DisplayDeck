'use strict';
// Adding DisplayDeck to a phone's home screen: the icons, the manifest, and the tags every page carries.
const test = require('node:test');
const assert = require('node:assert/strict');
const { spawn } = require('child_process');
const fs = require('fs');
const http = require('http');
const net = require('net');
const os = require('os');
const path = require('path');

const web = path.resolve(__dirname, '..');
const PNG_MAGIC = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);

function freePort() {
  return new Promise((resolve, reject) => {
    const s = net.createServer();
    s.listen(0, '127.0.0.1', () => { const { port } = s.address(); s.close(() => resolve(port)); });
    s.on('error', reject);
  });
}

function get(host, port, p, headers = {}) {
  return new Promise((resolve, reject) => {
    http.get({ host, port, path: p, headers }, (res) => {
      const chunks = [];
      res.on('data', (c) => chunks.push(c));
      res.on('end', () => resolve({ status: res.statusCode, headers: res.headers, body: Buffer.concat(chunks) }));
    }).on('error', reject);
  });
}

async function withServer(extraEnv, fn) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'dd-home-'));
  const port = await freePort();
  const child = spawn(process.execPath, ['server.js'], {
    cwd: web,
    env: { ...process.env, PORT: String(port), PROFILES_DIR: path.join(dir, 'profiles'), STATE_FILE: path.join(dir, 'state.json'), ...extraEnv },
    stdio: 'ignore',
  });
  try {
    for (let i = 0; i < 50; i++) { try { await get('127.0.0.1', port, '/app.css'); break; } catch (_) { await new Promise((r) => setTimeout(r, 100)); } }
    await fn(port);
  } finally {
    child.kill();
  }
}

const lanAddress = () => {
  for (const list of Object.values(os.networkInterfaces())) for (const a of list || []) if (a.family === 'IPv4' && !a.internal) return a.address;
  return null;
};

test('the home-screen icons are served as PNG images', async () => {
  await withServer({ HOST: '127.0.0.1' }, async (port) => {
    for (const [file, size] of [['apple-touch-icon.png', 180], ['icon-192.png', 192], ['icon-512.png', 512]]) {
      const r = await get('127.0.0.1', port, '/' + file);
      assert.equal(r.status, 200, file);
      assert.equal(r.headers['content-type'], 'image/png', file);
      assert.deepEqual(r.body.subarray(0, 8), PNG_MAGIC, file + ' is a real PNG, not text');
      assert.equal(r.body.readUInt32BE(16), size, file + ' width');
      assert.equal(r.body.readUInt32BE(20), size, file + ' height');
      assert.equal(r.body[25], 2, file + ' is plain RGB, with no transparency to show up black on a phone');
    }
  });
});

test('the manifest makes the page installable and points at the icons', async () => {
  await withServer({ HOST: '127.0.0.1' }, async (port) => {
    const r = await get('127.0.0.1', port, '/manifest.webmanifest');
    assert.equal(r.status, 200);
    assert.match(r.headers['content-type'], /^application\/manifest\+json/);
    const m = JSON.parse(r.body.toString('utf8'));
    assert.equal(m.name, 'DisplayDeck');
    assert.equal(m.display, 'standalone');
    assert.equal(m.start_url, '/', 'on this PC there is no key to carry');
    assert.deepEqual(m.icons.map((i) => i.sizes), ['192x192', '512x512']);
    for (const i of m.icons) assert.equal((await get('127.0.0.1', port, i.src)).status, 200, i.src + ' exists');
  });
});

test('every page carries the icon, manifest and app tags', async () => {
  await withServer({ HOST: '127.0.0.1' }, async (port) => {
    for (const p of ['/', '/phone', '/settings', '/editor']) {
      const html = (await get('127.0.0.1', port, p)).body.toString('utf8');
      assert.match(html, /<link rel="apple-touch-icon" href="\/apple-touch-icon\.png">/, p);
      assert.match(html, /<link rel="manifest" href="\/manifest\.webmanifest">/, p);
      assert.match(html, /apple-mobile-web-app-capable" content="yes"/, p);
      assert.match(html, /apple-mobile-web-app-title" content="DisplayDeck"/, p);
    }
  });
});

test('the phone page explains how to add it to the home screen on iPhone and Android', async () => {
  await withServer({ HOST: '127.0.0.1' }, async (port) => {
    const html = (await get('127.0.0.1', port, '/phone')).body.toString('utf8');
    assert.match(html, /Add to Home Screen/);
    assert.match(html, /Safari/);
    assert.match(html, /Share/);
    assert.match(html, /Android/);
  });
});

test('for a phone that is signed in, the manifest start address signs the app in again', { skip: !lanAddress() && 'no network address' }, async () => {
  const key = 'a'.repeat(32);
  await withServer({ HOST: '0.0.0.0', ACCESS_KEY: key }, async (port) => {
    const lan = lanAddress();
    assert.equal((await get(lan, port, '/manifest.webmanifest')).status, 403, 'not without the key');
    const ok = await get(lan, port, '/manifest.webmanifest', { Cookie: `dd_key=${key}` });
    assert.equal(ok.status, 200);
    assert.equal(JSON.parse(ok.body.toString('utf8')).start_url, `/?key=${key}`);
    const icon = await get(lan, port, '/apple-touch-icon.png', { Cookie: `dd_key=${key}` });
    assert.deepEqual(icon.body.subarray(0, 8), PNG_MAGIC);
    assert.equal((await get(lan, port, '/apple-touch-icon.png')).status, 403, 'icons need the key too');
  });
});
