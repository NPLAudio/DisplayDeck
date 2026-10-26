'use strict';
// Tolerant parser for DisplayDeck profile XML (serialized Windows
// DISPLAYCONFIG path/mode data). Unknown fields are ignored; anything it cannot
// make sense of is reported in `warnings` instead of throwing, except
// completely unreadable XML which throws from parseXml().

const ENTITIES = { '&lt;': '<', '&gt;': '>', '&amp;': '&', '&quot;': '"', '&apos;': "'" };
const unescapeXml = (s) => s.replace(/&(lt|gt|amp|quot|apos);/g, (m) => ENTITIES[m]);

function parseXml(input) {
  const text = String(input).replace(/^﻿/, '');
  const root = { name: '#root', children: [], text: '' };
  const stack = [root];
  const re = /<!--[\s\S]*?-->|<\?[\s\S]*?\?>|<!\[CDATA\[([\s\S]*?)\]\]>|<(\/)?([A-Za-z_][\w:.-]*)((?:\s[^>]*?)?)(\/)?>|([^<]+)/g;
  let m;
  while ((m = re.exec(text))) {
    const top = stack[stack.length - 1];
    if (m[1] !== undefined) {
      top.text += m[1];
    } else if (m[3]) {
      if (m[2]) {
        if (stack.length < 2 || top.name !== m[3]) throw new Error(`Mismatched closing tag </${m[3]}>`);
        stack.pop();
      } else {
        const node = { name: m[3], children: [], text: '' };
        top.children.push(node);
        if (!m[5]) stack.push(node);
      }
    } else if (m[6] !== undefined) {
      top.text += unescapeXml(m[6]);
    }
  }
  if (stack.length > 1) throw new Error(`Unclosed element <${stack[stack.length - 1].name}>`);
  if (!root.children.length) throw new Error('No XML root element found');
  return root.children[0];
}

const kid = (n, name) => (n && n.children.find((c) => c.name === name)) || null;
const kids = (n, name) => (n ? n.children.filter((c) => c.name === name) : []);
const dig = (n, ...names) => names.reduce((cur, nm) => kid(cur, nm), n);
const str = (n) => (n ? n.text.trim() : '');
const num = (n) => {
  const v = Number(str(n));
  return str(n) !== '' && Number.isFinite(v) ? v : null;
};
const ratio = (n) => {
  const a = num(kid(n, 'numerator'));
  const b = num(kid(n, 'denominator'));
  return a !== null && b ? a / b : null;
};

// monitorDevicePath / monitorFriendlyDevice are base64 of UTF-32LE text.
function decodeB64Text(s) {
  s = (s || '').trim();
  if (!s) return '';
  if (!/^[A-Za-z0-9+/=]+$/.test(s)) return s;
  const buf = Buffer.from(s, 'base64');
  if (buf.length >= 4 && buf.length % 4 === 0 && buf[1] === 0 && buf[2] === 0 && buf[3] === 0) {
    let out = '';
    for (let i = 0; i < buf.length; i += 4) out += String.fromCodePoint(buf.readUInt32LE(i));
    return out.replace(/\0+$/, '');
  }
  if (buf.length % 2 === 0 && buf.length && buf[1] === 0) return buf.toString('utf16le').replace(/\0+$/, '');
  return s;
}

const cleanName = (s) => s.replace(/\s+/g, ' ').trim();
const INVALID_IDX = 0xffffffff;

function modeAt(modes, idx, type, id) {
  if (idx !== null && idx !== INVALID_IDX && modes[idx] && modes[idx].type === type) return modes[idx];
  if (id !== null) return modes.find((m) => m.type === type && m.id === id) || null;
  return null;
}

function readModes(root) {
  return kids(dig(root, 'modeInfoArray'), 'modeInfo').map((mi) => {
    const type = str(kid(mi, 'DisplayConfigModeInfoType'));
    const mode = { type, id: num(kid(mi, 'id')) };
    const src = kid(mi, 'DisplayConfigSourceMode');
    if (src) {
      mode.width = num(kid(src, 'width'));
      mode.height = num(kid(src, 'height'));
      mode.x = num(dig(src, 'position', 'x'));
      mode.y = num(dig(src, 'position', 'y'));
    }
    const tgt = kid(mi, 'DisplayConfigTargetMode');
    if (tgt) {
      const sig = dig(tgt, 'targetVideoSignalInfo');
      mode.activeW = num(dig(sig, 'activeSize', 'cx'));
      mode.activeH = num(dig(sig, 'activeSize', 'cy'));
      mode.vSync = ratio(kid(sig, 'vSyncFreq'));
    }
    return mode;
  });
}

