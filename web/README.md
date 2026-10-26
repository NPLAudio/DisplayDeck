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
| `GET /api/audio-devices` | active playback devices, the default, and each device's spatial formats |
| `POST /api/audio-config/<name>` | save or remove (`{clear:true}`) a profile's audio settings; `applyNow` switches immediately |
| `GET/POST /api/settings` | the profiles folder (changes from this PC only) |
| `POST /api/pick-path` | native folder dialog on this PC |
| `GET /api/phone-link` | access link and QR code (this PC only) |

Every state-changing request refuses cross-origin calls, and `apply` only accepts names of profiles that exist.

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
