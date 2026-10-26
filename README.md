# DisplayDeck

**Switch your whole desk setup with one tap.** DisplayDeck saves your monitor layouts and audio devices
as named profiles ("Desk", "Living Room", "Den TV") and puts them behind a tray icon and a phone-friendly
web page. One click on the PC, or one tap on your phone, and the screens, the sound output and the
spatial format all change together.

It is a single self-contained app: nothing else needs to be downloaded or installed.

## What it does

- **Save the layout you have right now.** Arrange your monitors in Windows the way you like, then press
  **Save current layout** and give it a name. DisplayDeck records which displays are on, their
  arrangement, resolution and refresh rate.
- **One card per profile.** Each card shows which displays are on or off, each display's resolution and
  refresh rate, the primary display, and a to-scale preview of the layout.
- **Knows what is active.** It compares the live display state with every profile and marks the matching
  card **Current**. If nothing matches reliably it falls back to **Last applied** rather than guessing.
- **Compact cards.** Each profile is a one-line card you can tap to apply. A chevron expands it to show the
  preview, the displays and the audio settings, and **Expand all / Collapse all** handles the lot. Your
  choices are remembered.
- **Undo.** Every layout change remembers the previous layout, so **Undo layout change** takes you back.
- **Audio goes with the profile.** Give each profile a playback device and a spatial format (Off, Windows
  Sonic, Dolby Atmos, DTS, ...). Both switch together. The format list is read from the device itself, so
  you only see what that device supports.
- **Tray icon.** Left-click opens the page. Right-click lists your profiles so you can apply one without
  opening a browser. It can start with Windows.
- **From your phone.** Turn on **Allow other devices** in the tray menu, scan the QR code, and you have the
  same page on your phone, protected by a private access key and limited to your home network.
- **Friendly names.** Monitors are named from the profile, falling back to the Windows EDID name or a short
  ID, and you can override any name.
