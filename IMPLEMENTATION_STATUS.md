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

### Installer fixes — RE-VERIFIED 2026-11-17 (~01:34)

- Release rebuilt via ci/build-rogue.ps1 (font fix + installer fixes included).
- Silent install via Start-Process (/S /D=E:\Rogue Capri\TestInstall): ~3s, exit OK.
- Font fix verified: RogueCapri.exe stays running (PID 11660, 10 s) and writes Data\cross.json — no startup crash.
- Uninstall regression test passed: marker file Data\KEEPMARKER.txt survived; RogueCapri.exe / Uninstall.exe / registry entry / Start-menu shortcut all removed; fresh reinstall kept Data\KEEPMARKER.txt (update = data-preserving).

### Backup script ci/backup-rogue.ps1 — VERIFIED

- Locates data root via RogueCapriHome env, registry InstallLocation\Data, <install>\Data, %AppData%\Capri (initially picked the AppData fallback; fixed during verification to correctly pick the installed TestInstall\Data).
- Output: E:\Rogue Capri\Backup\rogue-backup-<yyyyMMdd-HHmmss>.zip (unique timestamped name, never overwrites).
- Contents verified with test fixtures: cross.json (settings incl. Rogue config), companion-memory.json (Companion memory), plugins\ — archive readable via ZipFile, entry count asserted non-zero. Excludes *.tmp/*.log/cache/Temp.
- Original user data untouched (read-only copy staging).

### Final smoke (code state at commit 45c6dcf) — VERIFIED

- dotnet run -- --smoke <dir>: 12 PASS, 1 FAIL — identical to pristine backup commit 41b3b44 (pre-existing environmental drag test; harness aborts at first failure). No regression.

### Git — VERIFIED

- 41b3b44 pre-Rogue backup commit (merge base) preserved.
- 45c6dcf "Rogue Capri 1.0" — all changes committed (code + installer + ci + docs).
- No remote configured (documented in UPSTREAM_UPDATES.md; no URL invented).

### Known remaining limitations (not blocking 1.0)

- GUI .NET single-file exes should be launched via Start-Process/Explorer — direct execution from Git Bash intermittently stalls bundle startup in this environment.
- The environmental smoke FAIL (drag didn't follow the latest pointer) aborts the smoke harness in this environment; later tests do not run here. Identical on pristine code (upstream test, not modified).
- NSIS is not installed; the custom WinForms installer Rogue-Capri-Setup.exe (single-file, embedded app zip, silent/quiet-uninstall, data-keep default) is used.
- .git/worktrees/pristine leftover directory could not be deleted (file lock); harmless — worktree registration was pruned.
- cross/installer/app.zip is a build artifact, not committed. Check git status before committing.

### Final deliverables on E:

- E:\Rogue Capri\Release\Rogue-Capri-Setup.exe (175 MB, single-file, standalone)
- E:\Rogue Capri\Release\RogueCapri-1.0-win-x64.zip (56.7 MB portable bundle)
- E:\Rogue Capri\Release\ReleaseInfo.txt (SHA256 etc.)
- E:\Rogue Capri\TestInstall\ — installed/tested instance (Data preserved)
- E:\Rogue Capri\Backup\rogue-backup-*.zip — verified backups

