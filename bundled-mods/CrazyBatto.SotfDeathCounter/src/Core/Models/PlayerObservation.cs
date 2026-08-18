namespace CrazyBatto.SotfDeathCounter.Core;

/// <summary>
/// A game adapter submits observations to the core. StableId should preferably
/// be a Steam/platform/account id. Name-only identities remain supported as a fallback.
/// </summary>
public sealed class PlayerObservation
{
    public string StableId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public int Confidence { get; set; }
    public int? RootInstanceId { get; set; }
    public DateTime SeenAtUtc { get; set; }
    public PlayerLifecycleState State { get; set; } = PlayerLifecycleState.Unknown;

    public bool HasUsableIdentity =>
        !string.IsNullOrWhiteSpace(StableId) || !string.IsNullOrWhiteSpace(DisplayName);

    public PlayerObservation Clone() => new()
    {
        StableId = StableId,
        DisplayName = DisplayName,
        Source = Source,
        Confidence = Confidence,
        RootInstanceId = RootInstanceId,
        SeenAtUtc = SeenAtUtc,
        State = State
    };

    public void MergeFrom(PlayerObservation other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (other.Confidence > Confidence)
        {
            Confidence = other.Confidence;
            Source = other.Source;
        }

        if (IsFallbackId(StableId) && !string.IsNullOrWhiteSpace(other.StableId) && !IsFallbackId(other.StableId))
        {
            StableId = other.StableId;
        }
        else if (string.IsNullOrWhiteSpace(StableId))
        {
            StableId = other.StableId;
        }

        if (IsGenericName(DisplayName) && !IsGenericName(other.DisplayName))
        {
            DisplayName = other.DisplayName;
        }
        else if (string.IsNullOrWhiteSpace(DisplayName))
        {
            DisplayName = other.DisplayName;
        }

        RootInstanceId ??= other.RootInstanceId;
        SeenAtUtc = other.SeenAtUtc > SeenAtUtc ? other.SeenAtUtc : SeenAtUtc;

        if (StatePriority(other.State) > StatePriority(State))
        {
            State = other.State;
        }
    }

    public static bool IsFallbackId(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.StartsWith("name:", StringComparison.OrdinalIgnoreCase);

    private static bool IsGenericName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "host" or "spieler" or "player" or "unknown" or "unbekannt" ||
               normalized.Contains("(clone)", StringComparison.Ordinal);
    }

    private static int StatePriority(PlayerLifecycleState state) => state switch
    {
        PlayerLifecycleState.Respawning => 5,
        PlayerLifecycleState.Dead => 4,
        PlayerLifecycleState.Downed => 3,
        PlayerLifecycleState.Alive => 2,
        _ => 1
    };
}
