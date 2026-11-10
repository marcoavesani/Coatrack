# Coatrack — project brief

A Windows notification-area app that switches the Claude desktop app between separate instances (Work, Personal…), each with its own sign-in, chats, settings, MCP servers and Claude Code data. A Windows take on Hatstand (macOS); see README.md for the user-facing description. Use British English in UI text, comments and docs.

## Hard constraints

- Builds with the .NET Framework 4.x `csc.exe` that ships with Windows: **C# 5 only** (no `$""`, `?.`, `nameof`, expression-bodied members, `out var`, tuples, pattern matching, auto-property initialisers). No NuGet, no SDK. `.\build.ps1 -Test` must pass.
- Never modify, copy or move Claude's install or its data folders. Only start and quit Claude.
- One instance at a time; switching quits Claude fully first.
- Instances must never use (or contain) Claude's default data locations: `%APPDATA%\Claude`, `%LOCALAPPDATA%\Claude-Data`, `%LOCALAPPDATA%\Packages\Claude_*`, `~\.claude`, `~\.claude.json`.
- Never log full `claude://` links (they carry one-time sign-in codes).

## Facts verified against Claude 4.23118 (MSIX), November 2026

- Claude is the MSIX package `Claude_pzs8sxrjxfjjc`. Its app execution alias `%LOCALAPPDATA%\Microsoft\WindowsApps\claude-desktop.exe` accepts arguments and inherits the environment, and the process keeps package identity.
- `--user-data-dir=<folder>` is honoured: Claude logs `[user-data-move] stay_chosen_folder` and skips moving data into `Claude-Data`. The single-instance lock is per data folder, so a second launch with the same folder hands its argv (including a `claude://` link) to the running instance and exits.
- The default instance (no switch) uses `%LOCALAPPDATA%\Claude-Data\claude_<package id>`.
- Claude rewrites `HKCU\Software\Classes\claude` on every start, but Windows sends `claude://` links to the MSIX package's own registration anyway, so that key can't be used to redirect them. Only a `UserChoice` (set by the user in Default apps) overrides the package. That's why Coatrack registers `Coatrack.ClaudeLink` with Capabilities/RegisteredApplications and asks the user to choose it.
- Sending `WM_ENDSESSION` (wParam = 1) to Claude's main-process windows makes it log `Windows session ending (shutdown) - quitting the app` and quit cleanly. Closing its window only hides it to the tray.
- Process tree: the main process is `claude-desktop.exe` (via the alias) or `Claude.exe` with no `--type=`; helpers carry `--type=`. Claude Code runs as `claude.exe` from inside the data folder's `claude-code\` directory, which must not be mistaken for the app.

## Layout

- `src/Model`: `Config`/`Instance`/`ConfigStore` (JSON at `%LOCALAPPDATA%\Coatrack\config.json`), `InstanceValidator`, `PathUtil`.
- `src/Claude`: `ClaudeController` (locate, detect, launch, quit and wait, force quit), `CommandLine` (Windows argv split/quote, Chromium switches), `NativeProcess` (Toolhelp, command lines via `NtQueryInformationProcess`, `WM_ENDSESSION`), `LinkRelay` (`--open-url` mode and registration).
- `src/App`: `Program` (single-instance mutex; `--open-url` relay; `--startup`), `TrayController` (NotifyIcon, menu, switching).
- `src/UI`: Manage, Add and Settings windows, `HatIcon` (draws all icons in code), `FolderPicker` (IFileOpenDialog).
- `tests/Tests.cs`: dependency-free runner for the pure logic.
- `tools/IconTool.cs`: writes the .ico embedded at build time.
