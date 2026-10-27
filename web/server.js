'use strict';
const http = require('http');
const crypto = require('crypto');
const fs = require('fs');
const path = require('path');
const { buildProfiles, scanFiles, writeLastApplied, currentStatus } = require('./lib/profiles');
const editor = require('./lib/editor');
const options = require('./lib/options');
const monitors = require('./lib/monitors');
const system = require('./lib/system');
const { createConfig } = require('./lib/settings');
const { qrSvg } = require('./lib/qr');
const os = require('os');

// The tray passes LAN_IP (it picks the adapter that has a gateway); otherwise guess.
function lanAddress() {
  if (process.env.LAN_IP) return process.env.LAN_IP;
  for (const [name, addrs] of Object.entries(os.networkInterfaces())) {
    if (/vethernet|wsl|virtualbox|vmware|loopback/i.test(name)) continue;
    const a = (addrs || []).find((x) => x.family === 'IPv4' && !x.internal && !x.address.startsWith('169.254.'));
    if (a) return a.address;
  }
  return null;
}

const root = path.resolve(__dirname, '..');
const config = createConfig({
  root,
  env: process.env,
  stateFile: process.env.STATE_FILE || path.join(__dirname, 'state.json'),
});
const port = Number(process.env.PORT) || 8765;
const host = process.env.HOST || '127.0.0.1';
const accessKey = process.env.ACCESS_KEY || '';

if (host !== '127.0.0.1' && host !== 'localhost' && accessKey.length < 16) {
  console.error('Refusing to listen on a network address without ACCESS_KEY (16+ hex chars).');
  process.exit(1);
}

// The only files served from public/: a fixed list, so no request can reach anything else.
const STATIC = {
  '/': ['index.html', 'text/html'],
  '/index.html': ['index.html', 'text/html'],
  '/phone': ['phone.html', 'text/html'],
  '/settings': ['settings.html', 'text/html'],
  '/editor': ['editor.html', 'text/html'],
  '/monitors': ['monitors.html', 'text/html'],
  '/app.css': ['app.css', 'text/css'],
  '/app.js': ['app.js', 'text/javascript'],
  '/favicon.svg': ['favicon.svg', 'image/svg+xml'],
  // Home-screen icons for phones (opaque squares; the phone rounds the corners).
  '/apple-touch-icon.png': ['apple-touch-icon.png', 'image/png'],
  '/icon-192.png': ['icon-192.png', 'image/png'],
  '/icon-512.png': ['icon-512.png', 'image/png'],
};

let version = '';
try { version = JSON.parse(fs.readFileSync(path.join(__dirname, 'package.json'), 'utf8').replace(/^﻿/, '')).version || ''; } catch (_) { /* optional */ }

const send = (res, code, body, type = 'application/json', cache = 'no-store') => {
  res.writeHead(code, { 'Content-Type': type.startsWith('image/png') ? type : type + '; charset=utf-8', 'Cache-Control': cache });
  res.end(typeof body === 'string' || Buffer.isBuffer(body) ? body : JSON.stringify(body));
};

const sameOrigin = (req) => !req.headers.origin || new URL(req.headers.origin).host === req.headers.host;

function readBody(req) {
  return new Promise((resolve, reject) => {
    let data = '';
    req.on('data', (c) => { data += c; if (data.length > 50000) { reject(new Error('Body too large')); req.destroy(); } });
    req.on('end', () => { try { resolve(data ? JSON.parse(data) : {}); } catch (_) { reject(new Error('Invalid JSON')); } });
    req.on('error', reject);
  });
}

// Requests from this machine are trusted. Anything else must present ACCESS_KEY,
// either as ?key=... on the first visit (which sets a cookie) or via that cookie.
const isLocal = (req) => /^(127\.0\.0\.1|::1|::ffff:127\.0\.0\.1)$/.test(req.socket.remoteAddress || '');
const keyMatches = (given) => {
  const a = Buffer.from(String(given || '')), b = Buffer.from(accessKey);
  return accessKey.length >= 16 && a.length === b.length && crypto.timingSafeEqual(a, b);
};
const cookieKey = (req) => /(?:^|;\s*)dd_key=([0-9a-f]+)/.exec(req.headers.cookie || '');

