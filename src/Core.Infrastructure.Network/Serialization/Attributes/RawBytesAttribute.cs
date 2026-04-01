namespace Core.Infrastructure.Network.Serialization.Attributes;

/// <summary>
/// Marks a <c>byte[]</c> property as raw (un-prefixed) bytes on the wire.
/// <para>
/// <b>Write</b>: the array contents are written directly — no length prefix is emitted.<br/>
/// <b>Read</b>: all remaining bytes in the packet buffer are consumed into the array.
/// </para>
/// <para>
/// Because the read path consumes the remainder of the buffer, a <c>[RawBytes]</c>
/// property should be the <b>last</b> field in the DTO, or the DTO should be
/// write-only (server→client).
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class RawBytesAttribute : Attribute;
