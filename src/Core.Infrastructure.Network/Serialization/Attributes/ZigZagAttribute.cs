namespace Core.Infrastructure.Network.Serialization.Attributes;

/// <summary>
/// Specifies that an integer property should be serialized using WAR-protocol ZigZag
/// variable-length encoding.
///
/// <para>
/// <b>Wire format:</b> The first byte carries the sign (bit 0), 6 data bits (bits 1-6),
/// and a continuation flag (bit 7). Subsequent bytes carry 7 data bits each with a
/// continuation flag. This encoding is compact for small magnitudes and supports
/// negative values natively.
/// </para>
///
/// Applies to <c>int</c> (and <c>int?</c>).
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class ZigZagAttribute : Attribute, ISerializerRule
{
    /// <inheritdoc />
    public bool CanRead(SerializerPropertyContext ctx) => IsSupported(ctx.UnderlyingType);

    /// <inheritdoc />
    public object? Read(ref BinaryPacketSerializer.SpanReader reader, SerializerPropertyContext ctx)
    {
        if (ctx.UnderlyingType == typeof(int))
            return reader.ReadZigZagInt32();

        throw new NotSupportedException($"[ZigZag] is not supported on type {ctx.UnderlyingType.Name}");
    }

    /// <inheritdoc />
    public bool CanWrite(SerializerPropertyContext ctx) => IsSupported(ctx.UnderlyingType);

    /// <inheritdoc />
    public void Write(ref BinaryPacketSerializer.SpanWriter writer, object? value, SerializerPropertyContext ctx)
    {
        if (ctx.UnderlyingType == typeof(int))
        {
            writer.WriteZigZagInt32((int)value!);
            return;
        }

        throw new NotSupportedException($"[ZigZag] is not supported on type {ctx.UnderlyingType.Name}");
    }

    /// <inheritdoc />
    public int? SerializedSize(SerializerPropertyContext ctx) => null; // variable-length

    private static bool IsSupported(Type type) => type == typeof(int);
}
