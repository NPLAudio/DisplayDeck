'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { createConfig } = require('../lib/settings');

function setup(env = {}) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'dd-settings-'));
  const config = createConfig({
    root: path.join(dir, 'app'),
    env: { APPDATA: path.join(dir, 'roaming'), ...env },
    stateFile: path.join(dir, 'data', 'state.json'),
  });
  return { dir, config };
}

test('defaults: profiles live under AppData\\DisplayDeck and the helper is in the app bin folder', () => {
  const { dir, config } = setup();
  const c = config.get();
  assert.equal(c.profilesDir, path.join(dir, 'roaming', 'DisplayDeck', 'Profiles'));
  assert.equal(c.ctl, path.join(dir, 'app', 'bin', 'DisplayDeckCtl.exe'));
  assert.equal(c.namesFile, path.join(c.profilesDir, 'names.json'));
  assert.equal(config.describe().effective.profilesDirFound, false);
});

test('a saved profiles folder is used immediately', () => {
  const { dir, config } = setup();
  const mine = path.join(dir, 'my-profiles');
  fs.mkdirSync(mine);
  config.save({ profilesDir: mine });
  assert.equal(config.get().profilesDir, mine);
  assert.equal(config.describe().effective.profilesDirFound, true);
  assert.equal(config.get().namesFile, path.join(mine, 'names.json'));
});

test('rejects missing, relative and network paths', () => {
  const { dir, config } = setup();
  assert.throws(() => config.save({ profilesDir: path.join(dir, 'nope') }), /not found/i);
  assert.throws(() => config.save({ profilesDir: 'relative\\dir' }), /full local folder/);
  assert.throws(() => config.save({ profilesDir: '\\\\server\\share\\p' }), /full local folder/);
  assert.equal(config.describe().settings.profilesDir, '');
});

test('empty string clears the saved folder', () => {
  const { dir, config } = setup();
  const mine = path.join(dir, 'p');
  fs.mkdirSync(mine);
  config.save({ profilesDir: mine });
  config.save({ profilesDir: '' });
  assert.equal(config.get().profilesDir, path.join(dir, 'roaming', 'DisplayDeck', 'Profiles'));
});

test('environment variables win over saved settings and are reported as locked', () => {
  const { dir, config } = setup({ PROFILES_DIR: 'C:\\env\\profiles', DISPLAYDECK_CTL: 'C:\\env\\ctl.exe' });
  const mine = path.join(dir, 'p');
  fs.mkdirSync(mine);
  config.save({ profilesDir: mine });
  assert.equal(config.get().profilesDir, 'C:\\env\\profiles');
  assert.equal(config.get().ctl, 'C:\\env\\ctl.exe');
  assert.equal(config.describe().lockedByEnvironment.profilesDir, true);
});

test('a corrupt settings file falls back to defaults instead of throwing', () => {
  const { dir, config } = setup();
  fs.mkdirSync(path.join(dir, 'data'), { recursive: true });
  fs.writeFileSync(path.join(dir, 'data', 'settings.json'), '{ not json');
  assert.equal(config.get().profilesDir, path.join(dir, 'roaming', 'DisplayDeck', 'Profiles'));
});
