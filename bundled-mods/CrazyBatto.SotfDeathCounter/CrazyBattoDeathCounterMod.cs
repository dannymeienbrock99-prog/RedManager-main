using CrazyBatto.SotfDeathCounter.Core;
using CrazyBatto.SotfDeathCounter.LocalApi;
using CrazyBatto.SotfDeathCounter.RedLoader;
using SonsSdk;
using SonsSdk.Attributes;

namespace CrazyBatto.SotfDeathCounter;

/// <summary>
/// Crash-resistant RedLoader entry point for the Sons of the Forest death counter.
/// Safe Mode is enabled by default and never performs broad Harmony patching or
/// a FindObjectsOfType scan across every MonoBehaviour in the game.
/// </summary>
public sealed class CrazyBattoDeathCounterMod : SonsMod, IOnInWorldUpdateReceiver
{
    private DeathCounterModule? _counter;
    private SotfDeathCounterAdapter? _adapter;
    private LocalApiOutput? _localApi;
    private string _dataDirectory = string.Empty;

    public CrazyBattoDeathCounterMod()
    {
        HarmonyPatchAll = false;
    }

    protected override void OnInitializeMod()
    {
        try
        {
            _dataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Crazy_Batto",
                "SOTFDeathCounter");
            Directory.CreateDirectory(_dataDirectory);

            var settingsPath = Path.Combine(_dataDirectory, "settings.json");
            var settings = DeathCounterSettings.LoadOrCreate(settingsPath, WriteLog);

            var store = new JsonFileDeathCounterStore(
                Path.Combine(_dataDirectory, "stats.json"),
                WriteLog);

            _counter = new DeathCounterModule(
                new DeathCounterOptions
                {
                    Title = "SONS OF THE FOREST – TODESZÄHLER",
                    CountKnockdowns = settings.CountKnockdowns,
                    ShowOfflinePlayersByDefault = settings.ShowOfflinePlayers,
                    UseLifetimeDeathsByDefault = settings.UseLifetimeDeaths
                },
                store,
                WriteLog);

            _counter.PlayerDiscovered += (_, args) =>
                WriteLog($"Mitspieler automatisch erkannt: {args.Player.Name}");
            _counter.PlayerDeathCounted += (_, args) =>
                WriteLog($"{args.Player.Name}: {args.Player.SessionDeaths} Tod/Tode in dieser Sitzung");

            _adapter = new SotfDeathCounterAdapter(
                _counter,
                _dataDirectory,
                new SotfAdapterOptions
                {
                    SafeMode = settings.SafeMode,
                    EnableRuntimeHooks = settings.EnableRuntimeHooks,
                    ScanIntervalMilliseconds = settings.ScanIntervalMilliseconds,
                    WorldScanIntervalMilliseconds = settings.WorldScanIntervalMilliseconds,
                    WriteDiscoveryDiagnostics = settings.WriteDiscoveryDiagnostics
                },
                WriteLog);

            if (settings.EnableObsOverlay)
            {
                _localApi = new LocalApiOutput(
                    new LocalApiOptions
                    {
                        Port = settings.OverlayPort,
                        EnableObsOverlay = true
                    },
                    WriteLog);
            }

            WriteLog($"Datenordner: {_dataDirectory}");
            WriteLog(settings.SafeMode
                ? "Safe Mode aktiv: Stabilität hat Vorrang; aggressive IL2CPP-Scans und Harmony-Hooks sind deaktiviert."
                : "Erweiterter Modus aktiv. Dieser Modus ist experimentell.");
        }
        catch (Exception ex)
        {
            _counter = null;
            _adapter = null;
            _localApi = null;
            WriteLog($"Death Counter wurde aus Sicherheitsgründen nicht gestartet: {ex.Message}");
        }
    }

    protected override void OnSdkInitialized()
    {
        try
        {
            _adapter?.Start();
        }
        catch (Exception ex)
        {
            WriteLog($"Spieleradapter wurde deaktiviert: {ex.Message}");
            _adapter = null;
        }

        if (_counter is not null && _localApi is not null)
        {
            try
            {
                _localApi.Start(_counter);
            }
            catch (Exception ex)
            {
                WriteLog($"OBS-Overlay konnte nicht gestartet werden: {ex.Message}");
                _localApi = null;
            }
        }
    }

    protected override void OnGameStart()
    {
        try
        {
            _adapter?.BeginSession();
            WriteLog("Neue Spiel-Sitzung gestartet; Host und Mitspieler werden im Safe Mode erfasst.");
        }
        catch (Exception ex)
        {
            WriteLog($"Sitzungserfassung wurde deaktiviert: {ex.Message}");
            _adapter = null;
        }
    }

    protected override void OnSonsSceneInitialized(ESonsScene sonsScene)
    {
        if (sonsScene == ESonsScene.Title)
        {
            try { _adapter?.MarkAllOffline(); }
            catch { /* The title scene must never be blocked by the overlay mod. */ }
        }
    }

    public void OnInWorldUpdate()
    {
        try { _adapter?.Tick(); }
        catch { /* Native game stability takes priority over counter updates. */ }
    }

    private void WriteLog(string message)
    {
        try { Log(message); }
        catch { /* Logging must never cause a game crash. */ }
    }
}
