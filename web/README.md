# DisplayDeck web server

A small mobile-first page that lists your saved display layouts as cards and applies one with a tap.
No npm dependencies; Node 20 or newer. All Windows-specific work is done by the helper
`bin\DisplayDeckCtl.exe` (built from `../ctl`); this server only reads and writes profile files, runs the
helper, and serves the page.

```
cd web
npm start          # http://127.0.0.1:8765
npm test
```

| Env var | Default |
| --- | --- |
| `PROFILES_DIR` | `%APPDATA%\DisplayDeck\Profiles` |
| `PORT` / `HOST` | `8765` / `127.0.0.1` |
| `DISPLAYDECK_CTL` | `..\bin\DisplayDeckCtl.exe` |
| `NAMES_FILE` | `<PROFILES_DIR>\names.json` |
| `STATE_FILE` | `web\state.json` (the installed app uses `%LOCALAPPDATA%\DisplayDeck`) |

Listening on anything other than `127.0.0.1` requires `ACCESS_KEY` (16+ hex characters); devices other
than this PC must present it. The tray app manages this for you ("Allow other devices").

## Profile file format

A profile is an XML file: a serialization of the Windows `DISPLAYCONFIG_PATH_INFO` / `MODE_INFO` arrays
for the displays that were on when it was saved.

```
displaySettings
  pathInfoArray / DisplayConfigPathInfo*      one per ACTIVE display path
    flags                                     1 = active
    sourceInfo   id, modeInfoIdx, statusFlags (InUse)
    targetInfo   id, modeInfoIdx, outputTechnology (Hdmi, DisplayportExternal...),
                 rotation, refreshRate{numerator,denominator}, statusFlags
  modeInfoArray / modeInfo*                   indexed by modeInfoIdx
    DisplayConfigModeInfoType = Source | Target
    Source: DisplayConfigSourceMode  width, height, position{x,y}
    Target: DisplayConfigTargetMode  targetVideoSignalInfo{activeSize, vSyncFreq...}
  additionalInfo / MonitorAdditionalInfo*     valid=true entries carry identity
    monitorDevicePath      base64 of UTF-32LE, e.g. \\?\DISPLAY#XXX1234#5&...&UID4353#{guid}
    monitorFriendlyDevice  base64 of UTF-32LE, e.g. "MPG 491C OLED"
```

How the page derives things:

- **Resolution / position:** the Source mode referenced by `sourceInfo.modeInfoIdx`.
- **Refresh rate:** `targetInfo.refreshRate` (numerator / denominator, so `119970/1000` = 119.97 Hz).
- **Primary:** the active display whose desktop position is `(0,0)`. That is how Windows defines it.
- **Which monitor is which:** `UID<n>` in the decoded device path equals `targetInfo.id`. The
  `additionalInfo` list is not in path order and has blank `valid=false` filler entries, so it is matched by that id.
- **Disabled displays:** the file only stores *active* paths. "Off" on a card means a display that another
  profile (or the live state) uses but this profile does not.
- **Identity across profiles and reboots:** 3-letter manufacturer code + target id. Adapter LUIDs change
  between boots, and the same TV can report a different trailing hardware ID, so the full ID is not used.

When applying, the helper does the same matching against the current adapters: it finds each saved display's
connector (by target id, using the EDID ids when several adapters offer it), rebuilds the path and mode arrays
with the current adapter ids, and calls `SetDisplayConfig`. Displays that are not connected are skipped and
reported as warnings; if none match, nothing is changed. Before every apply the current layout is saved as
`previous.xml` so **Undo layout change** can go back.

The parser (`lib/parser.js`) ignores unknown elements. A file that cannot be parsed still appears as a card with
a warning and can still be applied.

## Display names

In order: `names.json` override, the friendly name stored in the file, the monitor name from the registry EDID
(`HKLM\...\Enum\DISPLAY`), the hardware ID, then `Display <id>`.

