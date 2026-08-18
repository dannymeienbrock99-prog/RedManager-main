using CrazyBatto.SotfDeathCounter.Core;
using CrazyBatto.SotfDeathCounter.LocalApi;
using CrazyBatto.SotfDeathCounter.RedLoader;
using SonsSdk;

namespace CrazyBatto.SotfDeathCounter;

/// <summary>
/// RedLoader entry point for the bundled Sons of the Forest death counter.
/// The host automatically discovers all visible multiplayer participants and
/// exposes the current statistics through a loopback-only OBS browser overlay.
/// </summary>
public sealed class CrazyBattoDeathCounterMod : SonsMod, IOnInWorldUpdateReceiver
{
    private DeathCounterModule? _counter;
    private SotfDeathCounterAdapter? _adapter;
    private LocalApiOutput? _localApi;
    private string _dataDirectory = string.Empty;

    public CrazyBattoDeathCounterMod()
    {
        // RuntimeHookInstaller applies only its selected compatibility hooks.
        HarmonyPatchAll = false;
    }

    protected override void OnInitializeMod()
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
    }

    protected override void OnSdkInitialized()
    {
        _adapter?.Start();

        if (_counter is not null && _localApi is not null)
        {
            try
            {
                _localApi.Start(_counter);
            }
            catch (Exception ex)
            {
                WriteLog($"OBS-Overlay konnte nicht gestartet werden: {ex.Message}");
            }
        }
    }

    protected override void OnGameStart()
    {
        _adapter?.BeginSession();
        WriteLog("Neue Spiel-Sitzung gestartet; Host und Mitspieler werden automatisch erfasst.");
    }

    protected override void OnSonsSceneInitialized(ESonsScene sonsScene)
    {
        if (sonsScene == ESonsScene.Title)
        {
            _adapter?.MarkAllOffline();
        }
    }

    public void OnInWorldUpdate() => _adapter?.Tick();

    private void WriteLog(string message) => Log(message);
}
