namespace Core.Infrastructure.Network.Serialization.Attributes;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class CStringAttribute : Attribute, ISerializerRule
{
    public int? Length { get; }

    /// <summary>Null-terminated C-string with no fixed field width.</summary>
    public CStringAttribute()
    {
        Length = null;
    }

    /// <summary>Fixed-width C-string field of exactly <paramref name="length"/> bytes.</summary>
    public CStringAttribute(int length)
    {
        if (length <= 0)
            throw new ArgumentOutOfRangeException(nameof(length), "CString length must be positive");
        Length = length;
    }

    /// <inheritdoc />
    public bool CanRead(SerializerPropertyContext ctx)
        => ctx.UnderlyingType == typeof(string);

    /// <inheritdoc />
    public object? Read(ref BinaryPacketSerializer.SpanReader reader, SerializerPropertyContext ctx)
        => reader.ReadCString(Length);

    /// <inheritdoc />
    public bool CanWrite(SerializerPropertyContext ctx)
        => ctx.UnderlyingType == typeof(string);

    /// <inheritdoc />
    public void Write(ref BinaryPacketSerializer.SpanWriter writer, object? value, SerializerPropertyContext ctx)
        => writer.WriteCString((string?)value ?? string.Empty, Length);

    /// <inheritdoc />
    public int? SerializedSize(SerializerPropertyContext ctx) => Length;
}