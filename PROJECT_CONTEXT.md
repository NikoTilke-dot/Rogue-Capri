# Capri Desktop — Project Context

Open-source AI desktop companion. Repository layout:

- `app/` — Windows (C# / WinForms). Shared logic (`Brain.cs`, `Voice.cs`,
  `Hands.cs`, `Security`, mods) is compiled into both Windows and cross builds.
- `cross/` — Mac/Linux build (C# / Avalonia 11, .NET 10). Links shared files
  from `app/` via `Capri.Cross.csproj`.
- `quest/` — Meta Quest (Unity) companion room.
- `site/` — project website. `worker/` — Cloudflare worker.
- `mods/` — community mods.

The cross app (`cross/`) is the main development target. It already contains:
an animated sprite avatar (`Pet.cs`, sprite strips in `assets/sprites/`), the
Mainframe settings window (`Mainframe.cs` + `MainframeSheet.cs`), mods
(`Mods.cs`), voice (shared `Voice.cs`, ElevenLabs / local TTS), speech hooks,
desktop automation (`Hands.cs`, permission-gated through `Security`), and the
shared `Brain` (Ollama, local OpenAI-compatible servers, cloud API keys).

## What changed in 0.9.0 (this session)

Added **Capri Companion** (`cross/CompanionWindow.cs`), a second window on top
of the existing app, opened from the tray menu: "Open Capri Companion".
It adds a dashboard, streaming chat, a provider abstraction, personalities,
persistent memory, voice controls, desktop tools and a plugin system —
without touching the Mainframe, voice or avatar code paths.

New files:

- `cross/AiProviders.cs` — `IAiProvider` + Capri Brain / Ollama /
  OpenAI-compatible providers with token streaming, plus `Personalities`.
- `cross/CompanionCore.cs` — `MemoryStore` (JSON file next to settings),
  `ActivityLog`, permission-gated `DesktopTools`, `ICompanionPlugin` API and
  `PluginManager` (`.dll` plugins in `<data>/plugins`, isolated load context).
- `cross/Pet.Companion.cs` — companion settings fields (same settings file)
  and `Pet` additions: `OpenCompanion()`, `ChangeBodySize()`, voice helpers.
- `cross/CompanionWindow.cs` — the window: Dashboard, Chat, Character,
  AI & Models, Memory, Voice, Tools & Plugins, System. Animated `AvatarView`
  reuses the sprite strips.

Small edits: `Pet.cs` menu item, `Brain.cs` (CROSS "what's new" line),
`Capri.Cross.csproj` version 0.8.3 → 0.9.0.

## Not verified

No .NET SDK is available in the session environment, so the cross build was
**not compiled or run**. Syntax was checked by hand (brace/paren balance,
API existence greps against the codebase). First thing to do on a machine
with the SDK: `dotnet build capri-desktop-main/cross/Capri.Cross.csproj`.

## Decisions

- Additive only: no existing windows, tools or data files changed behaviour.
- No new NuGet packages: providers use `HttpClient` + `System.Text.Json`
  (shared framework); plugins use `AssemblyLoadContext` (shared framework).
- Tool calls from the model use an inline `[[TOOL name arg="value"]]`
  protocol with per-action user confirmation, reusing the existing
  `Security` toggles. Nothing runs without confirmation unless the matching
  toggle (or the "Always confirm" off-state) allows it.
- Memory is plain JSON (`companion-memory.json`) — easy backup/export.
