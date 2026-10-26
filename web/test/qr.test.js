'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { qrSvg } = require('../lib/qr');

const LINK = 'http://192.168.1.32:8765/?key=0123456789abcdef0123456789abcdef';

test('produces an SVG whose size follows the QR version (+ quiet zone)', () => {
  const svg = qrSvg(LINK);
  assert.match(svg, /^<svg [^>]*viewBox="0 0 (\d+) \1"/);
  const size = Number(/viewBox="0 0 (\d+)/.exec(svg)[1]);
  // a 62-byte payload at level M needs version 4-5: 33 or 37 modules, plus 8 quiet-zone modules
  assert.ok([41, 45].includes(size), `unexpected size ${size}`);
});

test('finder patterns are present in three corners and not the fourth', () => {
  const svg = qrSvg(LINK);
  const size = Number(/viewBox="0 0 (\d+)/.exec(svg)[1]);
  const dark = new Set([...svg.matchAll(/M(\d+) (\d+)h1v1h-1z/g)].map((m) => `${m[1]},${m[2]}`));
  const q = 4, n = size - 8;
  const ring = (x0, y0) => { // outer 7x7 ring of a finder pattern must be all dark
    for (let i = 0; i < 7; i++) {
      for (const [x, y] of [[x0 + i, y0], [x0 + i, y0 + 6], [x0, y0 + i], [x0 + 6, y0 + i]]) {
        if (!dark.has(`${x},${y}`)) return false;
      }
    }
    return true;
  };
  assert.ok(ring(q, q), 'top-left');
  assert.ok(ring(q + n - 7, q), 'top-right');
  assert.ok(ring(q, q + n - 7), 'bottom-left');
  assert.ok(!ring(q + n - 7, q + n - 7), 'bottom-right must not be a finder');
});

test('is deterministic and differs for different input', () => {
  assert.equal(qrSvg(LINK), qrSvg(LINK));
  assert.notEqual(qrSvg(LINK), qrSvg(LINK + 'x'));
});
