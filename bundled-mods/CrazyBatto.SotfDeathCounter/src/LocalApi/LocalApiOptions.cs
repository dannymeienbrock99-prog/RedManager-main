namespace CrazyBatto.SotfDeathCounter.LocalApi;

public sealed class LocalApiOptions
{
    public int Port { get; set; } = 19447;
    public bool EnableObsOverlay { get; set; } = true;

    internal LocalApiOptions CloneNormalized() => new()
    {
        Port = Math.Clamp(Port, 1024, 65535),
        EnableObsOverlay = EnableObsOverlay
    };
}
