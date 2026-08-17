# Crazy_Batto RedManager

Windows-Mod-Manager für **Sons of the Forest** und **RedLoader**.

Dieses Projekt baut auf dem Apache-2.0-lizenzierten RedManager von
[ToniMacaroni/RedManager](https://github.com/ToniMacaroni/RedManager) auf und
enthält Wartungs- und Stabilitätsanpassungen für die aktuelle
`sotf-mods.com`-Schnittstelle.

## Funktionen

- erkennt Sons of the Forest in der normalen und in zusätzlichen
  Steam-Bibliotheken;
- prüft ausdrücklich auf `SonsOfTheForest.exe` und Steam-App-ID `1326470`;
- installiert und aktualisiert RedLoader über das passende GitHub-Release-Asset;
- durchsucht Mods und Libraries über die öffentliche `sotf-mods.com`-API;
- installiert Mod-Abhängigkeiten mit Schleifenschutz;
- zeigt auch lokale Mods ohne Online-Eintrag oder gültiges `manifest.json` an;
- installiert private beziehungsweise selbst entwickelte Mod-ZIPs;
- aktiviert, deaktiviert und entfernt Einträge aus `Mods` und `Libs`;
- verwendet eine abgesicherte ZIP-Entpackung;
- erstellt über GitHub Actions einen Windows-NSIS-Installer.

## Abgrenzung

Das Universal-Heart-Rate-Modul gehört **nicht** in dieses Projekt. Es wird
separat im Projekt `dannymeienbrock99-prog/Batto-OBS-Tool` gepflegt.

## Windows-Build

Voraussetzungen:

- Node.js 20
- Rust stable mit MSVC-Toolchain
- Microsoft C++ Build Tools
- WebView2

```powershell
npm ci
npm run check
npm run tauri -- build --bundles nsis
```

Der Installer wird anschließend unter
`src-tauri/target/release/bundle/nsis/` erzeugt.

Alternativ startet jeder Push auf `main` den Workflow **Windows Build**. Das
Ergebnis liegt im Workflow unter **Artifacts** als
`Crazy_Batto-RedManager-Windows`.

## Nutzung

1. RedManager starten.
2. Den automatisch gefundenen Spielpfad prüfen oder
   `SonsOfTheForest.exe` manuell auswählen.
3. RedLoader installieren beziehungsweise aktualisieren.
4. Unter **Mods** Online-Mods oder ein lokales ZIP installieren.
5. Sons of the Forest über die Startseite öffnen.

Beim Installieren eines lokalen ZIPs wird ausschließlich in ein bestätigtes
Sons-of-the-Forest-Verzeichnis entpackt. Archive mit Pfad-Ausbruch,
symbolischen Links, ungewöhnlich vielen Dateien oder übergroßem Inhalt werden
abgelehnt.

## Hinweise

- Ein selbst gebauter, nicht digital signierter Windows-Installer kann von
  SmartScreen zunächst als unbekannt angezeigt werden. Der Quellcode und der
  reproduzierbare Build-Workflow liegen vollständig in diesem Repository.
- Die Mod-Kompatibilität hängt von der jeweils installierten Spiel- und
  RedLoader-Version ab.
- Vor größeren Änderungen am Mod-Ordner ist ein Backup sinnvoll.

## Lizenz

Apache License 2.0. Siehe [LICENSE](LICENSE) und [NOTICE.md](NOTICE.md).
