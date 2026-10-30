# Contributing

Thanks for helping out! Bug reports, ideas and pull requests are all welcome.

## Reporting a switch it got wrong

If it called something a steal that you did yourself, or missed a real one, open a [bug report](https://github.com/stanczykwyatt/who-stole-my-focus-windows/issues/new?template=bug.yml). Right-click the row in the log and choose **Copy Details**, then paste that and say what you actually did, like "pressed Win+3" or "clicked a link in a chat app". Paths in your user folder are already shortened to `~`.

## Getting started

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). The Core and its tests run anywhere .NET does, including macOS and Linux; the app itself needs Windows to run, but it builds anywhere.

```bash
dotnet test                         # the Core's tests
dotnet build -c Release             # everything, the Windows app included
dotnet publish WhoStoleMyFocus -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

## How it's put together

- `WhoStoleMyFocus.Core` holds everything that decides: input times, the rules that turn them into a cause, the tracker that judges each switch, the log, top offenders, the wording, the CSV and settings. It's a port of the Mac app's FocusCore, with the same names, steps and order, so the two apps judge the same switch the same way. Windows-specific parts are the list of system apps (by file name, only inside the Windows folder), app keys (so `app-1.2.3` version folders count as one app), and sorting raw input into clicks, keys and modifiers.
- `WhoStoleMyFocus.Core.Tests` has the Mac app's scenario tests, ported one for one, plus tests for the Windows parts. A change to how switches are judged comes with a scenario.
- `WhoStoleMyFocus` is the tray app. `InputClock.cs` is the only file that touches raw input, `FocusMonitor.cs` the only one that watches the foreground and looks at other processes, `LogStore.cs` the only one that reads or writes files, and `Native.cs` declares the Win32 calls.

If you change the judging rules, consider whether the Mac app should change too, and say so in the pull request.

## Ground rules

- **No network access, ever.** Pull requests that add networking, analytics or update checks won't be merged.
- **Times, never keys.** No keyboard hooks, nothing that reads which key or text, no window titles or contents, no screen capture.
- **Never act on other apps.** No sending input, no bringing windows forward, no reading other processes beyond their name and start time.
- **Runs as you.** No admin rights, services or drivers.
- **No package dependencies** in the app. The tests use xUnit.
- **Made-up apps only** in tests, screenshots, art and bug reports.
- Run `dotnet test` before opening a pull request.
- Keep commits small, with a short message saying what changed and why.

## Security issues

Please don't open public issues for vulnerabilities. See [SECURITY.md](SECURITY.md).
