namespace CrazyBatto.SotfDeathCounter.Core;

/// <summary>
/// Reusable, game-independent death-counter engine. A game adapter feeds player
/// observations and lifecycle signals into this class. The host project decides
/// which storage and outputs to use.
/// </summary>
public sealed class DeathCounterModule
{
    private readonly object _sync = new();
    private readonly DeathCounterOptions _options;
    private readonly IDeathCounterStore _store;
    private readonly Action<string> _log;
    private readonly Dictionary<string, PlayerRuntimeRecord> _players =
        new(StringComparer.OrdinalIgnoreCase);

    private string _sessionId = Guid.NewGuid().ToString("N");
    private DeathCounterEvent? _lastEvent;
    private long _eventSequence;

    public DeathCounterModule(
        DeathCounterOptions? options = null,
        IDeathCounterStore? store = null,
        Action<string>? log = null)
    {
        _options = (options ?? new DeathCounterOptions()).CloneNormalized();
        _store = store ?? new InMemoryDeathCounterStore();
        _log = log ?? (_ => { });

        foreach (var persisted in SafeLoad())
        {
            if (string.IsNullOrWhiteSpace(persisted.Id))
            {
                continue;
            }

            _players[persisted.Id] = new PlayerRuntimeRecord
            {
                Id = persisted.Id,
                DisplayName = string.IsNullOrWhiteSpace(persisted.DisplayName)
                    ? "Spieler"
                    : persisted.DisplayName,
                LifetimeDeaths = Math.Max(0, persisted.LifetimeDeaths),
                FirstSeenUtc = persisted.FirstSeenUtc == default ? DateTime.UtcNow : persisted.FirstSeenUtc,
                LastSeenUtc = persisted.LastSeenUtc,
                LastDeathUtc = persisted.LastDeathUtc,
                Online = false,
                State = PlayerLifecycleState.Unknown
            };
        }
    }

    public event EventHandler<PlayerDiscoveredEventArgs>? PlayerDiscovered;
    public event EventHandler<PlayerDeathCountedEventArgs>? PlayerDeathCounted;
    public event EventHandler<DeathCounterSnapshotChangedEventArgs>? SnapshotChanged;

    public DeathCounterOptions Options => _options.CloneNormalized();

