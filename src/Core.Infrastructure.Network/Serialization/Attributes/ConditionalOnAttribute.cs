namespace Core.Infrastructure.Network.Serialization.Attributes;

/// <summary>
/// Indicates that a property should only be serialized/deserialized when a sibling property
/// (declared earlier in the same class) matches one of the specified values.
/// The referenced property must appear before this property in declaration order.
/// When the condition is not met, the property is skipped entirely — no bytes are read or written.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class ConditionalOnAttribute : Attribute
{
    /// <summary>
    /// The name of the sibling property whose value determines whether this property is present on the wire.
    /// </summary>
    public string PropertyName { get; }

    /// <summary>
    /// The set of values that activate this property. If the referenced property's value
    /// matches any of these, the property is read/written; otherwise it is skipped.
    /// </summary>
    public object[] Values { get; }

    public ConditionalOnAttribute(string propertyName, params object[] values)
    {
        PropertyName = propertyName;
        Values = values;
    }
}
