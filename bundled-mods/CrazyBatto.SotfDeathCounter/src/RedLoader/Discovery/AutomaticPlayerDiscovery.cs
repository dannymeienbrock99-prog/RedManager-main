using System.Reflection;
using System.Text.Json;
using CrazyBatto.SotfDeathCounter.Core;
using UnityEngine;

namespace CrazyBatto.SotfDeathCounter.RedLoader;

internal sealed class AutomaticPlayerDiscovery
{
    private static readonly HashSet<string> IdNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "SteamId", "SteamID", "SteamId64", "SteamID64", "_steamId", "_steamID",
        "PlatformId", "PlatformID", "AccountId", "AccountID", "MemberId", "MemberID",
        "UserId", "UserID", "OwnerId", "OwnerID", "PlayerId", "PlayerID",
        "NetworkId", "NetworkID", "ConnectionId", "ConnectionID", "ClientId", "ClientID",
        "EntityId", "EntityID", "Guid", "GUID"
    };

    private static readonly HashSet<string> NameNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "DisplayName", "PlayerName", "SteamName", "PersonaName", "Nickname", "NickName",
        "UserName", "Username", "MemberName", "LobbyName", "CharacterName", "_playerName",
        "_displayName", "_steamName"
    };

    private static readonly HashSet<string> NestedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "PlayerData", "Profile", "Owner", "Connection", "ConnectionToken", "Token", "Identity",
        "User", "Member", "LobbyMember", "State", "NetworkState", "Entity", "Metadata", "Data",
        "Key", "Value"
    };

    private static readonly HashSet<string> IdValueNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Value", "m_SteamID", "m_SteamId", "SteamId", "SteamID", "Id", "ID", "RawValue", "Raw"
    };

    private static readonly HashSet<string> DeadNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "IsDead", "Dead", "_isDead", "_dead", "IsPlayerDead", "IsKilled", "Killed",
        "IsInDeathState", "HasDied"
    };

    private static readonly HashSet<string> DownedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "IsDowned", "Downed", "_isDowned", "IsIncapacitated", "Incapacitated",
        "IsKnockedDown", "KnockedDown", "IsDying", "RequiresRevive", "IsAwaitingRevive"
    };

    private static readonly HashSet<string> RespawnNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "IsRespawning", "Respawning", "_isRespawning", "RespawnPending", "IsRespawnPending",
        "WaitingForRespawn", "IsWaitingForRespawn", "IsCaptured", "Captured"
    };

    private static readonly HashSet<string> AliveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "IsAlive", "Alive", "_isAlive", "HasControl", "IsSpawned"
    };

    private static readonly HashSet<string> HealthNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Health", "CurrentHealth", "HitPoints", "CurrentHitPoints", "_health", "_currentHealth",
        "Hp", "HP"
    };

    private static readonly HashSet<string> StateNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CurrentState", "PlayerState", "LifeState", "DeathState", "RespawnState", "StateName"
    };

    private readonly SotfAdapterOptions _config;
    private readonly string _diagnosticsPath;
    private readonly Action<string> _log;
    private readonly List<Type> _managerTypes = new();
    private DateTime _nextManagerRefreshUtc = DateTime.MinValue;
    private DateTime _nextWorldScanUtc = DateTime.MinValue;
    private DateTime _nextDiagnosticsUtc = DateTime.MinValue;
    private IReadOnlyList<PlayerObservation> _cachedWorldObservations = Array.Empty<PlayerObservation>();

    public AutomaticPlayerDiscovery(SotfAdapterOptions config, string diagnosticsPath, Action<string> log)
    {
        _config = config;
        _diagnosticsPath = diagnosticsPath;
        _log = log;
    }

    public IReadOnlyList<PlayerObservation> Scan(DateTime nowUtc)
    {
        var observations = new List<PlayerObservation>();

        try
        {
            observations.AddRange(ScanManagers(nowUtc));
        }
        catch (Exception ex)
        {
            _log($"Lobby-Erfassung fehlgeschlagen: {ex.Message}");
        }

        try
        {
            if (nowUtc >= _nextWorldScanUtc)
            {
                _nextWorldScanUtc = nowUtc.AddMilliseconds(_config.WorldScanIntervalMilliseconds);
                _cachedWorldObservations = ScanWorld(nowUtc).ToList();
            }

            observations.AddRange(_cachedWorldObservations);
        }
        catch (Exception ex)
        {
            _cachedWorldObservations = Array.Empty<PlayerObservation>();
            _nextWorldScanUtc = nowUtc.AddSeconds(1);
            _log($"Welt-Erfassung fehlgeschlagen: {ex.Message}");
        }

        var merged = MergeObservations(observations, nowUtc);
        WriteDiagnostics(merged, nowUtc);
        return merged;
    }

    public PlayerObservation? ResolveFromRuntimeObject(object? instance, object?[]? arguments, DateTime nowUtc)
    {
        var objects = new List<object>();
        int? rootId = null;
        string? rootName = null;

        AddRuntimeObject(instance, objects, ref rootId, ref rootName);
        if (arguments is not null)
        {
            foreach (var argument in arguments)
            {
                AddRuntimeObject(argument, objects, ref rootId, ref rootName);
            }
        }

        var observation = CreateObservation(objects, rootName, "runtime-hook", 100, rootId, nowUtc);
        FinalizeIdentity(observation);
        return observation?.HasUsableIdentity == true ? observation : null;
    }

    private IEnumerable<PlayerObservation> ScanWorld(DateTime nowUtc)
    {
        var groups = new Dictionary<int, WorldGroup>();
        var behaviours = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>();

        foreach (var behaviour in behaviours)
        {
            if (behaviour is null)
            {
                continue;
            }

            try
            {
                var root = behaviour.transform?.root;
                var rootObject = root?.gameObject ?? behaviour.gameObject;
                if (rootObject is null)
                {
                    continue;
                }

                var typeName = behaviour.GetType().FullName ?? behaviour.GetType().Name;
                var rootName = rootObject.name ?? string.Empty;
                var objectName = behaviour.gameObject?.name ?? string.Empty;
                if (!LooksLikePlayerObject(typeName, rootName, objectName))
                {
                    continue;
                }

                var id = rootObject.GetInstanceID();
                if (!groups.TryGetValue(id, out var group))
                {
                    group = new WorldGroup(id, rootName);
                    groups[id] = group;
                }

                group.Objects.Add(behaviour);
            }
            catch
            {
                // A disconnect can invalidate IL2CPP objects during the scan.
            }
        }

        foreach (var group in groups.Values)
        {
            var observation = CreateObservation(
                group.Objects,
                group.RootName,
                "world",
                70,
                group.RootId,
                nowUtc);
            FinalizeIdentity(observation);
            if (observation?.HasUsableIdentity == true)
            {
                yield return observation;
            }
        }
    }

    private IEnumerable<PlayerObservation> ScanManagers(DateTime nowUtc)
    {
        RefreshManagerTypes(nowUtc);

        foreach (var type in _managerTypes)
        {
            foreach (var source in GetManagerSources(type))
            {
                foreach (var observation in ScanManagerSource(type, source, nowUtc))
                {
                    yield return observation;
                }
            }
        }
    }

    private IEnumerable<object> GetManagerSources(Type type)
    {
        var result = new List<object>();
        foreach (var methodName in new[] { "GetActiveInstance", "GetInstance", "GetSingleton" })
        {
            if (ReflectionReader.TryInvokeStaticNoArgs(type, methodName, out var value) && value is not null)
            {
                result.Add(value);
            }
        }

        foreach (var pair in ReflectionReader.ReadRelevantStaticValues(type, IsManagerStaticMember))
        {
            result.Add(pair.Name.Contains("instance", StringComparison.OrdinalIgnoreCase)
                ? pair.Value
                : new NamedValue(pair.Name, pair.Value));
        }

        return result.Distinct(ReferenceComparer.Instance);
    }

    private IEnumerable<PlayerObservation> ScanManagerSource(Type managerType, object source, DateTime nowUtc)
    {
        var typeName = managerType.FullName ?? managerType.Name;
        if (source is NamedValue named)
        {
            foreach (var observation in ScanCollection(named.Value, $"{typeName}.{named.Name}", nowUtc))
            {
                yield return observation;
            }
            yield break;
        }

        foreach (var pair in ReflectionReader.ReadRelevantMemberValues(source, IsRosterMember))
        {
            if (IsScalar(pair.Value))
            {
                continue;
            }

            var any = false;
            foreach (var observation in ScanCollection(pair.Value, $"{typeName}.{pair.Name}", nowUtc))
            {
                any = true;
                yield return observation;
            }

            if (!any && IsLobbyContainerName(pair.Name))
            {
                foreach (var nested in ReflectionReader.ReadRelevantMemberValues(pair.Value, IsRosterMember))
                {
                    foreach (var observation in ScanCollection(
                                 nested.Value,
                                 $"{typeName}.{pair.Name}.{nested.Name}",
                                 nowUtc))
                    {
                        yield return observation;
                    }
                }
            }
        }
    }

    private IEnumerable<PlayerObservation> ScanCollection(object collection, string source, DateTime nowUtc)
    {
        foreach (var item in UnknownCollectionReader.Enumerate(collection, 64))
        {
            var observation = CreateObservation(new[] { item }, null, source, 90, null, nowUtc);
            FinalizeIdentity(observation);
            if (observation?.HasUsableIdentity == true)
            {
                yield return observation;
            }
        }
    }

    private static PlayerObservation? CreateObservation(
        IEnumerable<object> initialObjects,
        string? fallbackName,
        string source,
        int confidence,
        int? rootId,
        DateTime nowUtc)
    {
        var expanded = new List<object>();
        var seen = new HashSet<object>(ReferenceComparer.Instance);
        foreach (var initial in initialObjects)
        {
            if (initial is null)
            {
                continue;
            }

            foreach (var candidate in ReflectionReader.ExpandKnownNestedObjects(initial, NestedNames, 2))
            {
                if (seen.Add(candidate))
                {
                    expanded.Add(candidate);
                }
            }
        }

        if (expanded.Count == 0)
        {
            return null;
        }

        var id = ReadIdentifier(expanded);
        var name = ReadName(expanded);
        if (string.IsNullOrWhiteSpace(name) && IsValidName(fallbackName))
        {
            name = CleanName(fallbackName!);
        }

        if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return new PlayerObservation
        {
            StableId = id,
            DisplayName = name,
            Source = source,
            Confidence = confidence,
            RootInstanceId = rootId,
            SeenAtUtc = nowUtc,
            State = ReadState(expanded)
        };
    }

    private static string ReadIdentifier(IEnumerable<object> sources)
    {
        var candidates = new List<(int Score, string Prefix, string Value)>();
        foreach (var source in sources)
        {
            if (TryReadDirectIdentifier(source, out var directPrefix, out var directValue))
            {
                candidates.Add((ScoreId(directPrefix, directValue) + 20, directPrefix, directValue));
            }

            if (LooksLikeIdentifierType(source.GetType()) && TryFlattenIdentifier(source, out var direct))
            {
                candidates.Add((ScoreId("Id", direct), "id", direct));
            }

            foreach (var pair in ReflectionReader.ReadNamedValues(source, IdNames))
            {
                if (TryFlattenIdentifier(pair.Value, out var value))
                {
                    candidates.Add((ScoreId(pair.Name, value), IdPrefix(pair.Name), value));
                }
            }
        }

        var best = candidates.OrderByDescending(item => item.Score).ThenByDescending(item => item.Value.Length).FirstOrDefault();
        return best.Score <= 0 ? string.Empty : $"{best.Prefix}:{SanitizeId(best.Value)}";
    }

    private static string ReadName(IEnumerable<object> sources)
    {
        var candidates = new List<(int Score, string Value)>();
        foreach (var source in sources)
        {
            if (source is string direct && IsValidName(direct))
            {
                candidates.Add((20, CleanName(direct)));
            }

            foreach (var pair in ReflectionReader.ReadNamedValues(source, NameNames))
            {
                if (!ReflectionReader.TryToCleanString(pair.Value, out var value) || !IsValidName(value))
                {
                    continue;
                }

                var score = pair.Name.Contains("display", StringComparison.OrdinalIgnoreCase) ||
                            pair.Name.Contains("steam", StringComparison.OrdinalIgnoreCase)
                    ? 100
                    : 80;
                candidates.Add((score, CleanName(value)));
            }
        }

        return candidates.OrderByDescending(item => item.Score).ThenByDescending(item => item.Value.Length)
            .Select(item => item.Value).FirstOrDefault() ?? string.Empty;
    }

    private static PlayerLifecycleState ReadState(IEnumerable<object> sources)
    {
        var dead = false;
        var downed = false;
        var respawning = false;
        var alive = false;
        double? health = null;

        foreach (var source in sources)
        {
            if (!LooksStateRelevant(source.GetType()))
            {
                continue;
            }

            dead |= ReadTrue(source, DeadNames);
            downed |= ReadTrue(source, DownedNames);
            respawning |= ReadTrue(source, RespawnNames);
            alive |= ReadTrue(source, AliveNames);

            foreach (var pair in ReflectionReader.ReadNamedValues(source, HealthNames))
            {
                if (ReflectionReader.TryToDouble(pair.Value, out var value) && !double.IsNaN(value) && !double.IsInfinity(value))
                {
                    health = health.HasValue ? Math.Min(health.Value, value) : value;
                }
            }

            foreach (var pair in ReflectionReader.ReadNamedValues(source, StateNames))
            {
                if (!ReflectionReader.TryToCleanString(pair.Value, out var text))
                {
                    continue;
                }

                var state = text.ToLowerInvariant();
                respawning |= state.Contains("respawn", StringComparison.Ordinal) || state.Contains("captur", StringComparison.Ordinal);
                dead |= state.Contains("dead", StringComparison.Ordinal) || state.Contains("death", StringComparison.Ordinal) || state.Contains("killed", StringComparison.Ordinal);
                downed |= state.Contains("down", StringComparison.Ordinal) || state.Contains("incap", StringComparison.Ordinal) || state.Contains("revive", StringComparison.Ordinal) || state.Contains("knock", StringComparison.Ordinal);
                alive |= state.Contains("alive", StringComparison.Ordinal) || state.Contains("playing", StringComparison.Ordinal);
            }
        }

        if (respawning) return PlayerLifecycleState.Respawning;
        if (dead) return PlayerLifecycleState.Dead;
        if (downed) return PlayerLifecycleState.Downed;
        if (alive) return PlayerLifecycleState.Alive;
        if (health.HasValue) return health.Value <= 0 ? PlayerLifecycleState.Downed : PlayerLifecycleState.Alive;
        return PlayerLifecycleState.Unknown;
    }

    private static bool ReadTrue(object source, IReadOnlySet<string> names)
    {
        foreach (var pair in ReflectionReader.ReadNamedValues(source, names))
        {
            if (ReflectionReader.TryToBoolean(pair.Value, out var value) && value)
            {
                return true;
            }
        }
        return false;
    }

    private static bool TryReadDirectIdentifier(object source, out string prefix, out string value)
    {
        prefix = string.Empty;
        value = string.Empty;

        if (!ReflectionReader.TryToCleanString(source, out var text))
        {
            return false;
        }

        text = text.Trim();
        if (text.All(char.IsDigit) && text.Length is >= 15 and <= 20)
        {
            prefix = "steam";
            value = text;
            return true;
        }

        if (Guid.TryParse(text, out _))
        {
            prefix = "id";
            value = text;
            return true;
        }

        return false;
    }

    private static bool TryFlattenIdentifier(object value, out string result)
    {
        result = string.Empty;
        if (ReflectionReader.TryToCleanString(value, out var direct) && IsValidId(direct))
        {
            result = direct;
            return true;
        }

        if (value.GetType().IsPrimitive || value.GetType().IsEnum)
        {
            return false;
        }

        foreach (var pair in ReflectionReader.ReadNamedValues(value, IdValueNames))
        {
            if (ReferenceEquals(pair.Value, value))
            {
                continue;
            }

            if (ReflectionReader.TryToCleanString(pair.Value, out var nested) && IsValidId(nested))
            {
                result = nested;
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<PlayerObservation> MergeObservations(IEnumerable<PlayerObservation> raw, DateTime nowUtc)
    {
        var merged = new List<PlayerObservation>();
        foreach (var observation in raw.Where(item => item.HasUsableIdentity).OrderByDescending(item => item.Confidence))
        {
            FinalizeIdentity(observation);
            var nameKey = NormalizeName(observation.DisplayName);
            var existing = merged.FirstOrDefault(item =>
                (!string.IsNullOrWhiteSpace(observation.StableId) &&
                 item.StableId.Equals(observation.StableId, StringComparison.OrdinalIgnoreCase)) ||
                (observation.RootInstanceId.HasValue && item.RootInstanceId == observation.RootInstanceId));

            if (existing is null && !string.IsNullOrWhiteSpace(nameKey))
            {
                var nameMatches = merged
                    .Where(item =>
                        NormalizeName(item.DisplayName) == nameKey &&
                        CanMergeByName(item.StableId, observation.StableId))
                    .Take(2)
                    .ToList();

                // Never guess when two different players use the same display name.
                if (nameMatches.Count == 1)
                {
                    existing = nameMatches[0];
                }
            }

            if (existing is null)
            {
                merged.Add(observation);
            }
            else
            {
                existing.MergeFrom(observation);
                FinalizeIdentity(existing);
            }
        }

        foreach (var observation in merged)
        {
            observation.SeenAtUtc = nowUtc;
        }
        return merged;
    }

    private void RefreshManagerTypes(DateTime nowUtc)
    {
        if (_managerTypes.Count > 0 && nowUtc < _nextManagerRefreshUtc)
        {
            return;
        }

        _nextManagerRefreshUtc = nowUtc.AddSeconds(15);
        var found = new List<Type>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (var type in ReflectionReader.SafeGetTypes(assembly))
            {
                var name = type.FullName ?? type.Name;
                if (name.EndsWith("CoopLobbyManager", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith("BoltNetwork", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("MultiplayerManager", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("NetworkPlayerManager", StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(type);
                }
            }
        }

        _managerTypes.Clear();
        _managerTypes.AddRange(found.Distinct());
    }

    private static void AddRuntimeObject(object? value, ICollection<object> target, ref int? rootId, ref string? rootName)
    {
        if (value is null)
        {
            return;
        }

        target.Add(value);
        if (value is not Component component)
        {
            return;
        }

        try
        {
            var root = component.transform?.root;
            if (root is null)
            {
                return;
            }

            rootId ??= root.gameObject.GetInstanceID();
            rootName ??= root.gameObject.name;
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour is not null)
                {
                    target.Add(behaviour);
                }
            }
        }
        catch
        {
            // Ignore destroyed objects.
        }
    }

    private void WriteDiagnostics(IReadOnlyList<PlayerObservation> players, DateTime nowUtc)
    {
        if (!_config.WriteDiscoveryDiagnostics || nowUtc < _nextDiagnosticsUtc)
        {
            return;
        }

        _nextDiagnosticsUtc = nowUtc.AddSeconds(10);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_diagnosticsPath)!);
            var value = new
            {
                generatedAtUtc = nowUtc,
                players = players.Select(player => new
                {
                    id = player.StableId,
                    name = player.DisplayName,
                    state = player.State.ToString(),
                    source = player.Source,
                    confidence = player.Confidence,
                    rootInstanceId = player.RootInstanceId
                })
            };
            File.WriteAllText(_diagnosticsPath, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Diagnostics are optional.
        }
    }

    private static void FinalizeIdentity(PlayerObservation? observation)
    {
        if (observation is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(observation.StableId) && !string.IsNullOrWhiteSpace(observation.DisplayName))
        {
            observation.StableId = $"name:{NormalizeName(observation.DisplayName)}";
        }

        if (string.IsNullOrWhiteSpace(observation.DisplayName) && !string.IsNullOrWhiteSpace(observation.StableId))
        {
            var suffix = observation.StableId.Length > 6 ? observation.StableId[^6..] : observation.StableId;
            observation.DisplayName = $"Spieler •{suffix}";
        }
    }

    private static bool CanMergeByName(string firstId, string secondId)
    {
        if (string.IsNullOrWhiteSpace(firstId) || string.IsNullOrWhiteSpace(secondId) ||
            PlayerObservation.IsFallbackId(firstId) || PlayerObservation.IsFallbackId(secondId))
        {
            return true;
        }

        if (firstId.Equals(secondId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var firstPrefix = IdentityPrefix(firstId);
        var secondPrefix = IdentityPrefix(secondId);
        if (!string.IsNullOrWhiteSpace(firstPrefix) &&
            firstPrefix.Equals(secondPrefix, StringComparison.OrdinalIgnoreCase))
        {
            // Same namespace but different ID: never merge only because the display names match.
            return false;
        }

        // Two distinct account-level IDs represent two different people even when the names match.
        return !(IsStrongIdentity(firstId) && IsStrongIdentity(secondId));
    }

    private static string IdentityPrefix(string id)
    {
        var separator = id.IndexOf(':');
        return separator > 0 ? id[..separator] : string.Empty;
    }

    private static bool IsStrongIdentity(string id) =>
        id.StartsWith("steam:", StringComparison.OrdinalIgnoreCase) ||
        id.StartsWith("platform:", StringComparison.OrdinalIgnoreCase) ||
        id.StartsWith("account:", StringComparison.OrdinalIgnoreCase) ||
        id.StartsWith("member:", StringComparison.OrdinalIgnoreCase) ||
        id.StartsWith("user:", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikePlayerObject(string typeName, string rootName, string objectName)
    {
        var text = $"{typeName} {rootName} {objectName}".ToLowerInvariant();
        if (!(text.Contains("player", StringComparison.Ordinal) || text.Contains("coop", StringComparison.Ordinal) ||
              text.Contains("multiplayer", StringComparison.Ordinal) || text.Contains("remotecharacter", StringComparison.Ordinal) ||
              text.Contains("networkcharacter", StringComparison.Ordinal)))
        {
            return false;
        }

        var root = rootName.ToLowerInvariant();
        return !(root.Contains("canvas", StringComparison.Ordinal) || root.Contains("menu", StringComparison.Ordinal) ||
                 root.Contains("hud", StringComparison.Ordinal) || root.Contains("lobbyui", StringComparison.Ordinal));
    }

    private static bool IsManagerStaticMember(string name) =>
        name.Contains("instance", StringComparison.OrdinalIgnoreCase) || IsRosterMember(name);

    private static bool IsRosterMember(string name)
    {
        var value = name.ToLowerInvariant();
        if (value.Contains("limit", StringComparison.Ordinal) || value.Contains("count", StringComparison.Ordinal) ||
            value.Contains("maximum", StringComparison.Ordinal) || value.Contains("maxplayer", StringComparison.Ordinal))
        {
            return false;
        }

        return value.Contains("member", StringComparison.Ordinal) || value.Contains("player", StringComparison.Ordinal) ||
               value.Contains("client", StringComparison.Ordinal) || value.Contains("connection", StringComparison.Ordinal) ||
               value.Contains("participant", StringComparison.Ordinal) || value.Contains("user", StringComparison.Ordinal) ||
               value.Contains("lobby", StringComparison.Ordinal);
    }

    private static bool IsLobbyContainerName(string name) =>
        name.Contains("lobby", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("session", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("room", StringComparison.OrdinalIgnoreCase);

    private static bool IsScalar(object value)
    {
        var type = value.GetType();
        return value is string || type.IsPrimitive || type.IsEnum || value is decimal;
    }

    private static bool LooksStateRelevant(Type type)
    {
        var name = (type.FullName ?? type.Name).ToLowerInvariant();
        return name.Contains("player", StringComparison.Ordinal) ||
               name.Contains("health", StringComparison.Ordinal) ||
               name.Contains("death", StringComparison.Ordinal) ||
               name.Contains("damage", StringComparison.Ordinal) ||
               name.Contains("state", StringComparison.Ordinal) ||
               name.Contains("respawn", StringComparison.Ordinal) ||
               name.Contains("revive", StringComparison.Ordinal) ||
               name.Contains("knock", StringComparison.Ordinal) ||
               name.Contains("down", StringComparison.Ordinal);
    }

    private static bool LooksLikeIdentifierType(Type type)
    {
        var name = type.FullName ?? type.Name;
        return name.Contains("SteamId", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("PlatformId", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("AccountId", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("ConnectionId", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("NetworkId", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidId(string value)
    {
        var text = value.Trim();
        return text.Length is >= 1 and <= 96 && text != "0" &&
               !text.Equals("null", StringComparison.OrdinalIgnoreCase) &&
               !text.Equals("none", StringComparison.OrdinalIgnoreCase);
    }

    private static int ScoreId(string member, string value)
    {
        var score = 10;
        if (member.Contains("steam", StringComparison.OrdinalIgnoreCase)) score += 100;
        if (member.Contains("platform", StringComparison.OrdinalIgnoreCase)) score += 90;
        if (member.Contains("account", StringComparison.OrdinalIgnoreCase)) score += 80;
        if (member.Contains("member", StringComparison.OrdinalIgnoreCase)) score += 75;
        if (member.Contains("user", StringComparison.OrdinalIgnoreCase)) score += 70;
        if (member.Contains("owner", StringComparison.OrdinalIgnoreCase)) score += 60;
        if (member.Contains("network", StringComparison.OrdinalIgnoreCase)) score += 50;
        if (member.Contains("connection", StringComparison.OrdinalIgnoreCase)) score += 40;
        if (value.All(char.IsDigit) && value.Length is >= 15 and <= 20) score += 120;
        return score;
    }

    private static string IdPrefix(string member)
    {
        if (member.Contains("steam", StringComparison.OrdinalIgnoreCase)) return "steam";
        if (member.Contains("platform", StringComparison.OrdinalIgnoreCase)) return "platform";
        if (member.Contains("account", StringComparison.OrdinalIgnoreCase)) return "account";
        if (member.Contains("member", StringComparison.OrdinalIgnoreCase)) return "member";
        if (member.Contains("user", StringComparison.OrdinalIgnoreCase)) return "user";
        if (member.Contains("owner", StringComparison.OrdinalIgnoreCase)) return "owner";
        if (member.Contains("network", StringComparison.OrdinalIgnoreCase)) return "network";
        if (member.Contains("connection", StringComparison.OrdinalIgnoreCase)) return "connection";
        if (member.Contains("client", StringComparison.OrdinalIgnoreCase)) return "client";
        if (member.Contains("entity", StringComparison.OrdinalIgnoreCase)) return "entity";
        return "id";
    }

    private static string SanitizeId(string value)
    {
        var characters = value.Trim().Where(character => char.IsLetterOrDigit(character) || character is '-' or '_' or ':' or '[' or ']' or '.').ToArray();
        return characters.Length == 0 ? "unknown" : new string(characters);
    }

    private static bool IsValidName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.Trim();
        if (text.Length is < 1 or > 64 || text.All(char.IsDigit)) return false;
        var lower = text.ToLowerInvariant();
        if ((text.StartsWith("[", StringComparison.Ordinal) && text.Contains(':')) ||
            Guid.TryParse(text, out _))
        {
            return false;
        }

        return lower is not ("player" or "spieler" or "unknown" or "unbekannt" or "none" or "null") &&
               !lower.Contains("(clone)", StringComparison.Ordinal) &&
               !lower.Contains("prefab", StringComparison.Ordinal) &&
               !lower.Contains("controller", StringComparison.Ordinal) &&
               !lower.Contains("manager", StringComparison.Ordinal) &&
               !lower.Contains("canvas", StringComparison.Ordinal);
    }

    private static string CleanName(string value)
    {
        var result = value.Trim();
        var clone = result.IndexOf("(Clone)", StringComparison.OrdinalIgnoreCase);
        return clone < 0 ? result : result[..clone].Trim();
    }

    private static string NormalizeName(string? value) => string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : new string(value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private sealed class WorldGroup
    {
        public WorldGroup(int rootId, string rootName)
        {
            RootId = rootId;
            RootName = rootName;
        }

        public int RootId { get; }
        public string RootName { get; }
        public List<object> Objects { get; } = new();
    }

    private sealed record NamedValue(string Name, object Value);

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceComparer Instance = new();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
