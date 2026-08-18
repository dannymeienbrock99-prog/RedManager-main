using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using CrazyBatto.SotfDeathCounter.Core;

namespace CrazyBatto.SotfDeathCounter.RedLoader;

/// <summary>
/// Conservative multiplayer discovery for IL2CPP builds. The safe scanner never
/// enumerates every MonoBehaviour, invokes arbitrary properties/methods, or installs
/// Harmony patches. It only inspects fields on a small set of known manager types.
/// </summary>
internal sealed class SafePlayerDiscovery
{
    private static readonly string[] ManagerTypeTokens =
    {
        "CoopLobbyManager", "MultiplayerManager", "NetworkPlayerManager",
        "PlayerManager", "ClientConnectionManager", "LobbyManager", "BoltNetwork"
    };

    private static readonly HashSet<string> RosterFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Players", "PlayerList", "AllPlayers", "RemotePlayers", "ConnectedPlayers",
        "Clients", "ClientList", "Connections", "Members", "LobbyMembers", "Roster",
        "_players", "_playerList", "_clients", "_connections", "_members",
        "Items", "_items", "Values", "Entries"
    };

    private static readonly HashSet<string> InstanceFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Instance", "_instance", "Singleton", "Current", "ActiveInstance"
    };

    private static readonly HashSet<string> IdFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "SteamId", "SteamID", "SteamId64", "SteamID64", "PlatformId", "PlatformID",
        "AccountId", "AccountID", "MemberId", "MemberID", "UserId", "UserID",
        "OwnerId", "OwnerID", "PlayerId", "PlayerID", "NetworkId", "NetworkID",
        "ConnectionId", "ConnectionID", "ClientId", "ClientID", "EntityId", "EntityID",
        "Guid", "GUID", "Value", "RawValue", "m_SteamID", "m_SteamId"
    };

    private static readonly HashSet<string> NameFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "DisplayName", "PlayerName", "SteamName", "PersonaName", "Nickname", "NickName",
        "UserName", "Username", "MemberName", "LobbyName", "CharacterName",
        "_playerName", "_displayName", "_steamName", "Name", "name"
    };

    private static readonly HashSet<string> NestedFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Player", "PlayerData", "Profile", "Owner", "Connection", "ConnectionToken",
        "Token", "Identity", "User", "Member", "LobbyMember", "State", "NetworkState",
        "Entity", "Metadata", "Data", "Key", "Value"
    };

    private static readonly HashSet<string> DeadFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "IsDead", "Dead", "_isDead", "_dead", "IsPlayerDead", "IsKilled", "Killed",
        "IsInDeathState", "HasDied"
    };

    private static readonly HashSet<string> DownedFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "IsDowned", "Downed", "_isDowned", "IsIncapacitated", "Incapacitated",
        "IsKnockedDown", "KnockedDown", "IsDying", "RequiresRevive", "IsAwaitingRevive"
    };

    private static readonly HashSet<string> RespawnFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "IsRespawning", "Respawning", "_isRespawning", "RespawnPending", "IsRespawnPending",
        "WaitingForRespawn", "IsWaitingForRespawn", "IsCaptured", "Captured"
    };

    private static readonly HashSet<string> AliveFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "IsAlive", "Alive", "_isAlive", "HasControl", "IsSpawned"
    };

    private static readonly HashSet<string> HealthFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Health", "CurrentHealth", "HitPoints", "CurrentHitPoints", "_health",
        "_currentHealth", "Hp", "HP"
    };

    private readonly Action<string> _log;
    private readonly List<Type> _managerTypes = new();
    private DateTime _nextTypeRefreshUtc = DateTime.MinValue;
    private DateTime _nextEmptyLogUtc = DateTime.MinValue;

    public SafePlayerDiscovery(Action<string> log)
    {
        _log = log;
    }

    public IReadOnlyList<PlayerObservation> Scan(DateTime nowUtc)
    {
        RefreshManagerTypes(nowUtc);

        var candidates = new List<object>();
        var seenCandidates = new HashSet<object>(ReferenceComparer.Instance);

        foreach (var type in _managerTypes)
        {
            foreach (var source in ReadStaticSources(type))
            {
                CollectCandidates(source, candidates, seenCandidates, 0);
                if (candidates.Count >= 64)
                {
                    break;
                }
            }

            if (candidates.Count >= 64)
            {
                break;
            }
        }

        var observations = new List<PlayerObservation>();
        foreach (var candidate in candidates)
        {
            var observation = CreateObservation(candidate, nowUtc);
            if (observation?.HasUsableIdentity == true)
            {
                MergeObservation(observations, observation);
            }
        }

        if (observations.Count == 0 && nowUtc >= _nextEmptyLogUtc)
        {
            _nextEmptyLogUtc = nowUtc.AddMinutes(1);
            _log("Safe Mode aktiv: Noch keine kompatible Multiplayer-Spielerliste gefunden.");
        }

        return observations;
    }

    private void RefreshManagerTypes(DateTime nowUtc)
    {
        if (_managerTypes.Count > 0 && nowUtc < _nextTypeRefreshUtc)
        {
            return;
        }

        _nextTypeRefreshUtc = nowUtc.AddSeconds(30);
        var found = new List<Type>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var assemblyName = assembly.GetName().Name ?? string.Empty;
            if (!assemblyName.StartsWith("Sons", StringComparison.OrdinalIgnoreCase) &&
                !assemblyName.StartsWith("Endnight", StringComparison.OrdinalIgnoreCase) &&
                !assemblyName.Equals("bolt.user", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var type in SafeGetTypes(assembly))
            {
                var name = type.FullName ?? type.Name;
                if (ManagerTypeTokens.Any(token =>
                        name.EndsWith(token, StringComparison.OrdinalIgnoreCase) ||
                        name.Contains($".{token}", StringComparison.OrdinalIgnoreCase)))
                {
                    found.Add(type);
                }
            }
        }

        _managerTypes.Clear();
        _managerTypes.AddRange(found.Distinct());
    }

    private static IEnumerable<object> ReadStaticSources(Type type)
    {
        FieldInfo[] fields;
        try
        {
            fields = type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        }
        catch
        {
            yield break;
        }

        foreach (var field in fields)
        {
            if (!InstanceFieldNames.Contains(field.Name) && !RosterFieldNames.Contains(field.Name))
            {
                continue;
            }

            object? value;
            try
            {
                value = field.GetValue(null);
            }
            catch
            {
                continue;
            }

            if (value is not null)
            {
                yield return value;
            }
        }
    }

    private static void CollectCandidates(
        object value,
        ICollection<object> result,
        ISet<object> seen,
        int depth)
    {
        if (value is null || result.Count >= 64 || depth > 3 || !seen.Add(value))
        {
            return;
        }

        if (LooksLikePlayerValue(value))
        {
            result.Add(value);
        }

        foreach (var item in EnumerateSafe(value, 64 - result.Count))
        {
            CollectCandidates(item, result, seen, depth + 1);
            if (result.Count >= 64)
            {
                return;
            }
        }

        if (depth >= 2)
        {
            return;
        }

        foreach (var field in SafeGetFields(value.GetType(), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!RosterFieldNames.Contains(field.Name) &&
                !NestedFieldNames.Contains(field.Name) &&
                !InstanceFieldNames.Contains(field.Name))
            {
                continue;
            }

            object? nested;
            try
            {
                nested = field.GetValue(value);
            }
            catch
            {
                continue;
            }

            if (nested is not null && !IsScalar(nested))
            {
                CollectCandidates(nested, result, seen, depth + 1);
            }
        }
    }

    private static IEnumerable<object> EnumerateSafe(object value, int maximumItems)
    {
        if (maximumItems <= 0 || value is string)
        {
            yield break;
        }

        if (value is Array array)
        {
            var count = Math.Min(array.Length, maximumItems);
            for (var index = 0; index < count; index++)
            {
                object? item;
                try { item = array.GetValue(index); }
                catch { continue; }
                if (item is not null) yield return item;
            }
            yield break;
        }

        if (value is IDictionary dictionary)
        {
            var count = 0;
            IDictionaryEnumerator? enumerator = null;
            try
            {
                enumerator = dictionary.GetEnumerator();
                while (count < maximumItems && enumerator.MoveNext())
                {
                    count++;
                    if (enumerator.Key is { } key) yield return key;
                    if (enumerator.Value is { } item) yield return item;
                }
            }
            finally
            {
                (enumerator as IDisposable)?.Dispose();
            }
            yield break;
        }

        if (value is IList list)
        {
            int count;
            try { count = Math.Min(list.Count, maximumItems); }
            catch { yield break; }

            for (var index = 0; index < count; index++)
            {
                object? item;
                try { item = list[index]; }
                catch { continue; }
                if (item is not null) yield return item;
            }
        }
    }

    private static PlayerObservation? CreateObservation(object root, DateTime nowUtc)
    {
        var queue = new Queue<(object Value, int Depth)>();
        var seen = new HashSet<object>(ReferenceComparer.Instance);
        queue.Enqueue((root, 0));

        string stableId = string.Empty;
        string displayName = string.Empty;
        var dead = false;
        var downed = false;
        var respawning = false;
        var alive = false;
        double? health = null;

        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();
            if (!seen.Add(current))
            {
                continue;
            }

            foreach (var field in SafeGetFields(current.GetType(), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                object? fieldValue;
                try { fieldValue = field.GetValue(current); }
                catch { continue; }
                if (fieldValue is null) continue;

                if (string.IsNullOrEmpty(stableId) && IdFieldNames.Contains(field.Name) &&
                    TryReadIdentifier(field.Name, fieldValue, out var id))
                {
                    stableId = id;
                }

                if (string.IsNullOrEmpty(displayName) && NameFieldNames.Contains(field.Name) &&
                    fieldValue is string name && IsValidName(name))
                {
                    displayName = CleanName(name);
                }

                if (DeadFieldNames.Contains(field.Name) && TryReadBoolean(fieldValue, out var deadValue)) dead |= deadValue;
                if (DownedFieldNames.Contains(field.Name) && TryReadBoolean(fieldValue, out var downedValue)) downed |= downedValue;
                if (RespawnFieldNames.Contains(field.Name) && TryReadBoolean(fieldValue, out var respawnValue)) respawning |= respawnValue;
                if (AliveFieldNames.Contains(field.Name) && TryReadBoolean(fieldValue, out var aliveValue)) alive |= aliveValue;

                if (HealthFieldNames.Contains(field.Name) && TryReadDouble(fieldValue, out var healthValue))
                {
                    health = health.HasValue ? Math.Min(health.Value, healthValue) : healthValue;
                }

                if (depth < 2 && NestedFieldNames.Contains(field.Name) && !IsScalar(fieldValue))
                {
                    queue.Enqueue((fieldValue, depth + 1));
                }
            }
        }

        if (string.IsNullOrWhiteSpace(stableId) && string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        var state = PlayerLifecycleState.Unknown;
        if (respawning) state = PlayerLifecycleState.Respawning;
        else if (dead) state = PlayerLifecycleState.Dead;
        else if (downed) state = PlayerLifecycleState.Downed;
        else if (alive) state = PlayerLifecycleState.Alive;
        else if (health.HasValue) state = health.Value <= 0 ? PlayerLifecycleState.Downed : PlayerLifecycleState.Alive;

        return new PlayerObservation
        {
            StableId = stableId,
            DisplayName = displayName,
            Source = "safe-manager-fields",
            Confidence = 80,
            SeenAtUtc = nowUtc,
            State = state
        };
    }

    private static void MergeObservation(ICollection<PlayerObservation> observations, PlayerObservation incoming)
    {
        var existing = observations.FirstOrDefault(item =>
            (!string.IsNullOrWhiteSpace(incoming.StableId) &&
             item.StableId.Equals(incoming.StableId, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(incoming.DisplayName) &&
             item.DisplayName.Equals(incoming.DisplayName, StringComparison.OrdinalIgnoreCase)));

        if (existing is null)
        {
            observations.Add(incoming);
        }
        else
        {
            existing.MergeFrom(incoming);
        }
    }

    private static bool LooksLikePlayerValue(object value)
    {
        var name = value.GetType().FullName ?? value.GetType().Name;
        return name.Contains("player", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("lobbymember", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("clientconnection", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadIdentifier(string fieldName, object value, out string result)
    {
        result = string.Empty;
        if (!TryReadScalarString(value, out var text))
        {
            return false;
        }

        text = text.Trim();
        if (text.Length is < 1 or > 128)
        {
            return false;
        }

        var prefix = fieldName.Contains("steam", StringComparison.OrdinalIgnoreCase)
            ? "steam"
            : fieldName.Contains("network", StringComparison.OrdinalIgnoreCase) ||
              fieldName.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
              fieldName.Contains("client", StringComparison.OrdinalIgnoreCase)
                ? "network"
                : "id";

        result = $"{prefix}:{new string(text.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_').ToArray())}";
        return result.Length > prefix.Length + 1;
    }

    private static bool TryReadScalarString(object value, out string result)
    {
        result = string.Empty;
        try
        {
            switch (value)
            {
                case string text:
                    result = text;
                    return true;
                case Guid guid:
                    result = guid.ToString("D");
                    return true;
                case byte or sbyte or short or ushort or int or uint or long or ulong or decimal:
                    result = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                    return result.Length > 0;
                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadBoolean(object value, out bool result)
    {
        result = false;
        try
        {
            if (value is bool boolean)
            {
                result = boolean;
                return true;
            }

            if (value is byte or sbyte or short or ushort or int or uint or long or ulong)
            {
                result = Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0;
                return true;
            }
        }
        catch { }
        return false;
    }

    private static bool TryReadDouble(object value, out double result)
    {
        result = 0;
        try
        {
            if (value is IConvertible convertible && value is not string)
            {
                result = convertible.ToDouble(CultureInfo.InvariantCulture);
                return !double.IsNaN(result) && !double.IsInfinity(result);
            }
        }
        catch { }
        return false;
    }

    private static bool IsScalar(object value) =>
        value is string || value.GetType().IsPrimitive || value.GetType().IsEnum || value is decimal || value is Guid;

    private static bool IsValidName(string value)
    {
        var cleaned = CleanName(value);
        return cleaned.Length is >= 1 and <= 64 &&
               !cleaned.Equals("player", StringComparison.OrdinalIgnoreCase) &&
               !cleaned.Equals("clone", StringComparison.OrdinalIgnoreCase);
    }

    private static string CleanName(string value) =>
        value.Replace("(Clone)", string.Empty, StringComparison.OrdinalIgnoreCase).Trim().Trim('\0');

    private static FieldInfo[] SafeGetFields(Type type, BindingFlags flags)
    {
        try { return type.GetFields(flags); }
        catch { return Array.Empty<FieldInfo>(); }
    }

    private static Type[] SafeGetTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(type => type is not null).Cast<Type>().ToArray(); }
        catch { return Array.Empty<Type>(); }
    }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceComparer Instance = new();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
