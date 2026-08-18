namespace CrazyBatto.SotfDeathCounter.Core;

public enum PlayerLifecycleState
{
    Unknown = 0,
    Alive = 1,
    Downed = 2,
    Dead = 3,
    Respawning = 4
}

public enum PlayerSignalKind
{
    Downed = 0,
    Death = 1,
    Respawn = 2,
    Revived = 3
}

public sealed class PlayerStatistics
{
    public int Rank { get; set; }
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int SessionDeaths { get; set; }
    public int LifetimeDeaths { get; set; }
    public bool Online { get; set; }
    public string State { get; set; } = "unknown";
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public DateTime? LastDeathUtc { get; set; }
    public string LastSource { get; set; } = string.Empty;
}

public sealed class DeathCounterEvent
{
    public long Sequence { get; set; }
    public string Type { get; set; } = "death";
    public string PlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public int SessionDeaths { get; set; }
    public int LifetimeDeaths { get; set; }
    public DateTime AtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class DeathCounterSnapshot
{
    public int Version { get; set; } = 1;
    public string Title { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public DateTime GeneratedAtUtc { get; set; }
    public int OnlinePlayers { get; set; }
    public int KnownPlayers { get; set; }
    public bool ShowOfflinePlayers { get; set; }
    public bool ShowLifetimeDeaths { get; set; }
    public DeathCounterEvent? LastEvent { get; set; }
    public List<PlayerStatistics> Players { get; set; } = new();
}

public sealed class PersistedPlayerStatistics
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int LifetimeDeaths { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public DateTime? LastDeathUtc { get; set; }
}

internal sealed class PlayerRuntimeRecord
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int LifetimeDeaths { get; set; }
    public int SessionDeaths { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public DateTime? LastDeathUtc { get; set; }
    public DateTime? LastDownedUtc { get; set; }
    public int? LastRootInstanceId { get; set; }
    public bool Online { get; set; }
    public bool HasSeenAlive { get; set; }
    public bool DeathLatched { get; set; }
    public PlayerLifecycleState State { get; set; } = PlayerLifecycleState.Unknown;
    public string LastSource { get; set; } = string.Empty;
}
