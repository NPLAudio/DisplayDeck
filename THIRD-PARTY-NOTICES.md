# Third-party notices

DisplayDeck itself is MIT licensed (see [LICENSE](LICENSE)). It has no runtime dependencies beyond the items
below, and it does not rely on any other program being installed.

| Component | Where | License |
| --- | --- | --- |
| [Node.js](https://nodejs.org/) runtime | `node\node.exe` in the installer (not in the git repository). Its full license text is installed as `licenses\node-LICENSE.txt`. | MIT (plus the licenses of Node's own dependencies, listed in that file) |
| QRCode for JavaScript by Kazuhiko Arase | `web/lib/vendor/qrcode/` | MIT |
| [qrcode-terminal](https://github.com/gtanner/qrcode-terminal) 0.12.0 (the Node port of the above) | `web/lib/vendor/qrcode/` | Apache License 2.0, text in `web/lib/vendor/qrcode/LICENSE-qrcode-terminal.txt` |

"QR Code" is a registered trademark of DENSO WAVE INCORPORATED.

DisplayDeck uses documented Windows APIs (display configuration, Core Audio) and one undocumented Windows
COM interface (the audio policy interface that the Sound control panel also uses) to set the default playback
device.

`installer\build.ps1` fails if the package contains any executable other than DisplayDeck's own two and the
Node.js runtime.
