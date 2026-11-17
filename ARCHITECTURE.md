# Architecture (companion additions)

```
cross/  (.NET 10, Avalonia 11)
├─ cross/CompanionWindow.cs   The window. 8 pages: Dashboard, Chat, Character,
│                            AI & Models, Memory, Voice, Tools & Plugins, System.
│                            AvatarView renders sprite strips (idle/talk/happy/sleep).
├─ cross/AiProviders.cs       IAiProvider abstraction.
│   ├─ CapriBrainProvider     Wraps pet.Brain.Ask (Mainframe brain: Ollama/cloud).
│   ├─ OllamaProvider         POST /api/chat, NDJSON token streaming.
│   └─ OpenAiProvider         POST /chat/completions, SSE token streaming.
│                            Personalities: capri/friendly/concise/coder/custom.
├─ cross/CompanionCore.cs
│   ├─ MemoryStore            Facts/preferences/history in companion-memory.json.
│   ├─ ActivityLog            Session action log (dashboard + tools pages).
│   ├─ DesktopTools           system_info, open_app, open_folder, search_files,
│   │                         current_time. Permission-gated via Security.
│   └─ PluginManager          Loads ICompanionPlugin .dll files from plugins/
│                             folder in an AssemblyLoadContext; exposes their
│                             tools; per-plugin enable/disable in memory file.
├─ cross/Pet.Companion.cs     Settings fields (same file) + Pet additions.
├─ cross/Pet.cs               Menu item "Open Capri Companion" (+2 lines).
├─ cross/Mainframe.cs / .Theme  Untouched; the companion reuses Neon* controls
│                            and MainframeTheme brushes.
└─ app/Brain.cs (shared)      Untouched except CROSS "what's new" line.
```

## Data flow (chat)

UI (CompanionWindow) → `BuildMessages()` (system prompt from personality +
memory, last 20 session messages) → provider `Stream()` on a thread-pool
thread → tokens posted to UI thread → bubble updates live. After completion,
the reply is scanned for `[[TOOL name arg="value"]]` lines. Each tool is
auto-run only if `Security` allows that category (or it's read-only);
otherwise a Run/Skip confirmation row appears in the chat. The tool result
is fed back as a `[tool name result]` user message and the model is asked
again (max 3 tool rounds). Results are logged in `ActivityLog` and stored
in memory history.

## Threading

Provider network calls never run on the UI thread. UI updates from workers
go through `Dispatcher.UIThread.Post`. Cancellation: a per-turn
`CancellationTokenSource`, cancelled by Stop or window close; sound gets a
fresh HTTP timeout per request (infinite timeout on the shared client,
token-checked between stream lines).

## Error model

Providers return `string error` (null on success) instead of throwing across
the UI boundary. Plugin load failures are captured per-plugin and shown in
Tools & Plugins. Memory load/save failures are logged to the activity log,
never thrown.
