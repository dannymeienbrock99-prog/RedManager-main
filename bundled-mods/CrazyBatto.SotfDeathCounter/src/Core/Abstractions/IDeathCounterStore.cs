namespace CrazyBatto.SotfDeathCounter.Core;

public interface IDeathCounterStore
{
    IReadOnlyCollection<PersistedPlayerStatistics> Load();
    void Save(IReadOnlyCollection<PersistedPlayerStatistics> players);
}
