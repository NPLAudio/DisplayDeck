'use strict';
// QR code as an inline SVG string. Encoding by the vendored Arase QRCode (MIT), see lib/vendor/qrcode.
const QRCode = require('./vendor/qrcode');
const QRErrorCorrectLevel = require('./vendor/qrcode/QRErrorCorrectLevel');

function qrSvg(text, { quiet = 4, dark = '#000000', light = '#ffffff' } = {}) {
  const qr = new QRCode(-1, QRErrorCorrectLevel.M); // -1 = smallest version that fits
  qr.addData(String(text));
  qr.make();
  const n = qr.getModuleCount();
  const size = n + quiet * 2;
  let d = '';
  for (let r = 0; r < n; r++) {
    for (let c = 0; c < n; c++) {
      if (qr.isDark(r, c)) d += `M${c + quiet} ${r + quiet}h1v1h-1z`;
    }
  }
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${size} ${size}" shape-rendering="crispEdges" role="img" aria-label="QR code for the phone link">`
    + `<rect width="${size}" height="${size}" fill="${light}"/><path d="${d}" fill="${dark}"/></svg>`;
}

module.exports = { qrSvg };
