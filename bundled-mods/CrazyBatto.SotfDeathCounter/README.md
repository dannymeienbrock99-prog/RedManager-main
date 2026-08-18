# CrazyBatto SOTF Death Counter 0.3.1

Gebündelte RedLoader-Mod für **Sons of the Forest**. Die Mod erfasst Host und Mitspieler automatisch und ordnet sie bevorzugt über Steam-, Plattform-, Account- oder Netzwerk-IDs zu. Namen werden nur als Rückfall verwendet.

## OBS

Nach dem Spielstart:

```text
http://127.0.0.1:19447/overlay
```

Empfohlene OBS-Browserquelle: `600 × 800` Pixel.

## Daten und Einstellungen

```text
%LOCALAPPDATA%\Crazy_Batto\SOTFDeathCounter\settings.json
%LOCALAPPDATA%\Crazy_Batto\SOTFDeathCounter\stats.json
%LOCALAPPDATA%\Crazy_Batto\SOTFDeathCounter\last-discovery.json
```

Die Mod zählt standardmäßig erst einen bestätigten Tod beziehungsweise Respawn und nicht jedes Niederschlagen. Wird ein niedergeschlagener Spieler von einem Mitspieler wiederbelebt (`DoRevive`), wird dieser Übergang ausdrücklich **nicht** als Tod gewertet. `CountKnockdowns` kann in `settings.json` aktiviert werden.

## Build

RedLoader muss installiert und einmal gestartet worden sein. Danach:

```powershell
$env:SOTF_GAME_DIR = "C:\Program Files (x86)\Steam\steamapps\common\Sons Of The Forest"
dotnet build .\CrazyBatto.SotfDeathCounter.csproj -c Release
```

Der CrazyBatto RedManager erledigt diesen Build und die Installation über den Tab **Crazy_Batto** automatisch.

## Hinweis

Die automatische Erkennung verwendet mehrere kompatible Laufzeitwege, weil interne Spielmethoden nach Spielupdates umbenannt werden können. Diagnoseinformationen werden deshalb getrennt gespeichert und können bei Anpassungen helfen.
