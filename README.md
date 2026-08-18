# CrazyBatto RedManager – Sons of the Forest

Windows-Desktopmanager für **Sons of the Forest**, **RedLoader** und Mods von `sotf-mods.com`.
Das Projekt basiert auf dem offenen RedManager von Toni Macaroni und wurde für den aktuellen Mod-Datenbankaufbau, sichere Installation und ein eigenes Crazy_Batto-Werkzeug erweitert.

## Enthalten

- automatische Suche nach `SonsOfTheForest.exe` in der normalen und in zusätzlichen Steam-Bibliotheken
- manuelle, validierte Spielauswahl
- Installation, Aktualisierung und Entfernung von RedLoader
- Bereinigung alter BepInEx-/MelonLoader-Reste, ohne pauschal den gesamten `Mods`-Ordner zu löschen
- aktuelle `sotf-mods.com`-Liste mit Suche, Paging, Mod-/Library-Unterstützung und Abhängigkeiten
- Anzeige, Aktivierung, Deaktivierung, Aktualisierung und Entfernung lokal installierter Mods
- offizielles RedLoader-Projekttemplate für eigene Modprojekte
- eingebauter **CrazyBatto SOTF Death Counter** als lokal kompilierbare RedLoader-Mod

Das Universal-Heart-Rate-Modul gehört **nicht** in dieses Repository. Es bleibt im separaten Projekt `Batto-OBS-Tool`.

## Integrierter Multiplayer-Todeszähler

Der Tab **Crazy_Batto** enthält den Quellcode für eine eigenständige RedLoader-Mod. Beim Klick auf „bauen und installieren“ geschieht Folgendes:

1. Der Manager prüft, ob RedLoader bereits installiert und mindestens einmal gestartet wurde.
2. Die mitgelieferte Modquelle wird in einen temporären Buildordner entpackt.
3. `dotnet build` kompiliert die Mod gegen die DLLs der lokalen Spiel-/RedLoader-Version.
4. Die fertige DLL und das Manifest werden nach `Sons Of The Forest\Mods` kopiert.

Dadurch werden keine Endnight-, Unity-, Bolt- oder RedLoader-Binärdateien in diesem Repository verteilt.

Die Mod:

- erfasst Host und sichtbare Mitspieler automatisch,
- bevorzugt Steam-/Plattform-/Netzwerk-IDs gegenüber Namen,
- behält Rejoins und Namensänderungen korrekt zusammen,
- zählt bestätigte Todes-/Respawn-Übergänge mit Doppelzählungsschutz und wertet Mitspieler-Rettungen nicht als Tod,
- speichert Sitzungs- und Gesamtwerte,
- stellt ein lokales OBS-Overlay bereit.

OBS-Browserquelle:

```text
http://127.0.0.1:19447/overlay
```

Datenordner:

```text
%LOCALAPPDATA%\Crazy_Batto\SOTFDeathCounter
```

## Voraussetzungen für Anwender

- Windows 10 oder Windows 11
- Steam-Version von Sons of the Forest
- Microsoft WebView2 Runtime
- .NET 6 Runtime für RedLoader
- zusätzlich .NET 6 SDK, wenn der integrierte Todeszähler lokal gebaut werden soll

## Projekt selbst bauen

Benötigt werden:

- Node.js 20 oder neuer
- Rust Stable mit MSVC-Toolchain
- Visual Studio Build Tools mit „Desktopentwicklung mit C++“
- WebView2
- PowerShell 5.1 oder neuer

Dann:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Create-BundledModArchive.ps1
npm ci
npm run check
npm run tauri -- build
```

Unter Windows kann stattdessen `BUILD_WINDOWS.cmd` gestartet werden.
Die Installer landen anschließend unter:

```text
src-tauri\target\release\bundle
```

## Todeszähler ohne Manager bauen

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Build-DeathCounter.ps1 `
  -GameDirectory "C:\Program Files (x86)\Steam\steamapps\common\Sons Of The Forest"
```

Sons of the Forest muss dabei geschlossen sein. RedLoader muss bereits installiert und einmal gestartet worden sein.

## GitHub

Das Zielrepository ist:

```text
git@github.com:dannymeienbrock99-prog/RedManager-main.git
```

`PUSH_TO_GITHUB.cmd` initialisiert bei Bedarf Git, erstellt einen Commit und pusht den Branch `main`, ohne Force-Push.
Alternativ übernimmt `.github/workflows/build-windows.yml` nach jedem Push die Svelte-/TypeScript-Prüfung, Rust-Unit-Tests und den Windows-Build und lädt die erzeugten Bundles als Workflow-Artefakt hoch.

## Sicherheit

- ZIP-Dateien werden gegen absolute Pfade, `..`-Traversal, symbolische Links, NTFS-ADS und reservierte Windows-Dateinamen geprüft.
- Die Spielauswahl akzeptiert nur eine existierende `SonsOfTheForest.exe`.
- Der OBS-Server bindet ausschließlich an `127.0.0.1`.
- Statistiken und Einstellungen werden bei einer Mod-Deinstallation absichtlich nicht gelöscht.
- Das Repository enthält keine Spieldateien und keine proprietären DLLs.

## Wichtige Einschränkung

Sons of the Forest und RedLoader ändern sich durch Updates. Die automatische Erfassung nutzt deshalb mehrere Lobby-, Netzwerk- und Weltobjekt-Wege sowie kompatibilitätsorientierte Harmony-Hooks. Ein echter Multiplayer-Test muss immer mit der aktuell installierten Spielversion erfolgen. Die GitHub-Buildprüfung kann lediglich Manager, Frontend und Paketstruktur prüfen; sie besitzt keine lokalen Spieldateien.

## Lizenz und Herkunft

Der Manager basiert auf `ToniMacaroni/RedManager` und bleibt unter Apache License 2.0. Details stehen in [ATTRIBUTION.md](ATTRIBUTION.md). Der neu hinzugefügte Crazy_Batto-Code befindet sich im selben Repository unter derselben Lizenz, sofern in einem Unterordner nichts Abweichendes angegeben ist.
