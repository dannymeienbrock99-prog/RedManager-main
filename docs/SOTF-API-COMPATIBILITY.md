# SOTF-Mod-Datenbank-Kompatibilität

Stand der Anpassung: RedManager 1.2.0.

## Verwendete Endpunkte

```text
GET /api/mods
GET /api/mods/:mod_id
GET /api/mods/slug/:userSlug/:modSlug/download/:version
```

## Unterstützte Antwortvarianten

Der Client akzeptiert sowohl den aktuellen verschachtelten Aufbau als auch ältere flache Felder:

- `shortDescription` und `short_description`
- `category.name` und `category_name`
- `user.slug` und `user_slug`
- `latestVersion` und `latest_version`
- `versions[]` mit `isLatest` und `downloadUrl`

Damit kann eine vorhandene Installation auch dann angezeigt werden, wenn der Online-Eintrag vorübergehend fehlt oder sich die Listenansicht vom Detailendpunkt unterscheidet.

## Download

Der Manager konstruiert die aktuelle Slug-basierte Downloadroute aus Autor, Mod-Slug und Version. Das ZIP wird in den Spielordner entpackt, weil SOTF-Modpakete ihre Zielstruktur (`Mods`, `Libs` und gegebenenfalls weitere Ordner) selbst enthalten.

## Lokale Erkennung

Der Manager scannt:

```text
<Spielordner>\Mods\*.dll
<Spielordner>\Mods\*.disabled
<Spielordner>\Libs\*.dll
<Spielordner>\Libs\*.disabled
```

Zu jeder Assembly wird der gleichnamige Unterordner mit `manifest.json` gelesen. Die Mod-ID aus dem Manifest bleibt die stabile Zuordnung zur Online-Datenbank.