`names.json` (in the profiles folder) maps a hardware ID, a friendly name, or `target:<id>` to a label:

```json
{ "SNY0406": "Living Room TV", "C49RG9x": "Samsung Ultrawide" }
```

## Audio

`<profile>.audio.json` next to the profile. Fields: `device` (the device id shown on the page), `spatial`
(a spatial format name or GUID, or `"off"`), and an optional `label` shown on the card.

```json
{ "label": "Living room TV",
  "device": "NVIDIA High Definition Audio\\Device\\SONY TV  *30\\Render",
  "spatial": "Dolby Atmos for Home Theater" }
```

A device id is `<adapter>\Device\<endpoint name>\Render`. Applying a profile switches the display layout, then the
default playback device (console, multimedia and communications roles), then the device's spatial format.
Each device advertises the spatial formats it supports (Windows Sonic, Dolby Atmos, DTS, ...), and the
**Edit audio** dialog offers exactly those. If Windows requires administrator permission to change the spatial
format, a single UAC prompt appears for that step.

## HTTP API

| Request | Purpose |
| --- | --- |
| `GET /api/profiles` | profiles with displays, audio, and which one matches the live state |
| `POST /api/apply/<name>` | apply a profile; `{ok, warnings, audio}` |
| `POST /api/profiles/save` `{name, overwrite?}` | save the current layout as a profile |
| `DELETE /api/profiles/<name>` | delete a profile and its audio file (this PC only) |
| `POST /api/undo-layout` | go back to the layout before the last apply |
| `POST /api/options/<name>` | a profile's shortcut, programs and saved extras (this PC only); see below |
| `GET /api/hotkeys` | the profile shortcuts, for the tray to register |
| `POST /api/hotkeys/report` | the tray reports shortcuts Windows refused (this PC only) |
| `GET /api/audio-devices` | active playback devices, the default, and each device's spatial formats |
| `POST /api/audio-config/<name>` | save or remove (`{clear:true}`) a profile's audio settings; `applyNow` switches immediately |
| `GET/POST /api/settings` | the profiles folder (changes from this PC only) |
| `POST /api/pick-path` | native folder dialog on this PC |
| `GET /api/phone-link` | access link and QR code (this PC only) |

Every state-changing request refuses cross-origin calls, and `apply` only accepts names of profiles that exist.

## Profile options

`<profile>.options.json` next to the profile holds what happens besides the layout and audio. Applying a profile runs: layout, then the saved extras, then audio, then the programs.

```json
{ "hotkey": "Ctrl+Alt+1",
  "run": [ { "path": "C:\\Tools\\lights.ps1", "args": "-Scene Desk" } ],
  "extras": {
    "hdr":       [ { "targetId": 4353, "hwId": "MSI3FA8", "name": "MPG 491C OLED", "enabled": true } ],
    "dpi":       [ { "targetId": 4353, "hwId": "MSI3FA8", "name": "MPG 491C OLED", "percent": 125 } ],
    "wallpaper": { "mode": "picture", "position": "fill", "color": "#000000",
                   "monitors": [ { "targetId": 4353, "hwId": "MSI3FA8", "name": "MPG 491C OLED", "path": "C:\\Pictures\\a.jpg" } ] } },
  "extrasAt": "2026-10-27T10:28:22.028Z" }
```

- `hotkey`: modifiers (Ctrl, Alt, Shift, Win) and one key (letters, digits, F1 to F24, arrows, Home, End, PageUp, PageDown, Insert, Delete, Space, Numpad0 to Numpad9). It needs Ctrl, Alt or Win and belongs to one profile. The tray registers it with Windows.
- `run`: up to five programs started after everything else, without waiting. `.exe` and `.com` start directly; `.ps1`, `.vbs` and `.js` through PowerShell and the Windows script host; `.bat`, `.cmd`, `.lnk`, `.py` and `.ahk` through a hidden command prompt. Paths must be full local paths of files that exist.
- `extras`: written by the helper (`DisplayDeckCtl extras capture`) from the screens as they are. Entries are matched to displays by display id, otherwise by make and model when only one display has it. `wallpaper.mode` is `picture`, `color` or `slideshow` (with `slideshow: { paths, options, tick }`).

