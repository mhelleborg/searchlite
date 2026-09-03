using System.Collections;

namespace SearchLite;

/// <summary>
/// Helpers for reasoning about collection-typed document fields (arrays, lists, sets...).
/// </summary>
public static class CollectionTypes
{
    /// <summary>
    /// True when <paramref name="type"/> is serialized as a JSON array: any <see cref="IEnumerable"/>
    /// except <see cref="string"/> and dictionaries (which serialize as JSON objects).
    /// </summary>
    public static bool IsCollection(Type type) =>
        type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type) && !IsDictionary(type);

    /// <summary>
    /// True for dictionary-shaped types, which are serialized as JSON objects rather than arrays.
    /// </summary>
    public static bool IsDictionary(Type type) =>
        typeof(IDictionary).IsAssignableFrom(type)
        || type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)
        || type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>));

    /// <summary>
    /// Returns the element type of an array or a type implementing <see cref="IEnumerable{T}"/>.
    /// </summary>
    public static Type GetElementType(Type collectionType)
    {
        if (collectionType.IsArray)
        {
            return collectionType.GetElementType()!;
        }

        if (collectionType.IsGenericType && collectionType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            return collectionType.GetGenericArguments()[0];
        }

        var enumerableInterface = collectionType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        if (enumerableInterface != null)
        {
            return enumerableInterface.GetGenericArguments()[0];
        }

        throw new NotSupportedException($"Cannot determine element type for collection type {collectionType}");
    }
}
