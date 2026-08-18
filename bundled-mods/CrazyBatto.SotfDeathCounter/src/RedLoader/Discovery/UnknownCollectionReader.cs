using System.Collections;
using System.Reflection;

namespace CrazyBatto.SotfDeathCounter.RedLoader;

internal static class UnknownCollectionReader
{
    public static IEnumerable<object> Enumerate(object? value, int maximumItems = 64)
    {
        if (value is null || value is string)
        {
            yield break;
        }

        var yielded = 0;

        if (value is IEnumerable enumerable)
        {
            IEnumerator? enumerator = null;
            try
            {
                enumerator = enumerable.GetEnumerator();
                while (yielded < maximumItems && enumerator.MoveNext())
                {
                    if (enumerator.Current is { } current)
                    {
                        yielded++;
                        yield return UnwrapDictionaryEntry(current);
                    }
                }
            }
            finally
            {
                (enumerator as IDisposable)?.Dispose();
            }

            yield break;
        }

        foreach (var item in EnumerateByIndex(value, maximumItems))
        {
            yielded++;
            yield return item;
            if (yielded >= maximumItems)
            {
                yield break;
            }
        }

        if (yielded > 0)
        {
            yield break;
        }

        foreach (var item in EnumerateByReflectedEnumerator(value, maximumItems))
        {
            yield return item;
        }
    }

    private static IEnumerable<object> EnumerateByIndex(object value, int maximumItems)
    {
        var type = value.GetType();
        var countProperty = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(property =>
                property.Name.Equals("Count", StringComparison.OrdinalIgnoreCase) &&
                property.GetIndexParameters().Length == 0);

        if (countProperty is null)
        {
            yield break;
        }

        int count;
        try
        {
            count = Convert.ToInt32(countProperty.GetValue(value));
        }
        catch
        {
            yield break;
        }

        count = Math.Clamp(count, 0, maximumItems);
        var itemProperty = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(property =>
                property.Name.Equals("Item", StringComparison.OrdinalIgnoreCase) &&
                property.GetIndexParameters().Length == 1);
        var getItemMethod = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(method =>
                method.Name.Equals("get_Item", StringComparison.OrdinalIgnoreCase) &&
                method.GetParameters().Length == 1);

        for (var index = 0; index < count; index++)
        {
            object? item = null;
            try
            {
                if (itemProperty is not null)
                {
                    item = itemProperty.GetValue(value, new object[] { index });
                }
                else if (getItemMethod is not null)
                {
                    item = getItemMethod.Invoke(value, new object[] { index });
                }
            }
            catch
            {
                // One broken item must not abort the whole roster scan.
            }

            if (item is not null)
            {
                yield return UnwrapDictionaryEntry(item);
            }
        }
    }

    private static IEnumerable<object> EnumerateByReflectedEnumerator(object value, int maximumItems)
    {
        var type = value.GetType();
        MethodInfo? getEnumerator;
        try
        {
            getEnumerator = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(method => method.Name == "GetEnumerator" && method.GetParameters().Length == 0);
        }
        catch
        {
            yield break;
        }

        if (getEnumerator is null)
        {
            yield break;
        }

        object? enumerator;
        try
        {
            enumerator = getEnumerator.Invoke(value, null);
        }
        catch
        {
            yield break;
        }

        if (enumerator is null)
        {
            yield break;
        }

        var enumeratorType = enumerator.GetType();
        var moveNext = enumeratorType.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var current = enumeratorType.GetProperty("Current", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (moveNext is null || current is null)
        {
            yield break;
        }

        for (var index = 0; index < maximumItems; index++)
        {
            bool hasNext;
            try
            {
                hasNext = Convert.ToBoolean(moveNext.Invoke(enumerator, null));
            }
            catch
            {
                yield break;
            }

            if (!hasNext)
            {
                yield break;
            }

            object? item;
            try
            {
                item = current.GetValue(enumerator);
            }
            catch
            {
                continue;
            }

            if (item is not null)
            {
                yield return UnwrapDictionaryEntry(item);
            }
        }
    }

    private static object UnwrapDictionaryEntry(object value)
    {
        // Keep dictionary entries intact: PlayerDiscoveryService inspects both Key and Value,
        // which preserves Steam/platform IDs stored as dictionary keys.
        return value;
    }
}
