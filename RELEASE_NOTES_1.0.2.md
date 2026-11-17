# Rogue Capri 1.0.2 — Release Notes (Bugfix-Release)

Stand: 2026-11-17. Branch: `bugfix/pet-1.0.2`, Basis `4fda442` (v1.0.1).
Tag: `v1.0.2`. Tags `v1.0.0`/`v1.0.1` und deren Artefakte bleiben unverändert.

## Fixes

### 1. Always-On-Top verlässlich auf Windows
Die native Fensterhürde (WS_EX_TOPMOST-Bit) wurde nicht nur einmal gesetzt, sondern
**überprüft** und **bei Verlust wiederhergestellt** (1 s Intervall über den bestehenden
Frame-Loop, keine aggressive Dauer-Polling-Schleife). Ursache für den bisherigen
Aussetzer: Avalonia erzeugt sein natives Fenster teils neu, wodurch das Bit später
verlorengeht — die bisherige 13-s-Stabilisierung deckte das nicht ab. Z-Order only:
keine Aktivierung, keine Fokus-Störung — Tippen im Browser funktioniert weiter.

### 2. Desktop-Icon-Layer
Geprüft auf echtem Desktop: Über Capris Fenster ist Capri das oberste Fenster
(140/140 Proben), alle übrigen Punkte (908/908) liefern den Desktop-Icon-Layer
(Klicks dort funktionieren). Kein separates Taskbar-Layering-Problem; Topmost +
Working-Area-Sitzposition decken Desktop und Taskbar ab.

### 3. Drag-and-Throw teleportierte nicht mehr in die linke Ecke
**Ursache gefunden und behoben:** doppelt angewendeter Größenfaktor bei den
Wandgrenzen (`Px(body.Size * FrameX)` — FrameX enthält body.Size bereits). Die
falsche Wand-Koordination (left=-2688, right=-329 statt -11/2348) hat die Reflexion
sofort auf X≈-1436 geworfen; von dort setzte sie auf dem Boden links ab. Jetzt
`Px(FrameX)` / `Px(FrameY)`. Weiterhin: Release-Geschwindigkeit aus der Cursor-Spur,
Physik mit Wandsprung, Dämpfung, Landen, erneut Greifen im Flug, Langsam-Ablegen
bleibt ein Drop. Smoke-Test prüft jetzt zusätzlich die Arbeitsbereichsgrenzen.

## Keine Änderungen
Mainframe, Voice, Avatar, Brain, KI-Anbieter, Companion, KI-Chat, Memory, Plugins,
Backup/Restore, Rogue Mode (35/35 Audit), Installer-Verhalten (Daten bleiben bei
Update/Deinstallation erhalten), Icon.

## Dateien
- `RogueCapri-1.0.2-win-x64.zip` — portables Paket (nur Programmdateien)
- `Rogue-Capri-Setup.exe` — Installer (Default `E:\Rogue Capri`)
- `SHA256SUMS.txt` — Prüfsummen der gesicherten Kopien

## Tests (ausgeführt)
- Realer Desktop: Topmost-Bit über Browser-Maximierung + Keystroke, Foreground blieb Browser
- Desktop-Icon-Layer: 140/140 über Capri, 908/908 Desktop antwortet außerhalb
- Throw-Matrix auf echtem Fenster: rechts (1586→2189), links (1819→1229), oben-rechts (Arc bis Y=310), Wand-Bounce, Landung, erneutes Greifen
- Smoke: `PASS throw`, `PASS slow release` (vorbestehender umgebungsbedingter Drag-Lag-FAIL — identisch auf unverändertem v1.0.0-Code)
- RogueAudit: 35/35 PASS
- Installer: Frisch-Install + Uninstall (Daten bleiben) → siehe BUGFIX_REPORT_1.0.2.md
