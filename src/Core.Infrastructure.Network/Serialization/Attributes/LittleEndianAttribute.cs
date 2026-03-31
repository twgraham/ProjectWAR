namespace Core.Infrastructure.Network.Serialization.Attributes;

/// <summary>
/// Specifies that a numeric property should be serialized in little-endian byte order.
/// By default the serializer uses big-endian (network byte order) for all multi-byte values.
/// Applies to <c>short</c>, <c>ushort</c>, <c>int</c>, <c>uint</c>, <c>long</c>, <c>ulong</c>,
/// <c>float</c>, and <c>double</c>. Ignored for single-byte types where byte order is irrelevant.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class LittleEndianAttribute : Attribute, ISerializerRule
{
    /// <inheritdoc />
    public bool CanRead(SerializerPropertyContext ctx) => IsNumeric(ctx.UnderlyingType);

    /// <inheritdoc />
    public object? Read(ref BinaryPacketSerializer.SpanReader reader, SerializerPropertyContext ctx)
    {
        var type = ctx.UnderlyingType;
        if (type == typeof(short)) return reader.ReadInt16LE();
        if (type == typeof(ushort)) return reader.ReadUInt16LE();
        if (type == typeof(int)) return reader.ReadInt32LE();
        if (type == typeof(uint)) return reader.ReadUInt32LE();
        if (type == typeof(long)) return reader.ReadInt64LE();
        if (type == typeof(ulong)) return reader.ReadUInt64LE();
        if (type == typeof(float)) return reader.ReadFloatLE();
        if (type == typeof(double)) return reader.ReadDoubleLE();
        throw new NotSupportedException($"[LittleEndian] is not supported on type {type.Name}");
    }

    /// <inheritdoc />
    public bool CanWrite(SerializerPropertyContext ctx) => IsNumeric(ctx.UnderlyingType);

    /// <inheritdoc />
    public void Write(ref BinaryPacketSerializer.SpanWriter writer, object? value, SerializerPropertyContext ctx)
    {
        var type = ctx.UnderlyingType;
        if (type == typeof(short)) { writer.WriteInt16LE((short)value!); return; }
        if (type == typeof(ushort)) { writer.WriteUInt16LE((ushort)value!); return; }
        if (type == typeof(int)) { writer.WriteInt32LE((int)value!); return; }
        if (type == typeof(uint)) { writer.WriteUInt32LE((uint)value!); return; }
        if (type == typeof(long)) { writer.WriteInt64LE((long)value!); return; }
        if (type == typeof(ulong)) { writer.WriteUInt64LE((ulong)value!); return; }
        if (type == typeof(float)) { writer.WriteFloatLE((float)value!); return; }
        if (type == typeof(double)) { writer.WriteDoubleLE((double)value!); return; }
        throw new NotSupportedException($"[LittleEndian] is not supported on type {type.Name}");
    }

    /// <inheritdoc />
    public int? SerializedSize(SerializerPropertyContext ctx)
    {
        var type = ctx.UnderlyingType;
        if (type == typeof(short) || type == typeof(ushort)) return 2;
        if (type == typeof(int) || type == typeof(uint) || type == typeof(float)) return 4;
        if (type == typeof(long) || type == typeof(ulong) || type == typeof(double)) return 8;
        return null;
    }

    private static bool IsNumeric(Type type)
        => type == typeof(short) || type == typeof(ushort) ||
           type == typeof(int) || type == typeof(uint) ||
           type == typeof(long) || type == typeof(ulong) ||
           type == typeof(float) || type == typeof(double);
}
