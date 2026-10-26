'use strict';
// DisplayDeck is self-contained: it must not depend on, bundle, or name any outside program.
// This guards against references creeping back in. The words are built from fragments so this
// file does not trip its own check.
const test = require('node:test');
const assert = require('node:assert/strict');
const { execFileSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const repo = path.resolve(__dirname, '..', '..');
const banned = [
  ['monitor', '[ _-]?', 'switcher'],
  ['sound', '[ _-]?', 'volume', '[ _-]?', 'view'],
  ['nir', '[ _-]?', 'soft'],
  ['source', 'forge'],
].map((parts) => new RegExp(parts.join(''), 'i'));

function trackedFiles() {
  try {
    // tracked files plus new files that are not ignored (so it also catches what is about to be added)
    return execFileSync('git', ['ls-files', '--cached', '--others', '--exclude-standard'], { cwd: repo, encoding: 'utf8' }).split('\n').filter(Boolean);
  } catch (_) {
    return null; // not a git checkout (for example an installed copy): nothing to scan
  }
}

test('no tracked file mentions the outside programs this project replaced', { skip: trackedFiles() === null && 'not a git checkout' }, () => {
  const hits = [];
  for (const rel of trackedFiles()) {
    if (/\.(png|ico|exe|dll)$/i.test(rel)) continue;
    const text = fs.readFileSync(path.join(repo, rel), 'utf8');
    text.split('\n').forEach((line, i) => {
      if (banned.some((re) => re.test(line))) hits.push(`${rel}:${i + 1}: ${line.trim().slice(0, 100)}`);
    });
    if (banned.some((re) => re.test(rel))) hits.push(`${rel}: file name`);
  }
  assert.deepEqual(hits, [], 'references found:\n' + hits.join('\n'));
});
