using System.Text.Json;

namespace CrazyBatto.SotfDeathCounter;

internal sealed class DeathCounterSettings
{
    public bool EnableObsOverlay { get; set; } = true;
    public int OverlayPort { get; set; } = 19447;
    public bool CountKnockdowns { get; set; } = false;
    public bool ShowOfflinePlayers { get; set; } = false;
    public bool UseLifetimeDeaths { get; set; } = false;
    public int ScanIntervalMilliseconds { get; set; } = 1000;
    public int WorldScanIntervalMilliseconds { get; set; } = 2500;
    public bool EnableRuntimeHooks { get; set; } = true;
    public bool WriteDiscoveryDiagnostics { get; set; } = true;

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
        ScanIntervalMilliseconds = Math.Clamp(ScanIntervalMilliseconds, 250, 10000);
        WorldScanIntervalMilliseconds = Math.Clamp(WorldScanIntervalMilliseconds, 1000, 15000);
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
            log($"Standardeinstellungen konnten nicht gespeichert werden: {ex.Message}");
        }
    }
}
