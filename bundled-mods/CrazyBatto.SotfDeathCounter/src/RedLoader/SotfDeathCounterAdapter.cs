using CrazyBatto.SotfDeathCounter.Core;

namespace CrazyBatto.SotfDeathCounter.RedLoader;

/// <summary>
/// Thin game adapter intended to be owned by an existing RedLoader/SonsMod project.
/// Safe Mode is the default and avoids broad IL2CPP object scans and dynamic patches.
/// </summary>
public sealed class SotfDeathCounterAdapter : IDisposable
{
    private readonly DeathCounterModule _counter;
    private readonly SotfAdapterOptions _options;
    private readonly AutomaticPlayerDiscovery? _discovery;
    private readonly SafePlayerDiscovery? _safeDiscovery;
    private readonly RuntimeHookInstaller? _hooks;
    private readonly Action<string> _log;
    private DateTime _nextScanUtc = DateTime.MinValue;
    private bool _started;
    private bool _hooksInstalled;

    public SotfDeathCounterAdapter(
        DeathCounterModule counter,
        string diagnosticsDirectory,
        SotfAdapterOptions? options = null,
        Action<string>? log = null)
    {
        _counter = counter ?? throw new ArgumentNullException(nameof(counter));
        _options = (options ?? new SotfAdapterOptions()).CloneNormalized();
        _log = log ?? (_ => { });

        if (string.IsNullOrWhiteSpace(diagnosticsDirectory))
        {
            diagnosticsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Crazy_Batto",
                "SOTFDeathCounter");
        }

        Directory.CreateDirectory(diagnosticsDirectory);

        if (_options.SafeMode)
        {
            _safeDiscovery = new SafePlayerDiscovery(_log);
        }
        else
        {
            _discovery = new AutomaticPlayerDiscovery(
                _options,
                Path.Combine(diagnosticsDirectory, "last-discovery.json"),
                _log);

            if (_options.EnableRuntimeHooks)
            {
                _hooks = new RuntimeHookInstaller(_discovery, OnHookSignal, _log);
            }
        }
    }

    public DeathCounterModule Counter => _counter;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        if (_options.SafeMode)
        {
            _log("SOTF Death Counter Safe Mode aktiv: keine Harmony-Patches und kein Vollscan aller Spielobjekte.");
        }
        else
        {
            InstallHooksIfRequired();
        }
    }

    public void BeginSession(string? sessionId = null)
    {
        Start();
        _counter.StartNewSession(sessionId);
        _nextScanUtc = DateTime.MinValue;
    }

    /// <summary>
    /// Call from the existing mod's in-world update callback.
    /// </summary>
    public void Tick()
    {
        if (!_started)
        {
            Start();
        }

        var nowUtc = DateTime.UtcNow;
        if (nowUtc < _nextScanUtc)
        {
            return;
        }

        _nextScanUtc = nowUtc.AddMilliseconds(_options.ScanIntervalMilliseconds);
        try
        {
            IReadOnlyList<PlayerObservation> players;
            if (_safeDiscovery is not null)
            {
                players = _safeDiscovery.Scan(nowUtc);
            }
            else if (_discovery is not null)
            {
                players = _discovery.Scan(nowUtc);
            }
            else
            {
                players = Array.Empty<PlayerObservation>();
            }

            _counter.ApplyObservations(players, nowUtc);
        }
        catch (Exception ex)
        {
            _log($"Automatische Spielererfassung fehlgeschlagen: {ex.Message}");
            // Back off after an error instead of retrying every frame.
            _nextScanUtc = nowUtc.AddSeconds(15);
        }
    }

    public void ForceScan() => _nextScanUtc = DateTime.MinValue;

    public void MarkAllOffline() => _counter.MarkAllOffline();

    private void InstallHooksIfRequired()
    {
        if (_hooks is null || _hooksInstalled)
        {
            return;
        }

        try
        {
            _hooks.Install();
            _hooksInstalled = true;
        }
        catch (Exception ex)
        {
            _log($"Dynamische Todes-/Respawn-Hooks konnten nicht installiert werden: {ex.Message}");
        }
    }

    private void OnHookSignal(PlayerObservation player, PlayerSignalKind signal, DateTime nowUtc) =>
        _counter.ReportSignal(player, signal, nowUtc);

    public void Dispose()
    {
        _hooks?.Deactivate();
        _counter.MarkAllOffline();
    }
}
