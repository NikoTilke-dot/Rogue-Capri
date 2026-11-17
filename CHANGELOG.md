# Changelog

## 0.9.0 — Capri Companion (cross-platform build)

- New **Capri Companion** window (tray menu → "Open Capri Companion"):
  Dashboard, streaming Chat, Character, AI & Models, Memory, Voice,
  Tools & Plugins and System pages in one modern window.
- **AI providers**: Capri Brain (existing Mainframe setup), standalone
  Ollama and any OpenAI-compatible endpoint (LM Studio, llama.cpp, OpenAI,
  OpenRouter...), all with real token streaming where the server supports
  it, connection tests and model listing.
- **Personalities** (capri / friendly / concise / coder / custom) and
  **persistent memory** (facts, preferences, history) with import/export.
- **Desktop tools** the AI can call, gated by the existing SECURITY
  permissions, with inline user confirmation and an action log.
- **Plugin system**: drop `ICompanionPlugin` assemblies into the plugins
  folder; plugins offer tools to the AI, load isolated and can be
  enabled/disabled.
- Animated avatar states (idle / talking / happy / sleeping) and desktop
  avatar size control.
