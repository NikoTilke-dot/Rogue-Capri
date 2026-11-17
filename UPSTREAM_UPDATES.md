# Upstream Updates for Rogue Capri 1.0

Rogue Capri 1.0 is a derivative of the local Capri Desktop 0.9.0 + Capri
Companion checkout. This document describes how to fold future upstream
Capri changes into **Rogue Capri** without losing Companion and Rogue Mode.

> **Current repository state (verify before working):**
> - Single branch `master`, one commit: `41b3b44` — "Backup: Capri Desktop
>   0.9.0 + Capri Companion (pre-Rogue-Capri 1.0)". That commit is the
>   **merge base** for every Rogue Capri change made on top of it.
> - **No git remote is configured yet.** When that changes, `git remote -v`
>   will show it. The commands below use the placeholder `<upstream-url>` —
>   replace it with the real upstream URL; never guess it.
> - Working tree may contain uncommitted Rogue Capri changes; commit them
>   (see section 6) before starting an upstream merge.

---

## 1. Check for upstream updates

```powershell
cd C:\Users\nikot\OneDrive\Dokumente\capri-desktop-main
git status
git log --oneline -3
git remote -v            # if empty, report this as "unknown" — do not invent a URL
```

Compare the version in `cross/Capri.Cross.csproj` (`<Version>`) against the
upstream release notes of the Capri project. There is no automated update
checker in the app; upstream changes arrive as Git history or releases.

## 2. Add the upstream remote (once)

```powershell
git remote add upstream <upstream-url>
git fetch upstream
git log --oneline master..upstream/<main-branch>
```

If you only have an upstream ZIP/snapshot, place it in a separate folder
(`C:\Users\nikot\AppData\Local\Temp\capri-upstream`) and diff against the
merge base instead (section 3).

## 3. Fetch and compare

```powershell
git fetch upstream
git diff --stat 41b3b44..upstream/<main-branch>   # what changed upstream
git diff 41b3b44..master -- cross/Pet.cs          # what Rogue Capri changed
```

The three-way view is the key: `41b3b44` is the common ancestor, `master`
is Rogue Capri, `upstream/<main-branch>` is the new upstream.

## 4. Merge

```powershell
git checkout -b upstream-merge-<new-version> master
git merge upstream/<main-branch>           # or: git merge -s ours upstream/<branch> (see below)
```

- Default: `git merge` and resolve conflicts (section 5).
- `git merge -s ours upstream/<branch>` keeps Rogue Capri's files on conflict
  and only takes upstream files that Rogue Capri never touched. Use this
  when the merge is mostly one-sided and you prefer to port individual
  upstream changes by hand later.

## 5. Conflict resolution

Rogue Capri's intentional local modifications (never resolve these *away*):

| Area | Files | Rogue Capri change |
|---|---|---|
| Branding | `cross/Capri.Cross.csproj`, `cross/Mainframe.cs`, `cross/Look.cs`, `cross/MainframeTheme.cs`, `cross/Smoke.cs` | Product name `Rogue Capri`, version `1.0.0`, `AssemblyName` `RogueCapri`, window titles `Rogue Capri ...`, font URIs `avares://RogueCapri/Assets`, installer excluded from app compile (`installer/**`) |
| Portable data root | `cross/Pet.cs`, `cross/Pet.Companion.cs` | `Pet.DataRoot()` (env `RogueCapriHome`, `<install dir>\Data`, AppData fallback), `EnsureRogueLayout()` |
| Companion | `cross/CompanionWindow.cs`, `cross/CompanionCore.cs`, `cross/AiProviders.cs`, `cross/Pet.Companion.cs` | `rogue` personality, Rogue page/UI, memory, providers, tools |
| Rogue Mode | `cross/RogueMode.cs` | New file — never conflicts; if upstream ever adds a same-named file, rename upstream's |
| Installer | `cross/installer/`, `ci/build-rogue.ps1` | New files — take Rogue Capri's version |
| Tray menu & news | `cross/Pet.cs`, `app/Brain.cs` | `Open Capri Companion` menu item, Companion news line |

Resolution order: (1) keep the Rogue Capri version of the rows above,
(2) accept upstream text for logic you did not change, (3) re-apply any
intentionally removed Rogue line by hand, (4) `git add` the resolved file.

## 6. Commit and test the merged version

```powershell
git add -A
git commit -m "Merge upstream <version> into Rogue Capri 1.0"
dotnet build cross/Capri.Cross.csproj
```

Then run the smoke test:

```powershell
dotnet run --project cross/Capri.Cross.csproj -- --smoke "C:\Users\nikot\AppData\Local\Temp\rogue-smoke"
Get-Content "C:\Users\nikot\AppData\Local\Temp\rogue-smoke\smoke-test.txt"
```

Smoke result expectation: everything passes **except** the one pre-existing
environmental failure `FAIL A drag didn't follow the latest pointer while
her window lagged.` — this failure exists identically on the pristine backup
commit `41b3b44` in this environment (verified 2026-11-17) and is **not** a
regression. New failures other than that one must be investigated.

Then build the release and do a real install round-trip:

```powershell
powershell -ExecutionPolicy Bypass -File ci\build-rogue.ps1
Start-Process "E:\Rogue Capri\Release\Rogue-Capri-Setup.exe" -ArgumentList "/S"  # or -Wait
Start-Process "E:\Rogue Capri\RogueCapri.exe"                                  # launch check
```

Note: start GUI apps (Setup, RogueCapri) through
`Start-Process`/Explorer — direct execution from a Git Bash shell has been
observed to stall intermittently on single-file bundle startup.

## 7. Roll back a failed update

```powershell
git log --oneline -5          # find the last good commit
git reset --soft <good-sha>   # keep working tree, or:
git reset --hard <good-sha>   # discard (after backing up first!)
```

Or re-install the previous release: `Rogue-Capri-Setup.exe /S` over the
current install keeps `Data\` (settings + memory) untouched — only app
files are overwritten. For a full rollback including data, restore the
most recent backup (see `ci\backup-rogue.ps1`) into `<install dir>\Data\`.

## 8. Keep the backup commit

Never rewrite or delete `41b3b44` — it is the merge base that makes every
future upstream merge computable. New baselines are created by committing
merged states on `master`/`upstream-merge-*` branches.
