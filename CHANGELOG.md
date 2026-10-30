# Changelog

## 0.1.0 (2026-11-28)

First release of the Windows version.

- **Logs every app that comes to the front,** and judges each switch with the same rules as the Mac app: stole focus, you switched, you opened it, came back, or not sure.
- **Catches steals while you type,** with a second look 0.4 seconds after each switch.
- **Recognizes your own switches:** clicks, Alt+Tab and other shortcuts, Start, Search and launchers like PowerToys Run, and apps you just opened.
- **Names parts of Windows** in plain words, and the product a helper belongs to.
- **Top offenders,** the full log, Copy Details, Copy as CSV, ignored apps, optional notifications and a tray icon that shows new steals.
- **Start with Windows,** off by default.
- No admin rights, no internet access, no package dependencies. One self-contained `.exe` for x64 or Arm64.
