'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');
const { parseXml, parseProfile, decodeB64Text, layoutSignature } = require('../lib/parser');

const fx = (n) => fs.readFileSync(path.join(__dirname, 'fixtures', n), 'utf8');
const active = (p) => p.displays.filter((d) => d.active);
const byHw = (p, hw) => p.displays.find((d) => d.hwId === hw);

test('Desk: three active displays with resolution, refresh, position', () => {
  const p = parseProfile(fx('desk.xml'));
  assert.equal(active(p).length, 3);
  const msi = byHw(p, 'MSI3FA8');
  assert.deepEqual([msi.width, msi.height, msi.refreshHz, msi.x, msi.y], [5120, 1440, 144, 0, 0]);
  assert.equal(msi.output, 'DisplayportExternal');
  const sam = p.displays.find((d) => d.xmlName === 'C49RG9x');
  assert.deepEqual([sam.width, sam.height, sam.refreshHz, sam.x, sam.y], [5120, 1440, 119.97, 0, -1440]);
  const sony = p.displays.find((d) => d.xmlName === 'SONY TV *30');
  assert.deepEqual([sony.width, sony.height, sony.refreshHz, sony.x, sony.y], [3840, 2160, 60, 5120, 0]);
  assert.equal(sony.output, 'Hdmi');
});

test('Desk: primary is the display at the desktop origin, and only one', () => {
  const p = parseProfile(fx('desk.xml'));
  const primaries = p.displays.filter((d) => d.primary);
  assert.equal(primaries.length, 1);
  assert.equal(primaries[0].xmlName, 'MPG 491C OLED');
});

test('Living Room: single HDMI TV at 4K120, primary', () => {
  const p = parseProfile(fx('living-room.xml'));
  assert.equal(p.displays.length, 1);
  const [d] = p.displays;
  assert.deepEqual([d.active, d.width, d.height, d.refreshHz, d.primary, d.xmlName], [true, 3840, 2160, 120, true, 'SONY TV *30']);
  assert.deepEqual(p.warnings, []);
});

test('monitor names and ids are decoded from UTF-32LE base64 and redacted ids still parse', () => {
  const p = parseProfile(fx('desk.xml'));
  const msi = byHw(p, 'MSI3FA8');
  assert.equal(msi.targetId, 4353);
  assert.match(msi.devicePath, /^\\\\\?\\DISPLAY#MSI3FA8#5&00000000&1&UID4353#/);
  assert.equal(decodeB64Text(''), '');
  assert.equal(decodeB64Text('not base64!'), 'not base64!');
});

test('layout signature ignores adapter LUID but tracks layout changes', () => {
  const a = layoutSignature(parseProfile(fx('desk.xml')));
  const b = layoutSignature(parseProfile(fx('desk.xml').replace(/<LowPart>1000</g, '<LowPart>2222<')));
  assert.equal(a, b);
  const moved = layoutSignature(parseProfile(fx('desk.xml').replace('<x>5120</x>', '<x>5121</x>')));
  assert.notEqual(a, moved);
  assert.notEqual(a, layoutSignature(parseProfile(fx('living-room.xml'))));
});

test('inactive path (flags 0) is reported as inactive, not as a display in the layout', () => {
  const x = fx('living-room.xml').replace('<flags>1</flags>', '<flags>0</flags>');
  const p = parseProfile(x);
  assert.equal(p.displays[0].active, false);
  assert.equal(p.displays.filter((d) => d.primary).length, 0);
  assert.equal(layoutSignature(p), null);
});

test('unknown fields and extra elements are ignored', () => {
  const x = fx('living-room.xml').replace('<flags>1</flags>', '<flags>1</flags><somethingNew a="1"><deep>x</deep></somethingNew>');
  assert.equal(parseProfile(x).displays[0].width, 3840);
});

test('missing monitor identity yields a warning, not a failure', () => {
  const x = fx('living-room.xml').replace(/<additionalInfo>[\s\S]*<\/additionalInfo>/, '');
  const p = parseProfile(x);
  assert.equal(p.displays[0].width, 3840);
  assert.equal(p.displays[0].hwId, null);
  assert.ok(p.warnings.some((w) => /no monitor identity/.test(w)));
});

test('well-formed XML with the wrong shape warns', () => {
  const p = parseProfile('<?xml version="1.0"?><other><a/></other>');
  assert.deepEqual(p.displays, []);
  assert.ok(p.warnings.length >= 2);
});

test('broken XML throws from parseXml/parseProfile (callers show a warning card)', () => {
  assert.throws(() => parseProfile('<displaySettings><pathInfoArray></displaySettings>'));
  assert.throws(() => parseProfile(''));
  assert.throws(() => parseProfile('this is not xml'));
  assert.throws(() => parseXml('<a><b></a>'));
});

test('BOM and entities are handled', () => {
  const n = parseXml('﻿<?xml version="1.0"?><a><b>x &amp; y</b></a>');
  assert.equal(n.children[0].text, 'x & y');
});

test('display identity survives the TV reporting a different hardware id', () => {
  const { displayKey } = require('../lib/parser');
  const a = parseProfile(fx('living-room.xml')).displays[0];
  const b = { ...a, hwId: 'SNYF305' };
  assert.equal(displayKey(a), displayKey(b));
  assert.notEqual(displayKey(a), displayKey({ ...a, targetId: 4356 }));
});
