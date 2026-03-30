namespace Core.Infrastructure.Network.Serialization.Attributes;

/// <summary>
/// Defines custom serialization behaviour for a property attribute.
/// Implement this interface on an <see cref="Attribute"/> to control how a
/// property is read from and written to the wire.
/// <para>
/// Both the reflection-based <see cref="BinaryPacketSerializer"/> and the
/// source-generated serializer honour this interface automatically — no
/// changes to either serializer are required when a new implementation is
/// added.
/// </para>
/// <para>
/// Prefer the generic <see cref="ICustomSerializationAttribute{T}"/> when
/// the property type is known at compile time.  The generic form provides
/// strongly-typed <c>Read</c>/<c>Write</c> methods and automatically
/// bridges to this non-generic interface via default interface methods, so
/// implementers only need to supply the typed overloads.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// The source generator avoids reflection by instantiating the attribute
/// directly (e.g. <c>new PascalStringAttribute().Read(ref reader)</c>).
/// </para>
/// <para>
/// <b>Wire size convention for the source generator:</b> Because the source
/// generator cannot evaluate <see cref="FixedWireSize"/> at compile time, it
/// infers the fixed wire size from the attribute's constructor arguments. If
/// the first constructor argument is a positive <see cref="int"/>, it is
/// treated as the fixed wire size in bytes; otherwise the encoding is assumed
/// to be variable-length. This convention is used when computing entry sizes
/// for <c>[SizedEntry]</c> at source-generation time. The reflection-based
/// serializer always uses the <see cref="FixedWireSize"/> property directly.
/// </para>
/// </remarks>
public interface ICustomSerializationAttribute
{
    /// <summary>
    /// Writes <paramref name="value"/> to the wire using this custom encoding.
    /// </summary>
    void Write(ref BinaryPacketSerializer.SpanWriter writer, object value);

    /// <summary>
    /// Reads a value from the wire using this custom encoding.
    /// </summary>
    object Read(ref BinaryPacketSerializer.SpanReader reader);

    /// <summary>
    /// The fixed wire size in bytes if this encoding always produces a fixed
    /// number of bytes, or <c>null</c> if the size is variable.
    /// Used by <c>[SizedEntry]</c> to compute per-entry byte counts.
    /// </summary>
    int? FixedWireSize { get; }
}

/// <summary>
/// Strongly-typed variant of <see cref="ICustomSerializationAttribute"/>.
/// Implement this when the serialized property type is known at compile time.
/// <para>
/// Default interface methods automatically bridge the non-generic
/// <see cref="ICustomSerializationAttribute.Write"/> and
/// <see cref="ICustomSerializationAttribute.Read"/> overloads, so
/// implementers only need to supply the typed <see cref="Write"/> and
/// <see cref="Read"/> methods plus <see cref="ICustomSerializationAttribute.FixedWireSize"/>.
/// </para>
/// </summary>
/// <typeparam name="T">The CLR type of the property being serialized.</typeparam>
public interface ICustomSerializationAttribute<T> : ICustomSerializationAttribute
{
    /// <summary>
    /// Writes <paramref name="value"/> to the wire using this custom encoding.
    /// </summary>
    void Write(ref BinaryPacketSerializer.SpanWriter writer, T value);

    /// <summary>
    /// Reads a value of type <typeparamref name="T"/> from the wire.
    /// </summary>
    new T Read(ref BinaryPacketSerializer.SpanReader reader);

    // Default interface methods bridging to the typed overloads.
    // The null-forgiving operator is safe: the non-generic Read contract
    // returns non-null object, and implementations must not return null.
    void ICustomSerializationAttribute.Write(ref BinaryPacketSerializer.SpanWriter writer, object value)
        => Write(ref writer, (T)value);

    object ICustomSerializationAttribute.Read(ref BinaryPacketSerializer.SpanReader reader)
        => Read(ref reader)!;
}
