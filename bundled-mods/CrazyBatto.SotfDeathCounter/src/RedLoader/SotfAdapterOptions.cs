namespace CrazyBatto.SotfDeathCounter.RedLoader;

public sealed class SotfAdapterOptions
{
    public int ScanIntervalMilliseconds { get; set; } = 1000;
    public int WorldScanIntervalMilliseconds { get; set; } = 2500;
    public bool EnableRuntimeHooks { get; set; } = true;
    public bool WriteDiscoveryDiagnostics { get; set; } = true;

    internal SotfAdapterOptions CloneNormalized() => new()
    {
        ScanIntervalMilliseconds = Math.Clamp(ScanIntervalMilliseconds, 250, 10000),
        WorldScanIntervalMilliseconds = Math.Clamp(WorldScanIntervalMilliseconds, 1000, 15000),
        EnableRuntimeHooks = EnableRuntimeHooks,
        WriteDiscoveryDiagnostics = WriteDiscoveryDiagnostics
    };
}
