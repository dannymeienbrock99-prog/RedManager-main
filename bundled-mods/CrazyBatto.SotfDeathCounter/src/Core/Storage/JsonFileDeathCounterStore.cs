using System.Text.Json;

namespace CrazyBatto.SotfDeathCounter.Core;

public sealed class JsonFileDeathCounterStore : IDeathCounterStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true
    };

    private readonly string _path;
    private readonly Action<string> _log;

    public JsonFileDeathCounterStore(string path, Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A storage path is required.", nameof(path));
        }

        _path = System.IO.Path.GetFullPath(path);
        _log = log ?? (_ => { });
    }

    public string Path => _path;

    public IReadOnlyCollection<PersistedPlayerStatistics> Load()
    {
        if (!File.Exists(_path))
        {
            return Array.Empty<PersistedPlayerStatistics>();
        }

        try
        {
            var file = JsonSerializer.Deserialize<PersistedStatsFile>(File.ReadAllText(_path), JsonOptions);
            return file?.Players?
                .Where(player => !string.IsNullOrWhiteSpace(player.Id))
                .Select(player => new PersistedPlayerStatistics
                {
                    Id = player.Id.Trim(),
                    DisplayName = string.IsNullOrWhiteSpace(player.DisplayName) ? "Spieler" : player.DisplayName.Trim(),
                    LifetimeDeaths = Math.Max(0, player.LifetimeDeaths),
                    FirstSeenUtc = player.FirstSeenUtc == default ? DateTime.UtcNow : player.FirstSeenUtc,
                    LastSeenUtc = player.LastSeenUtc,
                    LastDeathUtc = player.LastDeathUtc
                })
                .ToList() ?? new List<PersistedPlayerStatistics>();
        }
        catch (Exception ex)
        {
            _log($"Todeszähler-Daten konnten nicht geladen werden: {ex.Message}");
            return Array.Empty<PersistedPlayerStatistics>();
        }
    }

    public void Save(IReadOnlyCollection<PersistedPlayerStatistics> players)
    {
        ArgumentNullException.ThrowIfNull(players);

        try
        {
            var directory = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var file = new PersistedStatsFile
            {
                Version = 1,
                UpdatedAtUtc = DateTime.UtcNow,
                Players = players
                    .OrderBy(player => player.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .Select(player => new PersistedPlayerStatistics
                    {
                        Id = player.Id,
                        DisplayName = player.DisplayName,
                        LifetimeDeaths = Math.Max(0, player.LifetimeDeaths),
                        FirstSeenUtc = player.FirstSeenUtc,
                        LastSeenUtc = player.LastSeenUtc,
                        LastDeathUtc = player.LastDeathUtc
                    })
                    .ToList()
            };

            var temporaryPath = _path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(file, JsonOptions));
            File.Move(temporaryPath, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            _log($"Todeszähler-Daten konnten nicht gespeichert werden: {ex.Message}");
        }
    }

    private sealed class PersistedStatsFile
    {
        public int Version { get; set; } = 1;
        public DateTime UpdatedAtUtc { get; set; }
        public List<PersistedPlayerStatistics> Players { get; set; } = new();
    }
}
