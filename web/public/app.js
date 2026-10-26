// Shared by every page: DOM helpers, icons, API calls, the header with tabs, and the theme switch.
'use strict';

const $ = (id) => document.getElementById(id);
const NS = 'http://www.w3.org/2000/svg';

function el(tag, props = {}, ...kids) {
  const n = document.createElement(tag);
  for (const [k, v] of Object.entries(props)) {
    if (v == null || v === false) continue;
    if (k === 'class') n.className = v;
    else if (k.startsWith('on')) n.addEventListener(k.slice(2), v);
    else n.setAttribute(k, v === true ? '' : v);
  }
  kids.flat().forEach((c) => c != null && c !== false && n.append(c));
  return n;
}

// Lucide-style 24x24 stroke icons (paths only).
const ICONS = {
  chevron: '<path d="m6 9 6 6 6-6"/>',
  plus: '<path d="M5 12h14M12 5v14"/>',
  undo: '<path d="M3 7v6h6"/><path d="M21 17a9 9 0 0 0-9-9 9 9 0 0 0-6 2.3L3 13"/>',
  copy: '<rect width="14" height="14" x="8" y="8" rx="2"/><path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2"/>',
  trash: '<path d="M3 6h18"/><path d="M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6"/><path d="M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2"/>',
  volume: '<path d="M11 5 6 9H2v6h4l5 4V5Z"/><path d="M15.54 8.46a5 5 0 0 1 0 7.07"/><path d="M19.07 4.93a10 10 0 0 1 0 14.14"/>',
  monitor: '<rect width="20" height="14" x="2" y="3" rx="2"/><path d="M8 21h8M12 17v4"/>',
  sun: '<circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.93 4.93l1.41 1.41M17.66 17.66l1.41 1.41M2 12h2M20 12h2M6.34 17.66l-1.41 1.41M19.07 4.93l-1.41 1.41"/>',
  moon: '<path d="M12 3a6 6 0 0 0 9 9 9 9 0 1 1-9-9Z"/>',
  alert: '<path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3"/><path d="M12 9v4M12 17h.01"/>',
  check: '<path d="M20 6 9 17l-5-5"/>',
};

function icon(name, cls = '') {
  const s = document.createElementNS(NS, 'svg');
  s.setAttribute('viewBox', '0 0 24 24');
  s.setAttribute('class', 'icon-svg ' + cls);
  s.setAttribute('aria-hidden', 'true');
  s.innerHTML = ICONS[name] || '';
  return s;
}

async function api(path, opts) {
  const r = await fetch(path, opts);
  let j = {};
  try { j = await r.json(); } catch (_) { /* non-JSON error body */ }
  if (!r.ok) throw new Error(j.error || r.statusText);
  return j;
}
const postJson = (path, body) => api(path, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body || {}) });

// Per-viewer preferences (remembered tab state, theme). Never required for the page to work.
const prefs = {
  get(key, fallback = null) { try { const v = localStorage.getItem('dd.' + key); return v == null ? fallback : JSON.parse(v); } catch (_) { return fallback; } },
  set(key, value) { try { localStorage.setItem('dd.' + key, JSON.stringify(value)); } catch (_) { /* private mode */ } },
};

// ---- theme: follow the system unless the user picked one ----
function applyTheme(choice) {
  const root = document.documentElement;
  root.classList.remove('dark', 'light');
  if (choice === 'dark') root.classList.add('dark');
  else if (choice === 'light') root.classList.add('light');
}
applyTheme(prefs.get('theme', 'system')); // before first paint, to avoid a flash
function isDark() { return document.documentElement.classList.contains('dark') || (!document.documentElement.classList.contains('light') && matchMedia('(prefers-color-scheme: dark)').matches); }

// The DisplayDeck mark: an outlined screen behind a lit emerald one, on a dark tile. Same shapes as favicon.svg.
function logo(size = 26) {
  const s = document.createElementNS(NS, 'svg');
  s.setAttribute('viewBox', '0 0 64 64');
  s.setAttribute('width', String(size));
  s.setAttribute('height', String(size));
  s.setAttribute('aria-hidden', 'true');
  s.innerHTML = '<defs><linearGradient id="dd-t" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#2c2c31"/><stop offset="1" stop-color="#18181b"/></linearGradient>'
    + '<linearGradient id="dd-e" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#34d399"/><stop offset="1" stop-color="#059669"/></linearGradient></defs>'
    + '<rect x=".4" y=".4" width="63.2" height="63.2" rx="14" fill="url(#dd-t)" stroke="#fff" stroke-opacity=".16" stroke-width=".8"/>'
    + '<rect x="13.06" y="15.51" width="22.33" height="12.9" rx="1.54" fill="none" stroke="#fafafa" stroke-opacity=".75" stroke-width="3.07"/>'
    + '<rect x="27.09" y="32.82" width="25.4" height="15.97" rx="3.07" fill="url(#dd-e)"/>';
  return s;
}

// ---- header: brand, tabs, theme button ----
async function renderShell(active) {
  const header = el('header', { class: 'app-header' });
  const nav = el('nav', { class: 'tabs', 'aria-label': 'Pages' });
  const tab = (href, label, id) => el('a', { class: 'tab', href, 'aria-current': id === active ? 'page' : null }, label);
  nav.append(tab('/', 'Profiles', 'profiles'));
  const themeBtn = el('button', { class: 'btn ghost icon sm', type: 'button', 'aria-label': 'Toggle light and dark theme', title: 'Toggle theme' });
  const paintTheme = () => themeBtn.replaceChildren(icon(isDark() ? 'sun' : 'moon'));
  themeBtn.addEventListener('click', () => { const next = isDark() ? 'light' : 'dark'; prefs.set('theme', next); applyTheme(next); paintTheme(); });
  paintTheme();
  header.append(el('h1', { class: 'brand' }, logo(), 'DisplayDeck'), nav, themeBtn);
  document.body.prepend(el('div', { class: 'container', id: 'shell' }, header));
  const main = document.querySelector('main');
  $('shell').append(main);

  // The phone and settings pages only work from this PC, so only offer them there.
  try {
    const s = await api('/api/settings');
    if (s.canEdit) { nav.append(tab('/phone', 'Phone', 'phone'), tab('/settings', 'Settings', 'settings')); }
    return s;
  } catch (_) { return null; }
}
