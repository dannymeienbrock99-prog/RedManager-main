namespace CrazyBatto.SotfDeathCounter.RedLoader;

public sealed class SotfAdapterOptions
{
    public bool SafeMode { get; set; } = true;
    public int ScanIntervalMilliseconds { get; set; } = 2500;
    public int WorldScanIntervalMilliseconds { get; set; } = 15000;
    public bool EnableRuntimeHooks { get; set; } = false;
    public bool WriteDiscoveryDiagnostics { get; set; } = false;

    internal SotfAdapterOptions CloneNormalized()
    {
        var normalized = new SotfAdapterOptions
        {
            SafeMode = SafeMode,
            ScanIntervalMilliseconds = Math.Clamp(ScanIntervalMilliseconds, 500, 15000),
            WorldScanIntervalMilliseconds = Math.Clamp(WorldScanIntervalMilliseconds, 5000, 60000),
            EnableRuntimeHooks = EnableRuntimeHooks,
            WriteDiscoveryDiagnostics = WriteDiscoveryDiagnostics
        };

        if (normalized.SafeMode)
        {
            normalized.EnableRuntimeHooks = false;
            normalized.WriteDiscoveryDiagnostics = false;
            normalized.ScanIntervalMilliseconds = Math.Max(normalized.ScanIntervalMilliseconds, 2500);
            normalized.WorldScanIntervalMilliseconds = Math.Max(normalized.WorldScanIntervalMilliseconds, 15000);
        }

        return normalized;
    }
}
