# Rogue Capri 1.0.1 — Pet Behavior Bugfix Report

Branch: `bugfix/pet-behavior` — Commit: `34ad67c` (on top of `ea32764`, v1.0.0 stays untouched).
Release folder: `E:\Rogue-Capri-Release-1.0.1\` (Setup-Exe + 1.0.1 ZIP + notes + SHA256SUMS).

## 1. Always-on-top visibility (browser/editor/game overlay)

**Cause (verified at Win32 level):** the pet window never had `WS_EX_TOPMOST` set, so she sank below other app windows. Setting the Avalonia `Window.Topmost` property in the constructor did not apply the native topmost band on this Windows/Avalonia build; a native probe showed the visible pet window without the topmost bit while an invisible helper window carried it, and the bit was additionally cleared shortly after start.

**Fix:** `Look.EnsureOnTop` (cross/Look.cs) finds the visible native pet window of the process (by position/size) and puts it in Windows' topmost band directly via `SetWindowPos(HWND_TOPMOST)` + the ex-style bit, re-asserted after each show with bounded retries (~13 s worst case, then stops — no permanent polling). Z-order only: no activation, no movement, no focus change (matching the WinForms original's NativeTop approach). The appearance paths that already called `Look.OnTopAgain` (Bring/come-back style re-shows) now go through the same helper; `Pet.Stack` keeps talk windows in sync via the Avalonia property as before.

**Verified:** after start, the visible pet window reports TOPMOST (`WS_EX_TOPMOST` set) externally for minutes, over a focused game/browser; foreground window never becomes Capri's (keyboard focus stays in the browser).

## 2. Drag-and-throw with bounce

**Cause:** the throw system from the original Capri (`app/Capri.cs` — trail velocity, `Launch`, `Fly`, `Hit`, `Settle`, `Landed`, constants `ThrowFrom=1300`, `ThrowMost=7000`, `ThrowDrag=0.6`, `WallBounce=0.55`, `FloorBounce=0.42`, `FloorGrip=0.72`, `FlightLongest=8`) was never ported to the Avalonia pet; `cross/Pet.cs` only dragged and dropped.

**Adopted from the original (via the Capri source):** trail-based release velocity over `ThrowSpan=0.08 s` (gap-limited `TrailGap=0.03 s`), launch/settle/landing law, sub-stepped time-based integration (≤ 4 ms steps), per-bounce speed loss, floor grip, floor vs side/top bounce asymmetry, ledge catch while flying down, 8 s flight cap. Avalonia adaptation uses the cross pet's anchor/frame-offset model and `Perches`/`Land` landing; spin/tumble visuals and sound (Bonk) not ported (not needed for behavior).

**Verified:** smoke `ThrowChecks` PASS (fast release → thrown, travels, lands/sits ≤ 4 s; slow release unaffected). Live app drag worked (follows the pointer, wall-bounded settle, no off-screen rest).

**Limitations:** spin animation of the original not ported; head-sway/dizzy not ported (no cross equivalents).

## 3. Taskbar position

**Cause:** compounded — her sit pose already lands with the hip on the work-area bottom edge (like the original); because the window was not topmost, the taskbar always painted **over** her legs. With the topmost repair she now sits on top of the taskbar band like the original: same window geometry, legs visible in front of the taskbar on this machine (bottom taskbar, 2560×1440, scaled). Screenshot pixel-diff in the taskbar band confirmed sprite paint-over after the fix.

**Verified:** same rect as v1.0.0 (1171,1180 201×295) now TOPMOST and painting over the taskbar band (before: 0 px activity above the band, after: sprite down to the band edge and presence in the band rows with occluded taskbar pixels replaced by sprite).

## Files changed

- `cross/Look.cs` — `OnTopAgain` native re-assert + `EnsureOnTop`/`PetWindowHandle` helpers + Win32 P/Invoke.
- `cross/Pet.cs` — Opened-hook `EnsureOnTop`, `Stack` uses it; throw fields/constants/launch/fly/settle; trail recording in `DragFrame`; release integration (`Throw` before `Let`); grab cancels flight; frame loop dispatches `Fly` when thrown.
- `cross/Smoke.cs` — `ThrowChecks` + helper; called before `DragChecks`.

## Tests

| Test | Result |
|---|---|
| `dotnet build Capri.Cross.csproj` | 0 errors |
| Smoke `ThrowChecks` (new) | PASS ×2 |
| Smoke drag-lag check | pre-existing environmental FAIL, **identical on pristine v1.0.0 code** (verified by building/stashing and running the untouched commit) |
| Win32 probes (topmost/foreground/taskbar) | TOPMOST on, foreground never taken, band paint-over verified |
| RogueAudit harness (35 checks: Rogue Mode gates, tools, memory, providers, settings) | 35/35 PASS |
| v1.0.1 setup exe silent install + launch | exit 0, exe present, Data/ created, app runs |
| Browser focus test | pet topmost, foreground unchanged |

## Not changed

Companion, KI chat, memory, Rogue Mode, plugins, backup, installer flow, icon.

## Known limitations

- The smoke drag-lag check still fails in this environment (pre-existing, also on the untouchеd commit) — not a regression.
- Tumble/spin visual + landing sound not ported.
- Live Ollama/OpenAI E2E still not testable here (no endpoints/keys), graceful failure as in v1.0.0.

## Artifacts

- `E:\Rogue-Capri-Release-1.0.1\Rogue-Capri-Setup.exe`
- `E:\Rogue-Capri-Release-1.0.1\RogueCapri-1.0.1-win-x64.zip`
- `E:\Rogue-Capri-Release-1.0.1\RELEASE_NOTES.md`, `SHA256SUMS.txt`, `FINAL_ACCEPTANCE_REPORT.md` (v1.0.0), this report.
- Tag `v1.0.0` untouched (`7c42ce7`); new tag `v1.0.1` → `34ad67c` when created.
