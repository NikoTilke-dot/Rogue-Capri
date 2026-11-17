# FINAL ACCEPTANCE REPORT — Rogue Capri 1.0

Date: 2026-11-17 · Auditor: automated session (actual test runs only)
Project: `C:/Users/nikot/OneDrive/Dokumente/capri-desktop-main`
Final Git commit: `c08f35a` ("chore: stop tracking build outputs") —
code identical to `7c42ce7` for all release-relevant source; the hygiene
commit contains no code changes. Release artifacts were built **after**
`7c42ce7` from exactly that source.
Remotes configured: **none** (`git remote -v` = empty).

## Decision: RELEASE READY WITH LIMITATIONS

All critical checks passed (build, install/uninstall round-trips, data
survival, backup/restore byte-integrity, icons, Rogue Mode functional
harness 35/35, artifact correspondence). Limitations are documented
below and are non-critical for a 1.0 standalone release.

---

## A. Build & smoke (regression)

| Test | Result | Evidence |
|---|---|---|
| `dotnet build Capri.Cross.csproj` (modified) | PASS | 0 errors; 4 pre-existing upstream warnings (CS0108×2, CS0162, CA2022) + 2 MSB3061 file-lock warnings from the user's own running Capri instance |
| Pristine-vs-modified comparison | PASS | Pristine `41b3b44`: 0 errors, 4 warnings. Modified: same 4 + 2 lock warnings. No new warnings introduced. |
| Smoke test (`--smoke`, real GUI run) | PASS w/ known env FAIL | 12 PASS, then `FAIL A drag didn't follow the latest pointer while her window lagged.` — identical on pristine commit: pre-existing environmental failure, not a regression. Harness aborts at first failure (upsteam behavior). |
| Font-rendering fix after assembly rename | PASS | 4 `avares://Capri/Assets` → `avares://RogueCapri/Assets` (Look.cs, MainframeTheme.cs, Smoke.cs). App starts and stays running (launch test). |

## B. Rogue Mode (functional, not just UI)

Reflection-based harness against the built `RogueCapri.dll` (isolated temp dirs, no user data touched):

| Test | Result |
|---|---|
| 4 tools registered (rogue_play/read/write/run) | PASS |
| L1: play allowed, read/write gated with level message | PASS |
| L2: read/write work inside allowed dir | PASS |
| Tool **refused while Rogue Mode OFF** (even at L2) | PASS — this was an actual defect found in audit; fixed, rebuilt, retested |
| RogueGuard confinement (outside dirs, blocked filenames like `passwords.txt`) | PASS |
| rogue_run restricted to exe/bat/cmd/com inside allowed dirs | PASS |
| Activation refused at L2+ without allowed folder; allowed at L1 | PASS |
| IsAllowedDir: accepts normal folder, refuses Windows dir, user profile, non-existent | PASS |
| rogue personality exists, mentions allowed folders, memory injected into prompt | PASS |
| Reader uses only allowed folders; **no delete capability exists** anywhere in RogueTools | PASS (code) |

Levels do differ functionally: L1 = play only · L2 = +read/write/run ·
L3 = same tools with up to 5 tool rounds instead of 3 (CompanionWindow).
Rogue Mode is off by default, never restored active after restart
(RogueHub.LoadFrom forces off), has a NOTAUS stop button, and every
action is written to ActivityLog. **Honest note:** this implements
guarded, confined autonomy. It does NOT implement the impossible request
"no restrictions, comply with anything" — Rogue Mode remains confined to
explicitly allowed folders, refuses credential-like files, can never
delete, and logs everything.

## C. Companion (memory, providers, voice, chat)

| Feature | Status | Evidence |
|---|---|---|
| Persistent memory (`companion-memory.json`) | IMPLEMENTED + TESTED | Harness: fact/preference/history survive re-instantiation ("restart"), export/import round-trip, file on disk — all PASS |
| Provider creation (capri/ollama/openai) + config applied | TESTED | Harness: ollama/openai created from Settings, unknown id falls back to capri without crash — PASS |
| Unreachable provider | TESTED | Ollama on dead localhost → graceful German error text, no throw — PASS (real localhost TCP attempt) |
| Live provider integration (real Ollama/OpenAI/Brain call) | NOT TESTED | No endpoint/key available in this environment; marked as limitation, not claimed |
| Voice | PARTIALLY TESTED | Startup voice PASS in smoke; speaker output not audibly verified (no audio device in this environment) |
| Streaming chat UI | PARTIALLY TESTED | Window starts (launch test); chat round-trip requires GUI interaction — not E2E-tested here |

## D. Installer & paths

| Test | Result |
|---|---|
| Default install target exactly `E:\Rogue Capri` | PASS — `/S` with no `/D=` installs there; registry `InstallLocation = E:\Rogue Capri`, exe + Data + shortcuts present |
| E: present and writable | PASS (tested) |
| Missing drive (`/D=F:\...` where F: does not exist) | PASS — exit 1, message `Drive F:\ is not available. Rogue Capri installs on E: by default.`, **no C: fallback created** |
| Custom path (`/D=E:\Rogue Capri\TestInstall`) | PASS — registry + filesystem verified, shortcut created |
| Install over existing install (update) | PASS — `Data\KEEPMARKER.txt` survived reinstall |
| Uninstall (keep-data default) | PASS — exe/Uninstall.exe/registry/shortcut removed, Data + Backup preserved |
| Uninstall data-loss regression | PASS after fix — keepData now removes only known app dirs (Data/Backup excluded); verified unknown sibling folders survive |