async function handle(req, res) {
  const url = new URL(req.url, 'http://x');
  const local = isLocal(req);
  if (!local) {
    if (keyMatches(url.searchParams.get('key'))) {
      res.writeHead(302, {
        'Set-Cookie': `dd_key=${accessKey}; Path=/; HttpOnly; SameSite=Strict; Max-Age=31536000`,
        Location: url.pathname,
      });
      return res.end();
    }
    if (!keyMatches(cookieKey(req) && cookieKey(req)[1])) {
      return send(res, 403, '<p>Access key required. Use the "Copy phone link" item in the DisplayDeck tray menu.</p>', 'text/html');
    }
  }
  const cfg = config.get(); // re-read each request so settings changes apply immediately

  if (req.method === 'GET' && Object.prototype.hasOwnProperty.call(STATIC, url.pathname)) {
    const [file, type] = STATIC[url.pathname];
    const raw = fs.readFileSync(path.join(__dirname, 'public', file), type === 'image/png' ? undefined : 'utf8');
    return send(res, 200, raw, type, type === 'text/html' ? 'no-store' : 'no-cache');
  }
  // Lets a phone install the page as an app. Past the access check, so a phone that is allowed in gets a start
  // address that signs it in again when the app is opened from the home screen (the app has its own cookies).
  if (req.method === 'GET' && url.pathname === '/manifest.webmanifest') {
    const manifest = {
      name: 'DisplayDeck',
      short_name: 'DisplayDeck',
      description: 'Switch display layouts and audio devices.',
      start_url: local || accessKey.length < 16 ? '/' : `/?key=${accessKey}`,
      scope: '/',
      display: 'standalone',
      background_color: '#09090b',
      theme_color: '#09090b',
      icons: [
        { src: '/icon-192.png', sizes: '192x192', type: 'image/png', purpose: 'any maskable' },
        { src: '/icon-512.png', sizes: '512x512', type: 'image/png', purpose: 'any maskable' },
      ],
    };
    return send(res, 200, JSON.stringify(manifest), 'application/manifest+json', 'no-store');
  }
  if (req.method === 'GET' && url.pathname === '/api/profiles') return send(res, 200, await buildProfiles(cfg));
  // Light status for hardware decks and other pollers: profile names, current, last applied.
  if (req.method === 'GET' && url.pathname === '/api/current') return send(res, 200, await currentStatus(cfg));

  // Set the default playback device (and optionally its spatial format) without involving a profile.
  if (req.method === 'POST' && url.pathname === '/api/audio/apply') {
    if (!sameOrigin(req)) return send(res, 403, { error: 'Cross-origin request refused' });
    const body = await readBody(req);
    if (typeof body.device !== 'string' || !body.device.trim()) return send(res, 400, { error: 'device is required' });
    const spatial = typeof body.spatial === 'string' && body.spatial.trim() ? body.spatial.trim() : undefined;
    await system.applyAudio(cfg, { device: body.device, spatial });
    return send(res, 200, { ok: true });
  }
  if (req.method === 'GET' && url.pathname === '/api/audio-devices') {
    return send(res, 200, { devices: await system.listPlaybackDevices(cfg) });
  }

  if (url.pathname === '/api/settings') {
    if (req.method === 'GET') return send(res, 200, { ...config.describe(), canEdit: local, version });
    if (req.method === 'POST') {
      // These paths end up being executed, so only this PC may change them.
      if (!local) return send(res, 403, { error: 'Settings can only be changed from the PC running DisplayDeck.' });
      if (!sameOrigin(req)) return send(res, 403, { error: 'Cross-origin request refused' });
      try {
        config.save(await readBody(req));
      } catch (e) {
        return send(res, 400, { error: e.message });
      }
      return send(res, 200, { ...config.describe(), canEdit: true, version });
    }
  }

  if (req.method === 'GET' && url.pathname === '/api/phone-link') {
    // Contains the access key, so only this PC may see it.
    if (!local) return send(res, 403, { error: 'The phone link is only shown on the PC running DisplayDeck.' });
    const ip = lanAddress();
    if (host === '127.0.0.1' || host === 'localhost' || accessKey.length < 16) return send(res, 200, { enabled: false });
    if (!ip) return send(res, 200, { enabled: true, link: null });
    const link = `http://${ip}:${port}/?key=${accessKey}`;
    return send(res, 200, { enabled: true, link, svg: qrSvg(link) });
  }

  if (req.method === 'POST' && url.pathname === '/api/pick-path') {
    // Opens a dialog on this PC's desktop, so it is only for requests from this PC.
    if (!local) return send(res, 403, { error: 'The file picker only works on the PC running DisplayDeck.' });
    if (!sameOrigin(req)) return send(res, 403, { error: 'Cross-origin request refused' });
    const body = await readBody(req);
    const picked = await system.pickPath({
      initial: typeof body.initial === 'string' ? body.initial : '',
      title: typeof body.title === 'string' ? body.title.slice(0, 200) : undefined,
      file: body.file === true,
    });
    return send(res, 200, { path: picked });
  }

  const audioCfg = /^\/api\/audio-config\/(.+)$/.exec(url.pathname);
  if (req.method === 'POST' && audioCfg) {
    if (!sameOrigin(req)) return send(res, 403, { error: 'Cross-origin request refused' });
    const name = decodeURIComponent(audioCfg[1]);
    if (!scanFiles(cfg).some((f) => f.name === name)) return send(res, 404, { error: `No such profile: ${name}` });
    const body = await readBody(req);
    const target = path.join(cfg.profilesDir, `${name}.audio.json`);
    if (body.clear) {
      fs.rmSync(target, { force: true });
      return send(res, 200, { ok: true, cleared: true });
    }
    const conf = {};
    if (typeof body.label === 'string' && body.label.trim()) conf.label = body.label.trim();
    if (typeof body.device !== 'string' || !body.device.trim()) return send(res, 400, { error: 'device is required' });
    conf.device = body.device;
    conf.spatial = typeof body.spatial === 'string' && body.spatial.trim() ? body.spatial.trim() : 'off';
    fs.writeFileSync(target, JSON.stringify(conf, null, 2) + '\n');
    if (body.applyNow) await system.applyAudio(cfg, conf);
    return send(res, 200, { ok: true, audio: conf, applied: !!body.applyNow });
  }
  const apply = /^\/api\/apply\/(.+)$/.exec(url.pathname);
  if (req.method === 'POST' && apply) {
    if (!sameOrigin(req)) return send(res, 403, { error: 'Cross-origin request refused' });
    const name = decodeURIComponent(apply[1]);
    const entry = scanFiles(cfg).find((f) => f.name === name); // only names that exist on disk
    if (!entry) return send(res, 404, { error: `No such profile: ${name}` });
    const layout = await system.applyProfile(cfg, entry.file);
    const result = { ok: true, audio: 'none', warnings: [...(layout.skipped || [])] };
    const opts = options.read(cfg, name);
    if (Object.keys(opts.extras).length) {
      // HDR, scaling and wallpaper. A problem here is reported but does not undo the layout.
      try {
        const x = await system.applyExtras(cfg, opts.extras);
        result.warnings.push(...(x.skipped || []));
      } catch (e) { result.warnings.push(`HDR, scaling or wallpaper: ${e.message}`); }
    }
    try {
      const a = JSON.parse(fs.readFileSync(path.join(cfg.profilesDir, `${name}.audio.json`), 'utf8').replace(/^﻿/, ''));
      await system.applyAudio(cfg, a);
      result.audio = 'applied';
    } catch (e) {
      if (e.code !== 'ENOENT') { result.audio = 'failed'; result.audioError = e.message; }
    }
    writeLastApplied(cfg, name);
    if (opts.run.length) {
      result.ran = opts.run.length;
      result.runErrors = await options.runPrograms(opts.run);
    }
    return send(res, 200, result);
  }

  // Keyboard shortcuts for profiles. The tray reads the list to register them and says which ones Windows refused.
  if (req.method === 'GET' && url.pathname === '/api/hotkeys') {
    return send(res, 200, { hotkeys: options.allHotkeys(cfg, scanFiles(cfg).map((f) => f.name)) });
  }
  if (req.method === 'POST' && url.pathname === '/api/hotkeys/report') {
    if (!local) return send(res, 403, { error: 'Only the DisplayDeck app on this PC can report shortcuts.' });
    if (!sameOrigin(req)) return send(res, 403, { error: 'Cross-origin request refused' });
    const body = await readBody(req);
    options.reportHotkeys(body.failed);
    return send(res, 200, { ok: true });
  }

  // A profile's shortcut, programs to run and saved HDR, scaling and wallpaper. A program path ends up being
  // executed, so only this PC may change them.
  const opt = /^\/api\/options\/(.+)$/.exec(url.pathname);
  if (req.method === 'POST' && opt) {
    if (!local) return send(res, 403, { error: 'Profile options can only be changed on the PC running DisplayDeck.' });
    if (!sameOrigin(req)) return send(res, 403, { error: 'Cross-origin request refused' });
    const name = decodeURIComponent(opt[1]);
    const names = scanFiles(cfg).map((f) => f.name);
    if (!names.includes(name)) return send(res, 404, { error: `No such profile: ${name}` });
    try {
      return send(res, 200, await options.update(cfg, name, await readBody(req), system, names));
    } catch (e) { return send(res, e.status || 500, { error: e.message }); }
  }
  if (req.method === 'POST' && url.pathname === '/api/undo-layout') {
    if (!sameOrigin(req)) return send(res, 403, { error: 'Cross-origin request refused' });
    const layout = await system.undoLayout(cfg);
    return send(res, 200, { ok: true, warnings: layout.skipped || [] });
  }

  // Monitor controls over DDC/CI. Changing input, brightness or power is open to any signed-in device (switching
  // input from a phone is the point); the raw feature commands are for this PC only.
  const mon = /^\/api\/monitors(?:\/(\d+)\/(capabilities|set|vcp))?$/.exec(url.pathname);
  if (mon) {
    const fail = (e) => send(res, e.status || 400, { error: e.message });
    try {
      if (req.method === 'GET' && !mon[1]) return send(res, 200, await monitors.list(cfg));
      if (req.method === 'GET' && mon[2] === 'capabilities') return send(res, 200, await monitors.capabilities(cfg, mon[1], url.searchParams.get('refresh') === '1'));
      if (req.method === 'GET' && mon[2] === 'vcp') {
        if (!local) return send(res, 403, { error: 'Reading raw features only works on the PC running DisplayDeck.' });
        return send(res, 200, await monitors.get(cfg, mon[1], url.searchParams.get('code')));
      }
      if (req.method === 'POST' && mon[2] === 'set') {
        if (!sameOrigin(req)) return send(res, 403, { error: 'Cross-origin request refused' });
        const body = await readBody(req);
        if (body.raw && !local) return send(res, 403, { error: 'Raw commands only work on the PC running DisplayDeck.' });
        return send(res, 200, await monitors.set(cfg, mon[1], body.code, body.value, { raw: !!body.raw }));
      }
    } catch (e) { return fail(e); }
  }

  // The layout editor. It changes what is on screen and writes profile files, so only this PC may use it.
  if (url.pathname.startsWith('/api/editor/')) {
    if (!local) return send(res, 403, { error: 'The layout editor only works on the PC running DisplayDeck.' });
    const fail = (e) => send(res, e.status || 400, { error: e.message });
    if (req.method === 'GET' && url.pathname === '/api/editor/state') {
      try { return send(res, 200, await editor.editorState(cfg, url.searchParams.get('from') || null, url.searchParams.get('live') === '1')); } catch (e) { return fail(e); }
    }
    if (req.method === 'GET' && url.pathname === '/api/editor/test') return send(res, 200, editor.testStatus());
    if (req.method === 'POST') {
      if (!sameOrigin(req)) return send(res, 403, { error: 'Cross-origin request refused' });
      const body = await readBody(req);
      try {
        if (url.pathname === '/api/editor/validate') return send(res, 200, await editor.validate(cfg, body.layout, body.base || null));
        if (url.pathname === '/api/editor/test') return send(res, 200, await editor.startTest(cfg, body.layout, body.base || null, body.seconds));
        if (url.pathname === '/api/editor/keep') return send(res, 200, await editor.keep());
        if (url.pathname === '/api/editor/revert') return send(res, 200, await editor.revert());
        if (url.pathname === '/api/editor/save') {
          return send(res, 200, await editor.save(cfg, {
            name: typeof body.name === 'string' ? body.name.trim() : '',
            layout: body.layout, base: body.base || null, overwrite: !!body.overwrite,
            copyAudioFrom: typeof body.copyAudioFrom === 'string' ? body.copyAudioFrom : null,
          }, validProfileName));
        }
      } catch (e) { return fail(e); }
    }
  }

  // Save the displays that are on right now as a profile.
  if (req.method === 'POST' && url.pathname === '/api/profiles/save') {
    if (!sameOrigin(req)) return send(res, 403, { error: 'Cross-origin request refused' });
    const body = await readBody(req);
    const name = typeof body.name === 'string' ? body.name.trim() : '';
    if (!validProfileName(name)) {
      return send(res, 400, { error: 'Use 1 to 60 characters, without \\ / : * ? " < > | and not ending in a dot.' });
    }
    fs.mkdirSync(cfg.profilesDir, { recursive: true });
    const file = path.join(cfg.profilesDir, `${name}.xml`);
    if (fs.existsSync(file) && !body.overwrite) return send(res, 409, { error: `A profile named "${name}" already exists.` });
    const saved = await system.saveLayoutTo(cfg, file);
    return send(res, 200, { ok: true, name, displays: saved.displays });
  }

  // Delete a profile and its audio settings. Removes files, so only from this PC.
  const del = /^\/api\/profiles\/(.+)$/.exec(url.pathname);
  if (req.method === 'DELETE' && del) {
    if (!local) return send(res, 403, { error: 'Profiles can only be deleted from the PC running DisplayDeck.' });
    if (!sameOrigin(req)) return send(res, 403, { error: 'Cross-origin request refused' });
    const name = decodeURIComponent(del[1]);
    const entry = scanFiles(cfg).find((f) => f.name === name);
    if (!entry) return send(res, 404, { error: `No such profile: ${name}` });
    fs.rmSync(entry.file, { force: true });
    fs.rmSync(path.join(cfg.profilesDir, `${name}.audio.json`), { force: true });
    options.remove(cfg, name);
    return send(res, 200, { ok: true });
  }

  send(res, 404, { error: 'Not found' });
}

// File names must stay inside the profiles folder: no separators, no reserved characters.
function validProfileName(name) {
  return /^[^\\/:*?"<>|\x00-\x1f]{1,60}$/.test(name) && !/[. ]$/.test(name) && !/\.(audio|options)$/i.test(name) && name !== '.' && name !== '..';
}

// When launched by the tray app, exit if it goes away so no orphan server is left behind.
if (process.env.TRAY_PID) {
  setInterval(() => {
    try { process.kill(Number(process.env.TRAY_PID), 0); } catch (_) { process.exit(0); }
  }, 5000).unref();
}

http.createServer((req, res) => {
  handle(req, res).catch((e) => send(res, 500, { error: e.message }));
}).listen(port, host, () => console.log(`DisplayDeck on http://${host}:${port}  (profiles: ${config.get().profilesDir})`));