function readMonitors(root) {
  const out = [];
  for (const mi of kids(dig(root, 'additionalInfo'), 'MonitorAdditionalInfo')) {
    if (str(kid(mi, 'valid')).toLowerCase() !== 'true') continue;
    const devicePath = decodeB64Text(str(kid(mi, 'monitorDevicePath')));
    const uid = /UID(\d+)/i.exec(devicePath);
    const hw = /DISPLAY#([A-Za-z0-9]+)#/i.exec(devicePath);
    out.push({
      targetId: uid ? Number(uid[1]) : null,
      hwId: hw ? hw[1].toUpperCase() : null,
      friendlyName: cleanName(decodeB64Text(str(kid(mi, 'monitorFriendlyDevice')))),
      devicePath,
    });
  }
  return out;
}

function parseProfile(xmlText) {
  const root = parseXml(xmlText);
  const warnings = [];
  if (root.name !== 'displaySettings') warnings.push(`Unexpected root element <${root.name}>`);

  const modes = readModes(root);
  const monitors = readMonitors(root);
  const paths = kids(dig(root, 'pathInfoArray'), 'DisplayConfigPathInfo');
  if (!paths.length) warnings.push('No display paths found');

  const displays = [];
  for (const p of paths) {
    const src = kid(p, 'sourceInfo');
    const tgt = kid(p, 'targetInfo');
    const flags = num(kid(p, 'flags'));
    const targetId = num(kid(tgt, 'id'));
    const srcMode = modeAt(modes, num(kid(src, 'modeInfoIdx')), 'Source', num(kid(src, 'id')));
    const tgtMode = modeAt(modes, num(kid(tgt, 'modeInfoIdx')), 'Target', targetId);

    const inUse = /InUse/.test(str(kid(tgt, 'statusFlags')));
    const active = (flags === null ? inUse : (flags & 1) === 1) && !!srcMode;
    const refresh = ratio(kid(tgt, 'refreshRate')) || (tgtMode && tgtMode.vSync) || null;
    const mon = monitors.find((m) => m.targetId !== null && m.targetId === targetId) || null;

    displays.push({
      active,
      targetId,
      sourceId: num(kid(src, 'id')),
      output: str(kid(tgt, 'outputTechnology')) || null,
      rotation: str(kid(tgt, 'rotation')) || null,
      width: srcMode ? srcMode.width : tgtMode ? tgtMode.activeW : null,
      height: srcMode ? srcMode.height : tgtMode ? tgtMode.activeH : null,
      x: srcMode ? srcMode.x : null,
      y: srcMode ? srcMode.y : null,
      refreshHz: refresh === null ? null : Math.round(refresh * 100) / 100,
      hwId: mon ? mon.hwId : null,
      xmlName: mon ? mon.friendlyName || null : null,
      devicePath: mon ? mon.devicePath : null,
      primary: false,
    });
    if (active && (!srcMode || srcMode.width === null)) warnings.push(`Display ${targetId}: missing resolution`);
    if (!mon) warnings.push(`Display ${targetId}: no monitor identity found`);
  }

  // Windows' primary display is the one whose desktop origin is (0,0).
  const primary = displays.find((d) => d.active && d.x === 0 && d.y === 0);
  if (primary) primary.primary = true;
  else if (displays.some((d) => d.active)) warnings.push('No display at desktop origin; primary unknown');

  return { displays, warnings };
}

// Identity of a display across profiles and reboots. Adapter LUIDs change, and
// so does the trailing part of the monitor hardware ID (the same TV reported
// SNY0406 in a saved profile and SNYF305 live), so use the 3-letter
// manufacturer code plus the connector's target id.
const displayKey = (d) => {
  if (d.targetId === null) return null;
  return d.hwId ? `${d.hwId.slice(0, 3)}#${d.targetId}` : `t${d.targetId}`;
};

// Compact description of the active layout, for comparing profile vs. live state.
function layoutSignature(parsed) {
  const act = parsed.displays.filter((d) => d.active);
  if (!act.length) return null;
  const keys = act.map((d) => {
    const k = displayKey(d);
    if (!k || d.width === null || d.x === null) return null;
    return `${k}|${d.width}x${d.height}@${d.x},${d.y}|${Math.round(d.refreshHz || 0)}`;
  });
  return keys.includes(null) ? null : keys.sort().join(';');
}

module.exports = { parseXml, parseProfile, decodeB64Text, displayKey, layoutSignature };
