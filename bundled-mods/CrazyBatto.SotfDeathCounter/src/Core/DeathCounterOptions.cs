namespace CrazyBatto.SotfDeathCounter.Core;

/// <summary>
/// Behaviour of the reusable death-counter engine. It has no dependency on
/// Sons of the Forest, RedLoader, Unity or OBS.
/// </summary>
public sealed class DeathCounterOptions
{
    public string Title { get; set; } = "SONS OF THE FOREST – TODESZÄHLER";
    public int OfflineAfterSeconds { get; set; } = 5;
    public int MaxPlayersInSnapshot { get; set; } = 64;
    public int DeathLatchResetMilliseconds { get; set; } = 1500;
    public bool CountKnockdowns { get; set; }
    public bool ShowOfflinePlayersByDefault { get; set; }
    public bool UseLifetimeDeathsByDefault { get; set; }

    internal DeathCounterOptions CloneNormalized()
    {
        return new DeathCounterOptions
        {
            Title = string.IsNullOrWhiteSpace(Title)
                ? "SONS OF THE FOREST – TODESZÄHLER"
                : Title.Trim(),
            OfflineAfterSeconds = Math.Clamp(OfflineAfterSeconds, 2, 120),
            MaxPlayersInSnapshot = Math.Clamp(MaxPlayersInSnapshot, 1, 128),
            DeathLatchResetMilliseconds = Math.Clamp(DeathLatchResetMilliseconds, 500, 15000),
            CountKnockdowns = CountKnockdowns,
            ShowOfflinePlayersByDefault = ShowOfflinePlayersByDefault,
            UseLifetimeDeathsByDefault = UseLifetimeDeathsByDefault
        };
    }
}
