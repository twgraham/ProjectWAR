using System.Reflection;

namespace Core.Infrastructure.Network.Serialization;

/// <summary>
/// Provides metadata about a property being serialized/deserialized at runtime.
/// Passed to <see cref="ISerializerRule"/> implementations so they can inspect
/// the property type, nullability, and other attributes without coupling to
/// <see cref="PropertyInfo"/> directly.
/// </summary>
public readonly struct SerializerPropertyContext
{
    /// <summary>The declared property type (may be <c>Nullable&lt;T&gt;</c>).</summary>
    public Type PropertyType { get; }

    /// <summary>
    /// The underlying type after unwrapping <c>Nullable&lt;T&gt;</c>.
    /// Equal to <see cref="PropertyType"/> when the property is not nullable.
    /// </summary>
    public Type UnderlyingType { get; }

    /// <summary>Whether the property is a nullable value type or a nullable reference type.</summary>
    public bool IsNullable { get; }

    /// <summary>Whether the property is a <c>Nullable&lt;T&gt;</c> value type (e.g. <c>int?</c>).</summary>
    public bool IsNullableValueType { get; }

    /// <summary>The reflection <see cref="PropertyInfo"/>, if available.</summary>
    public PropertyInfo? PropertyInfo { get; }

    public SerializerPropertyContext(PropertyInfo propertyInfo)
    {
        PropertyInfo = propertyInfo;
        PropertyType = propertyInfo.PropertyType;
        var underlying = Nullable.GetUnderlyingType(propertyInfo.PropertyType);
        UnderlyingType = underlying ?? propertyInfo.PropertyType;
        IsNullable = underlying != null || !propertyInfo.PropertyType.IsValueType;
        IsNullableValueType = underlying != null;
    }

    public SerializerPropertyContext(Type propertyType, PropertyInfo? propertyInfo = null)
    {
        PropertyInfo = propertyInfo;
        PropertyType = propertyType;
        var underlying = Nullable.GetUnderlyingType(propertyType);
        UnderlyingType = underlying ?? propertyType;
        IsNullable = underlying != null || !propertyType.IsValueType;
        IsNullableValueType = underlying != null;
    }

    /// <summary>Returns the first attribute of type <typeparamref name="T"/> on the property, or <c>null</c>.</summary>
    public T? GetAttribute<T>() where T : Attribute
        => PropertyInfo?.GetCustomAttribute<T>();

    /// <summary>Returns <c>true</c> if the property has an attribute of type <typeparamref name="T"/>.</summary>
    public bool HasAttribute<T>() where T : Attribute
        => PropertyInfo?.GetCustomAttribute<T>() != null;
}
