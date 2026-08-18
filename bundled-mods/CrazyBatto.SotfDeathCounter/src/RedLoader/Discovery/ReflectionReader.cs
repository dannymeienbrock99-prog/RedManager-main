using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace CrazyBatto.SotfDeathCounter.RedLoader;

internal static class ReflectionReader
{
    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly ConcurrentDictionary<Type, MemberInfo[]> InstanceMembers = new();
    private static readonly ConcurrentDictionary<Type, MemberInfo[]> StaticMembers = new();

    public static IEnumerable<(string Name, object Value)> ReadNamedValues(
        object source,
        IReadOnlySet<string> acceptedNames)
    {
        foreach (var member in GetMembers(source.GetType(), staticOnly: false))
        {
            if (!acceptedNames.Contains(member.Name))
            {
                continue;
            }

            if (TryReadMember(source, member, out var value) && value is not null)
            {
                yield return (member.Name, value);
            }
        }
    }

    public static IEnumerable<(string Name, object Value)> ReadRelevantMemberValues(
        object source,
        Func<string, bool> predicate)
    {
        foreach (var member in GetMembers(source.GetType(), staticOnly: false))
        {
            if (!predicate(member.Name))
            {
                continue;
            }

            if (TryReadMember(source, member, out var value) && value is not null)
            {
                yield return (member.Name, value);
            }
        }
    }

    public static IEnumerable<(string Name, object Value)> ReadRelevantStaticValues(
        Type type,
        Func<string, bool> predicate)
    {
        foreach (var member in GetMembers(type, staticOnly: true))
        {
            if (!predicate(member.Name))
            {
                continue;
            }

            if (TryReadMember(null, member, out var value) && value is not null)
            {
                yield return (member.Name, value);
            }
        }
    }

    public static bool TryInvokeStaticNoArgs(Type type, string methodName, out object? result)
    {
        result = null;
        try
        {
            var method = type.GetMethods(StaticFlags)
                .FirstOrDefault(candidate =>
                    candidate.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase) &&
                    candidate.GetParameters().Length == 0 &&
                    !candidate.IsGenericMethodDefinition);

            if (method is null)
            {
                return false;
            }

            result = method.Invoke(null, null);
            return result is not null;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryReadFirst(object source, IReadOnlySet<string> names, out string memberName, out object? value)
    {
        foreach (var pair in ReadNamedValues(source, names))
        {
            memberName = pair.Name;
            value = pair.Value;
            return true;
        }

        memberName = string.Empty;
        value = null;
        return false;
    }

    public static bool TryToBoolean(object value, out bool result)
    {
        try
        {
            switch (value)
            {
                case bool boolean:
                    result = boolean;
                    return true;
                case string text when bool.TryParse(text, out var parsed):
                    result = parsed;
                    return true;
                case IConvertible convertible:
                    result = convertible.ToDouble(CultureInfo.InvariantCulture) != 0d;
                    return true;
                default:
                    result = false;
                    return false;
            }
        }
        catch
        {
            result = false;
            return false;
        }
    }

    public static bool TryToDouble(object value, out double result)
    {
        try
        {
            switch (value)
            {
                case double number:
                    result = number;
                    return true;
                case float number:
                    result = number;
                    return true;
                case decimal number:
                    result = (double)number;
                    return true;
                case string text when double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed):
                    result = parsed;
                    return true;
                case IConvertible convertible:
                    result = convertible.ToDouble(CultureInfo.InvariantCulture);
                    return true;
                default:
                    result = 0d;
                    return false;
            }
        }
        catch
        {
            result = 0d;
            return false;
        }
    }

    public static bool TryToCleanString(object value, out string result)
    {
        result = string.Empty;
        try
        {
            if (value is UnityEngine.Object)
            {
                return false;
            }

            var text = value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            text = text.Trim().Trim('\0');
            if (text.Length is < 1 or > 128)
            {
                return false;
            }

            result = text;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static IEnumerable<object> ExpandKnownNestedObjects(object source, IReadOnlySet<string> nestedMemberNames, int maxDepth = 2)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<(object Value, int Depth)>();
        queue.Enqueue((source, 0));

        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();
            if (!seen.Add(current))
            {
                continue;
            }

            yield return current;
            if (depth >= maxDepth)
            {
                continue;
            }

            foreach (var pair in ReadNamedValues(current, nestedMemberNames))
            {
                if (IsUsefulNestedObject(pair.Value))
                {
                    queue.Enqueue((pair.Value, depth + 1));
                }
            }
        }
    }

    public static Type[] SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type is not null).Cast<Type>().ToArray();
        }
        catch
        {
            return Array.Empty<Type>();
        }
    }

    private static MemberInfo[] GetMembers(Type type, bool staticOnly)
    {
        var cache = staticOnly ? StaticMembers : InstanceMembers;
        return cache.GetOrAdd(type, current =>
        {
            var flags = staticOnly ? StaticFlags : InstanceFlags;
            var fields = current.GetFields(flags).Cast<MemberInfo>();
            var properties = current.GetProperties(flags)
                .Where(property => property.GetIndexParameters().Length == 0 && property.GetMethod is not null)
                .Cast<MemberInfo>();
            return fields.Concat(properties).ToArray();
        });
    }

    private static bool TryReadMember(object? source, MemberInfo member, out object? value)
    {
        value = null;
        try
        {
            switch (member)
            {
                case FieldInfo field:
                    value = field.GetValue(source);
                    return value is not null;
                case PropertyInfo property:
                    value = property.GetValue(source, null);
                    return value is not null;
                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool IsUsefulNestedObject(object value)
    {
        if (value is string || value.GetType().IsPrimitive || value.GetType().IsEnum)
        {
            return false;
        }

        return value is not UnityEngine.Object || value is Component;
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
