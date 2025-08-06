using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Serialization;

namespace SearchLite;

public enum EnumSerializationFormat
{
    Integer,
    String
}

public static class EnumSerializationAnalyzer<T> where T : Enum
{
    private static readonly ConcurrentDictionary<string, EnumSerializationFormat> PropertyCache = new();
    private static readonly Lazy<EnumSerializationFormat> TypeLevelFormat = new(DetermineTypeLevelFormat);

    /// <summary>
    /// Gets the serialization format for the enum type T when no property-specific converter is present
    /// </summary>
    public static EnumSerializationFormat DefaultFormat => TypeLevelFormat.Value;

    /// <summary>
    /// Determines how an enum property will be serialized
    /// </summary>
    /// <param name="propertyInfo">The property to check</param>
    /// <returns>The serialization format for the property</returns>
    public static EnumSerializationFormat GetPropertyFormat(PropertyInfo propertyInfo)
    {
        if (propertyInfo == null)
            throw new ArgumentNullException(nameof(propertyInfo));

        if (propertyInfo.PropertyType != typeof(T) && 
            Nullable.GetUnderlyingType(propertyInfo.PropertyType) != typeof(T))
        {
            throw new ArgumentException($"Property type must be {typeof(T).Name} or Nullable<{typeof(T).Name}>");
        }

        var cacheKey = $"{propertyInfo.DeclaringType?.FullName}.{propertyInfo.Name}";
        
        return PropertyCache.GetOrAdd(cacheKey, _ => DeterminePropertyFormat(propertyInfo));
    }

    /// <summary>
    /// Determines how an enum property will be serialized by property name
    /// </summary>
    public static EnumSerializationFormat GetPropertyFormat<TClass>(string propertyName)
    {
        var propertyInfo = typeof(TClass).GetProperty(propertyName);
        if (propertyInfo == null)
            throw new ArgumentException($"Property {propertyName} not found on type {typeof(TClass).Name}");
        
        return GetPropertyFormat(propertyInfo);
    }

    private static EnumSerializationFormat DeterminePropertyFormat(PropertyInfo propertyInfo)
    {
        // First check for JsonConverter attribute on the property
        var propertyConverter = propertyInfo.GetCustomAttribute<JsonConverterAttribute>();
        if (propertyConverter != null)
        {
            return IsStringEnumConverter(propertyConverter.ConverterType) 
                ? EnumSerializationFormat.String 
                : EnumSerializationFormat.Integer;
        }

        // Fall back to type-level check
        return DefaultFormat;
    }

    private static EnumSerializationFormat DetermineTypeLevelFormat()
    {
        var enumType = typeof(T);

        // Check for JsonConverter attribute on the enum type itself
        var typeConverter = enumType.GetCustomAttribute<JsonConverterAttribute>();
        if (typeConverter != null)
        {
            return IsStringEnumConverter(typeConverter.ConverterType) 
                ? EnumSerializationFormat.String 
                : EnumSerializationFormat.Integer;
        }

        // Check if any enum member has JsonStringEnumMemberName attribute
        var fields = enumType.GetFields(BindingFlags.Public | BindingFlags.Static);
        foreach (var field in fields)
        {
            var jsonConverterAttribute = field.GetCustomAttribute<JsonConverterAttribute>();
            if (jsonConverterAttribute != null)
            {
                return IsStringEnumConverter(jsonConverterAttribute.ConverterType) 
                    ? EnumSerializationFormat.String 
                    : EnumSerializationFormat.Integer;
            }
        }

        // Default to integer serialization
        return EnumSerializationFormat.Integer;
    }

    private static bool IsStringEnumConverter(Type? converterType)
    {
        if (converterType == null) return false;

        // Check for JsonStringEnumConverter directly
        if (converterType == typeof(JsonStringEnumConverter))
            return true;

        // Check if it's the generic version
        if (converterType.IsGenericType && 
            converterType.GetGenericTypeDefinition() == typeof(JsonStringEnumConverter<>))
            return true;

        // Check base types
        var baseType = converterType.BaseType;
        while (baseType != null)
        {
            if (baseType == typeof(JsonStringEnumConverter) ||
                (baseType.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(JsonStringEnumConverter<>)))
                return true;
            
            baseType = baseType.BaseType;
        }

        return false;
    }
}

// Non-generic helper class for runtime enum type checking
public static class EnumSerializationAnalyzer
{
    private static readonly ConcurrentDictionary<(Type, PropertyInfo), EnumSerializationFormat> RuntimeCache = new();

    /// <summary>
    /// Determines serialization format for any enum property at runtime
    /// </summary>
    public static EnumSerializationFormat GetPropertyFormat(PropertyInfo propertyInfo)
    {
        if (propertyInfo == null)
            throw new ArgumentNullException(nameof(propertyInfo));

        var propertyType = Nullable.GetUnderlyingType(propertyInfo.PropertyType) ?? propertyInfo.PropertyType;
        
        if (!propertyType.IsEnum)
            throw new ArgumentException("Property type must be an enum");

        return RuntimeCache.GetOrAdd((propertyType, propertyInfo), key =>
        {
            // Use reflection to call the generic version
            var genericType = typeof(EnumSerializationAnalyzer<>).MakeGenericType(propertyType);
            var method = genericType.GetMethod(nameof(EnumSerializationAnalyzer<DayOfWeek>.GetPropertyFormat), 
                new[] { typeof(PropertyInfo) });
            
            return (EnumSerializationFormat)method!.Invoke(null, [propertyInfo])!;
        });
    }

    /// <summary>
    /// Gets default serialization format for any enum type
    /// </summary>
    public static EnumSerializationFormat GetDefaultFormat(Type enumType)
    {
        if (!enumType.IsEnum)
            throw new ArgumentException("Type must be an enum");

        var genericType = typeof(EnumSerializationAnalyzer<>).MakeGenericType(enumType);
        var property = genericType.GetProperty(nameof(EnumSerializationAnalyzer<DayOfWeek>.DefaultFormat));
        
        return (EnumSerializationFormat)property!.GetValue(null)!;
    }
}