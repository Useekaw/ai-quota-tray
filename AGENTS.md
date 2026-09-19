# AGENTS.md

Guidance for AI coding agents working in this repo. Humans: the README covers
the same ground with more prose.

## What this is

A Windows 11 taskbar tray app (.NET 8, WinForms) showing Codex and GitHub
Copilot quota. Two data providers, a tray icon, and a details popup. See
README.md for the full picture and for credit to the upstream project
([bernardopg/AiOverviewControl](https://github.com/bernardopg/AiOverviewControl))
this app's provider logic was ported from.

## Toolchain

- .NET SDK version is pinned in `global.json` and `.mise.toml`. Run
  `mise install` once, then plain `dotnet` commands work.
- Target framework is `net8.0-windows` (WinForms). `EnableWindowsTargeting`
  in the `.csproj` lets `dotnet build`/`publish` run on Linux/macOS too —
  only *running* the app needs actual Windows 11.

## Commands

```sh
dotnet build                                    # compile, any OS
dotnet publish src/AiQuotaTray -c Release -r win-x64 \
  --self-contained -p:PublishSingleFile=true    # ship a single .exe
```

There is no test project yet. If you add one, wire it into
`.github/workflows/ci.yml` and keep it runnable on `ubuntu-latest` (only
Windows-specific behavior — actually launching processes, the registry
startup toggle — needs `windows-latest`; keep that split in mind if adding
CI jobs).

## Architecture (read before changing provider or UI code)

```
src/AiQuotaTray/
  Program.cs                 entry point, single-instance Mutex guard
  TrayApplicationContext.cs  owns NotifyIcon + timer; orchestrates refreshes
  StartupManager.cs          HKCU Run-key registry toggle
  Usage/
    UsageModels.cs            UsageResult / UsageWindow / IUsageProvider
    CodexUsageProvider.cs     spawns `codex app-server`, speaks its JSON-RPC
    CopilotUsageProvider.cs   calls api.github.com/copilot_internal/user
  Ui/
    TrayIconRenderer.cs       draws the two-tone tray glyph
    UsageDetailsForm.cs       borderless flyout popup
```

Each provider implements `IUsageProvider.GetUsageAsync` and must never throw
past its own boundary — return `UsageResult.Failure(...)` instead, so one
provider's outage never takes down the other's display. `TrayApplicationContext`
also wraps each call in a try/catch as a second safety net; don't rely on
that alone when adding a new provider, fix at the source.

### Codex provider gotchas

- No REST API exists for rate limits; the only supported source is JSON-RPC
  against a `codex app-server` child process, matching what the Codex CLI's
  own client does. Don't try to find/invent a REST alternative.
- Launched via `cmd.exe /c codex app-server` (not `codex` directly) so
  PATHEXT resolves npm's `codex.cmd` shim as well as a standalone `codex.exe`.
- Every refresh currently spawns a fresh app-server (~1-2s of process
  overhead). The upstream plugin has a faster "daemon proxy" mode
  (`codex app-server daemon start` + `codex app-server proxy`) for
  standalone-installer Codex; this port doesn't use it yet (see README
  "Known limitations"). If you add it, keep the direct-spawn path as the
  fallback for npm/brew/distro-installed Codex, which don't support daemon
  mode.

### Copilot provider gotchas

- `api.github.com/copilot_internal/user` is undocumented, unversioned, and
  gated on looking like a real VS Code Copilot Chat client — keep the
  `Editor-Version` / `User-Agent` / `X-GitHub-Api-Version` headers in
  `CopilotUsageProvider.cs` in sync with what upstream uses, or GitHub may
  start rejecting requests.
- `unlimited: true` with `entitlement: 0` means "no cap", not "exhausted" —
  don't treat it as 0/0. `has_quota: false` means fully exhausted regardless
  of remaining/entitlement values. See `QuotaSnapshot.UsedPercent`.
- Token resolution order is `gh auth token` → `COPILOT_GITHUB_TOKEN` →
  `GH_TOKEN` → `GITHUB_TOKEN`. Keep that order if you touch
  `ResolveTokenAsync` — it matches the upstream plugin and most users'
  actual auth setup (gh CLI first).

## Conventions

- Conventional Commits, English only, in both code and docs.
- No code comments explaining *what* — only *why* (hidden constraints,
  workarounds, non-obvious protocol details). This file and the READMEs are
  where the *what* lives.
- Don't add abstractions (DI containers, config frameworks, plugin systems)
  the current two-provider scope doesn't need.

## Releases

Tags matching `v*` trigger `.github/workflows/release.yml`, which builds a
self-contained `win-x64` single-file executable on `windows-latest` and
attaches it to a GitHub Release. A tag containing `-alpha`, `-beta`, or `-rc`
is published as a prerelease automatically.
