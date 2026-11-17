# Tasks

## Done (0.9.0 session)

- [x] Repository analysis (app / cross / quest / site / worker).
- [x] Provider abstraction: Capri Brain, Ollama, OpenAI-compatible (streaming).
- [x] Companion window: Dashboard, Chat (streaming, regenerate, export,
      stop), Character (avatar states + desktop size), AI & Models
      (provider/endpoint/model/key/temperature/tokens + test + model list),
      Memory (facts/preferences/history, import/export), Voice (mode pick +
      say-hello test + speak replies), Tools & Plugins (Security toggles,
      tool runners, action log, plugin enable/disable), System.
- [x] Personalities: capri / friendly / concise / coder / custom, memory
      injection into the system prompt.
- [x] Persistent memory (JSON, import/export).
- [x] Desktop tools with permission gating + tool-call loop (max 3 rounds).
- [x] Plugin system (`ICompanionPlugin` / `ICompanionTool`, isolated loads).
- [x] Tray menu entry, version bump, "what's new" line, docs.

## Not done — first next steps

- [ ] **Compile + smoke test on a machine with the .NET SDK**
      (`dotnet build capri-desktop-main/cross/Capri.Cross.csproj`; the session
      environment had no SDK, so this was never run).
- [ ] Exercise the companion window end-to-end (open it, send a message,
      confirm a tool call, toggle a plugin).
- [ ] Add `Smoke` automation names for companion controls and a smoke case.
- [ ] Plugin SDK sample (a tiny example `.dll` plugin + README).
- [ ] Speech recognition page for the Windows build (cross has none — noted
      honestly in the UI).
- [ ] Markdown rendering + code highlighting in chat bubbles (plain text now).
- [ ] File attachments in companion chat.
- [ ] Voice latency/cost dashboard; session stats (tokens, turns).
