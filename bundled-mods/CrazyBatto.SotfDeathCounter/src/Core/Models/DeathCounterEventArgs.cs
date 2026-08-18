namespace CrazyBatto.SotfDeathCounter.Core;

public sealed class PlayerDiscoveredEventArgs : EventArgs
{
    public PlayerDiscoveredEventArgs(PlayerStatistics player) => Player = player;
    public PlayerStatistics Player { get; }
}

public sealed class PlayerDeathCountedEventArgs : EventArgs
{
    public PlayerDeathCountedEventArgs(DeathCounterEvent death, PlayerStatistics player)
    {
        Death = death;
        Player = player;
    }

    public DeathCounterEvent Death { get; }
    public PlayerStatistics Player { get; }
}

public sealed class DeathCounterSnapshotChangedEventArgs : EventArgs
{
    public DeathCounterSnapshotChangedEventArgs(DeathCounterSnapshot snapshot) => Snapshot = snapshot;
    public DeathCounterSnapshot Snapshot { get; }
}
