'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { currentStatus } = require('../lib/profiles');

// The helper is deliberately missing, so the live layout is "unavailable" and only file-based facts are tested.
function setup(lastApplied) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'dd-status-'));
  const profiles = path.join(dir, 'profiles');
  fs.mkdirSync(profiles);
  for (const n of ['Desk', 'Den TV']) fs.writeFileSync(path.join(profiles, `${n}.xml`), '<displaySettings/>');
  const stateFile = path.join(dir, 'state.json');
  if (lastApplied !== undefined) fs.writeFileSync(stateFile, JSON.stringify({ lastApplied }));
  return { profilesDir: profiles, stateFile, ctl: path.join(dir, 'missing.exe') };
}

test('lists profile names sorted, and reports the live layout as unavailable without the helper', async () => {
  const s = await currentStatus(setup());
  assert.deepEqual(s.profiles, ['Den TV', 'Desk']);
  assert.equal(s.current, null);
  assert.equal(s.liveAvailable, false);
});

test('reports a last-applied profile that still exists', async () => {
  assert.equal((await currentStatus(setup('Desk'))).lastApplied, 'Desk');
});

test('does not report a last-applied profile that was deleted', async () => {
  assert.equal((await currentStatus(setup('12'))).lastApplied, null);
});