- **Stream Deck.** An optional plugin puts profile, next-profile, undo and audio-output keys on your Stream Deck.
  The profile and audio keys light up when they are the active one. See [Stream Deck](#stream-deck).
- **Three pages.** Profiles, Phone (the QR code) and Settings each have their own page, reachable from the
  tabs at the top or from the tray menu.
- **Light and dark.** The interface uses [shadcn/ui](https://ui.shadcn.com) design tokens (zinc theme),
  follows your system's light or dark setting, and has a toggle. To re-theme, paste a theme from
  [ui.shadcn.com/themes](https://ui.shadcn.com/themes) over the two token blocks at the top of
  `web/public/app.css`.

## Install

Download `DisplayDeck-<version>.msi` from the
[latest release](https://github.com/NPLAudio/DisplayDeck/releases/latest) (or build it yourself, see
[Build from source](#build-from-source)), run it, and accept the administrator prompt. DisplayDeck starts
in the notification area and opens its page.

See **[INSTALL.md](INSTALL.md)** for the full walkthrough, phone setup, troubleshooting and uninstalling.

Requirements: Windows 10 or 11 (64-bit).

## A quick tour

The screenshots use made-up profiles and devices.

### 1. Save your first layout

1. Arrange your monitors in Windows the way you want them.
2. Right-click the DisplayDeck tray icon and choose **Save current layout...** (or press **Save layout** on the page).
3. Type a name, such as "Desk", and confirm. Repeat for each arrangement you use.

![The Profiles page with one-line cards, the current profile highlighted](docs/images/profiles-collapsed.png)

Each saved layout is one card. The highlighted card marked **Current** is the one that matches your screens right now.

### 2. Apply a profile

- On the PC: left-click the tray icon to open the page and tap a card, or right-click the tray icon and pick a profile from the menu.
- On your phone: see [Use it from your phone](INSTALL.md#4-use-it-from-your-phone-optional).
- On a Stream Deck: see [Stream Deck](#stream-deck).

Applying a profile switches the displays and, if the profile has audio settings, the sound output too.

### 3. Look inside a card, and set its audio

1. Press the arrow on the right of a card to expand it. You see a to-scale preview of the layout, each display's resolution and refresh rate, and the audio settings. The star marks the primary display.
2. Press **Edit audio** to pick a playback device and a spatial format. The list shows only what that device supports.
3. The **Audio devices** card at the bottom lists every playback device right now, and which profiles use it.

![An expanded profile card showing the layout preview, displays and audio, with the audio devices list open](docs/images/profiles-expanded.png)

On a phone the same page fits the screen:

<img src="docs/images/profiles-phone.png" alt="The Profiles page on a phone" width="300">

### 4. Changed your mind?

Press **Undo** at the top of the page (or **Undo layout change** in the tray menu) to go back to the previous layout.

## How it works

DisplayDeck is a small local web server (Node, no npm dependencies), a tray program, and a helper that talks
to Windows:

```
ctl/         DisplayDeckCtl.exe source: display layouts (QueryDisplayConfig / SetDisplayConfig) and audio devices (Core Audio)
tray/        DisplayDeck.exe source: tray icon, menu, starts the server, firewall prompt
web/         the server and the page; parses profile files and runs the helper
streamdeck/  the Stream Deck plugin (TypeScript, built with Node 24)
brand/       the icon drawing code (app icon, tray, installer, plugin icons)
installer/   build scripts and the WiX definition for the MSI
```

- A profile is an XML file in the profiles folder (`%APPDATA%\DisplayDeck\Profiles` by default). It holds
  the display paths and modes that were active when you saved it. Applying one remaps it onto the current
  graphics adapters (their ids change between boots), asks Windows to validate and apply it, and reports any
  display that is not connected instead of failing.
- Audio settings live next to each profile as `<profile>.audio.json`, for example:

  ```json
  { "label": "Living room TV",
    "device": "NVIDIA High Definition Audio\\Device\\SONY TV  *30\\Render",
    "spatial": "Dolby Atmos for Home Theater" }
  ```

  `device` is the id shown on the page and `spatial` is a format name, or `"off"`.
- The page re-scans the profile folder every time it loads, so new or edited profiles appear without
  restarting anything.
- The profile file format and the HTTP API are documented in [web/README.md](web/README.md).

## Stream Deck

If Stream Deck 7.6 or newer is installed, the tray menu has **Install Stream Deck plugin...**. Choose it and
confirm in Stream Deck. The plugin adds a **DisplayDeck** category with four keys, two dials and a status display:

| Key | What it does |
| --- | --- |
| **Apply profile** | Pick a profile from a dropdown; pressing the key applies it (layout and audio). A green ring shows while it is the active profile. |
| **Next profile** | Applies the next saved profile, wrapping around. Handy when you have more profiles than keys. |
| **Undo layout** | Goes back to the layout from before the last change. |
| **Audio output** | Makes a playback device the default, with an optional spatial format (the list shows what that device supports). Ringed while it is the default. |

**Dials (Stream Deck +).** Turn to browse, then press the dial or tap the touch strip to confirm. Nothing changes until you confirm, and a half-made choice is dropped after a few seconds.

| Dial | What it does |
| --- | --- |
| **Profile dial** | Turn to browse your saved profiles; press or tap to apply the one shown. Green while it is the active profile. |
| **Audio dial** | Turn to browse playback devices; press or tap to make the one shown the default. The spatial format is left as it is. |
| **Undo layout** | Also works on a dial: press or tap to go back. |
| **Status** | Shows the current profile and the default playback device. Press or tap to refresh. |

**Info bar (Stream Deck Neo).** Drop **Status** on the info bar to see the current profile and default audio device at a glance.

The keys and dials talk to DisplayDeck on the same PC, so DisplayDeck has to be running; if it isn't, the keys show a
warning mark. Stream Deck cannot create keys for you, so each profile you want on a key is one **Apply profile**
key set to that profile.

The plugin source is in `streamdeck/`. It is written in TypeScript against Elgato's SDK and needs Node.js 24 or
newer to build (`streamdeck\build.ps1` finds it on PATH or in an [nvm-windows](https://github.com/coreybutler/nvm-windows)
install). `npm test` there runs unit tests and end-to-end tests that drive the built plugin through a mock Stream
Deck host and a stub DisplayDeck server, so nothing real is changed.

## Privacy and security

- By default the server listens on `127.0.0.1` only. Nothing leaves your PC.
- Phone access is opt-in. It listens on your network, requires a random 128-bit access key, and the firewall
  rule only admits machines on your own subnet. Do not forward the port to the internet; for access away
  from home use a VPN such as Tailscale.
- Changing settings, deleting profiles and opening file dialogs are only allowed from the PC itself.

## Settings and data

| What | Where |
| --- | --- |
| Profiles, `names.json`, `*.audio.json` | `%APPDATA%\DisplayDeck\Profiles` (changeable under Settings) |
| Last applied, previous layout, phone key | `%LOCALAPPDATA%\DisplayDeck` |
| Program files | `C:\Program Files\DisplayDeck` |

`names.json` maps a hardware ID or monitor name to a friendlier label, for example
`{ "SNY0406": "Living Room TV" }`.

Advanced overrides (environment variables): `PROFILES_DIR`, `NAMES_FILE`, `PORT`, `DISPLAYDECK_CTL`.

## Build from source

You need Node 20 or newer, the .NET SDK (only to fetch the WiX tool once), and Windows (the C# compiler that
ships with Windows builds the tray program and the helper).

```
.\ctl\build.ps1         # builds bin\DisplayDeckCtl.exe and runs its self-test

cd web
npm test                # tests, including a check that no outside program is referenced
npm start               # http://127.0.0.1:8765

cd ..\installer
dotnet tool install wix --version 5.0.2 --tool-path .\tools     # once
.\build.ps1 -Version 2.0.0                                       # -> installer\dist\DisplayDeck-2.0.0.msi
```

The build fails if the package contains any executable other than DisplayDeck's own two and the Node.js
runtime.

## Third-party software

The QR code uses a vendored copy of Kazuhiko Arase's QRCode library (MIT) via
[qrcode-terminal](https://github.com/gtanner/qrcode-terminal) (Apache 2.0); the license is in
`web/lib/vendor/qrcode/`. The MSI bundles the official Node.js runtime (MIT) with its license.
Full details are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Support

DisplayDeck is free and open source. If it saves you some clicks, you can
[buy me a coffee](https://buymeacoffee.com/notpunny).

## License

DisplayDeck is released under the [MIT License](LICENSE).
