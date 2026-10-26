# Third-party notices

DisplayDeck itself is MIT licensed (see [LICENSE](LICENSE)). It has no runtime dependencies beyond the items
below, and it does not rely on any other program being installed.

| Component | Where | License |
| --- | --- | --- |
| [Node.js](https://nodejs.org/) runtime | `node\node.exe` in the installer (not in the git repository). Its full license text is installed as `licenses\node-LICENSE.txt`. | MIT (plus the licenses of Node's own dependencies, listed in that file) |
| QRCode for JavaScript by Kazuhiko Arase | `web/lib/vendor/qrcode/` | MIT |
| [qrcode-terminal](https://github.com/gtanner/qrcode-terminal) 0.12.0 (the Node port of the above) | `web/lib/vendor/qrcode/` | Apache License 2.0, text in `web/lib/vendor/qrcode/LICENSE-qrcode-terminal.txt` |

"QR Code" is a registered trademark of DENSO WAVE INCORPORATED.

### Stream Deck plugin (`streamdeck/`)

| Component | Where | License |
| --- | --- | --- |
| [@elgato/streamdeck](https://github.com/elgatosf/streamdeck) SDK and its dependencies (`@elgato/utils`, `@elgato/schemas`, `zod`, `ws`) | bundled into the plugin's `bin/plugin.js` at build time | MIT |
| [sdpi-components](https://sdpi-components.dev) 4.0.1 (which includes Lit, BSD-3-Clause) | `streamdeck/com.nplaudio.displaydeck.sdPlugin/ui/sdpi-components.js` | MIT (Lit: BSD-3-Clause; the file keeps its license header) |

The plugin is built with Node.js 24, Rollup, TypeScript and Elgato's CLI; none of them ship in the installer.

DisplayDeck uses documented Windows APIs (display configuration, Core Audio) and one undocumented Windows
COM interface (the audio policy interface that the Sound control panel also uses) to set the default playback
device.

`installer\build.ps1` fails if the package contains any executable other than DisplayDeck's own two and the
Node.js runtime.

## NVIDIA driver (not bundled)

On computers with an NVIDIA graphics card, the Monitors tab can reach displays that Windows will not hand out controls for by calling `nvapi64.dll`, a library the NVIDIA driver installs in the Windows system folder. DisplayDeck does not include or redistribute it.
