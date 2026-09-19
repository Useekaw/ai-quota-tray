# AI Quota Tray

A Windows 11 taskbar tray app that shows your Codex (5h + weekly) and GitHub
Copilot quota at a glance, with a detailed popup on click — the Windows
counterpart to the [DankMaterialShell](https://github.com/AvengeMedia/DankMaterialShell)
`aiOverviewControl` plugin's "AI Usage Control" widget.

## What it shows

- **Codex**: session (5h) and weekly rate-limit usage, read straight from the
  Codex CLI's own `codex app-server` backend (JSON-RPC over stdio — there is
  no REST endpoint for this).
- **GitHub Copilot**: premium request budget, plus chat/completions quota
  when your plan reports it, read from the same internal endpoint
  (`api.github.com/copilot_internal/user`) the VS Code Copilot Chat extension
  itself calls.

The tray icon is split into two color-coded halves (green / amber / red per
provider, by highest used-window percentage). Left-click opens a flyout with
per-window percentages, progress bars, and reset times.

## Requirements

- Windows 11.
- [Codex CLI](https://github.com/openai/codex), installed and authenticated
  (`codex login`), reachable on `PATH`.
- A GitHub token with Copilot access, resolved in this order:
  1. `gh auth token` (via the [GitHub CLI](https://cli.github.com/), if installed and logged in)
  2. `COPILOT_GITHUB_TOKEN`, `GH_TOKEN`, or `GITHUB_TOKEN` environment variable

Either data source is optional at runtime — if one is unavailable, its
section shows the error instead of quota data; the other keeps working.

## Development

Toolchain is pinned via [mise](https://mise.jdx.dev/) (`.mise.toml` /
`global.json`, .NET 8 SDK):

```sh
mise install
dotnet build
```

The app targets `net8.0-windows` (WinForms). Building works from any OS
(`EnableWindowsTargeting` is set for that), but the app only *runs* on
Windows.

To publish a self-contained single-file executable from Windows:

```sh
dotnet publish src/AiQuotaTray -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

## Project layout

```
src/AiQuotaTray/
  Program.cs                 entry point, single-instance guard
  TrayApplicationContext.cs  tray icon, context menu, polling loop
  StartupManager.cs          "Start with Windows" registry toggle
  Usage/                     provider abstraction + Codex/Copilot implementations
  Ui/                        tray icon rendering + details flyout
```

## Known limitations

- The Copilot endpoint is undocumented and unversioned; GitHub can change its
  response shape without notice.
- The Codex integration shells out to `codex app-server` on every refresh
  (default: every 5 minutes) since there's no long-lived daemon mode wired up
  yet — each refresh briefly starts a Codex backend process.

## Conventions

- [Conventional Commits](https://www.conventionalcommits.org/) for commit messages.
- English for code, comments, and docs.
