# BUGFIX_REPORT 1.0.2 — Rogue Capri

Autor: Muse Ember (DeepAI Code). Stand: 2026-11-17.
Branch: `bugfix/pet-1.0.2` (Basis `4fda442` = v1.0.1), Commit `56b0868` + Doku.
Tags `v1.0.0` (`7c42ce7`) / `v1.0.1` (`4fda442`) und deren Artefakte bleiben unverändert.

## 1. Always-On-Top: Verhalten, Ursache, Fix

**Verhalten (live vermessen, PID 35968/5588/33684/36648/21912):** Das sichtbare
Capri-Fenster trug zwar `WS_EX_TOPMOST` (Ex=200008) und blieb bei Maximieren +
Tastatureingabe (Foreground stets Brave) vorn. Das Problem aus der realen Nutzung
bleibt jedoch: **Avalonia erzeugt sein natives Fenster teils neu** (der Platform-
Handle zeigte auf ein unsichtbares Helferfenster; das sichtbare Fenster hat eine
andere HWND). Jede Neugenerierung verwirft das gesetzte Bit — die bisherige
Stabilisierung lief danach noch 13 s und war dann fertig. Spätere Neuerstellungen
ließen Capri wieder hinter Browserfenstern verschwinden, ohne dass der Code es
bemerkte.

**Fix** (`cross/Look.cs`, `cross/Pet.cs`): Neben dem einmaligen Setzen nach dem
Öffnen läuft jetzt ein **billiger Guard im bestehenden Frame-Loop (1 s Intervall)**:
`OnTopHeld` prüft das Bit am sichtbaren Pet-Fenster (`PetWindowHandle` enumeriert
die aktuellen Prozess-Fenster nach Position/Größe), `PetFrameTop` setzt es bei
Verlust wieder. Kein Polling-Massiv, keine Aktivierung, kein Umzug — nur Z-Order.
Der Setup/die Companion-Fenster bleiben unberührt. Fokustest: Browser blieb
Foreground (nie Capri), `SendKeys("x")` im Browser ohne Fokuseinbruch.

**Verifikation:** `LayerCheck`-Dumps vor/nach Maximieren + Keystroke: Fenster
`vis=True top=True` in allen Stufen, Foreground stets Browser.

## 2. Desktop-Icon-Layer (Progman/WorkerW/SHELLDLL_DefView/SysListView32)

**Verifikation auf echtem Desktop** (`IconCheck`): 140/140 Punkte über Capris
Fenster liefern Capri als oberstes Fenster (Capri malt über der Icon-Schicht,
`TOPMOST`); 908/908 Punkte außerhalb von Capri liefern den Desktop-Layer —
Desktop-Icons bleiben dort anklickbar/nutzbar. Keine separate Taskbar- oder
Monitor-Logik nötig: Topmost + Working-Area-Sitzposition (Hüfte auf der
Arbeitsbereichskante) decken Desktop und Taskbar wie im Original ab.

## 3. Drag-and-Throw: Ursache gefunden — doppelt angewendeter Größenfaktor

**Symptome (live reproduziert, reale Maus-Events):** Drag folgt, Release mit
Flug, nach +25 ms Sprung auf x=-1424, danach -39 (linke Fensterkante) — exakt der
gemeldete „bottom-left“-Teleport.

**Diagnose (eingebaute Flug-Logs, danach entfernt):**
```
Launch now=8,238922 vx=3150 vy=750 anchor=1675,1361 area=0,0,2560,1392
i=0 flyX=1684,99 flyY=1363,4 flyVX=3144 flyVY=756 left=-2688 right=-329   ← FALSCH
```
`left`/`right` sollten -11 / 2348 sein. Drei Faktoren: `Px(body.Size * FrameX)`,
wobei `FrameX` bereits `body.Size * 0.08 - 8 = 11.2` **enthält**.
`240 × 11.2 = 2688` → Wand-Koordination nach links verschoben, die erste
Reflexion (`right - (flyX-right)*0.55`) warf X auf ≈ -1436; Settle landete dort auf
dem Boden → sichtbar als Teleport nach links. **Fixed:** `Px(FrameX)` / `Px(FrameY)`
(`cross/Pet.cs`).

**Verhalten nach Fix (reale Fenster, ausführbare Tests):**
- rechts: Release (1586,1339) → 1710…2189 (Wurf ~600 px, Arc bis 1163), Landung
- links: Release (1819,1339) → 1229, Landung
- oben-rechts: Arc bis Y=310, Wand-Bounce bei 2348, Landung/Sitzen
- langsam: bleibt Drop ohne Wurf; erneut Greifen im Flug storniert den Flug

**Smoke-Test verschärft** (`cross/Smoke.cs`): zusätzlich zur Flugprüfung wird
versichert, dass die Position während des Flugs die Arbeitsbereichsgrenzen (mit
Toleranz) nie verlässt — diese Klasse von Fehlern wird künftig gefangen.
Ergebnis: `PASS throw: a fast release flies her at the cursor's speed, she travels, then lands and sits` +
`PASS slow release still drops without a throw`.

## Tests dieser Version
- Fensterseite: TOPMOST über maximiertem Browser + Keystroke, Fokus blieb Browser ✓
- Desktop: Icon-Layer 140/140 über Capri, 908/908 Desktop außerhalb ✓
- Throw-Matrix links/rechts/oben-diagnal/Wand/Landung/Greifen ✓
- RogueAudit (Tools/Levels/Memory/Provider/Settings): 35/35 PASS ✓
- Smoke: Throw-Checks PASS (Umgebungs-Drag-Lag-FAIL unverändert, identisch auf
  unverändertem v1.0.0-Code verifiziert) ✓
- Installer: Frisch-Install `E:\Rogue Capri\TestInstall102` + Uninstall
  (Daten bleibt erhalten, Exe/Register/Shortcut entfernt) ✓ — siehe unten
- ZIP enthält nur Programmdateien; DLL-Hash == Release-Staging ✓

## Limitationen (ehrlich)
- Vollbild-Spiele mit eigenem Topmost (exclusives Vollbild) können Capri zeitweise
  überdecken; der Guard stellt das Bit danach wieder her, unterscheidet aber nicht
  zwischen „verlorenem Bit“ und „bewusst dahinter“.
- Sourcetest für die Nicht-Existenz von Vollbild-Spielen war in dieser Umgebung
  nicht möglich (kein Vollbild-Spiel gestartet).
- Keine Multi-Monitor-/DPI-Varianten vermessen (nur 2560x1440, ein Monitor),
  Arbeitsbereichs- und DPI-Logik folgt aber exakt den Koordinaten des Systems.
- Die Smoke-Umgebung reproduziert den bekannten Drag-Lag-Fall auch auf v1.0.0
  (vorbestehend, nicht durch diese Änderung).

## Release
- `E:\Rogue-Capri-Release-1.0.2\Rogue-Capri-Setup.exe`, `RogueCapri-1.0.2-win-x64.zip`
- `SHA256SUMS.txt` aus den gesicherten Kopien, re-verifiziert
- Commit `56b0868` + Release-Doku, Tag `v1.0.2`
