using System.Reflection;
using CrazyBatto.SotfDeathCounter.Core;
using HarmonyLib;

namespace CrazyBatto.SotfDeathCounter.RedLoader;

internal sealed class RuntimeHookInstaller
{
    private static readonly HashSet<string> DeathMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "Die", "Death", "OnDeath", "Kill", "Killed", "OnKilled", "SetDead", "PlayerDied",
        "OnPlayerDied", "EnterDeath", "TriggerDeath", "HandleDeath", "GoToDeath"
    };

    private static readonly HashSet<string> DownedMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "KnockDown", "KnockedDown", "OnKnockedDown", "EnterDowned", "SetDowned", "Incapacitate",
        "OnIncapacitated", "RequireRevive", "StartReviveWait"
    };

    private static readonly HashSet<string> RespawnMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "Respawn", "OnRespawn", "RespawnPlayer", "StartRespawn", "CompleteRespawn",
        "OnRespawnComplete", "HandleRespawn", "GoToRespawn", "FinishRespawn"
    };

    private static readonly HashSet<string> ReviveMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "Revive", "OnRevive", "DoRevive", "RevivePlayer", "CompleteRevive",
        "OnReviveComplete", "FinishRevive"
    };

    private static RuntimeHookInstaller? _active;

    private readonly HarmonyLib.Harmony _harmony = new("CrazyBatto.SotfDeathCounter.RuntimeHooks");
    private readonly AutomaticPlayerDiscovery _discovery;
    private readonly Action<PlayerObservation, PlayerSignalKind, DateTime> _onSignal;
    private readonly Action<string> _log;
    private readonly HashSet<MethodBase> _patched = new();
    private bool _installed;

    public RuntimeHookInstaller(
        AutomaticPlayerDiscovery discovery,
        Action<PlayerObservation, PlayerSignalKind, DateTime> onSignal,
        Action<string> log)
    {
        _discovery = discovery;
        _onSignal = onSignal;
        _log = log;
    }

    public void Install()
    {
        if (_installed)
        {
            return;
        }

        _active = this;
        var deathPostfix = new HarmonyMethod(AccessTools.Method(typeof(RuntimeHookInstaller), nameof(DeathPostfix)));
        var downedPostfix = new HarmonyMethod(AccessTools.Method(typeof(RuntimeHookInstaller), nameof(DownedPostfix)));
        var respawnPostfix = new HarmonyMethod(AccessTools.Method(typeof(RuntimeHookInstaller), nameof(RespawnPostfix)));
        var revivedPostfix = new HarmonyMethod(AccessTools.Method(typeof(RuntimeHookInstaller), nameof(RevivedPostfix)));
        var patchedCount = 0;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!LooksLikeGameAssembly(assembly))
            {
                continue;
            }

            foreach (var type in ReflectionReader.SafeGetTypes(assembly))
            {
                if (!LooksLikePlayerLifecycleType(type))
                {
                    continue;
                }

                MethodInfo[] methods;
                try
                {
                    methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }
                catch
                {
                    continue;
                }

                foreach (var method in methods)
                {
                    if (patchedCount >= 80 || method.IsAbstract || method.ContainsGenericParameters || _patched.Contains(method))
                    {
                        continue;
                    }

                    HarmonyMethod? postfix = null;
                    if (DeathMethods.Contains(method.Name)) postfix = deathPostfix;
                    else if (DownedMethods.Contains(method.Name)) postfix = downedPostfix;
                    else if (RespawnMethods.Contains(method.Name)) postfix = respawnPostfix;
                    else if (ReviveMethods.Contains(method.Name)) postfix = revivedPostfix;

                    if (postfix is null)
                    {
                        continue;
                    }

                    try
                    {
                        _harmony.Patch(method, postfix: postfix);
                        _patched.Add(method);
                        patchedCount++;
                    }
                    catch
                    {
                        // A single incompatible IL2CPP method must not block all other hooks.
                    }
                }
            }
        }

        _installed = true;
        _log($"Dynamische Todes-/Respawn-Hooks installiert: {patchedCount}");
    }

    private static void DeathPostfix(object? __instance, object[]? __args) =>
        _active?.Emit(PlayerSignalKind.Death, __instance, __args);

    private static void DownedPostfix(object? __instance, object[]? __args) =>
        _active?.Emit(PlayerSignalKind.Downed, __instance, __args);

    private static void RespawnPostfix(object? __instance, object[]? __args) =>
        _active?.Emit(PlayerSignalKind.Respawn, __instance, __args);

    private static void RevivedPostfix(object? __instance, object[]? __args) =>
        _active?.Emit(PlayerSignalKind.Revived, __instance, __args);

    private void Emit(PlayerSignalKind kind, object? instance, object[]? arguments)
    {
        try
        {
            var nowUtc = DateTime.UtcNow;
            var player = _discovery.ResolveFromRuntimeObject(instance, arguments, nowUtc);
            if (player is not null)
            {
                _onSignal(player, kind, nowUtc);
            }
        }
        catch
        {
            // Hook callbacks run inside game methods and must never break the game method.
        }
    }

    public void Deactivate()
    {
        if (ReferenceEquals(_active, this))
        {
            _active = null;
        }
    }

    private static bool LooksLikeGameAssembly(Assembly assembly)
    {
        var name = assembly.GetName().Name ?? string.Empty;
        return name.StartsWith("Sons", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Endnight", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Assembly-CSharp", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("bolt.user", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikePlayerLifecycleType(Type type)
    {
        if (type.IsInterface)
        {
            return false;
        }

        var name = (type.FullName ?? type.Name).ToLowerInvariant();
        if (!name.Contains("player", StringComparison.Ordinal))
        {
            return false;
        }

        if (name.Contains("enemy", StringComparison.Ordinal) ||
            name.Contains("animal", StringComparison.Ordinal) ||
            name.Contains("npc", StringComparison.Ordinal) ||
            name.Contains("vail", StringComparison.Ordinal) ||
            name.Contains("gui", StringComparison.Ordinal) ||
            name.Contains("menu", StringComparison.Ordinal) ||
            name.Contains("manager", StringComparison.Ordinal) ||
            name.Contains("registry", StringComparison.Ordinal) ||
            name.Contains("collection", StringComparison.Ordinal) ||
            name.Contains("lobby", StringComparison.Ordinal) ||
            name.Contains("spawner", StringComparison.Ordinal) ||
            name.Contains("service", StringComparison.Ordinal))
        {
            return false;
        }

        return name.Contains("death", StringComparison.Ordinal) ||
               name.Contains("health", StringComparison.Ordinal) ||
               name.Contains("state", StringComparison.Ordinal) ||
               name.Contains("respawn", StringComparison.Ordinal) ||
               name.Contains("revive", StringComparison.Ordinal) ||
               name.Contains("coop", StringComparison.Ordinal) ||
               name.Contains("network", StringComparison.Ordinal) ||
               name.Contains("multiplayer", StringComparison.Ordinal);
    }
}
