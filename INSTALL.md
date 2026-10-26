# Installing DisplayDeck

This takes about two minutes. There is nothing else to download.

## Before you start

- **Windows 10 or 11, 64-bit.**
- At least two ways to arrange your screens (or just one, if you only want audio switching): DisplayDeck
  saves whatever layout you have configured in Windows.

## 1. Install DisplayDeck

1. Download `DisplayDeck-<version>.msi` from the
   [latest release](https://github.com/NPLAudio/DisplayDeck/releases/latest). Optionally check it against the
   `.sha256` file next to it (`Get-FileHash .\DisplayDeck-<version>.msi` in PowerShell). Or build it yourself
   with the steps in [README.md](README.md#build-from-source); it ends up in `installer\dist\`.
2. Double-click the MSI and accept the administrator (UAC) prompt. A progress bar appears; there are no
   questions to answer.
3. Windows SmartScreen may say the publisher is unknown, because the MSI is not code-signed. Choose
   **More info**, then **Run anyway**.

What gets installed:

- The program in `C:\Program Files\DisplayDeck` (it carries its own copy of Node.js, so you do not need to
  install anything else).
- A Start menu shortcut called **DisplayDeck**.
- A Windows Firewall rule named "DisplayDeck" (TCP 8765, local subnet only). It does nothing unless you turn
  on phone access later.

When the installer finishes, the DisplayDeck icon appears in the notification area (bottom right, you may
need to open the ^ overflow).

## 2. Save your first layout

1. Arrange your monitors in Windows the way you want (Settings > System > Display).
2. Right-click the DisplayDeck tray icon and choose **Save current layout...**, or open the page and press
   **Save current layout...**. Type a name, such as "Desk", and press **Save**.
3. Change your monitors to another arrangement and save that as well ("Living Room", "Den TV", ...).

Each saved layout is now a card. Tap one to apply it. If something is not what you wanted, press
**Undo layout change**.

If you already have layout files (`.xml`) in another folder, open **Settings...** and point
**Profiles folder** at it.

## 3. Set up audio (optional)

On any card press **Edit audio**, choose the playback device, choose the spatial format (the list shows what
that device supports, such as Windows Sonic or Dolby Atmos), and press **Save** (or **Save & apply now** to
switch immediately). From then on applying that profile also switches the sound.

Dolby Atmos and similar formats must be available for that device in Windows (for example the Dolby Access
app installed). Windows may ask for administrator permission once when a spatial format is changed.

## 4. Use it from your phone (optional)

1. Right-click the tray icon and choose **Allow other devices (phone)**. Accept the UAC prompt if it appears;
   it adds the firewall permission.
2. Choose **Show phone QR code**. The page shows a QR code.
3. Scan it with your phone's camera while the phone is on the same Wi-Fi or network as the PC. The phone
   opens DisplayDeck and remembers you.

Good to know:

- The link contains a private access key. Treat it like a password and do not share the QR code.
- It only works on your own network (your local subnet). It is not reachable from the internet, and you
  should not forward the port.
- Settings, deleting profiles and file dialogs are only available on the PC itself.
- To turn phone access off, untick **Allow other devices (phone)**.
- If you prefer text, **Copy phone link** puts the same link on the clipboard.

## Start with Windows

DisplayDeck turns on **Start with Windows** the first time it runs. To change that, right-click the tray icon
and toggle **Start with Windows**.

## Updating

Run the newer MSI. It stops the running copy, replaces the files, and starts DisplayDeck again. Your
profiles, settings and audio choices are kept.

## Uninstalling

Open **Settings > Apps > Installed apps**, find **DisplayDeck**, and uninstall. This removes the program, the
shortcut and the firewall rule. It leaves your profiles and `.audio.json` files alone. Per-user data is in
`%LOCALAPPDATA%\DisplayDeck` and `%APPDATA%\DisplayDeck` and can be deleted by hand if you want it gone too.

## Troubleshooting

**No profiles are listed.**
DisplayDeck looks in `%APPDATA%\DisplayDeck\Profiles`. Save a layout first, or point **Profiles folder** under
Settings at the folder that holds your layout files.

**A card says a display is not connected.**
That profile uses a monitor that is currently unplugged or off. DisplayDeck applies the rest and tells you
which one it skipped. If none of its displays are connected, it changes nothing.

**The screen is wrong after applying.**
Press **Undo layout change** (page or tray menu) to go back to the layout from before.

**"Current" is not highlighted.**
It only highlights a profile that matches the live display state exactly (same displays, sizes, positions and
refresh rates). After tweaking Windows display settings the match is gone until you save the layout again.
The card for the last profile you applied is marked **Last applied** meanwhile.

**My phone cannot connect.**
Check that the phone is on the same network as the PC, that **Allow other devices (phone)** is ticked, and
that you opened the link from the QR code (it must include `?key=`). If the firewall permission was declined,
choose **Allow through firewall...** in the tray menu. A phone on a guest network or a different subnet is
blocked on purpose.

**Audio did not switch.**
Check the card's audio line. The device list on the page shows exactly what Windows currently reports; choose
from it with **Edit audio**. Devices that are unplugged or disabled do not appear.

**The folder window did not appear.**
It should open in front of your browser. If it seems to do nothing, look for a new window in the taskbar, and
check that you clicked from the PC itself (not from a phone).

**SmartScreen warns about the installer.**
The MSI is not code-signed. Choose **More info**, then **Run anyway**.

**The tray icon is missing after install.**
Start **DisplayDeck** from the Start menu. Only one copy runs at a time.

## Files and logs

| What | Where |
| --- | --- |
| Program | `C:\Program Files\DisplayDeck` |
| Profiles, `names.json`, `*.audio.json` | `%APPDATA%\DisplayDeck\Profiles` |
| Last applied, previous layout, phone key | `%LOCALAPPDATA%\DisplayDeck` |

To see what the installer did, run it from a terminal with logging:

```
msiexec /i DisplayDeck-<version>.msi /l*v install.log
```
