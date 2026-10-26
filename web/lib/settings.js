'use strict';
// User settings (settings.json) layered under environment overrides:
//   env var  >  settings.json  >  default location
// Read on every request, so changes apply without restarting the server.

const fs = require('fs');
const path = require('path');

function createConfig({ root, env, stateFile }) {
  const settingsFile = env.SETTINGS_FILE || path.join(path.dirname(stateFile), 'settings.json');
  const defaultProfiles = path.join(env.APPDATA || '', 'DisplayDeck', 'Profiles');

  function readSettings() {
    try {
      const s = JSON.parse(fs.readFileSync(settingsFile, 'utf8').replace(/^﻿/, ''));
      return s && typeof s === 'object' ? s : {};
    } catch (_) {
      return {};
    }
  }

  const str = (v) => (typeof v === 'string' && v.trim() ? v.trim() : '');

  function get() {
    const s = readSettings();
    const profilesDir = env.PROFILES_DIR || str(s.profilesDir) || defaultProfiles;
    return {
      profilesDir,
      namesFile: env.NAMES_FILE || path.join(profilesDir, 'names.json'),
      stateFile,
      ctl: env.DISPLAYDECK_CTL || path.join(root, 'bin', 'DisplayDeckCtl.exe'),
    };
  }

  function describe() {
    const s = readSettings();
    const cfg = get();
    return {
      settings: { profilesDir: str(s.profilesDir) },
      effective: { profilesDir: cfg.profilesDir, profilesDirFound: isDir(cfg.profilesDir) },
      defaults: { profilesDir: defaultProfiles },
      lockedByEnvironment: { profilesDir: !!env.PROFILES_DIR },
    };
  }

  // Returns the cleaned folder ('' = use the default) or throws an Error with a user-facing message.
  function checkProfilesDir(value) {
    const v = str(value);
    if (!v) return '';
    if (!path.isAbsolute(v) || v.startsWith('\\\\')) throw new Error('Use a full local folder path for profiles.');
    if (!isDir(v)) throw new Error(`Folder not found: ${v}`);
    return path.normalize(v);
  }

  function save(body) {
    const next = readSettings();
    if ('profilesDir' in body) next.profilesDir = checkProfilesDir(body.profilesDir);
    fs.mkdirSync(path.dirname(settingsFile), { recursive: true });
    fs.writeFileSync(settingsFile, JSON.stringify(next, null, 2) + '\n');
  }

  return { get, describe, save, settingsFile };
}

function isDir(p) {
  try { return fs.statSync(p).isDirectory(); } catch (_) { return false; }
}

module.exports = { createConfig };
