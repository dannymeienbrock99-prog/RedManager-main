namespace CrazyBatto.SotfDeathCounter.Core;

public sealed class InMemoryDeathCounterStore : IDeathCounterStore
{
    private readonly object _sync = new();
    private List<PersistedPlayerStatistics> _players = new();

    public IReadOnlyCollection<PersistedPlayerStatistics> Load()
    {
        lock (_sync)
        {
            return _players.Select(Clone).ToList();
        }
    }

    public void Save(IReadOnlyCollection<PersistedPlayerStatistics> players)
    {
        ArgumentNullException.ThrowIfNull(players);
        lock (_sync)
        {
            _players = players.Select(Clone).ToList();
        }
    }

    private static PersistedPlayerStatistics Clone(PersistedPlayerStatistics player) => new()
    {
        Id = player.Id,
        DisplayName = player.DisplayName,
        LifetimeDeaths = player.LifetimeDeaths,
        FirstSeenUtc = player.FirstSeenUtc,
        LastSeenUtc = player.LastSeenUtc,
        LastDeathUtc = player.LastDeathUtc
    };
}
