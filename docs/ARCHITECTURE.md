# Architektur

## 1. Tauri-Manager

- `src/`: Svelte-/TypeScript-Oberfläche
- `src-tauri/`: Rust-Backend, Windows-Bundle und native Befehle
- `src/lib/mods.ts`: Adapter für Online-Mods und lokale Installationen
- `src/lib/featureInstaller.ts`: RedLoader-/Werkzeuginstallation
- `src/pages/BattoTools.svelte`: Build- und Installationsoberfläche für den Todeszähler

## 2. Gebündelte Modquelle

- `bundled-mods/CrazyBatto.SotfDeathCounter/`: vollständiger, quelloffener Modstand
- `src-tauri/resources/CrazyBatto.SotfDeathCounter-source.zip`: zur Laufzeit entpackte Kopie derselben Quelle
- `tools/Create-BundledModArchive.ps1`: erzeugt das Ressourcenarchiv reproduzierbar neu

Das Ressourcenarchiv enthält keine Buildausgaben und keine Abhängigkeiten aus der Spielinstallation.

## 3. Todeszähler

### Core

`src/Core` besitzt keine Unity- oder RedLoader-Abhängigkeit. Hier liegen:

- Spieleridentität und Zusammenführung
- Lebenszyklusautomat
- Sitzungs-/Gesamtstatistik
- Persistenzschnittstelle
- Snapshot- und Eventmodell

### RedLoader-Adapter

`src/RedLoader` verbindet den Core mit dem Spiel:

- automatische Lobby-/Netzwerk-/Weltobjekterfassung
- reflektive Kompatibilität über unterschiedliche Spielversionen
- optionale dynamische Harmony-Hooks
- Übergabe normalisierter Beobachtungen an den Core

### Local API

`src/LocalApi` stellt ausschließlich auf Loopback bereit:

- `GET /api/v1/snapshot`
- `GET /api/v1/health`
- `GET /overlay`
- CSS/JavaScript für die OBS-Browserquelle

## 4. Buildfluss

```text
Manager-Ressource ZIP
        │
        ▼
%TEMP%\CrazyBattoRedManager\CrazyBatto.SotfDeathCounter
        │
        ├── dotnet build -p:GameDir=<Spielordner>
        │
        ▼
Sons Of The Forest\Mods\CrazyBatto.SotfDeathCounter.dll
Sons Of The Forest\Mods\CrazyBatto.SotfDeathCounter\manifest.json
```

## 5. Bewusste Trennung

Das Universal-Heart-Rate-Modul gehört in `Batto-OBS-Tool`. In diesem Repository gibt es weder Watch-Adapter noch Pulsoid-, Wear-OS-, Apple-Watch- oder Herzfrequenzcode.
