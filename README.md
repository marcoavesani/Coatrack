# Coatrack

Run more than one Claude account on one Windows PC. Coatrack is a small notification-area (system tray) app that switches the Claude desktop app between separate **instances**, such as Work and Personal, without signing out and back in. Each instance has its own sign-in, chats, settings, MCP servers and Claude Code data. Only one instance runs at a time.

It's a Windows take on [Hatstand](https://github.com/willblackmore/hatstand) for macOS, written from scratch.

> Coatrack is an unofficial, independent project. It isn't made by or affiliated with Anthropic.

## What it does

- Shows a top hat in the notification area, with a badge showing the running instance's initial. Hover over it for the full name.
- Click it for a menu of your instances. Click one to switch: Coatrack quits Claude properly, then reopens it as that instance.
- **Manage Instances** window to add, rename and remove instances and see where their data lives.
- **Forwards Claude's sign-in links to the right instance** (see below). Without this, signing in to a second account doesn't work on Windows.
- Settings: start at sign-in, confirm before switching, sign-in links, and where to find Claude.

## Install

You need Windows 10 or 11 and the Claude desktop app (the Microsoft Store/MSIX build or the older installer).

1. Build it (see below), or copy `Coatrack.exe` somewhere permanent, for example `%LOCALAPPDATA%\Programs\Coatrack\`.
2. Run `Coatrack.exe`. The first time, the Manage Instances window opens.
3. Click **+** to add an instance (for example "Personal").
4. When asked, **set up sign-in links**: Settings opens on Default apps. Find **CLAUDE** (URL:claude) and choose **Coatrack**. You only do this once.
5. Open the new instance and sign in with your other account.

Turn on **Start Coatrack when I sign in to Windows** in Settings if you like.

Windows SmartScreen may warn about an unsigned app the first time; click **More info › Run anyway**, or build it yourself.

## How it works

The Claude desktop app is built on Electron. Coatrack starts it with two settings that keep each instance's data separate:

| Setting | What it holds |
| --- | --- |
| `--user-data-dir=<folder>` (Electron argument) | The app's own data: sign-in, chats, settings, MCP config and Cowork files. |
| `CLAUDE_CONFIG_DIR=<folder>` (environment variable) | Claude Code data for the Code tab: settings and sessions. |

The **default instance** uses neither, so Claude uses its normal locations (`%LOCALAPPDATA%\Claude-Data\…` or `%APPDATA%\Claude`, and `%USERPROFILE%\.claude`). Opening Claude from the Start menu, the taskbar or at sign-in always opens the default instance.

For the Microsoft Store/MSIX build, Coatrack starts Claude through its app execution alias (`%LOCALAPPDATA%\Microsoft\WindowsApps\claude-desktop.exe`). That keeps Claude's package identity and still passes the argument and environment variable through. Coatrack never modifies, copies or moves Claude or Claude's data folders. It only starts and quits Claude.

New instances are created in `%LOCALAPPDATA%\Coatrack\Instances\<Name>\` (or a folder you choose), with `app-data` and `claude-code` folders inside. You can also point an instance at folders you already have.

**Which instance is running?** Coatrack reads the command line of Claude's main process and matches its `--user-data-dir` against your instances. No `--user-data-dir` means the default instance; a folder no instance uses shows as "Unknown instance".

**Quitting Claude.** Closing Claude's window only hides it in the notification area, so Coatrack sends Claude's window the message Windows sends at sign-out. Claude handles it by quitting properly, the same as choosing Quit from its own menu. Coatrack then waits for every Claude process to exit. If that takes more than 20 seconds, it offers to force quit.

### Sign-in links

Claude signs in through your browser. At the end, the browser opens a `claude://` link that carries the sign-in back to the app. Windows starts Claude for that link **without** `--user-data-dir`, so it lands in the default instance and the instance you were signing in to never receives it. This is why a second account doesn't work if you only launch Claude with a different data folder.

Coatrack registers itself as another handler for `claude://` links (per user, no admin needed). Once you choose it in **Settings › Default apps**, Windows runs `Coatrack.exe --open-url <link>`. Coatrack starts Claude again with the **running** instance's `--user-data-dir` and the link. Claude sees that the instance is already open, so it hands the link to that instance and exits straight away. If Claude isn't running, Coatrack opens the instance you used last, with the link.

Coatrack has to be chosen in Default apps because Windows doesn't let apps make themselves the default handler. Claude also re-registers its own handler every time it starts, but your Default apps choice still takes priority. To undo it, click **Stop handling links** in Coatrack's Settings.

Links are logged to `%LOCALAPPDATA%\Coatrack\links.log` (scheme and host only, never the one-time codes) to help with troubleshooting.

### Files

- Settings and the instance list: `%LOCALAPPDATA%\Coatrack\config.json` (written atomically).
- Start at sign-in: a value named `Coatrack` in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
- Link handler: `HKCU\Software\Classes\Coatrack.ClaudeLink`, `HKCU\Software\Coatrack\Capabilities` and a `Coatrack` value in `HKCU\Software\RegisteredApplications`.

## Building

You don't need Visual Studio or the .NET SDK. Coatrack builds with the C# compiler that ships with Windows (.NET Framework 4.x, C# 5) and has no dependencies.

```powershell
.\build.ps1
```

The app is at `build\Coatrack.exe`. Run the unit tests with:

```powershell
.\build.ps1 -Test
```

The build first compiles a small tool that draws the app icon, so there are no binary assets in the repository.

## Tips

- **Pin the icon** so it's always visible: drag it from the overflow (^) onto the taskbar, or turn it on in Settings › Personalisation › Taskbar › Other system tray icons.
- **Importing an existing setup.** In Manage Instances, click **+**, choose **Use existing folders**, then pick the app data folder and (optionally) the Claude Code folder.
- **The first time a new instance opens**, Claude shows its sign-in screen. Set up sign-in links first, then sign in with the account you want for that instance.
- **Checking the Code tab's folder.** Open the Code tab in a non-default instance, then click **Open folder** next to its Claude Code folder in Manage Instances. Claude Code's files should appear there, not in `%USERPROFILE%\.claude`.

## Limitations

- **Unofficial.** Claude updates may change how it handles these settings and stop Coatrack working.
- **Update restarts return to the default instance.** When Claude restarts itself after an update, it drops its launch settings and comes back as the default instance. Coatrack shows this honestly; switch back from the menu.
- **One instance at a time.** Switching always quits Claude first, so finish anything in progress before you switch. (Claude can technically run two instances side by side, but sign-in links could then only go to one of them, and both share Cowork's background service.)
- **Opening Claude directly** (Start menu, taskbar, sign-in) always opens the default instance.
- **Code tab sign-in.** If the Code tab signs in separately in your version of Claude, it may use Windows Credential Manager entries shared between instances.