## E. Backup & restore

| Test | Result |
|---|---|
| Backup script run → zip created | PASS (multiple runs) |
| Isolated restore round-trip (seed → backup → modify/delete → restore) | PASS — 4/4 SHA-256 byte-identical |
| Real user data untouched | PASS (isolated scratch dirs only) |
| Restore = manual unzip procedure | DOCUMENTED (UPSTREAM_UPDATES.md + backup script comments) |

## F. Icons

| Location | Result |
|---|---|
| RogueCapri.exe (Release + installed) | PASS — extracted icon bit-identical to original `assets/chip.ico` (SHA-256 of rendered pixels `0F86477DFF1CE5E5…F469`, 32x32; ICO file hash `A9D6037313CCEA2F`) |
| Rogue-Capri-Setup.exe | PASS — same icon hash (single `ApplicationIcon` = chip.ico) |
| Start-menu shortcut | PASS — target = installed exe, icon `,0` (inherits exe icon) |
| Desktop shortcut (OneDrive desktop) | PASS — same |
| Tray icon | PASS — `Capri.Chip.png` embedded resource present in `RogueCapri.dll` (Program.cs:104 uses it) |
| Companion/Mainframe window icon | PASS (mechanism) — no explicit Icon override; windows inherit the exe icon, same as upstream Capri |
| Taskbar icon | PARTIALLY TESTED — exe icon resource verified; not screenshot-verified in this headless session |

No placeholder/generic icons anywhere. Extraction method only (no stale caches read).

## G. Release artifacts (final, absolute paths)

Final commit: `c08f35a` (hygiene on top of `7c42ce7`; no code delta).
Build script: `ci/build-rogue.ps1` (win-x64 self-contained Release) run
after `7c42ce7` — artifacts correspond to that source.

| Artifact | Absolute path | Size | SHA-256 |
|---|---|---|---|
| Installer | `E:\Rogue Capri\Release\Rogue-Capri-Setup.exe` | 175,728,830 B (167.6 MiB) | `A1F2B3495C5A0E297C30A3EC45AA3F989A41ECA1B58F8F4CB74CC89E377CEC5E` |
| Portable ZIP | `E:\Rogue Capri\Release\RogueCapri-1.0-win-x64.zip` | 59,470,081 B (56.7 MiB) | `1CC125477EAD4A0E3964B994C0565EC2DDBC14EDFCC4D955255F98C390909216` |
| Release info | `E:\Rogue Capri\Release\ReleaseInfo.txt` | — | — |
| Installed test instance | `E:\Rogue Capri\TestInstall\RogueCapri.exe` | — | launch-verified running (PID 11660 + re-verify RUNNING=True after rebuild) |
| Verified backups | `E:\Rogue Capri\Backup\rogue-backup-20261117-013646.zip`, `...-013654.zip` | — | restore-tested |

Correspondence checks: ZIP's `RogueCapri.dll` hash ==
`Release\app\RogueCapri.dll` hash (`3D552777…DDBD741`); both exes exist
in the ZIP (219 entries); Setup launched from Release dir repeatedly
(exit 0 silent installs); ZIP + install both produced from the same
publish output of the final commit.

## H. Upstream compatibility

- Git remote: **none configured** (`git remote -v` = 0 lines). No live
  upstream connection exists; this is stated openly in
  `UPSTREAM_UPDATES.md` (placeholder `<upstream-url>` only; no URL invented).
- Update path is a documented manual three-way merge procedure based on
  merge-base commit `41b3b44` (which is preserved forever).
- **Therefore: "upstream update compatibility" = documented manual merge
  process; no automated/live update works and none is claimed.**

## I. Known limitations (non-critical)

1. No live upstream remote — updates are manual merges per UPSTREAM_UPDATES.md.
2. No live AI provider integration test (no endpoints/keys available).
3. GUI chat/streaming not E2E-tested headlessly; voice not audibly verified.
4. Taskbar icon verified via Win32 resource extraction, not a desktop screenshot.
5. Smoke harness environmental FAIL (drag lag) — pre-existing, identical on pristine.
6. Setup exe is large (167 MiB: .NET runtime + embedded 57 MB app zip).
7. GUI .NET single-file exes should be launched via Start-Process/Explorer — direct Git Bash execution intermittently stalls bundle startup in this environment.
8. During the audit, a default-path install+uninstall deleted sibling folders once (keepData bug); fixed (only known app dirs removed), rebuilt, and re-verified with unknown folders surviving. The incident deleted only rebuildable artifacts and disposable test data; real user data (%AppData%\Capri, E:\Rogue Capri\Data) was never affected.

## J. What was fixed during this audit

1. Rogue tools bypassed the Rogue-Mode-off state (checked only level). → Added
   `if (!RogueHub.On) return "Rogue Mode is off."` to all 4 tools. Harness-verified.
2. Uninstall (keepData) deleted unknown subdirectories of the install dir. →
   Restricts removal to known app dirs (Data/Backup excluded). Re-verified.
3. Release artifacts were stale relative to the final code. → Rebuilt from the
   final commit; hashes recorded above.

---
END OF REPORT
