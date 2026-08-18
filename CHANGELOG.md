# Changelog

## 1.2.0 – Crazy_Batto Edition

### Manager

- aktuelle `sotf-mods.com`-Datenstruktur und Downloadroute unterstützt
- alte und neue API-Feldnamen werden sicher normalisiert
- semantische Versionsvergleiche statt reiner Textvergleiche
- installierte Mods und Libraries werden getrennt in `Mods` und `Libs` erkannt
- Aktivieren/Deaktivieren berücksichtigt den tatsächlichen Installationsordner
- Abhängigkeiten werden mit Zyklenschutz installiert
- bestehende Installationen bleiben bei einem fehlgeschlagenen Netzwerkdownload unangetastet
- sichere ZIP-Entpackung gegen Path Traversal, symbolische Links, ADS-Pfade und reservierte Windows-Dateinamen
- Steam-Suche unterstützt zusätzliche Bibliotheken aus `libraryfolders.vdf`
- gültige `SonsOfTheForest.exe` wird vor jeder Aktion geprüft
- deutsche, überarbeitete Benutzeroberfläche und lokale Fallback-Bilder
- fehlendes Installer-Icon aus der alten Konfiguration entfernt

### Crazy_Batto Death Counter 0.3.1

- Wiederbelebungen durch Mitspieler werden getrennt von echten Respawns erkannt und nicht als Tod gezählt
- echter RedLoader-`SonsMod`-Einstiegspunkt
- automatische Host- und Mitspieler-Erfassung
- stabile Identitäten über Steam-/Plattform-/Netzwerk-ID mit Namensfallback
- Sitzungs- und Gesamtzähler mit JSON-Persistenz
- Lebenszyklus-, Todes-, Respawn- und Knockdown-Signale
- Doppelzählungsschutz und Rejoin-Verarbeitung
- lokales OBS-Browser-Overlay auf `127.0.0.1:19447`
- Build direkt gegen die lokale Spiel-/RedLoader-Version
- Installation und Deinstallation aus dem Manager

### Entwicklung

- Windows-GitHub-Actions-Workflow mit Rust-Sicherheitstests
- reproduzierbares Ressourcenarchiv für die gebündelte Modquelle
- lokale Build-, Mod-Build- und Push-Skripte
- Architektur-, API-Kompatibilitäts- und Validierungsdokumentation

## 1.1.x – Upstream RedManager

- Infinite Scroll statt vollständigem Vorabruf
- Suche über die SOTF-Mods-API mit Debounce
- Navigationssymbole und UI-Überarbeitung
- Korrekturen an Aktualisierungsanzeige und Mod-Covern
