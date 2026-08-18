using System.Text.Json;

namespace CrazyBatto.SotfDeathCounter;

internal sealed class DeathCounterSettings
{
    public bool EnableObsOverlay { get; set; } = true;
    public int OverlayPort { get; set; } = 19447;
    public bool CountKnockdowns { get; set; } = false;
    public bool ShowOfflinePlayers { get; set; } = false;
    public bool UseLifetimeDeaths { get; set; } = false;

    /// <summary>
    /// Safe Mode deliberately disables the two operations most likely to destabilize
    /// an IL2CPP game: broad Harmony patching and scanning every MonoBehaviour.
    /// Existing settings files are migrated to Safe Mode automatically.
    /// </summary>
    public bool SafeMode { get; set; } = true;
    public int ScanIntervalMilliseconds { get; set; } = 2500;
    public int WorldScanIntervalMilliseconds { get; set; } = 15000;
    public bool EnableRuntimeHooks { get; set; } = false;
    public bool WriteDiscoveryDiagnostics { get; set; } = false;

    public static DeathCounterSettings LoadOrCreate(string filePath, Action<string> log)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            if (File.Exists(filePath))
            {
                var loaded = JsonSerializer.Deserialize<DeathCounterSettings>(
                    File.ReadAllText(filePath),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (loaded is not null)
                {
                    loaded.Normalize();
                    // Persist the migration so an old settings.json cannot re-enable
                    // unsafe scanning or Harmony hooks on the next game start.
                    loaded.Save(filePath, log);
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            log($"Einstellungen konnten nicht gelesen werden: {ex.Message}");
        }

        var settings = new DeathCounterSettings();
        settings.Normalize();
        settings.Save(filePath, log);
        return settings;
    }

    private void Normalize()
    {
        OverlayPort = Math.Clamp(OverlayPort, 1024, 65535);
        ScanIntervalMilliseconds = Math.Clamp(ScanIntervalMilliseconds, 500, 15000);
        WorldScanIntervalMilliseconds = Math.Clamp(WorldScanIntervalMilliseconds, 5000, 60000);

        if (SafeMode)
        {
            EnableRuntimeHooks = false;
            WriteDiscoveryDiagnostics = false;
            ScanIntervalMilliseconds = Math.Max(ScanIntervalMilliseconds, 2500);
            WorldScanIntervalMilliseconds = Math.Max(WorldScanIntervalMilliseconds, 15000);
        }
    }

    private void Save(string filePath, Action<string> log)
    {
        try
        {
            File.WriteAllText(
                filePath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            log($"Einstellungen konnten nicht gespeichert werden: {ex.Message}");
        }
    }
}