`POST /api/options/<name>` takes any of `{ hotkey, run, extras }` and leaves what is left out unchanged. `hotkey: ""` removes the shortcut, `run` replaces the list, and `extras` maps `hdr`, `dpi` or `wallpaper` to `"capture"` (read it from the screens now) or `"remove"`. If a requested extra cannot be read, nothing is saved and the answer says why. Only this PC may call it, because a program path ends up being executed. `apply` reports problems in `warnings` and `runErrors`.

`DisplayDeckCli.exe` (in `bin`) is a console tool over the same API: `apply <profile>`, `list`, `current`, `undo`, `open`.

## Behavior

- Every page load and `GET /api/profiles` re-scans the folder. Parsed files are cached by mtime and size.
- **Current:** the live layout is saved to a temporary file by the helper and compared with every profile
  (display identity, resolution, position, rounded refresh rate). A match is "Current"; if nothing matches
  reliably the page shows "Last applied" and says so.

## Layout editor API

These endpoints only answer requests from the PC running DisplayDeck.

| Method and path | What it does |
| --- | --- |
| `GET /api/editor/state?from=<profile>` | The displays the editor works with: the profile's layout (or `?live=1` for the screens as they are, or nothing for a blank layout), plus every other known display switched off, each with the resolutions and refresh rates on offer. |
| `POST /api/editor/validate` | Body `{ layout, base }`. Builds the layout and asks Windows to check it without changing anything. |
| `POST /api/editor/test` | Body `{ layout, base, seconds }`. Applies the layout and goes back by itself after `seconds` (5 to 60) unless kept. |
| `POST /api/editor/keep` | Keeps the tried layout. "Undo layout change" then returns to what was there before the test. |
| `POST /api/editor/revert` | Goes back to the layout from before the test now. |
| `POST /api/editor/save` | Body `{ name, layout, base, overwrite, copyAudioFrom }`. Writes the layout as a profile. |

A `layout` is `{ displays: [{ targetId, name, enabled, primary, width, height, x, y, refresh, rotation }] }`, where
`rotation` is 1 to 4 (0, 90, 180 and 270 degrees) and `width`/`height` are the size on the desktop after rotation.

## Monitor controls API

DDC/CI commands to the monitors. Changing a setting is open to any signed-in device; raw commands and raw reads only answer requests from the PC running DisplayDeck.

| Method and path | What it does |
| --- | --- |
| `GET /api/monitors` | The displays that are on, each with `id` (the display's target id), `name`, `ddc` (does it answer), `backend` (`windows`, `nvidia` or `none`) and `controls`: for each feature it reports, `{ current, max }`. Features: `input`, `brightness`, `contrast`, `volume`, `mute`, `power`, `preset`, `sharpness`. |
| `GET /api/monitors/<id>/capabilities` | The monitor's own capabilities string, and from it the `inputs`, `presets` and `power` values it lists. Slow (a second or two), so it is remembered for ten minutes; add `?refresh=1` to ask again. |
| `POST /api/monitors/<id>/set` | Body `{ code, value }`, where `code` is a feature code in hex (such as `"0x60"` for input) and `value` is 0 to 65535. Only the features above are accepted. With `raw: true` (this PC only) any feature code is accepted, except the ones that reset the monitor to factory settings. |
| `GET /api/monitors/<id>/vcp?code=0x10` | Reads one feature, returning `{ code, current, max }` (this PC only). |

Input values follow the monitor standard (MCCS): `0x0F` DisplayPort 1, `0x10` DisplayPort 2, `0x11` HDMI 1, `0x12` HDMI 2. Some monitors use their own numbers, which the capabilities list shows.
