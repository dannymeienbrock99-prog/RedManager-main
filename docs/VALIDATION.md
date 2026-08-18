# Validierungsprotokoll

Stand: **18. August 2026**  
Release: **CrazyBatto RedManager 1.2.0**  
Gebündelte Mod: **CrazyBatto SOTF Death Counter 0.3.1**

## In dieser Umgebung erfolgreich geprüft

- 18 zusammenhängende Release-Prüfgruppen ohne Befund
- TypeScript-Syntax von 16 `.ts`-Dateien
- TypeScript-Scriptblöcke und Strukturmarker von 11 Svelte-Dateien
- JavaScript-Syntax des OBS-Overlays mit `node --check`
- acht JSON-Dateien sowie Cargo-TOML, GitHub-Actions-YAML und das MSBuild-Projekt
- gleiche Manager-Version in `package.json`, `package-lock.json`, `Cargo.toml` und `tauri.conf.json`
- gleiche Mod-Version in Manifest, C#-Projekt, Rust-Konstante, README und Changelog
- identische npm-Abhängigkeiten zwischen `package.json` und dem Lockfile
- Tauri-Kommandos zwischen Frontend und Rust sowie alle benannten Shell-Scope-Einträge
- deaktiviertes `shell-all` und ausschließlich benannte Programmaufrufe
- lexikalisch ausgeglichene Klammern, Strings und Kommentare in 20 C#-Dateien und `main.rs`
- getrennte Behandlung von `DoRevive`: Mitspieler-Rettungen zählen nicht als Tod
- ausschließlich an `IPAddress.Loopback` gebundene Todeszähler-API
- keine Pulsoid-, Watch-, Wear-OS- oder Heart-Rate-Implementierung im Quellcode
- 16 PNG-/ICO-Ressourcen und das ICNS-Dateiformat
- keine DLL-, EXE-, PDB- oder sonstigen Spielbinärdateien im Projekt
- exakte Übereinstimmung aller 28 Dateien des eingebetteten Mod-Quellarchivs mit dem Quellordner
- fehlerfreie ZIP-Integrität und feste Archivzeitstempel

SHA-256 des eingebetteten Mod-Quellarchivs:

```text
e6a62f1a872ca53cd52a2d402aab16ba139b6e41ba345c6537cd99626df70003
```

## Im Windows-Workflow vorbereitet

Der Workflow `.github/workflows/build-windows.yml` führt nach dem Push aus:

1. Prüfung des eingebetteten Mod-Quellarchivs
2. `npm ci`
3. `npm run check`
4. `cargo test --manifest-path src-tauri/Cargo.toml --locked`
5. `npm run tauri -- build`
6. Upload der erzeugten Windows-Bundles

Die Rust-Unit-Tests prüfen insbesondere normale ZIP-Pfade sowie Traversal, absolute Pfade, NTFS-ADS, ungültige Zeichen und reservierte Windows-Gerätenamen.

## Nicht in dieser Linux-Arbeitsumgebung ausführbar

- `npm ci` und der vollständige Svelte-Build, weil die npm-Pakete nicht lokal gecacht sind und die Arbeitsumgebung keine direkte Paketnetzwerkverbindung besitzt
- Rust-/Tauri-Kompilierung und Rust-Unit-Tests, weil hier kein Rust-/MSVC-Toolchain installiert ist
- C#-Build des vollständigen Mods, weil hierfür sowohl das .NET-6-SDK als auch die versionsabhängigen DLLs einer lokalen Sons-of-the-Forest-/RedLoader-Installation erforderlich sind
- echter Host-/Client-Multiplayertest gegen die aktuell installierte Spielversion
- OBS-Browsertest gegen den tatsächlich laufenden In-Game-Server

Diese Punkte werden nicht als bestanden dargestellt. Der Windows-Workflow deckt Frontend und Rust nach dem Push ab; der vollständige Mod-Build und die Multiplayer-Funktionsprüfung erfolgen anschließend auf dem PC mit installiertem Spiel und RedLoader.
