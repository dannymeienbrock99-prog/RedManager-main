using System.Text.Json;

namespace CrazyBatto.SotfDeathCounter.Core;

public static class DeathCounterJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static string SerializeSnapshot(DeathCounterSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return JsonSerializer.Serialize(snapshot, Options);
    }

    public static DeathCounterSnapshot DeserializeSnapshot(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("Snapshot JSON is empty.", nameof(json));
        }

        return JsonSerializer.Deserialize<DeathCounterSnapshot>(json, Options)
               ?? throw new InvalidDataException("Snapshot JSON could not be deserialized.");
    }
}
