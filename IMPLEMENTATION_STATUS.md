# Rogue Capri 1.0 — Implementation Status

## 2026-11-17 (German session continued)

### Verified results (actual runs, not assumptions)

**Build comparison, pristine vs. modified (identical SDK 10.0.404, same command):**

| | Pristine backup commit (41b3b44) | Modified (Rogue Capri 1.0) |
|---|---|---|
| `dotnet build Capri.Cross.csproj` | 0 errors, **4 warnings** | 0 errors, **4 upstream warnings** + 2 file-lock warnings (user's running Capri instance holds old bin files — environmental) |
| Smoke test (`--smoke <dir>`) | 12 PASS, then `FAIL A drag didn't follow the latest pointer while her window lagged.` (run aborts at first failure) | **identically** 12 PASS, same 1 FAIL |
| Conclusion | — | **Pre-existing environment failure, NOT a regression.** The drag test fails identically on the untouched backup commit. Root cause: the harness aborts at the first failure; this specific drag-follow test depends on real window-position lag behaviour not available in this environment. Upstream test code was intentionally not modified. |

6 warnings in the modified build = the same 4 upstream warnings
(CS0108 x2, CS0162, CA2022) + 2 MSB3061 file-lock warnings from the user's
running Capri process (PID 21136) locking `bin/Debug/net10.0/Capri.exe` —
environmental, not code.

### Fonts fix (regression from AssemblyName rename — FIXED & rebuilt)

Renaming `AssemblyName` Capri → RogueCapri changed Avalonia resource URIs.
Updated 4 references `avares://Capri/Assets` → `avares://RogueCapri/Assets`:
`cross/Look.cs:21`, `cross/MainframeTheme.cs:81-82`, `cross/Smoke.cs:1986`.
`Capri.Sprite.*` / `Capri.Voice.*` / `Capri.Cursor.png` / `Capri.Chip.png`
are explicit EmbeddedResource LogicalNames — unaffected by assembly rename
(verified by grep; smoke test "sprites PASS" confirms).

### Release build (E:\Rogue Capri\Release)

- `dotnet publish` (win-x64, self-contained, Release) succeeds.
- `Rogue-Capri-Setup.exe` (single-file, 175 MB incl. embedded 57 MB app zip)
  produced by `ci/build-rogue.ps1`.
- Silent install test: `Rogue-Capri-Setup.exe /S /D=E:\Rogue Capri\TestInstall`
  → exit 0; created RogueCapri.exe, Uninstall.exe, Data/, Config/, Logs/,
  Cache/, Updates/, Backup/; Start-menu shortcut; HKCU uninstall entry.

### Setup incident 2026-11-17 (01:26-01:30) — uninstaller deleted E:\Rogue Capri (CAUGHT, FIXED, not yet re-verified)

- Exact command: `"E:/Rogue/Uninstall.exe" --uninstall /S` (bash, direct execution)
- Execution: hung 300s in bash; underlying process completed in ~5s when run via
  Start-Process (direct Git Bash execution of .NET single-file exes intermittently stalls
  on bundle extraction/console attach; Start-Process or double-click works).
  Previous 900s install hang: same pattern — same exe completed in 5s via Start-Process.
- Diagnosis: TWO installer bugs + one shell caveat:
  1. `Main` defaulted `dest = @"E:\Rogue Capri"` even in `--uninstall` mode, so an
     uninstall invoked without `/D=` targeted `E:\Rogue Capri` instead of its own folder.
  2. The cleanup `.bat` always did `rmdir /s /q <dir>`, ignoring `keepData`.
     Consequence: the entire `E:\Rogue Capri` (Release + TestInstall + Data) was deleted.
     Losses were rebuildable artifacts + disposable test settings only (source in
     OneDrive untouched; the app's own `%AppData%\Capri` was not touched).
  3. Arg parser now also handles `/D` without `=` and re-joins space-split paths.
- Fixes applied in `cross/installer/Program.cs` (not yet rebuilt/re-tested):
  - uninstall defaults to the exe's own folder; explicit `/D=` only accepted if it
    contains `Uninstall.exe`; rmdir bat only spawned when keepData=false;
    keepData=true bat deletes only `Uninstall.exe`; bounded retry loops, no
    parentheses-`goto`; full re-test pending (build → install → uninstall →
    verify Data preserved + dir removal only outside Data).

### Not yet done (revised)

- Rebuild Release with installer fixes via `ci/build-rogue.ps1`, reinstall to TestInstall (Start-Process), launch-test the font fix, verify Data persists across runs.
- Full uninstall flow test incl. keep-data verification (regression test for the incident).

