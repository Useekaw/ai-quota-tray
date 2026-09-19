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
  Program.cs                 entry point, single-instance Mutex guard, Serilog bootstrap
  TrayApplicationContext.cs  owns NotifyIcon + timer; orchestrates refreshes
  StartupManager.cs          HKCU Run-key registry toggle
  Logging/
    AppLog.cs                 Serilog file sink under %LOCALAPPDATA%\AiQuotaTray\logs
  Usage/
    UsageModels.cs            UsageResult / UsageWindow / IUsageProvider
    CodexUsageProvider.cs     spawns `codex app-server`, speaks its JSON-RPC
    CopilotUsageProvider.cs   calls api.github.com/copilot_internal/user
  Ui/
    TrayIconRenderer.cs       draws the split fill-bar tray glyph
    UsageDetailsForm.cs       borderless flyout popup (Win11 chrome, see below)
    Win11Window.cs             DWM P/Invoke: rounded corners, dark mode, acrylic
    ThemePalette.cs            reads light/dark from the registry
    UsageBar.cs                owner-drawn flat fill bar (stock ProgressBar looks dated)
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
- The exact path is resolved via `where.exe codex` first; a real `.exe` is
  launched directly, a `.cmd`/`.ps1` shim (npm installs) is routed through
  `cmd.exe /c`. Keep both paths — don't assume one install method.
- **Don't close `process.StandardInput` until after the response-read loop.**
  `codex app-server` exits the instant it sees EOF on stdin, even with
  requests still queued/unanswered — closing stdin right after writing (which
  this file did until alpha.4) silently starves every request after
  `initialize` of a response. This was a real shipped bug, found via the
  Serilog diagnostics log; don't reintroduce it.
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

### Win11 flyout chrome gotchas (`UsageDetailsForm`, `Win11Window`)

- Rounded corners and the dark-mode-aware frame use `DwmSetWindowAttribute`
  (`DWMWA_WINDOW_CORNER_PREFERENCE`, `DWMWA_USE_IMMERSIVE_DARK_MODE`) —
  low-risk, well-established APIs.
- The acrylic backdrop (`DWMWA_SYSTEMBACKDROP_TYPE` + `DwmExtendFrameIntoClientArea`
  + a `TransparencyKey`/glass-sheet trick) is a genuine hack: WinForms has no
  first-class support for DWM backdrop materials, and **this was never
  verified against a real Windows 11 box during development** (dev happens on
  Linux; the person testing it doesn't necessarily have time to eyeball every
  visual detail). If it ever misrenders, set
  `AIQUOTATRAY_DISABLE_ACRYLIC=1` for an instant, always-correct fallback to
  the flat themed background — don't first assume the rounded-corners/dark-mode
  parts are also broken, they're independent and much safer.
- `Win11Window.TryApplyAcrylicBackdrop` only returns `true` when DWM actually
  accepted the request. Never set `TransparencyKey` based on anything else —
  an unapplied backdrop with a transparency key still set renders as a literal
  hole into the desktop, not a graceful fallback.
- Any child control drawn under acrylic must have `BackColor = Color.Transparent`
  (or draw nothing outside its actual content, like `UsageBar`) — an opaque
  control background would show as a solid colored patch floating on the
  blurred backdrop instead of blending in.

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
