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

### Build a layout without arranging Windows first (optional)

Press **New layout** on the Profiles page, or **Edit layout** on a card. Drag the screens, set their sizes and refresh rates, press **Try layout** to see it (it goes back by itself after 15 seconds unless you press **Keep**), then name it and press **Save profile**.

### Switch monitor inputs and settings (optional)

Open the **Monitors** tab. Tap an input button (for example **HDMI 1**) to switch that monitor to it, or use the sliders for brightness, contrast and speaker volume. This needs DDC/CI, which most monitors support; if a monitor shows as **Not controllable**, look for a **DDC/CI** setting in its on-screen menu. TVs usually cannot be controlled this way.

## 3. Set up audio (optional)

On any card press **Edit audio**, choose the playback device, choose the spatial format (the list shows what
that device supports, such as Windows Sonic or Dolby Atmos), and press **Save** (or **Save & apply now** to
switch immediately). From then on applying that profile also switches the sound.

Dolby Atmos and similar formats must be available for that device in Windows (for example the Dolby Access
app installed). Windows may ask for administrator permission once when a spatial format is changed.

## 4. Use it from your phone (optional)

1. Right-click the tray icon and choose **Allow other devices (phone)**. Accept the UAC prompt if it appears;
   it adds the firewall permission.
2. Choose **Show phone QR code**. The Phone page opens with the QR code (you can also reach it from the
   **Phone** tab at the top of any page).
3. Scan it with your phone's camera while the phone is on the same Wi-Fi or network as the PC. The phone
   opens DisplayDeck and remembers you.

### Put it on your iPhone or iPad home screen

This makes DisplayDeck open like an app: full screen, with its own icon. You need iOS or iPadOS 16.4 or newer.

1. On your iPhone, scan the QR code with the **Camera** app and open the link in **Safari**.
2. Tap the **Share** button (the square with an arrow pointing up), at the bottom of the screen on iPhone.
3. Scroll down the list and tap **Add to Home Screen**.
4. Leave the name as **DisplayDeck** (or change it) and tap **Add**.
5. Close Safari and tap the new **DisplayDeck** icon on your home screen.

The icon is the DisplayDeck mark: a dark square with an outlined screen and a green one.

### Put it on your Android home screen

1. Scan the QR code and open the link in **Chrome**.
2. Tap the **⋮** menu, then **Add to Home screen** (or **Install app**).

Good to know:

- The link contains a private access key. Treat it like a password and do not share the QR code.
- It only works on your own network (your local subnet). It is not reachable from the internet, and you
  should not forward the port.
- Settings, deleting profiles and file dialogs are only available on the PC itself.
- To turn phone access off, untick **Allow other devices (phone)**.
- If you prefer text, **Copy phone link** puts the same link on the clipboard.

## 5. Add it to your Stream Deck (optional)

If you have Stream Deck 7.6 or newer installed, the tray menu shows **Install Stream Deck plugin...**.

1. Right-click the DisplayDeck tray icon and choose **Install Stream Deck plugin...**, then confirm in the
   Stream Deck window that opens.
2. In Stream Deck, find the **DisplayDeck** category in the actions list and drag **Apply profile** onto a key.
3. In the panel below, pick the profile from the dropdown. Repeat for each profile you want on a key.
4. Optional extras: **Next profile** (cycles through your profiles), **Undo layout**, and **Audio output**
   (makes a playback device the default, with an optional spatial format).

Using a Stream Deck +? Drag **Profile dial** or **Audio dial** onto a dial: turn to choose, press or tap to confirm.
**Status** shows the current profile and audio device on a Neo info bar or a dial.

The keys, dials and info bar need DisplayDeck running on the same PC; if it isn't, they show a warning mark. A key's green ring
means that profile (or audio device) is the active one right now.

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

**The home screen icon says "Access key required".**
The app was added from a page without the access key. Delete the icon, scan the QR code on the Phone page again, and repeat the steps above. If you turned phone access off and on again, the key changes, so any older icon needs the same fix.

**The home screen icon is blank or shows a letter instead of the DisplayDeck mark.**
iOS only picks up the icon when the app is added. Delete the icon, open the link in Safari again and re-add it. Add it from Safari: other iOS browsers may not offer **Add to Home Screen** on older iOS versions.

**A monitor on the Monitors tab says "Not controllable".**
Open the monitor's on-screen menu and make sure **DDC/CI** is turned on (it is sometimes under a general or system menu). Some monitors only allow it on certain inputs or when a power-saving mode is off. TVs usually do not support it at all.

**Switching a monitor's input made the screen go blank.**
The monitor is now showing an input that this PC is not on (or that has no signal). Use the monitor's own buttons to select the PC's input again.

**There is no "Install Stream Deck plugin..." item.**
It only appears when Stream Deck is installed on this PC and the installer included the plugin. Installers
built without Node 24 leave the plugin out; see [README.md](README.md#stream-deck).

**A Stream Deck key shows a warning mark.**
DisplayDeck isn't running, or the profile or audio device it points at no longer exists (the caption says
"offline", "missing" or "not found"). Start DisplayDeck, or pick another profile or device in the key's panel.

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