    public DeathCounterSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return BuildSnapshotUnsafe();
        }
    }

    public string GetSnapshotJson() => DeathCounterJson.SerializeSnapshot(GetSnapshot());

    public void StartNewSession(string? sessionId = null)
    {
        DeathCounterSnapshot snapshot;
        lock (_sync)
        {
            _sessionId = string.IsNullOrWhiteSpace(sessionId)
                ? Guid.NewGuid().ToString("N")
                : sessionId.Trim();
            _lastEvent = null;

            foreach (var player in _players.Values)
            {
                player.SessionDeaths = 0;
                player.Online = false;
                player.State = PlayerLifecycleState.Unknown;
                player.HasSeenAlive = false;
                player.DeathLatched = false;
                player.LastDownedUtc = null;
                player.LastRootInstanceId = null;
            }

            snapshot = BuildSnapshotUnsafe();
        }

        _log("Neue Todeszähler-Sitzung gestartet.");
        RaiseSnapshotChanged(snapshot);
    }

    public void ApplyObservations(IEnumerable<PlayerObservation> observations, DateTime? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(observations);
        var now = nowUtc ?? DateTime.UtcNow;
        var discovered = new List<PlayerStatistics>();
        var deaths = new List<(DeathCounterEvent Death, PlayerStatistics Player)>();
        DeathCounterSnapshot? snapshot = null;
        var persistRequired = false;
        var changed = false;

        lock (_sync)
        {
            foreach (var observation in observations)
            {
                if (observation is null || !observation.HasUsableIdentity)
                {
                    continue;
                }

                var player = FindOrCreatePlayerUnsafe(observation, now, out var created, ref persistRequired);
                if (created)
                {
                    discovered.Add(ToStatisticsUnsafe(player, 0));
                    changed = true;
                }

                if (!player.Online)
                {
                    player.Online = true;
                    changed = true;
                }

                if (player.LastSeenUtc != now)
                {
                    player.LastSeenUtc = now;
                }
                player.LastSource = observation.Source ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(observation.DisplayName) &&
                    !player.DisplayName.Equals(observation.DisplayName.Trim(), StringComparison.Ordinal))
                {
                    player.DisplayName = observation.DisplayName.Trim();
                    persistRequired = true;
                    changed = true;
                }

                var previousState = player.State;
                var previousRootInstanceId = player.LastRootInstanceId;
                var death = ApplyLifecycleStateUnsafe(player, observation.State, observation.RootInstanceId, now);
                if (player.State != previousState || player.LastRootInstanceId != previousRootInstanceId)
                {
                    changed = true;
                }

                if (death is not null)
                {
                    deaths.Add((death, ToStatisticsUnsafe(player, 0)));
                    persistRequired = true;
                    changed = true;
                }
            }

            foreach (var player in _players.Values)
            {
                if (player.Online && (now - player.LastSeenUtc).TotalSeconds > _options.OfflineAfterSeconds)
                {
                    player.Online = false;
                    player.State = PlayerLifecycleState.Unknown;
                    changed = true;
                }
            }

            if (persistRequired)
            {
                PersistUnsafe();
            }

            if (changed)
            {
                snapshot = BuildSnapshotUnsafe();
            }
        }

        foreach (var player in discovered)
        {
            _log($"Mitspieler automatisch erfasst: {player.Name}");
            PlayerDiscovered?.Invoke(this, new PlayerDiscoveredEventArgs(player));
        }

        foreach (var item in deaths)
        {
            _log($"Tod gezählt: {item.Player.Name} → {item.Player.SessionDeaths}");
            PlayerDeathCounted?.Invoke(this, new PlayerDeathCountedEventArgs(item.Death, item.Player));
        }

        if (snapshot is not null)
        {
            RaiseSnapshotChanged(snapshot);
        }
    }

    public void ReportSignal(
        PlayerObservation observation,
        PlayerSignalKind signal,
        DateTime? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (!observation.HasUsableIdentity)
        {
            return;
        }

        var now = nowUtc ?? DateTime.UtcNow;
        PlayerStatistics? discovered = null;
        DeathCounterEvent? death = null;
        PlayerStatistics? deathPlayer = null;
        DeathCounterSnapshot snapshot;
        var persistRequired = false;

        lock (_sync)
        {
            var player = FindOrCreatePlayerUnsafe(observation, now, out var created, ref persistRequired);
            if (created)
            {
                discovered = ToStatisticsUnsafe(player, 0);
            }

            player.Online = true;
            player.LastSeenUtc = now;
            player.LastSource = observation.Source ?? string.Empty;
            if (observation.RootInstanceId.HasValue)
            {
                player.LastRootInstanceId = observation.RootInstanceId;
            }

            switch (signal)
            {
                case PlayerSignalKind.Downed:
                    player.LastDownedUtc = now;
                    player.State = PlayerLifecycleState.Downed;
                    if (_options.CountKnockdowns && !player.DeathLatched && player.HasSeenAlive)
                    {
                        death = CountDeathUnsafe(player, now, "knockdown-hook");
                    }
                    break;

                case PlayerSignalKind.Death:
                    if (!player.DeathLatched)
                    {
                        death = CountDeathUnsafe(player, now, "death-hook");
                    }
                    player.State = PlayerLifecycleState.Dead;
                    break;

                case PlayerSignalKind.Respawn:
                    if (!player.DeathLatched &&
                        (player.State is PlayerLifecycleState.Downed or PlayerLifecycleState.Dead ||
                         player.LastDownedUtc.HasValue))
                    {
                        death = CountDeathUnsafe(player, now, "respawn-hook");
                    }
                    player.State = PlayerLifecycleState.Respawning;
                    break;

                case PlayerSignalKind.Revived:
                    // A teammate rescue is not a death. Clear only the downed state;
                    // an already latched real death remains latched until a stable alive observation.
                    player.State = PlayerLifecycleState.Alive;
                    player.HasSeenAlive = true;
                    player.LastDownedUtc = null;
                    break;
            }

            if (death is not null)
            {
                persistRequired = true;
                deathPlayer = ToStatisticsUnsafe(player, 0);
            }

            if (persistRequired)
            {
                PersistUnsafe();
            }

            snapshot = BuildSnapshotUnsafe();
        }

        if (discovered is not null)
        {
            _log($"Mitspieler automatisch erfasst: {discovered.Name}");
            PlayerDiscovered?.Invoke(this, new PlayerDiscoveredEventArgs(discovered));
        }

        if (death is not null && deathPlayer is not null)
        {
            _log($"Tod gezählt: {deathPlayer.Name} → {deathPlayer.SessionDeaths}");
            PlayerDeathCounted?.Invoke(this, new PlayerDeathCountedEventArgs(death, deathPlayer));
        }

        RaiseSnapshotChanged(snapshot);
    }

    public void MarkAllOffline()
    {
        DeathCounterSnapshot snapshot;
        lock (_sync)
        {
            foreach (var player in _players.Values)
            {
                player.Online = false;
                player.State = PlayerLifecycleState.Unknown;
            }
            snapshot = BuildSnapshotUnsafe();
        }

        RaiseSnapshotChanged(snapshot);
    }

    public bool SimulateDeath(string nameOrId)
    {
        if (string.IsNullOrWhiteSpace(nameOrId))
        {
            return false;
        }

        DeathCounterEvent death;
        PlayerStatistics playerStats;
        DeathCounterSnapshot snapshot;

        lock (_sync)
        {
            var player = FindByNameOrIdUnsafe(nameOrId);
            if (player is null)
            {
                return false;
            }

            player.DeathLatched = false;
            death = CountDeathUnsafe(player, DateTime.UtcNow, "manual-test");
            playerStats = ToStatisticsUnsafe(player, 0);
            PersistUnsafe();
            snapshot = BuildSnapshotUnsafe();
        }

        _log($"Test-Tod gezählt: {playerStats.Name} → {playerStats.SessionDeaths}");
        PlayerDeathCounted?.Invoke(this, new PlayerDeathCountedEventArgs(death, playerStats));
        RaiseSnapshotChanged(snapshot);
        return true;
    }

    public void ResetSession()
    {
        DeathCounterSnapshot snapshot;
        lock (_sync)
        {
            _sessionId = Guid.NewGuid().ToString("N");
            _lastEvent = null;
            foreach (var player in _players.Values)
            {
                player.SessionDeaths = 0;
                player.DeathLatched = false;
                player.State = PlayerLifecycleState.Unknown;
                player.LastDownedUtc = null;
            }
            snapshot = BuildSnapshotUnsafe();
        }

        RaiseSnapshotChanged(snapshot);
    }

    public void ResetAll()
    {
        DeathCounterSnapshot snapshot;
        lock (_sync)
        {
            _sessionId = Guid.NewGuid().ToString("N");
            _lastEvent = null;
            foreach (var player in _players.Values)
            {
                player.SessionDeaths = 0;
                player.LifetimeDeaths = 0;
                player.DeathLatched = false;
                player.State = PlayerLifecycleState.Unknown;
                player.LastDeathUtc = null;
                player.LastDownedUtc = null;
            }
            PersistUnsafe();
            snapshot = BuildSnapshotUnsafe();
        }

        RaiseSnapshotChanged(snapshot);
    }

    public bool TryGetPlayer(string nameOrId, out PlayerStatistics? player)
    {
        lock (_sync)
        {
            var found = FindByNameOrIdUnsafe(nameOrId);
            player = found is null ? null : ToStatisticsUnsafe(found, 0);
            return player is not null;
        }
    }

    private PlayerRuntimeRecord FindOrCreatePlayerUnsafe(
        PlayerObservation observation,
        DateTime nowUtc,
        out bool created,
        ref bool persistRequired)
    {
        created = false;
        var observedId = observation.StableId?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(observedId) && _players.TryGetValue(observedId, out var exact))
        {
            return exact;
        }

        var normalizedName = NormalizeName(observation.DisplayName);
        PlayerRuntimeRecord? byName = null;
        if (!string.IsNullOrWhiteSpace(normalizedName))
        {
            var matches = _players.Values
                .Where(player =>
                    NormalizeName(player.DisplayName) == normalizedName &&
                    CanReuseNameMatch(player.Id, observedId))
                .Take(2)
                .ToList();

            if (matches.Count == 1)
            {
                byName = matches[0];
            }
        }

        if (byName is not null)
        {
            if (!string.IsNullOrWhiteSpace(observedId) &&
                !byName.Id.Equals(observedId, StringComparison.OrdinalIgnoreCase) &&
                IdentityStrength(observedId) > IdentityStrength(byName.Id))
            {
                _players.Remove(byName.Id);
                byName.Id = observedId;
                _players[byName.Id] = byName;
                persistRequired = true;
                _log($"Stabile Spieler-ID übernommen: {byName.DisplayName}");
            }

            return byName;
        }

        var id = !string.IsNullOrWhiteSpace(observedId)
            ? observedId
            : $"name:{normalizedName}";
        var displayName = string.IsNullOrWhiteSpace(observation.DisplayName)
            ? CreateFallbackName(id)
            : observation.DisplayName.Trim();

        var playerRecord = new PlayerRuntimeRecord
        {
            Id = id,
            DisplayName = displayName,
            FirstSeenUtc = nowUtc,
            LastSeenUtc = nowUtc,
            Online = true,
            State = PlayerLifecycleState.Unknown
        };

        _players[id] = playerRecord;
        created = true;
        persistRequired = true;
        return playerRecord;
    }

    private DeathCounterEvent? ApplyLifecycleStateUnsafe(
        PlayerRuntimeRecord player,
        PlayerLifecycleState nextState,
        int? observedRootInstanceId,
        DateTime nowUtc)
    {
        if (nextState == PlayerLifecycleState.Unknown)
        {
            return null;
        }

        var previousState = player.State;
        var rootChanged = observedRootInstanceId.HasValue && player.LastRootInstanceId.HasValue &&
                          observedRootInstanceId.Value != player.LastRootInstanceId.Value;
        DeathCounterEvent? death = null;

        switch (nextState)
        {
            case PlayerLifecycleState.Alive:
                if (previousState == PlayerLifecycleState.Downed && rootChanged && !player.DeathLatched)
                {
                    death = CountDeathUnsafe(player, nowUtc, "respawn-new-player-object");
                }

                player.HasSeenAlive = true;
                if (player.DeathLatched && player.LastDeathUtc.HasValue &&
                    (nowUtc - player.LastDeathUtc.Value).TotalMilliseconds >= _options.DeathLatchResetMilliseconds)
                {
                    player.DeathLatched = false;
                }
                player.LastDownedUtc = null;
                break;

            case PlayerLifecycleState.Downed:
                if (previousState != PlayerLifecycleState.Downed)
                {
                    player.LastDownedUtc = nowUtc;
                    if (_options.CountKnockdowns && !player.DeathLatched && player.HasSeenAlive)
                    {
                        death = CountDeathUnsafe(player, nowUtc, "knockdown-state");
                    }
                }
                break;

            case PlayerLifecycleState.Dead:
                if (previousState != PlayerLifecycleState.Dead && !player.DeathLatched &&
                    (player.HasSeenAlive || previousState == PlayerLifecycleState.Downed))
                {
                    death = CountDeathUnsafe(player, nowUtc, "dead-state");
                }
                break;

            case PlayerLifecycleState.Respawning:
                if (previousState != PlayerLifecycleState.Respawning && !player.DeathLatched &&
                    (previousState is PlayerLifecycleState.Downed or PlayerLifecycleState.Dead ||
                     player.LastDownedUtc.HasValue))
                {
                    death = CountDeathUnsafe(player, nowUtc, "respawn-state");
                }
                break;
        }

        player.State = nextState;
        if (observedRootInstanceId.HasValue)
        {
            player.LastRootInstanceId = observedRootInstanceId;
        }

        return death;
    }

    private DeathCounterEvent CountDeathUnsafe(PlayerRuntimeRecord player, DateTime nowUtc, string reason)
    {
        player.SessionDeaths++;
        player.LifetimeDeaths++;
        player.LastDeathUtc = nowUtc;
        player.DeathLatched = true;
        _eventSequence++;

        _lastEvent = new DeathCounterEvent
        {
            Sequence = _eventSequence,
            Type = "death",
            PlayerId = player.Id,
            PlayerName = player.DisplayName,
            SessionDeaths = player.SessionDeaths,
            LifetimeDeaths = player.LifetimeDeaths,
            AtUtc = nowUtc,
            Reason = reason
        };

        return CloneDeathEvent(_lastEvent);
    }

    private DeathCounterSnapshot BuildSnapshotUnsafe()
    {
        var ordered = _players.Values
            .OrderByDescending(player => player.SessionDeaths)
            .ThenByDescending(player => player.LifetimeDeaths)
            .ThenBy(player => player.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(_options.MaxPlayersInSnapshot)
            .ToList();

        var snapshot = new DeathCounterSnapshot
        {
            Version = 1,
            Title = _options.Title,
            SessionId = _sessionId,
            GeneratedAtUtc = DateTime.UtcNow,
            OnlinePlayers = _players.Values.Count(player => player.Online),
            KnownPlayers = _players.Count,
            ShowOfflinePlayers = _options.ShowOfflinePlayersByDefault,
            ShowLifetimeDeaths = _options.UseLifetimeDeathsByDefault,
            LastEvent = _lastEvent is null ? null : CloneDeathEvent(_lastEvent)
        };

        var rank = 0;
        snapshot.Players = ordered
            .Select(player => ToStatisticsUnsafe(player, ++rank))
            .ToList();
        return snapshot;
    }

    private static PlayerStatistics ToStatisticsUnsafe(PlayerRuntimeRecord player, int rank) => new()
    {
        Rank = rank,
        Id = player.Id,
        Name = player.DisplayName,
        SessionDeaths = player.SessionDeaths,
        LifetimeDeaths = player.LifetimeDeaths,
        Online = player.Online,
        State = player.State.ToString().ToLowerInvariant(),
        FirstSeenUtc = player.FirstSeenUtc,
        LastSeenUtc = player.LastSeenUtc,
        LastDeathUtc = player.LastDeathUtc,
        LastSource = player.LastSource
    };

    private static DeathCounterEvent CloneDeathEvent(DeathCounterEvent value) => new()
    {
        Sequence = value.Sequence,
        Type = value.Type,
        PlayerId = value.PlayerId,
        PlayerName = value.PlayerName,
        SessionDeaths = value.SessionDeaths,
        LifetimeDeaths = value.LifetimeDeaths,
        AtUtc = value.AtUtc,
        Reason = value.Reason
    };

    private PlayerRuntimeRecord? FindByNameOrIdUnsafe(string value)
    {
        var trimmed = value.Trim();
        if (_players.TryGetValue(trimmed, out var exact))
        {
            return exact;
        }

        var normalized = NormalizeName(trimmed);
        return _players.Values.FirstOrDefault(player => NormalizeName(player.DisplayName) == normalized);
    }

    private IReadOnlyCollection<PersistedPlayerStatistics> SafeLoad()
    {
        try
        {
            return _store.Load() ?? Array.Empty<PersistedPlayerStatistics>();
        }
        catch (Exception ex)
        {
            _log($"Todeszähler-Speicher konnte nicht geladen werden: {ex.Message}");
            return Array.Empty<PersistedPlayerStatistics>();
        }
    }

    private void PersistUnsafe()
    {
        var players = _players.Values
            .Select(player => new PersistedPlayerStatistics
            {
                Id = player.Id,
                DisplayName = player.DisplayName,
                LifetimeDeaths = Math.Max(0, player.LifetimeDeaths),
                FirstSeenUtc = player.FirstSeenUtc,
                LastSeenUtc = player.LastSeenUtc,
                LastDeathUtc = player.LastDeathUtc
            })
            .ToList();

        try
        {
            _store.Save(players);
        }
        catch (Exception ex)
        {
            _log($"Todeszähler-Speicher konnte nicht geschrieben werden: {ex.Message}");
        }
    }

    private void RaiseSnapshotChanged(DeathCounterSnapshot snapshot) =>
        SnapshotChanged?.Invoke(this, new DeathCounterSnapshotChangedEventArgs(snapshot));

    private static bool CanReuseNameMatch(string existingId, string observedId)
    {
        if (string.IsNullOrWhiteSpace(existingId) || string.IsNullOrWhiteSpace(observedId) ||
            existingId.Equals(observedId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var existingPrefix = IdentityPrefix(existingId);
        var observedPrefix = IdentityPrefix(observedId);
        if (!string.IsNullOrWhiteSpace(existingPrefix) &&
            existingPrefix.Equals(observedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !(IdentityStrength(existingId) >= 100 && IdentityStrength(observedId) >= 100);
    }

    private static string IdentityPrefix(string id)
    {
        var separator = id.IndexOf(':');
        return separator > 0 ? id[..separator] : string.Empty;
    }

    private static int IdentityStrength(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.StartsWith("name:", StringComparison.OrdinalIgnoreCase)) return 0;
        if (id.StartsWith("steam:", StringComparison.OrdinalIgnoreCase)) return 150;
        if (id.StartsWith("platform:", StringComparison.OrdinalIgnoreCase)) return 140;
        if (id.StartsWith("account:", StringComparison.OrdinalIgnoreCase)) return 130;
        if (id.StartsWith("member:", StringComparison.OrdinalIgnoreCase)) return 120;
        if (id.StartsWith("user:", StringComparison.OrdinalIgnoreCase)) return 110;
        if (id.StartsWith("owner:", StringComparison.OrdinalIgnoreCase)) return 80;
        if (id.StartsWith("network:", StringComparison.OrdinalIgnoreCase)) return 70;
        if (id.StartsWith("connection:", StringComparison.OrdinalIgnoreCase)) return 60;
        if (id.StartsWith("client:", StringComparison.OrdinalIgnoreCase)) return 55;
        if (id.StartsWith("entity:", StringComparison.OrdinalIgnoreCase)) return 50;
        return 40;
    }

    private static string NormalizeName(string? value) => string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : new string(value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static string CreateFallbackName(string id)
    {
        var suffix = id.Length > 6 ? id[^6..] : id;
        return $"Spieler •{suffix}";
    }
}
