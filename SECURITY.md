# Security

Who Stole My Focus watches which app is in front and notices when you click or type, so it's built to see as little as it can: names of apps and the times of your input, never keys, text or what's on screen.

## Reporting a vulnerability

Please report security problems privately through **Security → Report a vulnerability** on this repository, not in a public issue. You'll get a reply within a few days.

## What it can access

| | |
|---|---|
| **Which window is in front** | Windows tells any app when the foreground window changes, and when a window is minimized. The window is only used to find the process it belongs to and its class name, which tells the Alt+Tab switcher, the taskbar and the desktop apart so they aren't logged. For Store apps, which Windows draws inside a frame, it looks at the frame's child windows only to find the app's own process. For a switch it isn't sure of, it asks Windows whether the window that was in front is still on the virtual desktop showing, to recognize a swipe to another desktop. |
| **The app in front** | Its executable's path, its file description and product name, and when it started, read with the least access Windows has for that (`PROCESS_QUERY_LIMITED_INFORMATION`). To notice an app quitting, it waits on the process with `SYNCHRONIZE`, which can do nothing else. |
| **When you clicked or typed** | Through Windows' raw input, which any app may ask for. See below. |
| **Its own files** | The log and settings, in `%LOCALAPPDATA%\WhoStoleMyFocus`, and a value in the current user's Run key if you turn on Start with Windows. |

### What it reads from raw input

Raw input sends an app a small record for each key, mouse and touch event. Who Stole My Focus reads these fields from it, and nothing else:

1. **The type,** keyboard, mouse or other device, and for other devices which one sent it.
2. **For a key, its flags and virtual-key code,** used only to tell whether it's a modifier (Shift, Ctrl, Alt or a Windows key) and whether it went down or up. The code is compared with that list and then dropped.
3. **For the mouse, the button flags,** to tell a click from a move or a scroll.
4. **For a touch device, what kind it is,** from Windows' description of the device: a touchscreen or pen counts as a click, a touchpad as a gesture. What was touched, and where, is never read.

Then it notes the time against one kind, click, key, modifier or gesture, and forgets the event. It keeps the last minute of times, in memory only, to work out how long before a switch you last clicked or typed. When it judges a switch, it also asks Windows whether Ctrl, Alt or a Windows key is held at that moment, and about no other key. It never knows which key you pressed, never sees text, and can't block, change or delay input: raw input only reports. CI checks the exact fields read.

## What it can't access

- **Your keys and text.** No keyboard hook, nothing that turns keys into text, and no window's title or contents. CI fails the build if any of those calls appear.
- **The screen and clipboard.** No screen capture, and it never reads the clipboard. It writes to it only when you click Copy Details or Copy as CSV.
- **Other apps' insides.** It never reads or writes another process's memory, never sends input, and never brings a window to the front. CI checks for each of those, and that processes are opened only with the two access rights above.
- **The internet.** It has no networking code and starts no other programs. CI fails the build if any appear.
- **Admin rights.** Its manifest asks to run as you, never as administrator, with no UI access. It installs no service or driver.

## Limits worth knowing

- Windows doesn't send raw input to an ordinary app while an app running as administrator is in front. Clicks and keys in it aren't seen, so switching away from one to another app can be logged as a steal; ignore the entry if it's yours.
- Windows won't give the path of other users' or the system's processes, like a User Account Control prompt shown on the normal desktop. Those are logged by their process name only.
- Anyone with your user account can read the log, as with any file in your profile. It holds app names, paths and times, never anything you typed. Paths in your user folder are shortened to `~` when you copy details or CSV.

## How it handles things

- **Text from other apps is cleaned.** App names and descriptions have control characters and invisible formatting marks removed, and are cut to 80 characters before they're shown. Names that look like spreadsheet formulas are quoted in the CSV, so they open as text.
- **Saved files are read as untrusted.** A damaged or newer log is set aside, not overwritten. Settings out of range keep their defaults.
- **Files are written whole.** Each is written to a temporary file and moved into place.
- The app has no package dependencies. Only its tests use xUnit.

## The signature

Releases aren't signed with a paid code-signing certificate, so Windows SmartScreen may warn the first time. Each release is built by CI from its tagged source, with a SHA-256 for each download, so you can check you have the build CI made.
