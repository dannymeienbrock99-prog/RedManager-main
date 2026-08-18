namespace CrazyBatto.SotfDeathCounter.Core;

/// <summary>
/// Optional output such as a local API, OBS overlay or a publisher owned by the host project.
/// Outputs are never started automatically by the core.
/// </summary>
public interface IDeathCounterOutput : IDisposable
{
    bool IsRunning { get; }
    void Start(DeathCounterModule module);
    void Stop();
}
