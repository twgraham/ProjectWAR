namespace Core.Infrastructure.Network;

/// <summary>
/// Specifies that a numeric property should be serialized in little-endian byte order.
/// By default the serializer uses big-endian (network byte order) for all multi-byte values.
/// Applies to <c>short</c>, <c>ushort</c>, <c>int</c>, <c>uint</c>, <c>long</c>, <c>ulong</c>,
/// <c>float</c>, and <c>double</c>. Ignored for single-byte types where byte order is irrelevant.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class LittleEndianAttribute : Attribute
{
}
