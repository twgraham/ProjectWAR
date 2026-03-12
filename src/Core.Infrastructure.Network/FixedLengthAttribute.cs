using System;

namespace Core.Infrastructure.Network;

/// <summary>
/// Specifies that a <c>byte[]</c> property is serialized as a fixed-length field with no length prefix.
/// Exactly <see cref="Length"/> bytes are read or written regardless of the array contents.
/// When writing, shorter arrays are zero-padded; longer arrays are truncated to <see cref="Length"/> bytes.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class FixedLengthAttribute : Attribute
{
    /// <summary>Gets the exact number of bytes to read or write.</summary>
    public int Length { get; }

    /// <summary>
    /// Creates a new <see cref="FixedLengthAttribute"/>.
    /// </summary>
    /// <param name="length">The exact byte count of the field.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="length"/> is not positive.</exception>
    public FixedLengthAttribute(int length)
    {
        if (length <= 0)
            throw new ArgumentOutOfRangeException(nameof(length), "FixedLength must be positive");
        Length = length;
    }
}
